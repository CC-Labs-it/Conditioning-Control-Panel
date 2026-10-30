/* sim/social.js - racer-to-racer interaction that rides on sim events (interaction wave lane C, 2026-10-30).
 *
 * Knock-loose: a hard bump (contact.js `hard-bump`, dv >= items.knockDvMps) or a Tunnel Vision ram (`tunnel-ram`)
 * knocks the loser's held item onto the road as a loose pickup carrying that exact id (sim/items.js knock()).
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

export function createSocial(feel) {
  let items = null;

  /** Every sim event, as it is emitted (socialTap). */
  function see(name0, p0) {
    const [name, p] = unwrap(name0, p0);
    const k = knockOf(feel, name, p);
    if (k && items && items.knock) items.knock(k.loser, k.by, k.p);
  }

  return {
    see,
    /** The run's items (sim/items.js or net/items.js; only the sim one can knock). */
    bind(it) { items = it; },
    reset() {},
  };
}
