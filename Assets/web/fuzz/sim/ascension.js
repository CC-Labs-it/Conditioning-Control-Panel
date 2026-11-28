/* ============================================================================
 * sim/ascension.js - the Renewal menu's and the Depth badge's one read of the prestige record (brief
 * handoff-1121/TC-ASCENSION-BRIEF.md). Pure: no DOM. ascensionView(life) never throws; a bad save reads as Depth 0.
 * bonuses = felt this run (owned stars in sky order, lit endings, the Core); next = bought mid-run, felt from the next run.
 * ==========================================================================*/

import { LANES, ENDING_STARS, CORE, MAX_DEPTH } from '../data/prestige.js';
import { parsePrestige } from './prestige.js';

/** Every word this view hands the UI, in one place (the owner may rename them). */
export const ASCENSION_LABELS = Object.freeze({
  endingLane: 'Endings',
  coreLane: 'The core',
  endingPlain: (which) => `Lit by ${which}. It holds in every later run.`,
});
const ENDING_HUE = '#f6c544';
const CORE_HUE = '#ff6fb5';
const CORE_ICON = 'spiral';

const starRow = (lane, s, next) => ({
  id: s.id, kind: 'star', lane: lane.id, laneName: lane.name, hue: lane.hue,
  icon: lane.icons[Math.floor((s.n - 1) / 3)] || lane.icons[0],
  name: s.name, line: s.flav, plain: s.plain, next,
});
const endingRow = (e) => ({
  id: e.id, kind: 'ending', lane: 'ending', laneName: ASCENSION_LABELS.endingLane, hue: ENDING_HUE,
  icon: e.icon, name: e.name, line: e.perk, plain: ASCENSION_LABELS.endingPlain(e.which), next: false,
});
const coreRow = (next) => ({
  id: CORE.id, kind: 'core', lane: 'core', laneName: ASCENSION_LABELS.coreLane, hue: CORE_HUE,
  icon: CORE_ICON, name: CORE.name, line: CORE.flav, plain: CORE.plain, next,
});

function build(src) {
  const p = parsePrestige(src);
  const bonuses = [], next = [];
  for (const lane of LANES) for (const s of lane.stars) {
    if (p.own[s.id]) bonuses.push(starRow(lane, s, false));
    else if (p.later.own[s.id]) next.push(starRow(lane, s, true));
  }
  let endings = 0;
  for (const e of ENDING_STARS) if (p.lit[e.id]) { bonuses.push(endingRow(e)); endings += 1; }
  if (p.core) bonuses.push(coreRow(false));
  else if (p.later.core) next.push(coreRow(true));
  return {
    depth: p.depth, maxDepth: MAX_DEPTH, dust: p.dust, core: !!p.core,
    bonuses, next,
    counts: { stars: bonuses.filter((b) => b.kind === 'star').length, endings, next: next.length },
  };
}

export function ascensionView(life) {
  try { return build(life && typeof life === 'object' ? life.prestige : null); }
  catch { return build(null); }
}
