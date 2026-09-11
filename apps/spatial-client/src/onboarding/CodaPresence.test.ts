import assert from 'node:assert/strict';
import test from 'node:test';
import {
  reduceCodaPresence,
  submitCodaInstruction,
  type CodaPresenceModel,
} from './CodaPresence.ts';

const initial: CodaPresenceModel = {
  state: 'waiting',
  caption: '',
  microphoneEnabled: true,
  captionsEnabled: true,
  transcriptVisible: false,
  terminalVisible: false,
  chatVisible: true,
  proactiveMode: 'CriticalOnly',
  agentProvider: 'Codex',
  terminalEvents: [],
};

test('caption messages become the current bottom-safe-area copy', () => {
  const next = reduceCodaPresence(initial, {
    type: 'caption',
    text: 'Welcome to your workspace environment.',
  });

  assert.equal(next.caption, 'Welcome to your workspace environment.');
  assert.equal(next.state, 'speaking');
});

test('caption and transcript toggles are independent', () => {
  const next = reduceCodaPresence(initial, {
    type: 'preferences',
    captionsEnabled: false,
    transcriptVisible: true,
  });

  assert.equal(next.captionsEnabled, false);
  assert.equal(next.transcriptVisible, true);
});

test('terminal progress stays summarized and bounded', () => {
  const next = reduceCodaPresence(initial, {
    type: 'terminal-events',
    events: Array.from({ length: 40 }, (_, index) => `event ${index}`),
  });

  assert.equal(next.terminalEvents.length, 24);
  assert.equal(next.terminalEvents.at(-1), 'event 39');
});

test('terminal visibility and microphone state remain directly togglable', () => {
  const terminal = reduceCodaPresence(initial, { type: 'terminal-visibility', visible: true });
  const microphone = reduceCodaPresence(terminal, {
    type: 'preferences',
    microphoneEnabled: false,
  });

  assert.equal(microphone.terminalVisible, true);
  assert.equal(microphone.microphoneEnabled, false);
});

test('chat can be collapsed without changing voice preferences', () => {
  const next = reduceCodaPresence(initial, { type: 'chat-visibility', visible: false });

  assert.equal(next.chatVisible, false);
  assert.equal(next.microphoneEnabled, true);
});

test('agent provider preference can cycle independently of voice toggles', () => {
  const next = reduceCodaPresence(initial, {
    type: 'preferences',
    agentProvider: 'SpaceXAI',
  });

  assert.equal(next.agentProvider, 'SpaceXAI');
  assert.equal(next.microphoneEnabled, true);
  assert.equal(next.proactiveMode, 'CriticalOnly');
});

test('typed chat posts the shared agent instruction envelope', () => {
  const posted: unknown[] = [];

  submitCodaInstruction('open Notepad here', (message) => posted.push(message));

  assert.deepEqual(posted, [
    { type: 'agent.instruction', payload: { text: 'open Notepad here' } },
  ]);
});
