/* fx/itemFeelMath.js - how an item feels from the thrower's seat (pure: no DOM, no three, no WebAudio).
 *
 * The sim says what happened (item-fire, item-hit, tether-hook); these say how the player hears and sees it:
 *   fireKick(mode)        which camera nudge a use plays (feel itemFeel.*KickM, x right, y up, z back, metres)
 *   comboNext(st, t, w)   hits from the player inside one window count up (Fog or Snap catching several karts)
 *   confirmWord(kind, n)  the comic stamp: one word per item, DOUBLE / TRIPLE / MULTI past one
 *   hitToast(p, who)      the toast for an item-hit the player is in: segments, tone and priority
 *   toastDecide(cur, nx)  whether a toast replaces the one on screen now, waits, or is dropped
 * Every number the HUD, camera and audio read lives in feel.json `itemFeel` (FEEL.md); Gentle is applied by the callers.
 */

/** Verdicts that mean the shot connected but did not land (sim/items.js hit()). */
export const BLOCKED_VERDICTS = Object.freeze(['glass', 'snapped', 'golden', 'decoy', 'mercy']);

/** Toast priorities: a hit you landed or took outranks a block, which outranks lap notes and the rest. */
export const PRIORITY = Object.freeze({ info: 0, note: 1, blocked: 2, hit: 3 });

/** The stamp word per item kind (one landed hit). */
export const HIT_WORDS = Object.freeze({
  pendulum: 'BONK!', disc: 'WHAM!', tether: 'YOINK!', snap: 'SNAP!', bigSpiral: 'WHIRL!', pink: 'POOF!',
  melt: 'SPLAT!', shatter: 'CRASH!', flash: 'FLASH!', spiral: 'SPUN!', brainDrain: 'DRAINED!', freeze: 'FROZEN!',
  blackout: 'LIGHTS OUT!',
});

/** The word for a shot that connected but did not land, by verdict. */
export const BLOCK_WORDS = Object.freeze({ glass: 'BLOCKED', snapped: 'SNAPPED', golden: 'IMMUNE', decoy: 'DECOY', mercy: 'SHRUGGED' });

const COMBO_WORDS = ['', '', 'DOUBLE!', 'TRIPLE!'];

/** The stamp for a landed hit: the kind's word for one, DOUBLE! / TRIPLE! / MULTI xN! past it. */
export function confirmWord(kind, count = 1) {
  const n = Math.max(1, count | 0);
  if (n === 1) return HIT_WORDS[kind] || 'HIT!';
  return COMBO_WORDS[n] || 'MULTI x' + n + '!';
}

/**
 * One more landed hit at time t (s). st { n, t0 } is updated in place: inside windowS of the chain's first hit
 * the count climbs, else a new chain starts at 1. @returns {number} the count including this hit
 */
export function comboNext(st, t, windowS) {
  const now = Number(t) || 0;
  if (st.n > 0 && now - st.t0 <= Math.max(0, Number(windowS) || 0) && now >= st.t0) st.n++;
  else { st.n = 1; st.t0 = now; }
  return st.n;
}

const MODES = ['throw', 'lob', 'wide', 'drop', 'self'];
/** The feel key of a use mode's camera nudge (bounce and tether fly like a throw). */
export function fireKickKey(mode) {
  const m = mode === 'bounce' || mode === 'tether' ? 'throw' : mode;
  return 'itemFeel.' + (MODES.includes(m) ? m : 'throw') + 'KickM';
}

/** A [x, y, z] nudge scaled by mul (Gentle or reduced motion pass their share); bad input reads as zero. */
export function scaleKick(v, mul = 1) {
  const k = Number(mul);
  const s = Number.isFinite(k) ? Math.max(0, k) : 1;
  const a = Array.isArray(v) ? v : [];
  return [0, 1, 2].map((i) => (Number(a[i]) || 0) * s);
}

/** The ordinal suffix of a place (1st, 2nd ...), '' for none. */
function placeText(place) {
  const n = Math.round(Number(place) || 0);
  if (!(n > 0)) return '';
  const t = n % 100, o = n % 10;
  return n + (t >= 11 && t <= 13 ? 'th' : o === 1 ? 'st' : o === 2 ? 'nd' : o === 3 ? 'rd' : 'th');
}

