import assert from 'node:assert/strict';
import test from 'node:test';
import { FIRST_RUN_NARRATION, returningWelcome } from './CodaPresence.ts';

test('first-run copy is narration rather than a persistent text panel', () => {
  assert.deepEqual(FIRST_RUN_NARRATION.slice(0, 2), [
    'Welcome to your workspace environment.',
    "This is the place where we'll build the way you work.",
  ]);
});

test('returning welcome addresses the saved preferred name', () => {
  assert.equal(returningWelcome('Morgan'), 'Welcome back, Morgan.');
  assert.equal(returningWelcome('  '), 'Welcome back.');
});
