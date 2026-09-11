export type SurfaceViewMode = 'spatial' | 'docked' | 'collapsed';

export type SurfaceDockControlAction = 'dock' | 'undock' | 'collapse' | 'show';

export type SurfaceDockControlModel = Readonly<{
  mode: SurfaceViewMode;
  primary: Readonly<{
    label: 'Dock' | 'Undock';
    action: 'dock' | 'undock';
    disabled: boolean;
  }>;
  visibility: Readonly<{
    label: 'Collapse' | 'Show';
    action: 'collapse' | 'show';
  }>;
}>;

export function surfaceDockControlState(mode: SurfaceViewMode): SurfaceDockControlModel {
  if (mode === 'docked') {
    return {
      mode,
      primary: { label: 'Undock', action: 'undock', disabled: false },
      visibility: { label: 'Collapse', action: 'collapse' },
    };
  }
  if (mode === 'collapsed') {
    return {
      mode,
      primary: { label: 'Dock', action: 'dock', disabled: true },
      visibility: { label: 'Show', action: 'show' },
    };
  }
  return {
    mode,
    primary: { label: 'Dock', action: 'dock', disabled: false },
    visibility: { label: 'Collapse', action: 'collapse' },
  };
}

export type SurfaceDockControlsOptions = Readonly<{
  label: string;
  onDock(): void;
  onUndock(): void;
  onCollapse(): void;
  onShow(): void;
  onFocus?(): void | Promise<void>;
}>;

export class SurfaceDockControls {
  readonly #element: HTMLElement;
  readonly #primaryButton: HTMLButtonElement;
  readonly #visibilityButton: HTMLButtonElement;
  readonly #focusButton: HTMLButtonElement | null;
  readonly #options: SurfaceDockControlsOptions;
  #mode: SurfaceViewMode = 'spatial';

  constructor(root: HTMLElement, options: SurfaceDockControlsOptions) {
    this.#options = options;
    this.#element = document.createElement('nav');
    this.#element.className = 'surface-dock-controls chatgpt-surface-controls';
    this.#element.setAttribute('aria-label', `${options.label} surface controls`);
    this.#element.hidden = true;

    const label = document.createElement('span');
    label.className = 'surface-dock-label chatgpt-surface-label';
    label.textContent = options.label;

    this.#primaryButton = document.createElement('button');
    this.#primaryButton.type = 'button';
    this.#primaryButton.addEventListener('click', () => {
      const action = surfaceDockControlState(this.#mode).primary.action;
      if (action === 'dock') this.#options.onDock();
      else this.#options.onUndock();
    });

    this.#visibilityButton = document.createElement('button');
    this.#visibilityButton.type = 'button';
    this.#visibilityButton.addEventListener('click', () => {
      const action = surfaceDockControlState(this.#mode).visibility.action;
      if (action === 'collapse') this.#options.onCollapse();
      else this.#options.onShow();
    });

    this.#focusButton = options.onFocus ? document.createElement('button') : null;
    if (this.#focusButton) {
      this.#focusButton.type = 'button';
      this.#focusButton.textContent = 'Focus';
      this.#focusButton.addEventListener('click', () => {
        void this.#options.onFocus?.();
      });
    }

    this.#element.append(label, this.#primaryButton, this.#visibilityButton);
    if (this.#focusButton) this.#element.append(this.#focusButton);
    root.append(this.#element);
    this.#render();
  }

  setVisible(visible: boolean): void {
    this.#element.hidden = !visible;
  }

  setMode(mode: SurfaceViewMode): void {
    this.#mode = mode;
    this.#render();
  }

  setFocusEnabled(enabled: boolean): void {
    if (this.#focusButton) this.#focusButton.disabled = !enabled;
  }

  destroy(): void {
    this.#element.remove();
  }

  #render(): void {
    const state = surfaceDockControlState(this.#mode);
    this.#primaryButton.textContent = state.primary.label;
    this.#primaryButton.disabled = state.primary.disabled;
    this.#primaryButton.setAttribute('aria-pressed', String(this.#mode === 'docked'));
    this.#visibilityButton.textContent = state.visibility.label;
    this.#visibilityButton.setAttribute('aria-pressed', String(this.#mode === 'collapsed'));
  }
}
