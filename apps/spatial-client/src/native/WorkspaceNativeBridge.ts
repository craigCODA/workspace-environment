export type WorkspaceNativeEnvelope = Readonly<{
  version: 1;
  type: string;
  payload: unknown;
}>;

export interface NativeMessageTransport {
  postMessage(message: unknown): void;
  subscribe(listener: (message: unknown) => void): () => void;
  destroy?(): void;
}

type EnvelopeListener = (message: WorkspaceNativeEnvelope) => void;

type WebViewMessageSource = {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: MessageEvent<unknown>) => void): void;
  removeEventListener(type: 'message', listener: (event: MessageEvent<unknown>) => void): void;
};

declare global {
  interface Window {
    chrome?: {
      webview?: WebViewMessageSource;
    };
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isEnvelope(value: unknown): value is WorkspaceNativeEnvelope {
  return isRecord(value)
    && value.version === 1
    && typeof value.type === 'string'
    && value.type.length > 0
    && Object.hasOwn(value, 'payload');
}

class WebViewTransport implements NativeMessageTransport {
  readonly #webview: WebViewMessageSource;

  constructor(webview: WebViewMessageSource) {
    this.#webview = webview;
  }

  postMessage(message: unknown): void {
    this.#webview.postMessage(message);
  }

  subscribe(listener: (message: unknown) => void): () => void {
    const onMessage = (event: MessageEvent<unknown>): void => listener(event.data);
    this.#webview.addEventListener('message', onMessage);
    return () => this.#webview.removeEventListener('message', onMessage);
  }
}

export class BrowserFallbackTransport implements NativeMessageTransport {
  readonly #listeners = new Set<(message: unknown) => void>();
  #destroyed = false;

  postMessage(message: unknown): void {
    if (!isEnvelope(message)) return;
    if (message.type === 'renderer.ready') {
      queueMicrotask(() => this.#welcome());
      return;
    }
    if (message.type === 'agent.instruction') {
      queueMicrotask(() => this.#reportNativeAgentUnavailable());
    }
  }

  subscribe(listener: (message: unknown) => void): () => void {
    this.#listeners.add(listener);
    return () => this.#listeners.delete(listener);
  }

  destroy(): void {
    this.#destroyed = true;
    this.#listeners.clear();
  }

  #emit(type: string, payload: unknown): void {
    if (this.#destroyed) return;
    const envelope: WorkspaceNativeEnvelope = { version: 1, type, payload };
    for (const listener of this.#listeners) listener(envelope);
  }

  #reportNativeAgentUnavailable(): void {
    const text = 'Coda’s native agent runtime is unavailable in the Electron/browser fallback. Open the native Workspace Environment build to use ChatGPT (Codex) or Grok.';
    this.#emit('agent.event', { level: 'error', summary: text });
    this.#emit('voice.caption', { text, final: true, utteranceId: 'native-agent-unavailable' });
  }

  #welcome(): void {
    if (typeof window === 'undefined' || this.#destroyed) return;

    let completed = false;
    let preferredName = '';
    try {
      completed = window.localStorage.getItem('workspace.coda.onboarding.completed') === 'true';
      preferredName = window.localStorage.getItem('workspace.coda.preferred-name')?.trim() ?? '';
    } catch {
      // Storage can be unavailable in hardened browser profiles. The welcome still runs.
    }

    const lines = completed
      ? [returningWelcome(preferredName)]
      : FIRST_RUN_NARRATION;

    this.#emit('preference.changed', {
      captionsEnabled: true,
      transcriptRetentionEnabled: false,
    });
    this.#emit('voice.state', { state: 'speaking' });

    const synthesis = window.speechSynthesis;
    if (!synthesis || typeof SpeechSynthesisUtterance === 'undefined') {
      this.#emit('voice.caption', { text: lines.join(' '), final: true });
      this.#finishWelcome();
      return;
    }

    let index = 0;
    const speakNext = (): void => {
      if (this.#destroyed) return;
      const line = lines[index];
      if (!line) {
        this.#finishWelcome();
        return;
      }

      const utterance = new SpeechSynthesisUtterance(line);
      utterance.rate = 0.96;
      utterance.onstart = () => this.#emit('voice.caption', { text: line, final: true });
      utterance.onend = () => {
        index += 1;
        speakNext();
      };
      utterance.onerror = () => {
        this.#emit('voice.caption', { text: line, final: true });
        index += 1;
        speakNext();
      };
      synthesis.speak(utterance);
    };
    speakNext();
  }

  #finishWelcome(): void {
    if (typeof window !== 'undefined') {
      try {
        window.localStorage.setItem('workspace.coda.onboarding.completed', 'true');
      } catch {
        // A failed preference write is non-fatal in the Electron development fallback.
      }
    }
    this.#emit('voice.state', { state: 'waiting' });
  }
}

function defaultTransport(): NativeMessageTransport {
  const webview = typeof window === 'undefined' ? undefined : window.chrome?.webview;
  return webview ? new WebViewTransport(webview) : new BrowserFallbackTransport();
}

export class WorkspaceNativeBridge {
  readonly #transport: NativeMessageTransport;
  readonly #listeners = new Map<string, Set<EnvelopeListener>>();
  readonly #unsubscribeTransport: () => void;

  constructor(transport: NativeMessageTransport = defaultTransport()) {
    this.#transport = transport;
    this.#unsubscribeTransport = transport.subscribe((message) => this.#receive(message));
  }

  post(type: string, payload: unknown = {}): void {
    this.#transport.postMessage({ version: 1, type, payload } satisfies WorkspaceNativeEnvelope);
  }

  subscribe(type: string, listener: EnvelopeListener): () => void {
    const listeners = this.#listeners.get(type) ?? new Set<EnvelopeListener>();
    listeners.add(listener);
    this.#listeners.set(type, listeners);
    return () => {
      listeners.delete(listener);
      if (listeners.size === 0) this.#listeners.delete(type);
    };
  }

  destroy(): void {
    this.#unsubscribeTransport();
    this.#listeners.clear();
    this.#transport.destroy?.();
  }

  #receive(value: unknown): void {
    if (!isEnvelope(value)) return;
    for (const listener of this.#listeners.get(value.type) ?? []) listener(value);
  }
}
import { FIRST_RUN_NARRATION, returningWelcome } from '../onboarding/CodaPresence.ts';
