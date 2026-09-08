import { WorkspaceSocket } from '../protocol/WorkspaceSocket.ts';
import { WorldReplica } from '../replica/WorldReplica.ts';
import { SceneReplicaSynchronizer } from '../replica/SceneReplicaSynchronizer.ts';
import { WorkspaceScene } from '../rendering/WorkspaceScene.ts';
import { WelcomeSequence } from '../onboarding/WelcomeSequence.ts';
import {
  ProtocolSurfaceStream,
  ProtocolWindowInputSink,
  type PointerButton,
} from '../surfaces/SurfaceStream.ts';
import type { ApplicationSurface } from '../surfaces/ApplicationSurface.ts';

export type WorkspaceApp = {
  destroy(): void;
};

type InitialSyncSocket = Pick<WorkspaceSocket, 'waitUntilOpen' | 'sendCommand'>;

export async function initializeWorkspaceConnection(socket: InitialSyncSocket): Promise<void> {
  await socket.waitUntilOpen();
  await socket.sendCommand('application.list');
}

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
  );
  const replica = new WorldReplica();
  const synchronizer = new SceneReplicaSynchronizer(replica, scene);

  const unsubscribe = socket.subscribe((envelope) => synchronizer.apply(envelope));

  const welcome = new WelcomeSequence(root, {
    onOpenApplication: async (displayName) => {
      await socket.sendCommand('application.launch', displayName);
    },
  });

  void initializeWorkspaceConnection(socket).catch((error) => {
    const message = error instanceof Error ? error.message : String(error);
    welcome.setStatus(`Windows Workspace Host is not connected. ${message}`, 'error');
  });

  const reticle = document.createElement('div');
  reticle.className = 'reticle';
  reticle.setAttribute('aria-hidden', 'true');

  const movementHint = document.createElement('p');
  movementHint.className = 'movement-hint';
  movementHint.textContent = 'Drag to look around. Use W A S D to move.';
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
  let lastX = 0;
  let lastY = 0;

  const reportInputError = (error: unknown): void => {
    const message = error instanceof Error ? error.message : String(error);
    welcome.setStatus(`Windows rejected that surface input. ${message}`, 'error');
  };

  const onPointerDown = (event: PointerEvent): void => {
    if (event.target instanceof Element && event.target.closest('button')) return;
    const button: PointerButton | null = event.button === 0
      ? 'primary'
      : event.button === 2
        ? 'secondary'
        : null;
    const hit = button ? scene.hitTestApplicationSurface(event.clientX, event.clientY) : null;
    if (hit && button) {
      selectedSurface = hit.surface;
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
    root.classList.remove('has-selected-surface');
    pointerId = event.pointerId;
    lastX = event.clientX;
    lastY = event.clientY;
    root.setPointerCapture(event.pointerId);
    root.classList.add('is-looking');
  };

  const onPointerMove = (event: PointerEvent): void => {
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
    scene.lookBy(event.clientX - lastX, event.clientY - lastY);
    lastX = event.clientX;
    lastY = event.clientY;
  };

  const endLook = (event: PointerEvent): void => {
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
    selectedSurface = hit.surface;
    root.classList.add('has-selected-surface');
    event.preventDefault();
    void hit.surface.wheel(hit.u, hit.v, event.deltaX, event.deltaY).catch(reportInputError);
  };

  const onContextMenu = (event: MouseEvent): void => {
    if (scene.hitTestApplicationSurface(event.clientX, event.clientY)) event.preventDefault();
  };

  const onKeyDown = (event: KeyboardEvent): void => {
    if (event.target instanceof HTMLButtonElement) return;
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
    scene.move(...direction);
  };

  const onKeyUp = (event: KeyboardEvent): void => {
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

  const arrivalTimer = window.setTimeout(
    () => root.classList.remove('workspace-arrival'),
    1200,
  );

  return {
    destroy(): void {
      unsubscribe();
      socket.close();
      welcome.destroy();
      scene.dispose();
      window.clearTimeout(arrivalTimer);
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
