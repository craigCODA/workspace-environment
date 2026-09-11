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
  microphoneEnabled: boolean;
  captionsEnabled: boolean;
  transcriptVisible: boolean;
  terminalVisible: boolean;
  chatVisible: boolean;
  proactiveMode: ProactiveMode;
  agentProvider: AgentProvider;
  terminalEvents: readonly string[];
}>;

export type ProactiveMode = 'CriticalOnly' | 'IncludeCompletion' | 'Quiet' | 'Custom';
export type AgentProvider = 'Codex' | 'SpaceXAI' | 'Cursor';

export const AGENT_PROVIDERS: readonly AgentProvider[] = Object.freeze([
  'Codex',
  'SpaceXAI',
  'Cursor',
]);

export const AGENT_PROVIDER_OPTIONS: readonly Readonly<{
  value: AgentProvider;
  label: string;
}>[] = Object.freeze([
  { value: 'Codex', label: 'Codex' },
  { value: 'SpaceXAI', label: 'SpaceXAI' },
  { value: 'Cursor', label: 'Cursor (soon)' },
]);

type CodaPresenceAction =
  | Readonly<{ type: 'state'; state: CodaState }>
  | Readonly<{ type: 'caption'; text: string }>
  | Readonly<{
      type: 'preferences';
      microphoneEnabled?: boolean;
      captionsEnabled?: boolean;
      transcriptVisible?: boolean;
      proactiveMode?: ProactiveMode;
      agentProvider?: AgentProvider;
    }>
  | Readonly<{ type: 'terminal-events'; events: readonly string[] }>
  | Readonly<{ type: 'terminal-visibility'; visible: boolean }>
  | Readonly<{ type: 'chat-visibility'; visible: boolean }>;

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

export function createInitialCodaPresenceModel(): CodaPresenceModel {
  return {
    state: 'waiting',
    caption: '',
    microphoneEnabled: true,
    captionsEnabled: true,
    transcriptVisible: false,
    terminalVisible: false,
    chatVisible: false,
    proactiveMode: 'CriticalOnly',
    agentProvider: 'Codex',
    terminalEvents: [],
  };
}

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
        microphoneEnabled: action.microphoneEnabled ?? model.microphoneEnabled,
        captionsEnabled: action.captionsEnabled ?? model.captionsEnabled,
        transcriptVisible: action.transcriptVisible ?? model.transcriptVisible,
        proactiveMode: action.proactiveMode ?? model.proactiveMode,
        agentProvider: action.agentProvider ?? model.agentProvider,
      };
    case 'terminal-events':
      return { ...model, terminalEvents: action.events.slice(-24) };
    case 'terminal-visibility':
      return { ...model, terminalVisible: action.visible };
    case 'chat-visibility':
      return { ...model, chatVisible: action.visible };
  }
}

export type CodaPresenceOptions = Readonly<{
  onPreferenceChange?(change: Record<string, boolean | string>): void;
  onVoiceControl?(action: 'listen' | 'pause' | 'resume' | 'stop'): void;
  onAgentInstruction?(text: string): void;
}>;

export type CodaInstructionEnvelope = Readonly<{
  type: 'agent.instruction';
  payload: Readonly<{ text: string }>;
}>;

export function submitCodaInstruction(
  text: string,
  post: (message: CodaInstructionEnvelope) => void,
): boolean {
  const instruction = text.trim();
  if (!instruction) return false;
  post({ type: 'agent.instruction', payload: { text: instruction } });
  return true;
}

export class CodaPresence {
  readonly #element: HTMLElement;
  readonly #stateLabel: HTMLSpanElement;
  readonly #caption: HTMLParagraphElement;
  readonly #transcript: HTMLElement;
  readonly #terminal: HTMLElement;
  readonly #terminalList: HTMLUListElement;
  readonly #chat: HTMLElement;
  readonly #chatMessages: HTMLDivElement;
  readonly #chatInput: HTMLInputElement;
  readonly #chatMessagesById = new Map<string, HTMLElement>();
  readonly #talkButton: HTMLButtonElement;
  readonly #microphoneButton: HTMLButtonElement;
  readonly #captionsButton: HTMLButtonElement;
  readonly #transcriptButton: HTMLButtonElement;
  readonly #terminalButton: HTMLButtonElement;
  readonly #chatButton: HTMLButtonElement;
  readonly #alertsButton: HTMLButtonElement;
  readonly #agentProviderSelect: HTMLSelectElement;
  readonly #options: CodaPresenceOptions;
  #model = createInitialCodaPresenceModel();

