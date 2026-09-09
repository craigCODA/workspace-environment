import assert from 'node:assert/strict';
import test from 'node:test';
import { WELCOME_COPY } from './WelcomeSequence.ts';

test('welcome begins with the approved first-run language', () => {
  assert.deepEqual(WELCOME_COPY.slice(0, 2), [
    'Welcome to your workspace environment.',
    "This is the place where we'll build the way you work.",
  ]);
});