/**
 * The toast for an item-hit that names the player. who(i) -> { name, colour, place } for a racer, item(kind) -> its
 * name, count = the landed-hit chain (comboNext), names = the victims of that chain (for DOUBLE and on).
 * @returns {null | { tone: 'hit'|'ouch'|'blocked'|'saved', priority: number, group: string, word: string,
 *   parts: Array<{ text: string, cls?: string, colour?: string }> }}
 */
export function hitToast({ victim = -1, by = -1, kind = '', verdict = 'hit' } = {}, who, item, { count = 1, names = [] } = {}) {
  const W = (i) => (who && who(i)) || { name: i === 0 ? 'You' : 'Bot ' + i, colour: '', place: 0 };
  const it = (item && item(kind)) || kind || 'Item';
  const racer = (i) => { const r = W(i); return { text: r.name, cls: 'who', colour: r.colour || '' }; };
  const place = (i) => { const p = placeText(W(i).place); return p ? { text: p, cls: 'place' } : null; };
  const keep = (a) => a.filter(Boolean);
  if (by === 0 && victim !== 0) {
    if (verdict === 'hit') {
      const word = confirmWord(kind, count);
      if (count > 1 && names.length > 1) {
        return { tone: 'hit', priority: PRIORITY.hit, group: 'confirm', word,
          parts: [{ text: word, cls: 'word' }, { text: it + ' caught ' }, ...names.flatMap((i, k) => keep([k ? { text: k === names.length - 1 ? ' & ' : ', ' } : null, racer(i)]))] };
      }
      return { tone: 'hit', priority: PRIORITY.hit, group: 'confirm', word,
        parts: keep([{ text: word, cls: 'word' }, { text: it + ' on ' }, racer(victim), place(victim)]) };
    }
    const word = BLOCK_WORDS[verdict] || 'MISSED';
    const why = verdict === 'glass' ? ['', "'s shield took it"] : verdict === 'snapped' ? ['', ' popped it'] : verdict === 'golden' ? ['', ' is golden']
      : verdict === 'decoy' ? ['hit ', "'s echo"] : ['', ' shook it off'];
    return { tone: 'blocked', priority: PRIORITY.blocked, group: 'blocked', word,
      parts: keep([{ text: word, cls: 'word' }, why[0] ? { text: why[0] } : null, racer(victim), { text: why[1] }]) };
  }
  if (victim === 0) {
    const from = by >= 0 && by !== 0 ? [{ text: ' from ' }, racer(by)] : [];
    if (verdict === 'hit') return { tone: 'ouch', priority: PRIORITY.hit, group: 'ouch', word: 'OUCH!', parts: [{ text: 'OUCH!', cls: 'word' }, { text: it }, ...from] };
    if (verdict === 'glass' || verdict === 'golden' || verdict === 'snapped' || verdict === 'decoy') {
      const word = verdict === 'glass' ? 'SHIELDED' : verdict === 'golden' ? 'GOLDEN' : verdict === 'snapped' ? 'SNAPPED' : 'DODGED';
      return { tone: 'saved', priority: PRIORITY.blocked, group: 'saved', word, parts: [{ text: word, cls: 'word' }, { text: it }, ...from] };
    }
  }
  return null;
}

/**
 * Toast queue policy at time t (s). cur { priority, group, at, until } is the toast on screen (null or gone: none);
 * nx { priority, group } the one asking. 'now': show it at once (nothing showing, it outranks the one up, the same
 * group updating itself (a combo climbing), or the one up has had minShowS and does not outrank it); 'wait': hold it
 * until the current one ends; 'drop' never happens for a higher or equal priority.
 */
export function toastDecide(cur, nx, t, { minShowS = 0.45 } = {}) {
  if (!cur || !(t < cur.until)) return 'now';
  const a = Number(cur.priority) || 0, b = Number(nx.priority) || 0;
  if (b > a) return 'now';
  if (b === a && nx.group && nx.group === cur.group) return 'now';
  if (b >= a && t - cur.at >= minShowS) return 'now';
  return 'wait';
}
