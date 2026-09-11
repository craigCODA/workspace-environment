export type ChatGptSurfaceSessionState = Readonly<{
  available: boolean;
  docked: boolean;
  collapsed: boolean;
  canFocus: boolean;
}>;

export type ChatGptSurfaceSessionOptions = Readonly<{
  setDocked(surfaceEntityId: string, docked: boolean): void;
  setCollapsed(surfaceEntityId: string, collapsed: boolean): void;
  isDocked(surfaceEntityId: string): boolean;
  isCollapsed(surfaceEntityId: string): boolean;
  focusWindow(windowEntityId: string): Promise<void>;
}>;

export class ChatGptSurfaceSession {
  readonly #options: ChatGptSurfaceSessionOptions;
  #surfaceEntityId: string | null = null;
  #windowEntityId: string | null = null;

  constructor(options: ChatGptSurfaceSessionOptions) {
    this.#options = options;
  }

  get state(): ChatGptSurfaceSessionState {
    const surfaceEntityId = this.#surfaceEntityId;
    return {
      available: surfaceEntityId !== null,
      docked: surfaceEntityId !== null && this.#options.isDocked(surfaceEntityId),
      collapsed: surfaceEntityId !== null && this.#options.isCollapsed(surfaceEntityId),
      canFocus: this.#windowEntityId !== null,
    };
  }

  attach(surfaceEntityId: string | null, windowEntityId: string | null): void {
    this.#surfaceEntityId = surfaceEntityId;
    this.#windowEntityId = windowEntityId;
    if (!surfaceEntityId) return;
    this.#options.setDocked(surfaceEntityId, true);
    this.#options.setCollapsed(surfaceEntityId, false);
  }

  dock(): void {
    const surfaceEntityId = this.#surfaceEntityId;
    if (!surfaceEntityId || this.#options.isDocked(surfaceEntityId)) return;
    this.#options.setDocked(surfaceEntityId, true);
  }

  undock(): void {
    const surfaceEntityId = this.#surfaceEntityId;
    if (!surfaceEntityId || !this.#options.isDocked(surfaceEntityId)) return;
    this.#options.setDocked(surfaceEntityId, false);
  }

  collapse(): void {
    const surfaceEntityId = this.#surfaceEntityId;
    if (!surfaceEntityId || this.#options.isCollapsed(surfaceEntityId)) return;
    this.#options.setCollapsed(surfaceEntityId, true);
  }

  show(): void {
    const surfaceEntityId = this.#surfaceEntityId;
    if (!surfaceEntityId || !this.#options.isCollapsed(surfaceEntityId)) return;
    this.#options.setCollapsed(surfaceEntityId, false);
  }

  async focus(): Promise<void> {
    if (!this.#windowEntityId) return;
    await this.#options.focusWindow(this.#windowEntityId);
  }
}
