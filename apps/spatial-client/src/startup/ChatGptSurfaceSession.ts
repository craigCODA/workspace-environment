export type ChatGptSurfaceSessionState = Readonly<{
  available: boolean;
  docked: boolean;
  collapsed: boolean;
  canFocus: boolean;
}>;

export type ChatGptSurfaceSessionOptions = Readonly<{
  setDocked(surfaceEntityId: string, docked: boolean): void;
  setCollapsed(surfaceEntityId: string, collapsed: boolean): void;
  focusWindow(windowEntityId: string): Promise<void>;
}>;

export class ChatGptSurfaceSession {
  readonly #options: ChatGptSurfaceSessionOptions;
  #surfaceEntityId: string | null = null;
  #windowEntityId: string | null = null;
  #docked = false;
  #collapsed = false;

  constructor(options: ChatGptSurfaceSessionOptions) {
    this.#options = options;
  }

  get state(): ChatGptSurfaceSessionState {
    return {
      available: this.#surfaceEntityId !== null,
      docked: this.#surfaceEntityId !== null && this.#docked,
      collapsed: this.#surfaceEntityId !== null && this.#collapsed,
      canFocus: this.#windowEntityId !== null,
    };
  }

  attach(surfaceEntityId: string | null, windowEntityId: string | null): void {
    this.#surfaceEntityId = surfaceEntityId;
    this.#windowEntityId = windowEntityId;
    this.#docked = false;
    this.#collapsed = false;
    if (!surfaceEntityId) return;
    this.#docked = true;
    this.#options.setDocked(surfaceEntityId, true);
    this.#options.setCollapsed(surfaceEntityId, false);
  }

  dock(): void {
    if (!this.#surfaceEntityId || this.#docked) return;
    this.#docked = true;
    this.#options.setDocked(this.#surfaceEntityId, true);
  }

  undock(): void {
    if (!this.#surfaceEntityId || !this.#docked) return;
    this.#docked = false;
    this.#options.setDocked(this.#surfaceEntityId, false);
  }

  collapse(): void {
    if (!this.#surfaceEntityId || this.#collapsed) return;
    this.#collapsed = true;
    this.#options.setCollapsed(this.#surfaceEntityId, true);
  }

  show(): void {
    if (!this.#surfaceEntityId || !this.#collapsed) return;
    this.#collapsed = false;
    this.#options.setCollapsed(this.#surfaceEntityId, false);
  }

  async focus(): Promise<void> {
    if (!this.#windowEntityId) return;
    await this.#options.focusWindow(this.#windowEntityId);
  }
}
