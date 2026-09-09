export const FIRST_RUN_NARRATION = Object.freeze([
  'Welcome to your workspace environment.',
  "This is the place where we'll build the way you work.",
  'The applications, files, projects, and tools on your computer can exist here, but they do not have to look or behave like a traditional desktop.',
  'This space is intentionally unfinished.',
  'Look around.',
  "When you're ready, we'll start by bringing something from your computer into the workspace.",
]);

export function returningWelcome(preferredName: string): string {
  const name = preferredName.trim();
  return name ? `Welcome back, ${name}.` : 'Welcome back.';
}

export type CodaState =
  | 'waiting'
  | 'listening'
  | 'wake-detected'
  | 'thinking'
  | 'speaking'
  | 'working'
  | 'needs-attention'
  | 'mic-off';

export type CodaPresenceModel = Readonly<{
  state: CodaState;
  caption: string;
  captionsEnabled: boolean;
  transcriptVisible: boolean;
  terminalEvents: readonly string[];
}>;

type CodaPresenceAction =
  | Readonly<{ type: 'state'; state: CodaState }>
  | Readonly<{ type: 'caption'; text: string }>
  | Readonly<{
      type: 'preferences';
      captionsEnabled?: boolean;
      transcriptVisible?: boolean;
    }>
  | Readonly<{ type: 'terminal-events'; events: readonly string[] }>;

const STATE_LABELS: Record<CodaState, string> = {
  waiting: 'Waiting for Hey Coda',
  listening: 'Listening',
  'wake-detected': 'Coda heard you',
  thinking: 'Thinking',
  speaking: 'Speaking',
  working: 'Working',
  'needs-attention': 'Needs your attention',
  'mic-off': 'Microphone off',
};

const INITIAL_MODEL: CodaPresenceModel = {
  state: 'waiting',
  caption: '',
  captionsEnabled: true,
  transcriptVisible: false,
  terminalEvents: [],
};

export function reduceCodaPresence(
  model: CodaPresenceModel,
  action: CodaPresenceAction,
): CodaPresenceModel {
  switch (action.type) {
    case 'state':
      return { ...model, state: action.state };
    case 'caption':
      return { ...model, state: 'speaking', caption: action.text.trim() };
    case 'preferences':
      return {
        ...model,
        captionsEnabled: action.captionsEnabled ?? model.captionsEnabled,
        transcriptVisible: action.transcriptVisible ?? model.transcriptVisible,
      };
    case 'terminal-events':
      return { ...model, terminalEvents: action.events.slice(-24) };
  }
}

export class CodaPresence {
  readonly #element: HTMLElement;
  readonly #stateLabel: HTMLSpanElement;
  readonly #caption: HTMLParagraphElement;
  readonly #transcript: HTMLElement;
  readonly #terminal: HTMLElement;
  readonly #terminalList: HTMLUListElement;
  #model = INITIAL_MODEL;

  constructor(root: HTMLElement) {
    this.#element = document.createElement('section');
    this.#element.className = 'coda-presence';
    this.#element.dataset.state = this.#model.state;
    this.#element.setAttribute('aria-label', 'Coda voice agent');

    const beacon = document.createElement('div');
    beacon.className = 'coda-beacon';
    beacon.setAttribute('aria-hidden', 'true');

    this.#stateLabel = document.createElement('span');
    this.#stateLabel.className = 'coda-state-label';

    this.#caption = document.createElement('p');
    this.#caption.className = 'coda-caption';
    this.#caption.setAttribute('role', 'status');
    this.#caption.setAttribute('aria-live', 'polite');
    this.#caption.dataset.visible = 'false';

    this.#transcript = document.createElement('aside');
    this.#transcript.className = 'coda-transcript';
    this.#transcript.setAttribute('aria-label', 'Coda transcript');
    this.#transcript.hidden = true;

    this.#terminal = document.createElement('aside');
    this.#terminal.className = 'coda-terminal';
    this.#terminal.setAttribute('aria-label', 'Coda activity');
    this.#terminal.hidden = true;
    const terminalHeading = document.createElement('p');
    terminalHeading.className = 'coda-terminal-heading';
    terminalHeading.textContent = 'Coda activity';
    this.#terminalList = document.createElement('ul');
    this.#terminal.append(terminalHeading, this.#terminalList);

    this.#element.append(
      beacon,
      this.#stateLabel,
      this.#caption,
      this.#transcript,
      this.#terminal,
    );
    root.append(this.#element);
    this.#render();
  }

  setState(state: CodaState): void {
    this.#model = reduceCodaPresence(this.#model, { type: 'state', state });
    this.#render();
  }

  showCaption(text: string): void {
    this.#model = reduceCodaPresence(this.#model, { type: 'caption', text });
    this.#render();
  }

  setPreferences(options: {
    captionsEnabled?: boolean;
    transcriptVisible?: boolean;
  }): void {
    this.#model = reduceCodaPresence(this.#model, { type: 'preferences', ...options });
    this.#render();
  }

  setTranscript(text: string): void {
    this.#transcript.textContent = text;
    this.#transcript.hidden = !this.#model.transcriptVisible || text.trim().length === 0;
  }

  setTerminalEvents(events: readonly string[]): void {
    this.#model = reduceCodaPresence(this.#model, { type: 'terminal-events', events });
    this.#terminalList.replaceChildren();
    for (const event of this.#model.terminalEvents) {
      const item = document.createElement('li');
      item.textContent = event;
      this.#terminalList.append(item);
    }
    this.#terminal.hidden = this.#model.terminalEvents.length === 0;
  }

  destroy(): void {
    this.#element.remove();
  }

  #render(): void {
    this.#element.dataset.state = this.#model.state;
    this.#stateLabel.textContent = STATE_LABELS[this.#model.state];
    this.#caption.textContent = this.#model.caption;
    this.#caption.dataset.visible = String(
      this.#model.captionsEnabled && this.#model.caption.length > 0,
    );
    if (!this.#model.transcriptVisible) this.#transcript.hidden = true;
  }
}
