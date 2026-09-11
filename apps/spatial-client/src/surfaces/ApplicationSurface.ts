import * as THREE from 'three';
import type { PresentationState } from '@workspace/world-schema';
import type {
  KeyPhase,
  PointerButton,
  PointerPhase,
  SurfaceFrame,
  SurfaceCommandClient,
  SurfaceStream,
  WindowInputSink,
} from './SurfaceStream.ts';

export interface SurfaceTextureTarget {
  update(frame: SurfaceFrame): Promise<void> | void;
  markUnavailable(): void;
  setPresentation?(presentation: PresentationState): void;
  setCursor?(u: number | null, v?: number): void;
  dispose(): void;
}

export interface PresentationSink {
  setPresentation(presentation: PresentationState): Promise<unknown>;
}

export class ProtocolPresentationSink implements PresentationSink {
  readonly #client: SurfaceCommandClient;
  readonly #entityId: string;

  constructor(client: SurfaceCommandClient, entityId: string) {
    this.#client = client;
    this.#entityId = entityId;
  }

  setPresentation(presentation: PresentationState): Promise<unknown> {
    return this.#client.sendCommand('entity.setPresentation', this.#entityId, presentation);
  }
}

export type ApplicationSurfaceOptions = {
  frameIntervalMs?: number;
  retryIntervalMs?: number;
  inputSink?: WindowInputSink;
  initialPresentation?: PresentationState;
  presentationSink?: PresentationSink;
  /** A null value represents a durable, selectable display surface with no window bound. */
  boundWindowId?: string | null;
};

export class ThreeSurfaceTextureTarget implements SurfaceTextureTarget {
  readonly object: THREE.Mesh<THREE.PlaneGeometry, THREE.MeshBasicMaterial>;
  readonly #cursor: THREE.Mesh<THREE.CircleGeometry, THREE.MeshBasicMaterial>;
  #bitmap: ImageBitmap | null = null;
  #disposed = false;
  #cursorVisible = false;

  constructor() {
    this.object = new THREE.Mesh(
      new THREE.PlaneGeometry(1, 1),
      new THREE.MeshBasicMaterial({
        color: 0x162226,
        side: THREE.DoubleSide,
        toneMapped: false,
      }),
    );
    this.#cursor = new THREE.Mesh(
      new THREE.CircleGeometry(0.018, 20),
      new THREE.MeshBasicMaterial({
        color: 0xf0eee6,
        transparent: true,
        opacity: 0.92,
        depthTest: false,
        toneMapped: false,
      }),
    );
    this.#cursor.position.z = 0.02;
    this.#cursor.visible = false;
    this.#cursor.renderOrder = 2;
    this.object.add(this.#cursor);
  }

  get cursorVisible(): boolean {
    return this.#cursorVisible;
  }

