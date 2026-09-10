import * as THREE from 'three';
import { displayedWindowId, type PresentationState, type WorkspaceEntity } from '@workspace/world-schema';
import { RendererRegistry } from './RendererRegistry.ts';
import type { CameraPose } from '../navigation/CameraNavigator.ts';
import type { SceneSnapshot } from '../navigation/SceneCommandController.ts';
import type { SurfaceStream, WindowInputSink } from '../surfaces/SurfaceStream.ts';
import {
  ApplicationSurface,
  ThreeSurfaceTextureTarget,
  type PresentationSink,
} from '../surfaces/ApplicationSurface.ts';

type Point = Readonly<{ x: number; y: number; z: number }>;

export const INITIAL_CAMERA_POSITION: Point = Object.freeze({ x: 0, y: 1.65, z: 4 });
export const INITIAL_VIEW_TARGET: Point = Object.freeze({ x: 0, y: 1.3, z: -2 });
export const FIRST_WORK_AREA_POSITION: Point = Object.freeze({ x: 0, y: 0, z: 8 });

const PALETTE = {
  foundryBlue: 0x26373b,
  pouredSlate: 0x4d5a56,
  distanceFog: 0x8fa19d,
  chalk: 0xf0eee6,
  kilnCopper: 0xc8784e,
  deepSeam: 0x162226,
} as const;

export function calculatePlanarMovement(
  yaw: number,
  forward: number,
  right: number,
): { x: number; z: number } {
  const sin = Math.sin(yaw);
  const cos = Math.cos(yaw);
  return {
    x: right * cos - forward * sin,
    z: -right * sin - forward * cos,
  };
}

export type SurfaceBinding = Readonly<{
  captureWindowId: string | null;
  inputWindowId: string | null;
  presentationSurfaceId: string;
}>;

/** Splits transient window ownership from durable display-surface presentation. */
export function surfaceBindingFor(entity: WorkspaceEntity): SurfaceBinding {
  const windowId = entity.relationships
    .find((relationship) => relationship.type === 'displays')
    ?.targetId ?? null;
  return {
    captureWindowId: windowId,
    inputWindowId: windowId,
    presentationSurfaceId: entity.id,
  };
}

export function surfaceBindingNeedsReplacement(
  currentWindowId: string | null | undefined,
  entity: WorkspaceEntity,
): boolean {
  return currentWindowId !== surfaceBindingFor(entity).captureWindowId;
}

/** Owns the one-time disposal decision when a display surface changes window bindings. */
export class SurfaceBindingLifecycle {
  #windowId: string | null;

  constructor(entity: WorkspaceEntity) {
    this.#windowId = surfaceBindingFor(entity).captureWindowId;
  }

  rebind(entity: WorkspaceEntity, dispose: () => void): boolean {
    const nextWindowId = surfaceBindingFor(entity).captureWindowId;
    if (this.#windowId === nextWindowId) return false;
    this.#windowId = nextWindowId;
    dispose();
    return true;
  }
}

export type ApplicationSurfaceHit = Readonly<{
  entityId: string;
  surface: ApplicationSurface;
  u: number;
  v: number;
}>;

export type WorkspaceSceneTestOptions = Readonly<{
  renderer?: THREE.WebGLRenderer;
  resizeObserver?: ResizeObserver;
}>;

export class WorkspaceScene {
  readonly #scene = new THREE.Scene();
  readonly #camera = new THREE.PerspectiveCamera(52, 1, 0.05, 160);
  readonly #renderer: THREE.WebGLRenderer;
  readonly #registry: RendererRegistry;
  readonly #surfaceStreamFactory: ((entityId: string) => SurfaceStream) | null;
  readonly #inputSinkFactory: ((entityId: string) => WindowInputSink) | null;
  readonly #presentationSinkFactory: ((entityId: string) => PresentationSink) | null;
  readonly #raycaster = new THREE.Raycaster();
  readonly #entities = new Map<string, THREE.Object3D>();
  readonly #entityStates = new Map<string, WorkspaceEntity>();
  readonly #resizeObserver: ResizeObserver;
  #yaw = 0;
  #pitch = 0;

