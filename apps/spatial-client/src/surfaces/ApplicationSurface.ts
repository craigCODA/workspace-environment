import * as THREE from 'three';
import type { SurfaceFrame, SurfaceStream } from './SurfaceStream.ts';

export interface SurfaceTextureTarget {
  update(frame: SurfaceFrame): Promise<void> | void;
  markUnavailable(): void;
  dispose(): void;
}

export type ApplicationSurfaceOptions = {
  frameIntervalMs?: number;
  retryIntervalMs?: number;
};

export class ThreeSurfaceTextureTarget implements SurfaceTextureTarget {
  readonly object: THREE.Mesh<THREE.PlaneGeometry, THREE.MeshBasicMaterial>;
  #bitmap: ImageBitmap | null = null;
  #disposed = false;

  constructor() {
    this.object = new THREE.Mesh(
      new THREE.PlaneGeometry(1, 1),
      new THREE.MeshBasicMaterial({
        color: 0x162226,
        side: THREE.DoubleSide,
        toneMapped: false,
      }),
    );
  }

  async update(frame: SurfaceFrame): Promise<void> {
    if (this.#disposed) return;
    const bytes = decodeBase64(frame.dataBase64);
    const bitmap = await createImageBitmap(new Blob([bytes], { type: frame.mimeType }));
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

  dispose(): void {
    if (this.#disposed) return;
    this.#disposed = true;
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
  }

  async renderNextFrame(): Promise<boolean> {
    if (this.#disposed) return false;
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
    if (this.#disposed || this.#timer) return;

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

  async dispose(): Promise<void> {
    if (this.#disposed) return;
    this.#disposed = true;
    if (this.#timer) clearTimeout(this.#timer);
    this.#timer = null;
    try {
      await this.#stream.close();
    } finally {
      this.#textureTarget.dispose();
    }
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
