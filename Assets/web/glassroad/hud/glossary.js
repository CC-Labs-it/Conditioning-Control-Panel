/* hud/glossary.js - the Power-ups card (owner 2026-10-31: "a glossario of the power ups with the effects").
 * Strings and one scroll helper, no DOM building; hud/cards.js shows it as kind 'glossary', reached from the how-to
 * card and the pause card.
 *
 * One row per LIVE item (a draw weight above 0 in feel.json): icon, name, one dry line on the effect, how it fires
 * and who tends to get it. Nothing a table already knows is typed here: the numbers in a line come from feel.json,
 * the fire word from sim/itemTable.js ITEMS.use, the who word from the draw weights. When sim and FEEL.md disagree the
 * sim wins (FEEL.md's Melt "view blurs" never plays: Melt's view is 'none', fx/effects.js skips it).
 * tests/glossary.test.mjs fails on a live item with no line, a weight-0 item with a line, and a row with no name.
 */
import { ITEM_IDS, ITEMS, BUCKETS } from '../sim/itemTable.js';
import { ITEM_NAMES, iconOf, ordinal } from './format.js';
import { itemTip } from './labels.js';

/** How a press fires it, by ITEMS.use (itemUseMode: 'trap' always drops, a rear press drops anything but 'self'). */
export const FIRE_WORDS = Object.freeze({
  throw: 'Throw ahead', bounce: 'Throw ahead', lob: 'Lob over the pack', wide: 'Wave ahead', tether: 'Hook ahead',
  trap: 'Drop behind', self: 'On yourself', ride: 'On yourself',
});
/** bucketOf's names as a player reads them; 'front' is place 1 or close behind the leader. */
export const WHO_WORDS = Object.freeze({ front: 'leader', middle: 'middle', back: 'back' });
/** A bucket counts as "mostly" when this item's share of its draws reaches this part of the item's best share. */
const WHO_NEAR_BEST = 0.6;
/** Only listed when the id exists in ITEM_IDS: Tunnel Vision waits for the interaction wave (feat/gr-online). */
export const OPTIONAL_ROWS = Object.freeze(['tunnel']);

const pct = (mul) => Math.round(Math.abs(1 - mul) * 100) + '%';

/** The line per id; g reads items.<key> from feel.json. Sim facts live in sim/items.js writeMods, hit and use. */
export const GLOSSARY_LINES = Object.freeze({
  pink: (g) => 'Every kart up to ' + g('pinkRangeM') + ' m ahead gets ' + g('pinkS') + ' s of mist in the course colours. People lose the view, bots lose the line.',
  melt: (g) => 'A puddle that stays on the road for ' + g('trapLifeS') + ' s. Whoever drives in slides on ice for ' + g('meltS') + ' s, you included.',
  surge: (g) => 'A ' + g('surgeS') + ' s boost. The further back you are, the bigger it is.',
  glass: (g) => 'Soaks the next item that hits you, then pops. It pops anyway after ' + g('glassMaxS') + ' s.',
  pendulum: (g) => 'Homes on the kart ahead. A hit swings it left and right for ' + g('pendulumS') + ' s.',
  disc: (g) => 'Aimed, never homes. Banks off rails up to ' + g('discBounces') + ' times. A hit spins the kart for ' + g('discS') + ' s and costs ' + pct(g('discSpeedKeep')) + ' of its speed.',
  tether: (g) => 'Hooks a kart up to ' + g('tetherRangeM') + ' m ahead. Your top speed up ' + pct(g('tetherTowCap')) + ', theirs down ' + pct(g('tetherDragCap')) +
    '. Pass it inside ' + g('tetherMaxS') + ' s and it spins. Nobody in range, you keep it.',
  snap: (g) => 'A ' + g('snapRadiusM') + ' m shockwave shoves nearby karts aside and breaks a Tether on you. For ' + g('snapGuardS') + ' s, throws and traps that reach you pop.',
  bigSpiral: (g) => 'Lands on the leader from above, or on 2nd if you lead. Top speed down ' + pct(g('bigSpiralCapMul')) + ' for ' + g('bigSpiralS') + ' s.',
  tunnel: (g) => { const o = ordinal(g('tunnelStopPlace')); return 'A ' + g('tunnelS') + ' s ride up the pack that launches karts in the way. Nothing lands on you. It stops at ' + o.n + o.suf + ', never takes the lead.'; },
});

const REAL = Object.freeze({ ids: ITEM_IDS, items: ITEMS, names: ITEM_NAMES, lines: GLOSSARY_LINES });

