import * as THREE from 'three';
import type { WorkspaceEntity } from '@workspace/world-schema';
import { RendererRegistry } from './RendererRegistry.ts';

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

export class WorkspaceScene {
  readonly #scene = new THREE.Scene();
  readonly #camera = new THREE.PerspectiveCamera(52, 1, 0.05, 160);
  readonly #renderer: THREE.WebGLRenderer;
  readonly #registry: RendererRegistry;
  readonly #entities = new Map<string, THREE.Object3D>();
  readonly #resizeObserver: ResizeObserver;
  #yaw = 0;
  #pitch = 0;

  constructor(root: HTMLElement, registry = new RendererRegistry()) {
    this.#registry = registry;
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

    this.#renderer = new THREE.WebGLRenderer({
      antialias: true,
      powerPreference: 'high-performance',
    });
    this.#renderer.domElement.className = 'workspace-canvas';
    this.#renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.#renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.#renderer.toneMappingExposure = 0.92;
    this.#renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.#renderer.xr.enabled = true;
    root.append(this.#renderer.domElement);

    this.#buildPlace();
    this.#resizeObserver = new ResizeObserver(() => this.resize());
    this.#resizeObserver.observe(root);
    this.resize();
    this.#renderer.setAnimationLoop(() => this.#renderer.render(this.#scene, this.#camera));
  }

  upsert(entity: WorkspaceEntity): void {
    let object = this.#entities.get(entity.id);
    if (!object) {
      object = this.#createEntityObject(entity);
      object.name = `entity:${entity.id}`;
      this.#entities.set(entity.id, object);
      this.#scene.add(object);
    }

    const { position, rotation, size } = entity.presentation;
    object.position.set(position.x, position.y, position.z);
    object.quaternion.set(rotation.x, rotation.y, rotation.z, rotation.w);
    object.scale.set(size.x, size.y, size.z);
    object.visible = true;
  }

  remove(entityId: string): void {
    const object = this.#entities.get(entityId);
    if (!object) return;

    this.#scene.remove(object);
    object.traverse((child) => {
      if (!(child instanceof THREE.Mesh)) return;
      child.geometry.dispose();
      const materials = Array.isArray(child.material) ? child.material : [child.material];
      for (const material of materials) material.dispose();
    });
    this.#entities.delete(entityId);
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

  #createEntityObject(entity: WorkspaceEntity): THREE.Object3D {
    const descriptor = this.#registry.resolve(entity.kind);
    if (descriptor.kind === 'application-surface') {
      return new THREE.Mesh(
        new THREE.BoxGeometry(1.6, 0.95, 0.035),
        new THREE.MeshStandardMaterial({
          color: PALETTE.deepSeam,
          roughness: 0.7,
          metalness: 0.1,
        }),
      );
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
