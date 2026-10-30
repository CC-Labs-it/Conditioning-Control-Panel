/* sim/social.js - racer-to-racer interaction that rides on sim events (interaction wave lane C, 2026-10-30).
 *
 * Knock-loose: a hard bump (contact.js `hard-bump`, dv >= items.knockDvMps) or a Tunnel Vision ram (`tunnel-ram`)
 * knocks the loser's held item onto the road as a loose pickup carrying that exact id (sim/items.js knock()).
 *
 * Payback: whoever last hit you (an item hit with a known thrower, a hard bump you lost, a tunnel ram) is your mark
 * for items.paybackS. Your next homing throw prefers the mark when it is ahead in throw range (sim/items.js asks
 * through setPayback); the mark floats over that kart on YOUR screen only (markView, render/items/visuals.js).
 *
 * Reactions: four preset reactions (no free text anywhere), one per react.gapS per racer. press(i) plays the next one
 * in turn; `kart-react { racer, id }` shows EMI's face, mark and clip over that kart (render/kartPose.js) with a short
 * sound (audio/cues-b1.js). Bots react now and then: after a hard bump, a landed hit and at the finish. Online the
 * room relays `react` (net/protocol.js, net/room.js); a relayed one arrives with `remote: true`.
 *
 * The sim emits into a queue that boot.js flushes once per frame; socialTap(q, fn) wraps that queue so this module
 * hears every event the moment it is emitted, inside the same fixed step. Bot events arrive wrapped as racer-event
 * and are unwrapped here. Three-free, so node --test runs it.
 */

/** A queue (or any `{ emit }`) that also hands every event to fn(name, payload) right after queueing it. */
export function socialTap(q, fn) {
  return {
    emit(name, p = {}) {
      q.emit(name, p);
      try { fn(name, p); } catch (e) { console.warn('[social]', name, e); }
    },
    clear() { if (q.clear) q.clear(); },
    flush(bus) { return q.flush ? q.flush(bus) : 0; },
  };
}

/** A bot event (racer-event { racer, name, p }) as its own name and payload. */
export function unwrap(name, p) {
  return name === 'racer-event' && p && typeof p.name === 'string' ? [p.name, p.p || {}] : [name, p || {}];
}

/** Who knocked whom, or null: a hard bump at or above items.knockDvMps, or any tunnel ram. */
export function knockOf(feel, name, p) {
  if (name === 'hard-bump') {
    return (p.dv || 0) >= feel.get('items.knockDvMps') && p.loser >= 0 && p.winner !== p.loser ? { loser: p.loser, by: p.winner, p: p.p || null } : null;
  }
  if (name === 'tunnel-ram') return p.victim >= 0 && p.attacker !== p.victim ? { loser: p.victim, by: p.attacker, p: p.p || null } : null;
  return null;
}

/** Who should pay back whom, or null: [victim, hitter]. */
export function paybackOf(name, p) {
  let v = -1, by = -1;
  if (name === 'item-hit') { if (p.verdict === 'hit') { v = p.victim; by = p.by; } }
  else if (name === 'hard-bump') { v = p.loser; by = p.winner; }
  else if (name === 'tunnel-ram') { v = p.victim; by = p.attacker; }
  return Number.isInteger(v) && Number.isInteger(by) && v >= 0 && by >= 0 && v !== by ? [v, by] : null;
}

/** Wire order (net/protocol.js REACTIONS). Append only. */
export const REACTIONS = Object.freeze(['laugh', 'kiss', 'wink', 'pout']);

/** Seeded 0..1 (mulberry32, as sim/itemTable.js makeRand). */
function rng(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export function createSocial(feel, { seed = 1 } = {}) {
  let items = null, events = null, isBot = () => false, now = 0, rand = rng(seed);
  const lastAt = new Map(), turn = new Map(), pending = [];
  const marks = new Map();   // victim -> { target, t (s left) }
  const markRow = { racer: -1, k: 0, t: 0 };

  /** Every sim event, as it is emitted (socialTap). */
  function see(name0, p0) {
    const [name, p] = unwrap(name0, p0);
    const k = knockOf(feel, name, p);
    if (k && items && items.knock) items.knock(k.loser, k.by, k.p);
    const pb = paybackOf(name, p);
    if (pb) marks.set(pb[0], { target: pb[1], t: feel.get('items.paybackS'), age: 0 });
    botsReact(name, p);
  }

  /** Racer i reacts with id (one of REACTIONS). False when unknown or inside react.gapS of its last one. */
  function react(i, id, extra = null) {
    if (!REACTIONS.includes(id) || !Number.isInteger(i) || i < 0) return false;
    const last = lastAt.get(i);
    if (last !== undefined && now - last < feel.get('react.gapS')) return false;
    lastAt.set(i, now);
    if (events) events.emit('kart-react', { racer: i, id, ...(extra || {}) });
    return true;
  }

  function botsReact(name, p) {
    const maybe = (i, id) => {
      if (isBot(i) && rand() < feel.get('react.botChance')) pending.push({ i, id, at: now + feel.get('react.botDelayS') * (0.6 + 0.8 * rand()) });
    };
    if (name === 'hard-bump') { maybe(p.winner, 'laugh'); maybe(p.loser, 'pout'); }
    else if (name === 'item-hit' && p.verdict === 'hit' && p.by !== p.victim) maybe(p.by, rand() < 0.5 ? 'laugh' : 'wink');
    else if (name === 'item-knocked') maybe(p.by, 'wink');
    else if (name === 'overtake' && p.passed >= 0) maybe(p.passed, 'pout');
    else if (name === 'race-result') for (const row of p.rows || []) maybe(row.i, row.place <= 3 ? 'kiss' : 'pout');
  }

  function update(dt) {
    now += dt;
    for (let k = pending.length - 1; k >= 0; k--) if (pending[k].at <= now) { const e = pending.splice(k, 1)[0]; react(e.i, e.id); }
    for (const [i, m] of marks) { m.age += dt; if ((m.t -= dt) <= 0) marks.delete(i); }
  }

  /** The racer i should pay back, or -1. */
  const markOf = (i) => (marks.has(i) ? marks.get(i).target : -1);

  return {
    see, update, markOf, react,
    /** Racer i's reaction button: the next of the four, in turn. @returns the id played, or null (too soon). */
    press(i = 0) {
      const n = turn.get(i) || 0, id = REACTIONS[n % REACTIONS.length];
      if (!react(i, id)) return null;
      turn.set(i, n + 1);
      return id;
    },
    /** A reaction the room relayed for a remote racer (no local rate limit: the room keeps it). */
    remote(i, id) { if (REACTIONS.includes(id) && events) events.emit('kart-react', { racer: i, id, remote: true }); },
    /** The run's event sink and which racers are bots this client drives. */
    attach(q, botTest) { events = q; if (typeof botTest === 'function') isBot = botTest; },
    /** A throw went to the mark: the score is settled. */
    spend(i) { marks.delete(i); },
    /** Racer i's mark for its own screen: { racer, k (0..1 in and out), t } or null. A reader must not keep it. */
    markView(i = 0) {
      const m = marks.get(i);
      if (!m) return null;
      markRow.racer = m.target; markRow.t = m.age;
      markRow.k = Math.max(0, Math.min(1, m.age / 0.3, m.t / 0.5));
      return markRow;
    },
    /** The run's items (sim/items.js or net/items.js; only the sim one can knock). */
    bind(it) { items = it; if (it && it.setPayback) it.setPayback({ markOf, spend: (i) => marks.delete(i) }); },
    reset() { marks.clear(); lastAt.clear(); pending.length = 0; now = 0; rand = rng(seed); },
  };
}