/** items.<id>Weights, or none for an id this feel.json has no row for. */
function weightsOf(feel, id) {
  try { const w = feel.get('items.' + id + 'Weights'); return Array.isArray(w) ? w : [0, 0, 0]; } catch { return [0, 0, 0]; }
}
const isLive = (w) => w.some((n) => n > 0);

/**
 * Who tends to draw it, from the share of each bucket's draws (a bucket with fewer tickets makes each ticket count
 * more): 'Back only', 'Mostly leader and middle', 'Anyone'.
 * @param {number[]} weights this item's [front, middle, back]
 * @param {number[]} totals every live item's weights summed per bucket
 */
export function whoGets(weights, totals) {
  const shares = BUCKETS.map((_, b) => (totals[b] > 0 ? (weights[b] || 0) / totals[b] : 0));
  const best = Math.max(...shares);
  if (!(best > 0)) return '';
  const near = BUCKETS.filter((_, b) => shares[b] > 0 && shares[b] >= best * WHO_NEAR_BEST);
  if (near.length === BUCKETS.length) return 'Anyone';
  const words = near.map((b) => WHO_WORDS[b]);
  if (near.length === 1 && shares.filter((v) => v > 0).length === 1) return words[0][0].toUpperCase() + words[0].slice(1) + ' only';
  return 'Mostly ' + words.join(' and ');
}

/** @returns {{ id, name, icon, line, fire, who }[]} one row per live item, ITEM_IDS order */
export function glossaryRows(feel, { ids, items, names, lines } = REAL) {
  const g = (k) => feel.get('items.' + k);
  const live = ids.map((id) => [id, weightsOf(feel, id)]).filter(([, w]) => isLive(w));
  const totals = BUCKETS.map((_, b) => live.reduce((sum, [, w]) => sum + (w[b] || 0), 0));
  return live.map(([id, w]) => ({
    id, name: names[id] || 'Item', icon: iconOf(id), line: lines[id] ? lines[id](g) : '',
    fire: FIRE_WORDS[items[id]?.use] || '', who: whoGets(w, totals),
  }));
}

/** What the guard test fails on; empty is healthy. Takes the tables so the test can prove each check bites. */
export function glossaryProblems(feel, { ids, items, names, lines } = REAL) {
  const out = [];
  for (const id of ids) {
    if (!isLive(weightsOf(feel, id))) continue;
    if (!lines[id]) out.push(id + ': live with no glossary row');
    if (!names[id]) out.push(id + ': a row with no name in ITEM_NAMES');
    if (!FIRE_WORDS[items[id]?.use]) out.push(id + ': a use with no fire word');
  }
  for (const id of Object.keys(lines)) {
    if (OPTIONAL_ROWS.includes(id)) continue;
    if (!ids.includes(id)) out.push(id + ': a glossary row for an id ITEM_IDS lacks');
    else if (!isLive(weightsOf(feel, id))) out.push(id + ': weight 0 with a glossary row');
    else if (!names[id]) out.push(id + ': a row with no name in ITEM_NAMES');
  }
  return out;
}

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

/** '20 s' never breaks across two lines. */
const keepUnits = (s) => s.replace(/(\d) (s|m)\b/g, '$1\u00a0$2');

/** The foot line, in the device's own buttons. */
export const glossaryKeys = (devices) => itemTip(devices || {}) + '. Anything not used on yourself can also go behind you as a trap.';

/** The card's inner html. The list scrolls by itself, so Back and the top close stay in reach. */
export function glossaryHtml(feel, devices = {}) {
  return '<ul class="gl-list">' + glossaryRows(feel).map((r) =>
    '<li class="gl-row" data-item="' + esc(r.id) + '"><img class="gl-i" src="' + esc(r.icon) + '" alt="" width="44" height="44" draggable="false">' +
    '<div class="gl-t"><b class="gl-n">' + esc(r.name) + '</b><p class="gl-l">' + keepUnits(esc(r.line)) + '</p>' +
    '<p class="gl-m"><span class="gl-f">' + esc(r.fire) + '</span><span class="gl-w">' + esc(r.who) + '</span></p></div></li>').join('') +
    '</ul><p class="c-hint gl-keys">' + esc(glossaryKeys(devices)) + '</p>';
}

/** Pad and arrow keys: one row up or down. The rows hold no buttons, so focus stays on Back. @returns moved */
export function scrollGlossary(list, dir) {
  if (!list) return false;
  const was = list.scrollTop;
  list.scrollTop = was + (dir === 'up' ? -1 : 1) * (list.querySelector?.('.gl-row')?.offsetHeight || 72);
  return list.scrollTop !== was;
}
