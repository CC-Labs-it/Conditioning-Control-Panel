// The Intake lane (owner 2026-11-10): the question tricks sell as three stages, each asking more often and paying a
// bonus on every answer; EMI's calls can be picked up for a short tap boost; jealous and petted EMI pay.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { BY_ID, ITEMS } from '../data/items.js';
import { FOLD, LEAD_OF } from '../data/folds.js';
import { clauseLines } from '../ui/clause.js';
import { CLAUSES } from '../data/content/clauses.js';
import { FAKE_CALL } from '../data/content/fakes.js';
import { STAGE_BONUS_S, STAGE_GAP_MS, STAGE_ROWS, quizStage, stageBonus, stageGap } from '../ui/unlocks/extras/quiz-stages.js';
import * as E from '../sim/economy.js';

const LEADS = { 1: 'melt', 2: 'slider', 3: 'grid' };

test('every trick row after Pop-Up Question belongs to exactly one of three stages, sold as one purchase each', () => {
  const tricks = ITEMS.filter((it) => it.lane === 'trouble' && it.id !== 'quiz').map((it) => it.id);
  const staged = Object.values(STAGE_ROWS).flat();
  assert.deepEqual([...staged].sort(), [...tricks].sort());
  assert.deepEqual(Object.keys(STAGE_ROWS), ['1', '2', '3']);
  for (const [n, lead] of Object.entries(LEADS)) {
    assert.equal(STAGE_ROWS[n][0], lead);
    assert.deepEqual(FOLD[lead], STAGE_ROWS[n].slice(1), `stage ${n} brings its rows`);
    for (const r of FOLD[lead]) assert.equal(LEAD_OF[r], lead);
  }
  for (const req of ['melt', 'nag', 'magnet', 'dragno', 'onlyyes']) if (!Object.values(LEADS).includes(req)) assert.ok(!FOLD[req], `${req} is no longer its own bundle`);
  assert.deepEqual([BY_ID.melt.name, BY_ID.slider.name, BY_ID.grid.name], ['Softer Answers', 'Leaning Answers', 'Nothing But Yes']);
});

test('a stage is one purchase that owns every trick it brings, and stages cost more in order', () => {
  const s = E.createRun();
  E.lump(s, 1e12, 'test');
  E.grant(s, 'quiz');
  const prices = [];
  for (const lead of Object.values(LEADS)) {
    prices.push(E.price(s, lead));
    const buys = s.buys;
    const res = E.buy(s, lead);
    assert.equal(res.ok, true, lead);
    assert.equal(s.buys, buys + 1);
    assert.ok(FOLD[lead].every((id) => E.has(s, id)), `${lead} owns its stage`);
    assert.equal(quizStage((id) => E.has(s, id)), BY_ID[lead].qstage);
  }
  assert.ok(prices[0] < prices[1] && prices[1] < prices[2], prices.join(' < '));
});

test('questions come more often each stage and the answer bonus grows', () => {
  assert.equal(stageGap(0, 0.5), null, 'before a stage the old clock runs');
  const means = [1, 2, 3].map((n) => stageGap(n, 0.5) / 1000);
  assert.deepEqual(means, [60, 45, 35]);
  for (const n of [1, 2, 3]) assert.ok(stageGap(n, 0) >= 20000 && STAGE_GAP_MS[n][1] > 0);
  const rates = { fps: 100, click: 1 };
  const bonus = [0, 1, 2, 3].map((n) => stageBonus(rates, n));
  assert.equal(bonus[0], 0);
  assert.ok(bonus[1] > 0 && bonus[1] < bonus[2] && bonus[2] < bonus[3], bonus.join(' < '));
  assert.equal(bonus[3], 100 * STAGE_BONUS_S[3]);
  assert.ok(stageBonus({ fps: 0, click: 10 }, 1) > 0, 'a Tap route gets the bonus too');
  assert.equal(quizStage(() => false), 0);
  assert.equal(quizStage((id) => id === 'nag'), 1, 'an old save owning a rider counts its stage');
});

test('a stage shows one clause on its terms card, in the house voice', () => {
  for (const lead of Object.values(LEADS)) {
    const lines = clauseLines(lead, FOLD[lead], 1);
    assert.equal(lines.length, 1, lead);
    assert.equal(lines[0], CLAUSES[lead][0]);
    assert.match(lines[0], /^I accept/);
    assert.ok(!/[–—!]/.test(lines[0]));
  }
});

test('EMI Calls can be picked up for a short tap boost; jealous and petted EMI pay', () => {
  assert.equal(FAKE_CALL.pickUp, 'Pick up');
  assert.ok(FAKE_CALL.pickedUp.length >= 3 && FAKE_CALL.pickedUp.length <= 5);
  for (const t of [...FAKE_CALL.pickedUp, FAKE_CALL.boost]) assert.ok(!/[–—!]/.test(t) && t.length < 80, t);
  const src = readFileSync(new URL('../ui/unlocks/extras/emi-unlocks.js', import.meta.url), 'utf8');
  assert.match(src, /CALL_BOOST = 3, CALL_BOOST_MS = 20000/);
  assert.match(src, /ctx\.mod\(\{ timed: true, click:/, 'the boost is a timed buff, like Golden Frenzy');
  assert.ok(src.includes("ctx.pay(ctx.rates().fps * JEAL_PAY_S, 'em_jeal'"), 'saying hello to a blocking EMI pays');
  assert.ok(src.includes("ctx.pay(ctx.rates().fps * PET_PAY_S, 'em_pet'"), 'reaching glee pays');
  assert.match(src, /PET_COOLDOWN_MS = 60000/);
});
