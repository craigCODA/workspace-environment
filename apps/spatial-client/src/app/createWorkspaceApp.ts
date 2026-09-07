import type { ProtocolEnvelope } from '@workspace/protocol';
import { WorkspaceSocket } from '../protocol/WorkspaceSocket.ts';
import { WorldReplica } from '../replica/WorldReplica.ts';
import { WorkspaceScene } from '../rendering/WorkspaceScene.ts';
import { WelcomeSequence } from '../onboarding/WelcomeSequence.ts';

export type WorkspaceApp = {
  destroy(): void;
};

export function createWorkspaceApp(root: HTMLElement): WorkspaceApp {
  root.replaceChildren();
  root.className = 'workspace-root workspace-arrival';
  root.tabIndex = 0;

  const sceneRoot = document.createElement('div');
  sceneRoot.className = 'scene-root';
  sceneRoot.setAttribute('aria-hidden', 'true');
  root.append(sceneRoot);

  const scene = new WorkspaceScene(sceneRoot);
  const replica = new WorldReplica();
  const socket = new WorkspaceSocket();
  const renderedIds = new Set<string>();

  const syncScene = (): void => {
    const currentIds = new Set(replica.entities.map((entity) => entity.id));
    for (const entityId of renderedIds) {
      if (!currentIds.has(entityId)) {
        scene.remove(entityId);
        renderedIds.delete(entityId);
      }
    }

    for (const entity of replica.entities) {
      scene.upsert(entity);
      renderedIds.add(entity.id);
    }
  };

  const unsubscribe = socket.subscribe((envelope: ProtocolEnvelope) => {
    if (envelope.type !== 'snapshot' && envelope.type !== 'event') return;
    replica.apply(envelope);
    syncScene();
  });

  const welcome = new WelcomeSequence(root, {
    onOpenApplication: async (displayName) => {
      await socket.sendCommand('application.launch', displayName);
    },
  });

  const reticle = document.createElement('div');
  reticle.className = 'reticle';
  reticle.setAttribute('aria-hidden', 'true');

  const movementHint = document.createElement('p');
  movementHint.className = 'movement-hint';
  movementHint.textContent = 'Drag to look around. Use W A S D to move.';
  root.append(reticle, movementHint);

  let pointerId: number | null = null;
  let lastX = 0;
  let lastY = 0;

  const onPointerDown = (event: PointerEvent): void => {
    if ((event.target as Element).closest('button')) return;
    pointerId = event.pointerId;
    lastX = event.clientX;
    lastY = event.clientY;
    root.setPointerCapture(event.pointerId);
    root.classList.add('is-looking');
  };

  const onPointerMove = (event: PointerEvent): void => {
    if (event.pointerId !== pointerId) return;
    scene.lookBy(event.clientX - lastX, event.clientY - lastY);
    lastX = event.clientX;
    lastY = event.clientY;
  };

  const endLook = (event: PointerEvent): void => {
    if (event.pointerId !== pointerId) return;
    pointerId = null;
    root.classList.remove('is-looking');
  };

  const onKeyDown = (event: KeyboardEvent): void => {
    if (event.target instanceof HTMLButtonElement) return;
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

  root.addEventListener('pointerdown', onPointerDown);
  root.addEventListener('pointermove', onPointerMove);
  root.addEventListener('pointerup', endLook);
  root.addEventListener('pointercancel', endLook);
  root.addEventListener('keydown', onKeyDown);
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
      root.removeEventListener('keydown', onKeyDown);
      root.replaceChildren();
    },
  };
}