  get cursorPosition(): Readonly<{ x: number; y: number }> {
    return { x: this.#cursor.position.x, y: this.#cursor.position.y };
  }

  setCursor(uv: Readonly<{ u: number; v: number }> | null): void;
  setCursor(u: number | null, v?: number): void;
  setCursor(
    uvOrU: Readonly<{ u: number; v: number }> | number | null,
    v?: number,
  ): void {
    if (this.#disposed) return;
    if (uvOrU === null) {
      this.#cursorVisible = false;
      this.#cursor.visible = false;
      return;
    }
    const u = typeof uvOrU === 'number' ? uvOrU : uvOrU.u;
    const nextV = typeof uvOrU === 'number' ? v : uvOrU.v;
    if (typeof u !== 'number' || typeof nextV !== 'number' || !Number.isFinite(u) || !Number.isFinite(nextV)) {
      this.#cursorVisible = false;
      this.#cursor.visible = false;
      return;
    }
    this.#cursor.position.x = u - 0.5;
    this.#cursor.position.y = nextV - 0.5;
    this.#cursorVisible = true;
    this.#cursor.visible = true;
  }

  async update(frame: SurfaceFrame): Promise<void> {
    if (this.#disposed) return;
    const bytes = decodeBase64(frame.dataBase64);
    const bitmap = await createImageBitmap(
      new Blob([bytes], { type: frame.mimeType }),
      { imageOrientation: 'flipY' },
    );
    if (this.#disposed) {
      bitmap.close();
      return;
    }

    const texture = new THREE.Texture(bitmap);
    texture.colorSpace = THREE.SRGBColorSpace;
    texture.needsUpdate = true;

    this.#disposeTexture();
    this.#bitmap = bitmap;
    this.object.material.map = texture;
    this.object.material.color.setHex(0xffffff);
    this.object.material.needsUpdate = true;
  }

  markUnavailable(): void {
    if (this.#disposed) return;
    this.#disposeTexture();
    this.object.material.map = null;
    this.object.material.color.setHex(0x162226);
    this.object.material.needsUpdate = true;
  }

  setPresentation(presentation: PresentationState): void {
    const { position, rotation, size } = presentation;
    this.object.position.set(position.x, position.y, position.z);
    this.object.quaternion.set(rotation.x, rotation.y, rotation.z, rotation.w);
    this.object.scale.set(size.x, size.y, size.z);
  }

  dispose(): void {
    if (this.#disposed) return;
    this.#disposed = true;
    this.#cursorVisible = false;
    this.#cursor.visible = false;
    this.#cursor.geometry.dispose();
    this.#cursor.material.dispose();
    this.object.remove(this.#cursor);
    this.#disposeTexture();
  }

  #disposeTexture(): void {
    this.object.material.map?.dispose();
    this.object.material.map = null;
    this.#bitmap?.close();
    this.#bitmap = null;
  }
}

export class ApplicationSurface {
  readonly #stream: SurfaceStream;
  readonly #textureTarget: SurfaceTextureTarget;
  readonly #frameIntervalMs: number;
  readonly #retryIntervalMs: number;
  readonly #inputSink: WindowInputSink | null;
  readonly #presentationSink: PresentationSink | null;
  readonly #boundWindowId: string | null;
  #presentation: PresentationState | null;
  #displayedPresentation: PresentationState | null;
  #transientPresentation: PresentationState | null = null;
  #pendingPresentationCount = 0;
  #presentationQueue: Promise<void> = Promise.resolve();
  #opened = false;
  #disposed = false;
  #timer: ReturnType<typeof setTimeout> | null = null;

  constructor(
    stream: SurfaceStream,
    textureTarget: SurfaceTextureTarget,
    options: ApplicationSurfaceOptions = {},
  ) {
    this.#stream = stream;
    this.#textureTarget = textureTarget;
    this.#frameIntervalMs = options.frameIntervalMs ?? 80;
    this.#retryIntervalMs = options.retryIntervalMs ?? 1_000;
    this.#inputSink = options.inputSink ?? null;
    this.#presentationSink = options.presentationSink ?? null;
    this.#boundWindowId = options.boundWindowId === undefined ? 'bound' : options.boundWindowId;
    this.#presentation = options.initialPresentation ?? null;
    this.#displayedPresentation = this.#presentation;
    this.#applyPresentation();
  }

  get presentation(): PresentationState {
    if (!this.#presentation) throw new Error('Presentation is not configured for this surface.');
    return this.#presentation;
  }

  get displayedPresentation(): PresentationState {
    if (!this.#displayedPresentation) {
      throw new Error('Presentation is not configured for this surface.');
    }
    return this.#displayedPresentation;
  }

  get isBound(): boolean {
    return this.#boundWindowId !== null;
  }

  async renderNextFrame(): Promise<boolean> {
    if (this.#disposed || !this.isBound) return false;
    if (!this.#opened) {
      await this.#stream.open();
      this.#opened = true;
    }

    const frame = await this.#stream.readFrame();
    if (!frame) return false;
    await this.#textureTarget.update(frame);
    return true;
  }

  start(): void {
    if (this.#disposed || this.#timer || !this.isBound) return;

    const poll = async (): Promise<void> => {
      try {
        await this.renderNextFrame();
      } catch {
        this.#textureTarget.markUnavailable();
        try {
          await this.#stream.close();
        } catch {
          // A disconnected transport is already unavailable; retry from semantic identity later.
        }
        this.#opened = false;
        if (!this.#disposed) {
          this.#timer = setTimeout(poll, this.#retryIntervalMs);
        }
        return;
      }
      if (!this.#disposed) {
        this.#timer = setTimeout(poll, this.#frameIntervalMs);
      }
    };

    void poll();
  }

  pointer(
    phase: PointerPhase,
    u: number,
    v: number,
    button?: PointerButton,
  ): Promise<unknown> {
    if (!this.isBound) return Promise.resolve();
    return this.#requireInput().pointer(phase, u, v, button);
  }

  wheel(u: number, v: number, deltaX: number, deltaY: number): Promise<unknown> {
    if (!this.isBound) return Promise.resolve();
    return this.#requireInput().wheel(u, v, deltaX, deltaY);
  }

  key(phase: KeyPhase, key: string): Promise<unknown> {
    if (!this.isBound) return Promise.resolve();
    return this.#requireInput().key(phase, key);
  }

  text(value: string): Promise<unknown> {
    if (!this.isBound) return Promise.resolve();
    return this.#requireInput().text(value);
  }

  setCursor(u: number | null, v?: number): void {
    this.#textureTarget.setCursor?.(u, v);
  }

  setTransientPresentation(presentation: PresentationState | null): void {
    this.#transientPresentation = presentation;
    this.#applyPresentation();
  }

  previewPresentation(presentation: PresentationState): void {
    this.#displayedPresentation = presentation;
    this.#applyPresentation();
  }

  acceptAuthoritativePresentation(presentation: PresentationState): void {
    this.#presentation = presentation;
    if (this.#pendingPresentationCount === 0) this.previewPresentation(presentation);
  }

  async commitPresentation(presentation: PresentationState): Promise<unknown> {
    if (!this.#presentationSink || !this.#presentation) {
      throw new Error('Presentation persistence is not configured for this surface.');
    }

    const sink = this.#presentationSink;
    this.#pendingPresentationCount += 1;
    this.previewPresentation(presentation);
    const operation = this.#presentationQueue.then(async () => {
      try {
        const result = await sink.setPresentation(presentation);
        this.#pendingPresentationCount -= 1;
        return result;
      } catch (error) {
        this.#pendingPresentationCount -= 1;
        if (this.#pendingPresentationCount === 0 && this.#presentation) {
          this.previewPresentation(this.#presentation);
        }
        throw error;
      }
    });
    this.#presentationQueue = operation.then(() => undefined, () => undefined);
    return operation;
  }

  async dispose(): Promise<void> {
    if (this.#disposed) return;
    this.#disposed = true;
    if (this.#timer) clearTimeout(this.#timer);
    this.#timer = null;
    try {
      if (this.isBound) await this.#stream.close();
    } finally {
      this.#textureTarget.dispose();
    }
  }

  #applyPresentation(): void {
    const presentation = this.#transientPresentation ?? this.#displayedPresentation;
    if (presentation) this.#textureTarget.setPresentation?.(presentation);
  }

  #requireInput(): WindowInputSink {
    if (!this.#inputSink) throw new Error('Window input is not configured for this surface.');
    return this.#inputSink;
  }
}

function decodeBase64(value: string): Uint8Array<ArrayBuffer> {
  const binary = atob(value);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index++) {
    bytes[index] = binary.charCodeAt(index);
  }
  return bytes;
}
