export const WELCOME_COPY = Object.freeze([
  'Welcome to your workspace environment.',
  "This is the place where we'll build the way you work.",
  'The applications, files, projects, and tools on your computer can exist here, but they do not have to look or behave like a traditional desktop.',
  'This space is intentionally unfinished.',
  'Look around.',
  "When you're ready, we'll start by bringing something from your computer into the workspace.",
]);

type WelcomeSequenceOptions = {
  onOpenApplication(displayName: string): Promise<void>;
};

export class WelcomeSequence {
  readonly #element: HTMLElement;
  readonly #status: HTMLParagraphElement;
  readonly #openButton: HTMLButtonElement;
  readonly #onOpenApplication: WelcomeSequenceOptions['onOpenApplication'];

  constructor(root: HTMLElement, options: WelcomeSequenceOptions) {
    this.#onOpenApplication = options.onOpenApplication;
    this.#element = document.createElement('section');
    this.#element.className = 'welcome-sequence';
    this.#element.setAttribute('aria-labelledby', 'welcome-heading');

    const heading = document.createElement('h1');
    heading.id = 'welcome-heading';
    heading.textContent = WELCOME_COPY[0]!;

    const lead = document.createElement('p');
    lead.className = 'welcome-lead';
    lead.textContent = WELCOME_COPY[1]!;

    const orientation = document.createElement('div');
    orientation.className = 'welcome-orientation';
    for (const line of WELCOME_COPY.slice(2)) {
      const paragraph = document.createElement('p');
      paragraph.textContent = line;
      orientation.append(paragraph);
    }

    this.#openButton = document.createElement('button');
    this.#openButton.className = 'bring-application';
    this.#openButton.type = 'button';
    this.#openButton.textContent = 'Open Microsoft Edge';
    this.#openButton.addEventListener('click', () => void this.#openApplication());

    this.#status = document.createElement('p');
    this.#status.className = 'welcome-status';
    this.#status.setAttribute('role', 'status');
    this.#status.setAttribute('aria-live', 'polite');

    this.#element.append(heading, lead, orientation, this.#openButton, this.#status);
    root.append(this.#element);
  }

  setStatus(message: string, state: 'quiet' | 'working' | 'ready' | 'error' = 'quiet'): void {
    this.#status.textContent = message;
    this.#status.dataset.state = state;
  }

  destroy(): void {
    this.#element.remove();
  }

  async #openApplication(): Promise<void> {
    this.#openButton.disabled = true;
    this.setStatus('Asking Windows to open Microsoft Edge…', 'working');

    try {
      await this.#onOpenApplication('Microsoft Edge');
      this.setStatus('Microsoft Edge is opening on your PC.', 'ready');
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.setStatus(`Microsoft Edge could not be opened. ${message}`, 'error');
      this.#openButton.disabled = false;
    }
  }
}
