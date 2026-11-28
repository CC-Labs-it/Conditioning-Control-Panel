/* ui/run-links.js - how many outside links a run may open (lane D, owner 2026-11-24, DOORS-BRIEF.md).
 * Full edition: the ads alone, CONFIG.AD_TABS_PER_RUN as before. Small edition: ONE link a run, shared by the ad cards
 * and the in-run doors (the shop's locked tile, the locked ending seeds, the menu's door). The record is the ads' own,
 * run.store.adtabs ({ n: links opened, clicks: ad presses }), so it saves, reloads and resets with the run exactly as the
 * ads' tab count always has. The recap wall after Go Outside never asks here: the run is over and it is the main door. */

import { CONFIG } from '../data/config.js';
import { editionState } from './edition.js';

/** Small edition: links a run, ads and doors together. */
export const SMALL_LINKS_PER_RUN = 1;

/** The cap for this edition. */
export const linkCap = (ed = editionState()) => (ed?.edition === 'small' ? SMALL_LINKS_PER_RUN : CONFIG.AD_TABS_PER_RUN);

/** The run's link record (the ads' tab record), made on first use. */
export const linkRecord = (run) => ((run.store ||= {}).adtabs ||= { n: 0 });

/** True while this run may still open a link here. */
export const linkLeft = (run, ed = editionState()) => (linkRecord(run).n || 0) < linkCap(ed);

/** A door's view of the cap: left() and spend(). getRun answers the live run (a New run swaps it). */
export const runLinks = (getRun, ed = editionState()) => Object.freeze({
  left: () => linkLeft(getRun(), ed),
  spend: () => { const rec = linkRecord(getRun()); rec.n = (rec.n || 0) + 1; },
});