  constructor(
    root: HTMLElement,
    registry = new RendererRegistry(),
    surfaceStreamFactory: ((entityId: string) => SurfaceStream) | null = null,
    inputSinkFactory: ((entityId: string) => WindowInputSink) | null = null,
    presentationSinkFactory: ((entityId: string) => PresentationSink) | null = null,
    testOptions: WorkspaceSceneTestOptions = {},
  ) {
    this.#registry = registry;
    this.#surfaceStreamFactory = surfaceStreamFactory;
    this.#inputSinkFactory = inputSinkFactory;
    this.#presentationSinkFactory = presentationSinkFactory;
    this.#scene.background = new THREE.Color(PALETTE.foundryBlue);
    this.#scene.fog = new THREE.Fog(PALETTE.distanceFog, 18, 74);

    this.#camera.position.set(
      INITIAL_CAMERA_POSITION.x,
      INITIAL_CAMERA_POSITION.y,
      INITIAL_CAMERA_POSITION.z,
    );
    this.#camera.lookAt(
      INITIAL_VIEW_TARGET.x,
      INITIAL_VIEW_TARGET.y,
      INITIAL_VIEW_TARGET.z,
    );
    this.#camera.rotation.order = 'YXZ';
    this.#pitch = this.#camera.rotation.x;
    this.#yaw = this.#camera.rotation.y;

