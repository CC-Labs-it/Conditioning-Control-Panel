/* The editions copy (lane S, owner 2026-11-22, fourth dialog): no placeholder is left in shipped code, and every approved
 * line is pinned here character for character, so a changed letter, capital or full stop fails. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { SAVE_SLEEP_COPY, SKY_ASLEEP_COPY } from '../ui/save-sleep.js';
import { TRANSMISSION_COPY, TRANSMISSION_LINES } from '../ui/transmission.js';
import { NUDGE_COPY } from '../ui/nudges.js';
import { SAVE_CARRY_COPY } from '../ui/save-carry.js';
import { LOCKED_TILE_COPY } from '../ui/shop-locked.js';

/* [table, key, the approved text]. The slot ids are the survey's, for the record. */
const APPROVED = [
  [SAVE_SLEEP_COPY, 'title', 'Some clauses are asleep.'],   // S4-sleep-title
  [SAVE_SLEEP_COPY, 'body', 'This is the small edition. The parts of your run that do not fit here are asleep, not gone. They wake in the full game.'],
  [SAVE_SLEEP_COPY, 'nudge', 'The full game'],   // S4-sleep-nudge-button
  [SAVE_SLEEP_COPY, 'dismiss', 'Carry on'],
  [SKY_ASLEEP_COPY, 'title', 'This run has ended.'],   // S4-sky-title
  [SKY_ASLEEP_COPY, 'body', 'It ended somewhere this edition does not go. The full game picks it up.'],   // S4-ended-nudge-line
  [SKY_ASLEEP_COPY, 'nudge', 'The full game'],   // S4-ended-nudge-button
  [SKY_ASLEEP_COPY, 'newRun', 'New run'],
  [TRANSMISSION_COPY, 'title', 'INCOMING TRANSMISSION'],
  [TRANSMISSION_COPY, 'body', 'A message from the future is transmitting some data. Do you accept the transmission?'],
  [TRANSMISSION_COPY, 'size', '{mb} MB, saved to your CCP content folder.'],
  [TRANSMISSION_COPY, 'yes', 'YES'],
  [TRANSMISSION_COPY, 'notNow', 'NOT NOW'],
  [TRANSMISSION_COPY, 'smallBody', 'A message from the future is transmitting some data.'],
  [TRANSMISSION_COPY, 'smallLine', 'It does not fit here. The full game can receive it.'],
  [TRANSMISSION_COPY, 'smallYes', 'THE FULL GAME'],
  [TRANSMISSION_LINES, 'progress', 'Receiving. {pct}%'],
  [TRANSMISSION_LINES, 'arrived', 'Transmission received.'],
  [TRANSMISSION_LINES, 'arrivedClose', 'CLOSE'],
  [TRANSMISSION_LINES, 'stop', 'STOP'],
  [TRANSMISSION_LINES, 'stopped', 'Stopped. What arrived is kept.'],
  [TRANSMISSION_LINES, 'offline', 'No signal. The future will try again.'],
  [TRANSMISSION_LINES, 'disk', 'Not enough room on the disk.'],
  [TRANSMISSION_LINES, 'refused', 'The transmission did not check out. It can be sent again.'],
  [TRANSMISSION_LINES, 'penSign', 'TRANSMISSION WAITING'],
  [TRANSMISSION_LINES, 'llama8Title', 'A full pen.'],
  [TRANSMISSION_LINES, 'llama8Body', 'Eight llamas open something this edition cannot show. It is in the full game.'],
  [TRANSMISSION_LINES, 'llama8Button', 'The full game'],
  [NUDGE_COPY, 'pauseLine', 'This is the small edition.'],
  [NUDGE_COPY, 'pauseButton', 'The full game'],
  [NUDGE_COPY, 'endingButton', 'More endings in the full game'],
  [SAVE_CARRY_COPY, 'send', 'Send this run to my account'],
  [SAVE_CARRY_COPY, 'ask', "Fetch my account's run"],
  [SAVE_CARRY_COPY, 'sent', 'Sent. Your account holds this run.'],
  [SAVE_CARRY_COPY, 'fullLine', 'The full game can pick it up.'],
  [SAVE_CARRY_COPY, 'fullButton', 'The full game'],
  [SAVE_CARRY_COPY, 'sign-in', 'Sign in first.'],
  [SAVE_CARRY_COPY, 'too-big', 'This run is too big to send.'],
  [SAVE_CARRY_COPY, 'offline', 'No signal. Try again later.'],
  [SAVE_CARRY_COPY, 'refused', 'That did not go through.'],
  [SAVE_CARRY_COPY, 'none', 'Your account holds no run yet.'],
  [SAVE_CARRY_COPY, 'broken', 'That run could not be read. Yours is untouched.'],
  [SAVE_CARRY_COPY, 'incomingTitle', 'A run from your account'],
  [SAVE_CARRY_COPY, 'incomingBody', 'Saved on {from}, {when}. Play it here instead of this one?'],
  [SAVE_CARRY_COPY, 'fromWeb', 'the website'],
  [SAVE_CARRY_COPY, 'fromDesktop', 'the app'],
  [SAVE_CARRY_COPY, 'accept', 'Play that run'],
  [SAVE_CARRY_COPY, 'keep', 'Keep mine'],
  [LOCKED_TILE_COPY, 'label', 'More in the full game'],
];

test('no placeholder is left in shipped code', () => {
  const root = new URL('../', import.meta.url);
  const skip = /^(tests|dev|vendor|node_modules)\//;
  const hits = [];
  for (const rel of readdirSync(root, { recursive: true })) {
    const p = String(rel).split(String.fromCharCode(92)).join('/');
    if (!p.endsWith('.js') || skip.test(p)) continue;
    if (readFileSync(new URL(p, root), 'utf8').includes('[PLACEHOLDER')) hits.push(p);
  }
  assert.deepEqual(hits, []);
});

test('every approved line, character for character, and no line in those tables left unpinned', () => {
  for (const [table, key, text] of APPROVED) assert.equal(table[key], text, key);
  for (const table of [SAVE_SLEEP_COPY, SKY_ASLEEP_COPY, TRANSMISSION_COPY, TRANSMISSION_LINES, NUDGE_COPY, SAVE_CARRY_COPY, LOCKED_TILE_COPY]) {
    for (const key of Object.keys(table)) assert.ok(APPROVED.some(([t, k]) => t === table && k === key), `${key} is not pinned`);
  }
});
