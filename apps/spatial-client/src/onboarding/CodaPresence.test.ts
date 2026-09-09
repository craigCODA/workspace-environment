import assert from 'node:assert/strict';
import test from 'node:test';
import { reduceCodaPresence, type CodaPresenceModel } from './CodaPresence.ts';

const initial: CodaPresenceModel = {
  state: 'waiting',
  caption: '',
  captionsEnabled: true,
  transcriptVisible: false,
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