  constructor(root: HTMLElement, options: CodaPresenceOptions = {}) {
    this.#options = options;
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

    this.#chat = document.createElement('aside');
    this.#chat.className = 'coda-chat';
    this.#chat.setAttribute('aria-label', 'Chat with Coda');
    const chatHeading = document.createElement('p');
    chatHeading.className = 'coda-chat-heading';
    chatHeading.textContent = 'Coda';
    this.#chatMessages = document.createElement('div');
    this.#chatMessages.className = 'coda-chat-messages';
    this.#chatMessages.setAttribute('aria-live', 'polite');
    const chatForm = document.createElement('form');
    chatForm.className = 'coda-chat-form';
    this.#chatInput = document.createElement('input');
    this.#chatInput.type = 'text';
    this.#chatInput.placeholder = 'Message Coda…';
    this.#chatInput.setAttribute('aria-label', 'Message Coda');
    const send = document.createElement('button');
    send.type = 'submit';
    send.textContent = 'Send';
    chatForm.append(this.#chatInput, send);
    chatForm.addEventListener('submit', (event) => {
      event.preventDefault();
      submitCodaInstruction(this.#chatInput.value, (message) => {
        const { text } = message.payload;
        this.addChatMessage('user', text);
        this.#chatInput.value = '';
        this.#options.onAgentInstruction?.(text);
      });
    });
    this.#chat.append(chatHeading, this.#chatMessages, chatForm);

    const controls = document.createElement('nav');
    controls.className = 'coda-controls';
    controls.setAttribute('aria-label', 'Coda controls');
    this.#talkButton = this.#controlButton(controls, 'Talk', () => {
      const active = ['listening', 'wake-detected', 'thinking', 'speaking', 'working']
        .includes(this.#model.state);
      this.#options.onVoiceControl?.(active ? 'stop' : 'listen');
    });
    this.#microphoneButton = this.#controlButton(controls, 'Mic', () => {
      const enabled = !this.#model.microphoneEnabled;
      this.setPreferences({ microphoneEnabled: enabled });
      this.#options.onPreferenceChange?.({ microphoneEnabled: enabled });
    });
    this.#captionsButton = this.#controlButton(controls, 'CC', () => {
      const enabled = !this.#model.captionsEnabled;
      this.setPreferences({ captionsEnabled: enabled });
      this.#options.onPreferenceChange?.({ captionsEnabled: enabled });
    });
    this.#transcriptButton = this.#controlButton(controls, 'Transcript', () => {
      const enabled = !this.#model.transcriptVisible;
      this.setPreferences({ transcriptVisible: enabled });
      this.#options.onPreferenceChange?.({ transcriptRetentionEnabled: enabled });
    });
    this.#terminalButton = this.#controlButton(controls, 'Activity', () => {
      this.setTerminalVisible(!this.#model.terminalVisible);
    });
    this.#chatButton = this.#controlButton(controls, 'Chat', () => {
      this.setChatVisible(!this.#model.chatVisible);
    });
    this.#alertsButton = this.#controlButton(controls, 'Alerts', () => {
      const modes: ProactiveMode[] = ['CriticalOnly', 'IncludeCompletion', 'Quiet'];
      const index = modes.indexOf(this.#model.proactiveMode);
      const proactiveMode = modes[(index + 1) % modes.length]!;
      this.setPreferences({ proactiveMode });
      this.#options.onPreferenceChange?.({ proactiveMode });
    });

    const agentProviderControl = document.createElement('label');
    agentProviderControl.className = 'coda-agent-provider';
    const agentProviderLabel = document.createElement('span');
    agentProviderLabel.textContent = 'Agent';
    this.#agentProviderSelect = document.createElement('select');
    this.#agentProviderSelect.setAttribute('aria-label', 'Agent provider');
    for (const optionModel of AGENT_PROVIDER_OPTIONS) {
      const option = document.createElement('option');
      option.value = optionModel.value;
      option.textContent = optionModel.label;
      this.#agentProviderSelect.append(option);
    }
    this.#agentProviderSelect.addEventListener('change', () => {
      const value = this.#agentProviderSelect.value;
      if (!AGENT_PROVIDERS.includes(value as AgentProvider)) return;
      const agentProvider = value as AgentProvider;
      this.setPreferences({ agentProvider });
      this.#options.onPreferenceChange?.({ agentProvider });
    });
    agentProviderControl.append(agentProviderLabel, this.#agentProviderSelect);
    controls.append(agentProviderControl);

    this.#element.append(
      beacon,
      this.#stateLabel,
      controls,
      this.#caption,
      this.#transcript,
      this.#terminal,
      this.#chat,
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
    microphoneEnabled?: boolean;
    captionsEnabled?: boolean;
    transcriptVisible?: boolean;
    proactiveMode?: ProactiveMode;
    agentProvider?: AgentProvider;
  }): void {
    this.#model = reduceCodaPresence(this.#model, { type: 'preferences', ...options });
    this.#render();
  }

  setTranscript(text: string): void {
    this.#transcript.textContent = text;
    this.#transcript.hidden = !this.#model.transcriptVisible || text.trim().length === 0;
  }

  setTerminalEvents(events: readonly string[], reveal = false): void {
    this.#model = reduceCodaPresence(this.#model, { type: 'terminal-events', events });
    if (reveal) {
      this.#model = reduceCodaPresence(this.#model, {
        type: 'terminal-visibility',
        visible: true,
      });
      this.#model = reduceCodaPresence(this.#model, {
        type: 'chat-visibility',
        visible: false,
      });
    }
    this.#terminalList.replaceChildren();
    for (const event of this.#model.terminalEvents) {
      const item = document.createElement('li');
      item.textContent = event;
      this.#terminalList.append(item);
    }
    this.#render();
  }

  setTerminalVisible(visible: boolean): void {
    this.#model = reduceCodaPresence(this.#model, { type: 'terminal-visibility', visible });
    if (visible) {
      this.#model = reduceCodaPresence(this.#model, {
        type: 'chat-visibility',
        visible: false,
      });
    }
    this.#render();
  }

  setChatVisible(visible: boolean): void {
    this.#model = reduceCodaPresence(this.#model, { type: 'chat-visibility', visible });
    if (visible) {
      this.#model = reduceCodaPresence(this.#model, {
        type: 'terminal-visibility',
        visible: false,
      });
    }
    this.#render();
    if (visible) this.#chatInput.focus();
  }

  addChatMessage(role: 'user' | 'assistant', text: string, messageId?: string): void {
    const trimmed = text.trim();
    if (!trimmed) return;
    let message = messageId ? this.#chatMessagesById.get(messageId) : undefined;
    if (!message) {
      message = document.createElement('p');
      message.className = `coda-chat-message coda-chat-message-${role}`;
      message.dataset.role = role;
      if (messageId) {
        message.dataset.messageId = messageId;
        this.#chatMessagesById.set(messageId, message);
      }
      this.#chatMessages.append(message);
    }
    message.textContent = trimmed;
    this.#chatMessages.scrollTop = this.#chatMessages.scrollHeight;
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
    const conversationActive = ['listening', 'wake-detected', 'thinking', 'speaking', 'working']
      .includes(this.#model.state);
    this.#talkButton.textContent = conversationActive ? 'Stop' : 'Talk';
    this.#talkButton.setAttribute('aria-pressed', String(conversationActive));
    this.#talkButton.disabled = this.#model.state === 'mic-off';
    this.#microphoneButton.setAttribute('aria-pressed', String(this.#model.microphoneEnabled));
    this.#microphoneButton.textContent = this.#model.microphoneEnabled ? 'Mic on' : 'Mic off';
    this.#captionsButton.setAttribute('aria-pressed', String(this.#model.captionsEnabled));
    this.#captionsButton.textContent = this.#model.captionsEnabled ? 'CC on' : 'CC off';
    this.#transcriptButton.setAttribute('aria-pressed', String(this.#model.transcriptVisible));
    this.#terminalButton.setAttribute('aria-pressed', String(this.#model.terminalVisible));
    this.#chatButton.setAttribute('aria-pressed', String(this.#model.chatVisible));
    this.#alertsButton.setAttribute(
      'aria-label',
      `Proactive alerts: ${this.#model.proactiveMode}`,
    );
    this.#alertsButton.textContent = this.#model.proactiveMode === 'CriticalOnly'
      ? 'Alerts: critical'
      : this.#model.proactiveMode === 'IncludeCompletion'
        ? 'Alerts: +done'
        : 'Alerts: quiet';
    this.#agentProviderSelect.value = this.#model.agentProvider;
    this.#terminal.hidden = !this.#model.terminalVisible || this.#model.terminalEvents.length === 0;
    this.#chat.hidden = !this.#model.chatVisible;
    this.#transcript.hidden = !this.#model.transcriptVisible
      || (this.#transcript.textContent?.trim().length ?? 0) === 0;
  }

  #controlButton(
    root: HTMLElement,
    label: string,
    onClick: () => void,
  ): HTMLButtonElement {
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = label;
    button.addEventListener('click', onClick);
    root.append(button);
    return button;
  }
}
