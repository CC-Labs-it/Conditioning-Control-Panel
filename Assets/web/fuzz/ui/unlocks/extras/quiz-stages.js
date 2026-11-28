/* ui/unlocks/extras/quiz-stages.js - the three question stages (owner 2026-11-10). The Intake trick rows sell as three
 * purchases (dev/gen-items.mjs INTAKE_STAGES writes `qstage` on every row of a stage). Each stage asks more often and
 * adds a bonus to every answer, so the player sees what the purchase did. Pure: the quiz and the tests read it. */

import { ITEMS } from '../../../data/items.js';

/** Stage number -> the row ids it brings, lead first. */
export const STAGE_ROWS = Object.freeze(ITEMS.filter((it) => it.qstage).reduce((m, it) => {
  (m[it.qstage] ??= []).push(it.id);
  return m;
}, {}));

/** Ask to ask, [base, jitter] ms: about 60 / 45 / 35 s. Stage 0 keeps the old clock (48 s plus up to 18, skipped while a
 *  card is up). From stage 1 a question that finds a card up waits STAGE_RETRY_MS and tries again. */
export const STAGE_GAP_MS = Object.freeze([null, Object.freeze([50000, 20000]), Object.freeze([37000, 16000]), Object.freeze([28000, 14000])]);
export const STAGE_RETRY_MS = 4000;

/** What every answer adds, right or wrong: seconds of per-second pay (taps count double, as in quizPay's floor). */
export const STAGE_BONUS_S = Object.freeze([0, 5, 10, 16]);

/** The stage a run is at: the highest one any of whose rows it owns (an old save may own a rider without its lead). */
export function quizStage(has) {
  let n = 0;
  for (const it of ITEMS) if (it.qstage > n && has(it.id)) n = it.qstage;
  return n;
}

/** The wait before the next question at a stage; r is a roll in [0, 1). */
export function stageGap(stage, r) {
  const g = STAGE_GAP_MS[stage];
  return g ? g[0] + r * g[1] : null;
}

/** The bonus an answer asks for at a stage, before the side budget. */
export function stageBonus(rates, stage) {
  const k = STAGE_BONUS_S[stage] || 0;
  return k ? Math.max((rates.fps || 0) * k, (rates.click || 0) * k * 2) : 0;
}
