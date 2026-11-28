/* The free edition's funnel copy is APPROVED by the owner and FROZEN (2026-11-24,
 * handoff-1124/tc-small-funnel/COPY-APPROVED.md). Every string below is copied letter for letter from that file:
 * a change to the game's data file fails here. Never edit a literal in this test without the owner's yes on old
 * against new. Numbers ({n}) come from the data; the words stay. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { SMALL_WALL_COPY, SMALL_CCP_ADS, LOCKED_SEED_COPY, EDITION_TAG_COPY } from '../data/content/small-funnel.js';
import { NUDGE_COPY } from '../ui/nudges.js';
import { wallModel } from '../ui/small-funnel.js';

test('approved copy: the end card', () => {
  assert.equal(SMALL_WALL_COPY.cap, 'The upsell, as promised');
  assert.equal(SMALL_WALL_COPY.offer, 'The full game is in CCP, for subscribers.');
  assert.equal(SMALL_WALL_COPY.sub, 'This is the part where a free game discovers it has a paid version. We held out for forty-five minutes, which by the standards of this industry makes us saints.');
  assert.equal(NUDGE_COPY.endingButton, 'More endings in the full game');
  assert.equal(SMALL_WALL_COPY.main, 'Fall into the DLC trap');
  assert.equal(SMALL_WALL_COPY.second, 'Just get CCP');
});

const ROWS = [
  ['endings', '{n} more endings', "though, let's be honest, you will probably live without five more endings"],
  ['arcade', '{n} arcade toys', 'cut from the free edition for space, and by space we mean money'],
  ['quiz', '{n} more quiz tricks for EMI', 'she does them for subscribers, and wants you to know it is not personal'],
  ['popups', '{n} pop-ups', 'yes, we are charging for pop-ups, and somehow it is working'],
  ['combos', '{n} crossovers', 'nobody has ever needed sixteen of anything, and yet here we are'],
  ['renewal', 'Renewal, {n} stars', 'you finish the game and then pay to start it again, which is our best trick'],
  ['expo', 'The Llama Expo', "The best part of the game, according to the dev's mom."],
];

test('approved copy: the comparison list, seven rows in order, the count from the data', () => {
  assert.deepEqual(Object.keys(SMALL_WALL_COPY.parts), ROWS.map(([k]) => k));
  for (const [k, perk, tag] of ROWS) assert.deepEqual({ ...SMALL_WALL_COPY.parts[k] }, { perk, tag }, k);
  const { endings, parts } = wallModel();
  assert.deepEqual(parts.map((p) => p.key), ROWS.map(([k]) => k), 'every row has something behind it today');
  const first = parts[0];
  assert.equal(first.n, endings.length, 'the endings row counts the fan itself');
  assert.equal(first.label, `${endings.length} more endings`);
  for (const p of parts) assert.equal(p.tag, SMALL_WALL_COPY.parts[p.key].tag);
});

test('approved copy: the DLC stamp and the title tag', () => {
  assert.equal(LOCKED_SEED_COPY.stamp, 'DLC');
  assert.equal(EDITION_TAG_COPY.tag, 'FREE EDITION');
});

const ADS = [
  'This is the free edition, which is the same game with the good parts taken out and sold back to you. We did not invent that business model, but we have come to love it.',
  'Do not fall into the DLC trap. The dev probably left the best endings out because the dev wants your money. They are in CCP, for subscribers.',
  'An honest ad, for once: you are playing the sample, the rest is in CCP, and we both know how this ends.',
  'EMI knows more tricks than this and is saving them for people who pay her, a policy she describes as nothing personal.',
  'The only product on this network that actually exists is the rest of this game. Sorry about the socks.',
  'The llamas are holding an Expo in the full game. You could live a full and happy life without seeing it. They would notice, though.',
];

test('approved copy: the six ad texts, split over headline and sub without changing a word', () => {
  assert.equal(SMALL_CCP_ADS.length, ADS.length);
  SMALL_CCP_ADS.forEach((ad, i) => {
    assert.ok(ad.headline && ad.sub, ad.id);
    assert.equal(`${ad.headline} ${ad.sub}`, ADS[i], ad.id);
  });
});