    this.#renderer = testOptions.renderer ?? new THREE.WebGLRenderer({
      antialias: true,
      powerPreference: 'high-performance',
    });
    this.#renderer.domElement.className = 'workspace-canvas';
    this.#renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.#renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.#renderer.toneMappingExposure = 0.92;
    this.#renderer.setPixelRatio(Math.min(typeof window === 'undefined' ? 1 : window.devicePixelRatio, 2));
    this.#renderer.xr.enabled = true;
    root.append(this.#renderer.domElement);

    this.#buildPlace();
    this.#resizeObserver = testOptions.resizeObserver ?? new ResizeObserver(() => this.resize());
    this.#resizeObserver.observe(root);
    this.resize();
    this.#renderer.setAnimationLoop(() => this.#renderer.render(this.#scene, this.#camera));
  }

  upsert(entity: WorkspaceEntity): void {
    this.#entityStates.set(entity.id, entity);
    let object = this.#entities.get(entity.id);
    const lifecycle = object?.userData.surfaceBindingLifecycle;
    if (object && this.#registry.resolve(entity.kind).kind === 'application-surface') {
      const shouldReplace = lifecycle instanceof SurfaceBindingLifecycle
        ? lifecycle.rebind(entity, () => {
          this.#scene.remove(object!);
          this.#disposeEntityObject(object!);
          this.#entities.delete(entity.id);
        })
        : surfaceBindingNeedsReplacement(object.userData.displayedWindowId, entity);
      if (shouldReplace) object = undefined;
    }
    if (!object) {
      object = this.#createEntityObject(entity);
      object.name = `entity:${entity.id}`;
      object.userData.entityId = entity.id;
      this.#entities.set(entity.id, object);
      this.#scene.add(object);
    }

    const applicationSurface = object.userData.applicationSurface;
    if (applicationSurface instanceof ApplicationSurface) {
      applicationSurface.acceptAuthoritativePresentation(entity.presentation);
    } else {
      const { position, rotation, size } = entity.presentation;
      object.position.set(position.x, position.y, position.z);
      object.quaternion.set(rotation.x, rotation.y, rotation.z, rotation.w);
      object.scale.set(size.x, size.y, size.z);
    }
    object.visible = true;
  }

  remove(entityId: string): void {
    const object = this.#entities.get(entityId);
    if (!object) return;

    this.#scene.remove(object);
    this.#disposeEntityObject(object);
    this.#entities.delete(entityId);
    this.#entityStates.delete(entityId);
  }

  #disposeEntityObject(object: THREE.Object3D): void {
    const applicationSurface = object.userData.applicationSurface;
    if (applicationSurface instanceof ApplicationSurface) {
      void applicationSurface.dispose().catch(() => {
        // Scene teardown must remain safe if the host connection has already closed.
      });
    }
    object.traverse((child) => {
      if (!(child instanceof THREE.Mesh)) return;
      child.geometry.dispose();
      const materials = Array.isArray(child.material) ? child.material : [child.material];
      for (const material of materials) {
        if ('map' in material && material.map instanceof THREE.Texture) material.map.dispose();
        material.dispose();
      }
    });
  }

  getCameraPose(): CameraPose {
    return {
      position: {
        x: this.#camera.position.x,
        y: this.#camera.position.y,
        z: this.#camera.position.z,
      },
      yaw: this.#yaw,
      pitch: this.#pitch,
    };
  }

  setCameraPose(pose: CameraPose): void {
    this.#camera.position.set(pose.position.x, pose.position.y, pose.position.z);
    this.#yaw = pose.yaw;
    this.#pitch = THREE.MathUtils.clamp(pose.pitch, -Math.PI * 0.42, Math.PI * 0.42);
    this.#camera.rotation.set(this.#pitch, this.#yaw, 0);
  }

  snapshot(selectedEntityId: string | null = null): SceneSnapshot {
    return {
      camera: this.getCameraPose(),
      entities: [...this.#entityStates.values()].map((entity) => ({
        id: entity.id,
        kind: entity.kind,
        name: entity.name,
        presentation: this.presentationFor(entity.id) ?? entity.presentation,
        relationships: entity.relationships.map((relationship) => ({ ...relationship })),
        selected: entity.id === selectedEntityId,
      })),
    };
  }

  presentationFor(entityId: string): PresentationState | null {
    const object = this.#entities.get(entityId);
    const surface = object?.userData.applicationSurface;
    if (surface instanceof ApplicationSurface) return surface.displayedPresentation;
    return this.#entityStates.get(entityId)?.presentation ?? null;
  }

  async commitPresentation(entityId: string, presentation: PresentationState): Promise<void> {
    const object = this.#entities.get(entityId);
    const surface = object?.userData.applicationSurface;
    if (!(surface instanceof ApplicationSurface)) {
      throw new Error(`Entity is not a controllable application surface: ${entityId}`);
    }
    await surface.commitPresentation(presentation);
  }

  lookBy(deltaX: number, deltaY: number): void {
    this.#yaw -= deltaX * 0.003;
    this.#pitch -= deltaY * 0.003;
    this.#pitch = THREE.MathUtils.clamp(this.#pitch, -Math.PI * 0.42, Math.PI * 0.42);
    this.#camera.rotation.set(this.#pitch, this.#yaw, 0);
  }

  move(forward: number, right: number): void {
    const step = 0.2;
    const movement = calculatePlanarMovement(this.#yaw, forward, right);
    this.#camera.position.x += movement.x * step;
    this.#camera.position.z += movement.z * step;
  }

  hitTestApplicationSurface(
    clientX: number,
    clientY: number,
    requiredSurface?: ApplicationSurface,
  ): ApplicationSurfaceHit | null {
    const bounds = this.#renderer.domElement.getBoundingClientRect();
    if (bounds.width <= 0 || bounds.height <= 0) return null;

    this.#raycaster.setFromCamera(
      new THREE.Vector2(
        ((clientX - bounds.left) / bounds.width) * 2 - 1,
        -((clientY - bounds.top) / bounds.height) * 2 + 1,
      ),
      this.#camera,
    );

    const intersections = this.#raycaster.intersectObjects([...this.#entities.values()], true);
    for (const intersection of intersections) {
      let object: THREE.Object3D | null = intersection.object;
      while (object) {
        const surface = object.userData.applicationSurface;
        if (surface instanceof ApplicationSurface && intersection.uv) {
          if (requiredSurface && surface !== requiredSurface) break;
          const entityId = this.#entityIdForObject(object);
          if (!entityId) break;
          return { entityId, surface, u: intersection.uv.x, v: intersection.uv.y };
        }
        object = object.parent;
      }
    }

    return null;
  }

  resize(): void {
    const parent = this.#renderer.domElement.parentElement;
    if (!parent) return;

    const width = Math.max(parent.clientWidth, 1);
    const height = Math.max(parent.clientHeight, 1);
    this.#camera.aspect = width / height;
    this.#camera.updateProjectionMatrix();
    this.#renderer.setSize(width, height, false);
  }

  dispose(): void {
    this.#renderer.setAnimationLoop(null);
    this.#resizeObserver.disconnect();
    for (const entityId of [...this.#entities.keys()]) this.remove(entityId);
    this.#scene.traverse((child) => {
      if (child instanceof THREE.Mesh || child instanceof THREE.Line) {
        child.geometry.dispose();
        const materials = Array.isArray(child.material) ? child.material : [child.material];
        for (const material of materials) material.dispose();
      }
    });
    this.#renderer.dispose();
    this.#renderer.domElement.remove();
  }

  #buildPlace(): void {
    const hemisphere = new THREE.HemisphereLight(PALETTE.chalk, PALETTE.deepSeam, 2.15);
    this.#scene.add(hemisphere);

    const directional = new THREE.DirectionalLight(PALETTE.chalk, 1.7);
    directional.position.set(-4, 9, 3);
    this.#scene.add(directional);

    const floor = new THREE.Mesh(
      new THREE.CircleGeometry(58, 96),
      new THREE.MeshStandardMaterial({
        color: PALETTE.pouredSlate,
        roughness: 0.98,
        metalness: 0,
      }),
    );
    floor.name = 'workspace-floor';
    floor.rotation.x = -Math.PI / 2;
    floor.position.y = -0.04;
    this.#scene.add(floor);

    const guide = new THREE.Line(
      new THREE.BufferGeometry().setFromPoints([
        new THREE.Vector3(0, 0.015, INITIAL_CAMERA_POSITION.z + 0.4),
        new THREE.Vector3(0, 0.015, FIRST_WORK_AREA_POSITION.z - 0.8),
      ]),
      new THREE.LineBasicMaterial({ color: PALETTE.kilnCopper }),
    );
    guide.name = 'orientation-seam';
    this.#scene.add(guide);

    const workArea = new THREE.Group();
    workArea.name = 'first-work-area-behind-spawn';
    workArea.position.set(
      FIRST_WORK_AREA_POSITION.x,
      FIRST_WORK_AREA_POSITION.y,
      FIRST_WORK_AREA_POSITION.z,
    );

    const surface = new THREE.Mesh(
      new THREE.BoxGeometry(3.8, 0.13, 1.35),
      new THREE.MeshStandardMaterial({
        color: PALETTE.deepSeam,
        roughness: 0.82,
        metalness: 0.08,
      }),
    );
    surface.position.y = 0.92;
    workArea.add(surface);

    const threshold = new THREE.Mesh(
      new THREE.TorusGeometry(1.65, 0.032, 10, 96),
      new THREE.MeshBasicMaterial({ color: PALETTE.kilnCopper }),
    );
    threshold.position.set(0, 1.68, 0.72);
    workArea.add(threshold);

    const thresholdLight = new THREE.PointLight(PALETTE.kilnCopper, 18, 7, 2);
    thresholdLight.position.set(0, 1.25, 0.2);
    workArea.add(thresholdLight);
    this.#scene.add(workArea);
  }

  #entityIdForObject(object: THREE.Object3D): string | null {
    let current: THREE.Object3D | null = object;
    while (current) {
      if (typeof current.userData.entityId === 'string') return current.userData.entityId;
      current = current.parent;
    }
    return null;
  }

  #createEntityObject(entity: WorkspaceEntity): THREE.Object3D {
    const descriptor = this.#registry.resolve(entity.kind);
    if (descriptor.kind === 'application-surface') {
      const binding = surfaceBindingFor(entity);
      const windowId = binding.captureWindowId;
      const textureTarget = new ThreeSurfaceTextureTarget();
      const surface = new ApplicationSurface(
        windowId && this.#surfaceStreamFactory
          ? this.#surfaceStreamFactory(windowId)
          : { async open() {}, async readFrame() { return null; }, async close() {} },
        textureTarget,
        {
          inputSink: binding.inputWindowId ? this.#inputSinkFactory?.(binding.inputWindowId) : undefined,
          boundWindowId: windowId,
          initialPresentation: entity.presentation,
          presentationSink: this.#presentationSinkFactory?.(binding.presentationSurfaceId),
        },
      );
      textureTarget.object.userData.applicationSurface = surface;
      textureTarget.object.userData.displayedWindowId = windowId;
      textureTarget.object.userData.surfaceBindingLifecycle = new SurfaceBindingLifecycle(entity);
      if (windowId && this.#surfaceStreamFactory) {
        surface.start();
      }
      return textureTarget.object;
    }

    return new THREE.Mesh(
      new THREE.OctahedronGeometry(0.16, 0),
      new THREE.MeshStandardMaterial({
        color: PALETTE.chalk,
        roughness: 0.65,
      }),
    );
  }
}
