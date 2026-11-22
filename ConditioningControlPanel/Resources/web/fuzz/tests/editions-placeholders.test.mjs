/* The owner survey list (lane S7a): every `[PLACEHOLDER <slot>]` in shipped code is one slot on this pinned list, and
 * every slot on the list is still in the code. A new slot fails here until it is added, so the survey never drifts. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';

const SURVEY = [
  'S4-sleep-title', 'S4-sleep-body', 'S4-sleep-nudge-button', 'S4-sleep-dismiss',
  'S4-sky-title', 'S4-ended-nudge-line', 'S4-ended-nudge-button', 'S4-sky-new-run',
  'S6-progress', 'S6-arrived', 'S6-arrived-close', 'S6-failed-offline', 'S6-failed-disk', 'S6-failed-refused',
  'S6-stop', 'S6-stopped', 'S6-pen-sign', 'S6-llama8-title', 'S6-llama8-body', 'S6-llama8-button',
  'S7-ending-button', 'S7-pause-line', 'S7-pause-button',
];

test('every placeholder in shipped code is on the owner survey list, and the list has no stale slot', () => {
  const root = new URL('../', import.meta.url);
  const skip = /^(tests|dev|vendor|node_modules)\//;
  const found = new Map();
  for (const rel of readdirSync(root, { recursive: true })) {
    const p = String(rel).split('\\').join('/');
    if (!p.endsWith('.js') || skip.test(p)) continue;
    for (const m of readFileSync(new URL(p, root), 'utf8').matchAll(/\[PLACEHOLDER ([^\]\s]+)\]/g)) {
      if (!found.has(m[1])) found.set(m[1], p);
    }
  }
  assert.deepEqual([...found.keys()].filter((id) => !SURVEY.includes(id)), [], 'new slots: add them to SURVEY (and the lane note)');
  assert.deepEqual(SURVEY.filter((id) => !found.has(id)), [], 'slots no longer in the code: drop them from SURVEY');
  assert.equal(new Set(SURVEY).size, SURVEY.length);
});
