import type { PresentationState } from '@workspace/world-schema';
import { WorkspaceSocket } from '../protocol/WorkspaceSocket.ts';
import { WorldReplica } from '../replica/WorldReplica.ts';
import { SceneReplicaSynchronizer } from '../replica/SceneReplicaSynchronizer.ts';
import { WorkspaceScene } from '../rendering/WorkspaceScene.ts';
import {
  CodaPresence,
  type CodaState,
  type ProactiveMode,
} from '../onboarding/CodaPresence.ts';
import { CameraNavigator } from '../navigation/CameraNavigator.ts';
import { SceneCommandController } from '../navigation/SceneCommandController.ts';
import {
  WorkspaceNativeBridge,
  type WorkspaceNativeEnvelope,
} from '../native/WorkspaceNativeBridge.ts';
import {
  ProtocolSurfaceStream,
  ProtocolWindowInputSink,
  type PointerButton,
} from '../surfaces/SurfaceStream.ts';
import {
  ApplicationSurface,
  ProtocolPresentationSink,
} from '../surfaces/ApplicationSurface.ts';

export type WorkspaceApp = {
  destroy(): void;
};

export class SuppressedKeyReleaseTracker {
  readonly #keys = new Set<string>();

  reserve(key: string): void {
    if (key !== 'Alt') this.#keys.add(key);
  }

  consume(key: string): boolean {
    return this.#keys.delete(key);
  }
}

type InitialSyncSocket = Pick<WorkspaceSocket, 'waitUntilOpen' | 'sendCommand'>;

export async function initializeWorkspaceConnection(socket: InitialSyncSocket): Promise<void> {
  await socket.waitUntilOpen();
  await socket.sendCommand('application.list');
}

function payloadRecord(message: WorkspaceNativeEnvelope): Record<string, unknown> {
  return typeof message.payload === 'object' && message.payload !== null
    ? message.payload as Record<string, unknown>
    : {};
}

const CODA_STATES = new Set<CodaState>([
  'waiting',
  'listening',
  'wake-detected',
  'thinking',
  'speaking',
  'working',
  'needs-attention',
  'mic-off',
]);

const PROACTIVE_MODES = new Set<ProactiveMode>([
  'CriticalOnly',
  'IncludeCompletion',
  'Quiet',
  'Custom',
]);

