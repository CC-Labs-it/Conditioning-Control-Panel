/* ui/unlocks/fate/museum/terms.js - which terms the player actually accepted this run, by clause. The museum narrator
 * reads back recorded clauses (`term-<item>-<n>` in ui/documentary/narration-data.js), but only ones the player really
 * ticked or accepted on a terms card (ui/clause.js). The card shows clauseLines(id, riders, stage); each shown line maps
 * back to its item and its place in that item's list. Saved in run.store.museumTerms so a reload keeps them. */

import { CLAUSES } from '../../../../data/content/clauses.js';
import { clauseLines, clauseStage } from '../../../clause.js';

export const TERMS_KEY = 'museumTerms';
export const TERMS_CAP = 600;

/** Clause ids (`term-<item>-<n>`, n from 1) for the lines a card showed. */
export function acceptedClauseIds(id, riders = [], stage = 1, map = CLAUSES) {
  const out = [];
  for (const line of clauseLines(id, riders, stage, map)) {
    for (const who of [id, ...riders]) {
      const n = (map[who] || []).indexOf(line);
      if (n >= 0) { out.push(`term-${who}-${n + 1}`); break; }
    }
  }
  return out;
}

/** Records an accepted card on the run. `purchase` and `laterRun` are the values the card itself was opened with. */
export function noteAcceptedTerms(run, id, riders, purchase, laterRun) {
  if (!run) return [];
  const ids = acceptedClauseIds(id, riders, clauseStage(purchase, laterRun));
  if (!ids.length) return ids;
  run.store ||= {};
  const bag = (run.store[TERMS_KEY] ||= {});
  const list = Array.isArray(bag.ids) ? bag.ids : (bag.ids = []);
  for (const t of ids) if (!list.includes(t) && list.length < TERMS_CAP) list.push(t);
  return ids;
}

/** The run's accepted clause ids, oldest first. */
export function acceptedTerms(run) {
  const list = run?.store?.[TERMS_KEY]?.ids;
  return Array.isArray(list) ? list.filter((t) => typeof t === 'string') : [];
}