export function createWorkspaceApp(root: HTMLElement): WorkspaceApp {
  const originalClassName = root.className;
  const originalTabIndex = root.getAttribute('tabindex');
  root.replaceChildren();
  root.className = 'workspace-root workspace-arrival';
  root.tabIndex = 0;

  const sceneRoot = document.createElement('div');
  sceneRoot.className = 'scene-root';
  sceneRoot.setAttribute('aria-hidden', 'true');
  root.append(sceneRoot);

  const socket = new WorkspaceSocket();
  const scene = new WorkspaceScene(
    sceneRoot,
    undefined,
    (entityId) => new ProtocolSurfaceStream(socket, entityId),
    (entityId) => new ProtocolWindowInputSink(socket, entityId),
    (entityId) => new ProtocolPresentationSink(socket, entityId),
  );
  const replica = new WorldReplica();
  const synchronizer = new SceneReplicaSynchronizer(replica, scene);

  const unsubscribe = socket.subscribe((envelope) => synchronizer.apply(envelope));
  const bridge = new WorkspaceNativeBridge();
  const coda = new CodaPresence(root, {
    onPreferenceChange: (change) => bridge.post('preference.change.request', change),
    onVoiceControl: (action) => bridge.post('voice.control', { action }),
  });
  let selectedEntityId: string | null = null;
  const navigator = new CameraNavigator(scene);
  const sceneCommands = new SceneCommandController(
    scene,
    navigator,
    () => selectedEntityId,
    scene.getCameraPose(),
  );
  const unsubscribeNative = [
    bridge.subscribe('voice.state', (message) => {
      const state = payloadRecord(message).state;
      if (typeof state === 'string' && CODA_STATES.has(state as CodaState)) {
        coda.setState(state as CodaState);
      }
    }),
    bridge.subscribe('voice.caption', (message) => {
      const text = payloadRecord(message).text;
      if (typeof text === 'string') coda.showCaption(text);
    }),
    bridge.subscribe('voice.transcript', (message) => {
      const text = payloadRecord(message).text;
      if (typeof text === 'string') coda.setTranscript(text);
    }),
    bridge.subscribe('agent.event', (message) => {
      const payload = payloadRecord(message);
      if (typeof payload.summary === 'string') coda.showCaption(payload.summary);
      if (Array.isArray(payload.terminalEvents)) {
        coda.setTerminalEvents(payload.terminalEvents.filter(
          (event): event is string => typeof event === 'string',
        ), payload.reveal === true);
      }
      if (payload.level === 'error') coda.setState('needs-attention');
    }),
    bridge.subscribe('preference.changed', (message) => {
      const payload = payloadRecord(message);
      const proactiveMode = payload.proactiveMode;
      coda.setPreferences({
        microphoneEnabled: typeof payload.microphoneEnabled === 'boolean'
          ? payload.microphoneEnabled
          : undefined,
        captionsEnabled: typeof payload.captionsEnabled === 'boolean'
          ? payload.captionsEnabled
          : undefined,
        transcriptVisible: typeof payload.transcriptRetentionEnabled === 'boolean'
          ? payload.transcriptRetentionEnabled
          : undefined,
        proactiveMode: typeof proactiveMode === 'string'
          && PROACTIVE_MODES.has(proactiveMode as ProactiveMode)
          ? proactiveMode as ProactiveMode
          : undefined,
      });
    }),
    bridge.subscribe('ui.command', (message) => {
      const action = payloadRecord(message).action;
      if (action === 'show-terminal') coda.setTerminalVisible(true);
      if (action === 'hide-terminal') coda.setTerminalVisible(false);
    }),
    bridge.subscribe('scene.command', (message) => {
      void sceneCommands.handle(message.payload).then((result) => {
        bridge.post('scene.command.result', result);
      });
    }),
  ];

  void initializeWorkspaceConnection(socket)
    .then(() => bridge.post('renderer.ready', { surface: 'spatial', version: 1 }))
    .catch((error) => {
      const message = error instanceof Error ? error.message : String(error);
      coda.setState('needs-attention');
      coda.showCaption(`Windows Workspace Host is not connected. ${message}`);
      coda.setState('needs-attention');
    });

  const reticle = document.createElement('div');
  reticle.className = 'reticle';
  reticle.setAttribute('aria-hidden', 'true');

  const movementHint = document.createElement('p');
  movementHint.className = 'movement-hint';
  movementHint.textContent = 'Drag to look around. Use W A S D to move. Alt-drag a surface to move; add Shift to resize.';
  root.append(reticle, movementHint);

  let pointerId: number | null = null;
  let surfacePointer: {
    pointerId: number;
    surface: ApplicationSurface;
    u: number;
    v: number;
    button: PointerButton;
  } | null = null;
  let selectedSurface: ApplicationSurface | null = null;
  const suppressedKeyReleases = new SuppressedKeyReleaseTracker();
  let presentationDrag: {
    pointerId: number;
    surface: ApplicationSurface;
    mode: 'move' | 'resize';
    lastX: number;
    lastY: number;
    start: PresentationState;
    current: PresentationState;
  } | null = null;
  let lastX = 0;
  let lastY = 0;

  const reportInputError = (error: unknown): void => {
    const message = error instanceof Error ? error.message : String(error);
    coda.showCaption(`Windows rejected that surface input. ${message}`);
    coda.setState('needs-attention');
  };

  const reportPresentationError = (error: unknown): void => {
    const message = error instanceof Error ? error.message : String(error);
    coda.showCaption(`Placement was not saved and has been restored. ${message}`);
    coda.setState('needs-attention');
  };

  const onPointerDown = (event: PointerEvent): void => {
    if (event.target instanceof Element && event.target.closest('button')) return;
    sceneCommands.cancel('manual-pointer');
    const button: PointerButton | null = event.button === 0
      ? 'primary'
      : event.button === 2
        ? 'secondary'
        : null;
    const hit = button ? scene.hitTestApplicationSurface(event.clientX, event.clientY) : null;
    if (hit && button === 'primary' && event.altKey) {
      selectedSurface = hit.surface;
      selectedEntityId = hit.entityId;
      root.classList.add('has-selected-surface', 'is-presentation-drag');
      presentationDrag = {
        pointerId: event.pointerId,
        surface: hit.surface,
        mode: event.shiftKey ? 'resize' : 'move',
        lastX: event.clientX,
        lastY: event.clientY,
        start: hit.surface.displayedPresentation,
        current: hit.surface.displayedPresentation,
      };
      root.setPointerCapture(event.pointerId);
      event.preventDefault();
      return;
    }
    if (hit && button) {
      selectedSurface = hit.surface;
      selectedEntityId = hit.entityId;
      root.classList.add('has-selected-surface');
      surfacePointer = {
        pointerId: event.pointerId,
        surface: hit.surface,
        u: hit.u,
        v: hit.v,
        button,
      };
      root.setPointerCapture(event.pointerId);
      root.classList.add('is-surface-input');
      event.preventDefault();
      void hit.surface.pointer('down', hit.u, hit.v, button).catch(reportInputError);
      return;
    }

    selectedSurface = null;
    selectedEntityId = null;
    root.classList.remove('has-selected-surface');
    pointerId = event.pointerId;
    lastX = event.clientX;
    lastY = event.clientY;
    root.setPointerCapture(event.pointerId);
    root.classList.add('is-looking');
  };

  const onPointerMove = (event: PointerEvent): void => {
    if (presentationDrag?.pointerId === event.pointerId) {
      const deltaX = event.clientX - presentationDrag.lastX;
      const deltaY = event.clientY - presentationDrag.lastY;
      presentationDrag.lastX = event.clientX;
      presentationDrag.lastY = event.clientY;
      presentationDrag.current = presentationDrag.mode === 'move'
        ? {
            ...presentationDrag.current,
            position: {
              ...presentationDrag.current.position,
              x: presentationDrag.current.position.x + deltaX * 0.01,
              y: presentationDrag.current.position.y - deltaY * 0.01,
            },
          }
        : {
            ...presentationDrag.current,
            size: {
              ...presentationDrag.current.size,
              x: Math.max(0.5, presentationDrag.current.size.x + deltaX * 0.01),
              y: Math.max(0.5, presentationDrag.current.size.y + deltaY * 0.01),
            },
          };
      presentationDrag.surface.previewPresentation(presentationDrag.current);
      return;
    }
    if (surfacePointer?.pointerId === event.pointerId) {
      const hit = scene.hitTestApplicationSurface(
        event.clientX,
        event.clientY,
        surfacePointer.surface,
      );
      if (hit) {
        surfacePointer.u = hit.u;
        surfacePointer.v = hit.v;
        void hit.surface.pointer('move', hit.u, hit.v).catch(reportInputError);
      }
      return;
    }
    if (event.pointerId !== pointerId) return;
    sceneCommands.manualLook(event.clientX - lastX, event.clientY - lastY);
    lastX = event.clientX;
    lastY = event.clientY;
  };

  const endLook = (event: PointerEvent): void => {
    if (presentationDrag) {
      if (event.pointerId !== presentationDrag.pointerId) return;
      const active = presentationDrag;
      presentationDrag = null;
      root.classList.remove('is-presentation-drag');
      if (root.hasPointerCapture(event.pointerId)) root.releasePointerCapture(event.pointerId);
      if (event.type === 'pointercancel') {
        active.surface.previewPresentation(active.start);
      } else {
        void active.surface.commitPresentation(active.current).catch(reportPresentationError);
      }
      return;
    }
    if (surfacePointer) {
      if (event.pointerId !== surfacePointer.pointerId) return;
      const active = surfacePointer;
      surfacePointer = null;
      root.classList.remove('is-surface-input');
      if (root.hasPointerCapture(event.pointerId)) root.releasePointerCapture(event.pointerId);
      void active.surface.pointer(
        'up',
        active.u,
        active.v,
        active.button,
      ).catch(reportInputError);
      return;
    }
    if (event.pointerId !== pointerId) return;
    pointerId = null;
    root.classList.remove('is-looking');
    if (root.hasPointerCapture(event.pointerId)) root.releasePointerCapture(event.pointerId);
  };

  const onWheel = (event: WheelEvent): void => {
    const hit = scene.hitTestApplicationSurface(event.clientX, event.clientY);
    if (!hit) return;
    sceneCommands.cancel('manual-wheel');
    selectedSurface = hit.surface;
    selectedEntityId = hit.entityId;
    root.classList.add('has-selected-surface');
    event.preventDefault();
    void hit.surface.wheel(hit.u, hit.v, event.deltaX, event.deltaY).catch(reportInputError);
  };

  const onContextMenu = (event: MouseEvent): void => {
    if (scene.hitTestApplicationSurface(event.clientX, event.clientY)) event.preventDefault();
  };

  const onKeyDown = (event: KeyboardEvent): void => {
    if (event.target instanceof HTMLButtonElement) return;
    if (event.key === 'Escape') {
      sceneCommands.cancel('manual-escape');
      selectedSurface = null;
      selectedEntityId = null;
      root.classList.remove('has-selected-surface');
      event.preventDefault();
      return;
    }
    sceneCommands.cancel('manual-keyboard');
    if (selectedSurface && event.altKey && event.key.startsWith('Arrow')) {
      suppressedKeyReleases.reserve(event.key);
      event.preventDefault();
      const current = selectedSurface.displayedPresentation;
      const deltaX = event.key === 'ArrowRight' ? 0.2 : event.key === 'ArrowLeft' ? -0.2 : 0;
      const deltaY = event.key === 'ArrowUp' ? 0.2 : event.key === 'ArrowDown' ? -0.2 : 0;
      const next: PresentationState = event.shiftKey
        ? {
            ...current,
            size: {
              ...current.size,
              x: Math.max(0.5, current.size.x + deltaX),
              y: Math.max(0.5, current.size.y + deltaY),
            },
          }
        : {
            ...current,
            position: {
              ...current.position,
              x: current.position.x + deltaX,
              y: current.position.y + deltaY,
            },
          };
      void selectedSurface.commitPresentation(next).catch(reportPresentationError);
      return;
    }
    if (event.key === 'Alt' || event.altKey) {
      suppressedKeyReleases.reserve(event.key);
      event.preventDefault();
      return;
    }
    if (selectedSurface) {
      event.preventDefault();
      if (event.key.length === 1 && !event.altKey && !event.ctrlKey && !event.metaKey) {
        void selectedSurface.text(event.key).catch(reportInputError);
      } else {
        void selectedSurface.key('down', event.key).catch(reportInputError);
      }
      return;
    }
    const key = event.key.toLowerCase();
    const movement: Record<string, [number, number]> = {
      w: [1, 0],
      arrowup: [1, 0],
      s: [-1, 0],
      arrowdown: [-1, 0],
      a: [0, -1],
      arrowleft: [0, -1],
      d: [0, 1],
      arrowright: [0, 1],
    };
    const direction = movement[key];
    if (!direction) return;
    event.preventDefault();
    sceneCommands.manualMove(...direction);
  };

  const onKeyUp = (event: KeyboardEvent): void => {
    if (suppressedKeyReleases.consume(event.key)) {
      event.preventDefault();
      return;
    }
    if (event.key === 'Alt' || event.altKey) {
      event.preventDefault();
      return;
    }
    if (!selectedSurface || event.target instanceof HTMLButtonElement) return;
    if (event.key.length === 1 && !event.altKey && !event.ctrlKey && !event.metaKey) return;
    event.preventDefault();
    void selectedSurface.key('up', event.key).catch(reportInputError);
  };

  root.addEventListener('pointerdown', onPointerDown);
  root.addEventListener('pointermove', onPointerMove);
  root.addEventListener('pointerup', endLook);
  root.addEventListener('pointercancel', endLook);
  root.addEventListener('wheel', onWheel, { passive: false });
  root.addEventListener('contextmenu', onContextMenu);
  root.addEventListener('keydown', onKeyDown);
  root.addEventListener('keyup', onKeyUp);
  root.focus({ preventScroll: true });

  let previousFrame = performance.now();
  let navigationFrame = 0;
  const tickNavigation = (now: number): void => {
    sceneCommands.tick(now - previousFrame);
    previousFrame = now;
    navigationFrame = window.requestAnimationFrame(tickNavigation);
  };
  navigationFrame = window.requestAnimationFrame(tickNavigation);

  const arrivalTimer = window.setTimeout(
    () => root.classList.remove('workspace-arrival'),
    1200,
  );

  return {
    destroy(): void {
      unsubscribe();
      for (const unsubscribeMessage of unsubscribeNative) unsubscribeMessage();
      socket.close();
      bridge.destroy();
      coda.destroy();
      scene.dispose();
      window.clearTimeout(arrivalTimer);
      window.cancelAnimationFrame(navigationFrame);
      root.removeEventListener('pointerdown', onPointerDown);
      root.removeEventListener('pointermove', onPointerMove);
      root.removeEventListener('pointerup', endLook);
      root.removeEventListener('pointercancel', endLook);
      root.removeEventListener('wheel', onWheel);
      root.removeEventListener('contextmenu', onContextMenu);
      root.removeEventListener('keydown', onKeyDown);
      root.removeEventListener('keyup', onKeyUp);
      root.replaceChildren();
      root.className = originalClassName;
      if (originalTabIndex === null) {
        root.removeAttribute('tabindex');
      } else {
        root.setAttribute('tabindex', originalTabIndex);
      }
    },
  };
}
