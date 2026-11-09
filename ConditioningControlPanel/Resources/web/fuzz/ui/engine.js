/* ============================================================================
 * ui/engine.js - the game clock, the scopes that track everything an unlock makes, the modifiers, and the verbs (tap,
 * pay, buy, choose, end). It does not know how anything looks: `ui` (ui/app.js) draws. A failing unlock is logged and
 * switched off; it never takes the game down. Unlock authors read ENGINE.md, not this file.
 * ==========================================================================*/

import { BY_ID, ITEMS, FORKS } from '../data/items.js';
import { CONFIG } from '../data/config.js';
import { brandText } from '../data/brand.js';
import * as E from '../sim/economy.js';
import { adClickFactor, moodOf } from '../sim/moods.js';
import { noteSeen, recordEnding, ENDING_IDS } from '../sim/save.js';
import { familyOf, liveOn, panelGroups, parentOf, switchable } from '../sim/switches.js';
import { makeContext } from './context.js';
import { createPictures } from './pictures.js';
import { paletteOf } from './palette.js';
import { createAds, subscriberPlan } from './ads.js';

const MAX_UNLOCK_ERRORS = 3;
const ENDING_SCOPE = '__ending';

import { createJourney } from '../sim/journey.js';
import { takeActivity, journeyChapter } from '../sim/journey-effects.js';
import * as P from '../sim/prestige.js';
import * as PE from '../sim/prestige-endings.js';   // ending-star perks that are behaviour (Endings lane)
import { LANE_BY_ID } from '../data/prestige.js';
import { PRESTIGE_REGISTRY } from './unlocks/prestige/index.js';

/* ---- Renewal Sky: what a fresh run starts with (sim/prestige.js runStamp). Applied once, at the first second. ---- */
const ownFree = (run, id) => {
  if (!BY_ID[id] || run.owned[id]) return;
  run.owned[id] = 1;
  if (!run.order.includes(id)) run.order.push(id);
};
function stampRun(run, p) {
  const st = P.runStamp(p);
  run.prestige = st;
  run.pace *= st.pay;
  const before = run.order.length;
  for (const id of st.keep) ownFree(run, id);
  if (st.wheel) ownFree(run, 'wheel');
  if (PE.startsAsBubble(p)) ownFree(run, PE.BUBBLE_ROW);   // Bimbodoll ending (bi_end): the button starts as a bubble
  if (run.order.length !== before) E.recalc(run);
  return st;
}
/** Skip Ahead (head_9): every purchase of chapter 1 is bought for the player, forks as last run picked them. Journey only. */
function skipChapter(run, journey, st, last) {
  for (let n = 0; n < 80 && !run.ended && journeyChapter(run) < 1; n += 1) {
    const it = journey.shop(8).goal;
    if (!it || it.kind === 'end') break;
    const cost = journey.price(it.id, null);
    run.fuzz += cost; run.total += cost; st.gift += cost;
    const res = it.kind === 'fork'
      ? journey.choose(it.id, st.prepick[it.id] || last?.route?.[it.id] || Object.keys(FORKS[it.id].opts)[0], null)
      : journey.buy(it.id, it.options?.[0]?.[0], null);
    if (!res.ok) { run.fuzz -= cost; run.total -= cost; st.gift -= cost; break; }
  }
}

export function createEngine({ run, life, ui, audio, registry, prestigeRegistry = PRESTIGE_REGISTRY, host = {}, rng = Math.random, save = () => {}, env = {} }) {
  if (!env.journey) delete run.store.journey;
  if (life && typeof life === 'object' && !life.prestige) life.prestige = P.createPrestige();
  const prestigeOf = () => life?.prestige || P.createPrestige();
  /* A brand new run (nothing played, nothing bought) takes the stamp; a saved run keeps the one it started with, and a run
   * from before prestige existed plays as Depth 0. */
  const freshRun = !run.prestige && !run.clock && run.buys === 0 && !run.ended;
  const stamp = freshRun ? stampRun(run, prestigeOf()) : run.prestige || null;
  const journey = env.journey ? createJourney(run) : null;
  if (freshRun && stamp) {
    if (stamp.skip && journey) skipChapter(run, journey, stamp, prestigeOf().last);
    if (stamp.start) { run.fuzz += stamp.start; run.total += stamp.start; stamp.gift += stamp.start; }
  }
  const scopes = new Map();          // unlock id -> scope
  const mods = [];                   // live modifiers, in mount order
  const listeners = new Map();       // event -> Set of {fn, scope}
  let paused = false, suspended = false, stopped = !!run.ended, now = run.clock;
  let endingBegun = false, creditsUp = false, real = run.clock, suspendReason = null;
  const speeds = new Set();
  const guards = new Set();
  const refusals = new Map();        // row id + count -> times refused
  const ads = createAds({ noCcp: () => subscriberPlan(env.caps) });
  const pictures = createPictures({ source: env.pictureSource, scrolller: env.hosted ? env.caps?.scrolller : null,
    send: host.send, niches: () => paletteOf(run).themes, onAdded: source => emit('pictures-added', { source }),
    onProgress: () => emit('pictures-progress'), onMeter: () => emit('picture-meter'),
    onChange: (source) => { emit('pictures', { source }); save(); } });

  /* ---- scopes ---- */
  function newScope(item) {
    return { item, ctx: null, ret: null, els: new Set(), timers: new Set(), frames: new Set(), anims: new Set(), mods: [], subs: new Set(), loops: new Set(), cleanups: new Set(), errors: 0, dead: false };
  }

  function guard(scope, fn, ...args) {
    if (scope.dead) return undefined;
    try { return fn(...args); } catch (e) {
      scope.errors += 1;
      console.error(`unlock ${scope.item.id}:`, e);
      if (scope.errors >= MAX_UNLOCK_ERRORS) unmount(scope.item.id);
      return undefined;
    }
  }

  function dropScope(scope) {
    scope.dead = true;
    for (const f of scope.cleanups) { try { f(); } catch (e) { console.error(e); } }
    for (const a of scope.anims) { try { a.cancel(); } catch { /* gone */ } }
    for (const l of scope.loops) l.stop(300);
    for (const s of scope.subs) s();
    for (const m of scope.mods) { const i = mods.indexOf(m); if (i >= 0) mods.splice(i, 1); }
    for (const el of scope.els) el.remove();
    scope.els.clear(); scope.timers.clear(); scope.frames.clear(); scope.anims.clear(); scope.loops.clear();
  }

  function mount(id, restored = false) {
    const item = BY_ID[id];
    const entry = registry[id];
    if (!item || !entry?.mount) return false;
    if (scopes.has(id)) unmount(id);
    const scope = newScope(item);
    scope.restored = restored;
    scopes.set(id, scope);
    scope.ctx = makeContext(api, scope);
    scope.mounting = true;
    scope.ret = guard(scope, () => entry.mount(scope.ctx)) || {};
    scope.mounting = false;
    return true;
  }

  function unmount(id) {
    const scope = scopes.get(id);
    if (!scope) return;
    scopes.delete(id);
    if (scope.ret?.off) { try { scope.ret.off(); } catch (e) { console.error(e); } }
    dropScope(scope);
  }

  /** restored: this run came from a save, so nothing here just arrived. */
  const mountAll = (restored = false) => {
    if (run.mood) mount(run.mood, restored);
    for (const id of [...run.order]) if (liveOn(run.off, id)) mount(id, restored);
    mountPrestige(restored);
  };

  /* ---- Renewal Sky entries (ui/unlocks/prestige/): mounted at run start when the lane has a star, or always (`always`).
   * Same ctx, timers, pause and panic as any unlock; ctx.pstore is the lane's bag in the life save. ---- */
  function mountEntry(item, entry, lane, restored) {
    if (scopes.has(item.id)) unmount(item.id);
    const scope = newScope(item);
    scope.restored = restored;
    scope.lane = lane;
    scopes.set(item.id, scope);
    scope.ctx = makeContext(api, scope);
    scope.mounting = true;
    scope.ret = guard(scope, () => entry.mount(scope.ctx)) || {};
    scope.mounting = false;
  }
  const prestigeItem = (id, name) => ({ id, name, group: 'prestige', lane: 'prestige', kind: 'prestige', req: [], cost: 0 });
  /* Ending-star perks that are plain numbers, and the run's price scale outside the Journey plan (which scales its own). */
  const corePerks = {
    mount(ctx) {
      const p = prestigeOf();
      if (P.lit(p, 'si_end')) ctx.mod({ price: (v, s, now, it) => (it?.lane === 'look' ? v * 0.5 : v) });
      const scale = stamp?.price || 1;
      if (!journey && scale !== 1) ctx.mod({ price: (v) => v * scale });
    },
  };
  function mountPrestige(restored = false) {
    if (stopped) return;
    const p = prestigeOf();
    if (P.lit(p, 'si_end') || (!journey && (stamp?.price || 1) !== 1)) mountEntry(prestigeItem('prestige_core', 'Renewal Sky'), corePerks, null, restored);
    for (const [lane, entry] of Object.entries(prestigeRegistry)) {
      if (!entry?.mount || !(entry.always || P.level(p, lane) > 0)) continue;
      mountEntry(prestigeItem(`prestige_${lane}`, LANE_BY_ID[lane]?.name || lane), entry, lane, restored);
    }
  }
  const unmountAll = () => { for (const id of [...scopes.keys()]) unmount(id); };

  /* ---- the Control Panel: a row switched off is not mounted, now or on the next load. Its own pay stays in the sim. ---- */
  const isOn = (id) => !run.off[id];
  function setOn(id, on) {
    if (stopped || !switchable(BY_ID[id]) || !E.has(run, id) || !registry[id]?.mount) return false;
    if (on === isOn(id)) return true;
    if (on) delete run.off[id]; else run.off[id] = 1;
    const family = familyOf(id);
    if (family) {
      const owned = run.order.filter((x) => family.includes(x));
      for (const x of [...owned].reverse()) unmount(x);
      for (const x of owned) if (liveOn(run.off, x)) mount(x, true);
      restack();
    } else if (on) { mount(id, true); restack(); }
    else unmount(id);
    syncQuiet();
    save();
    emit('switch', { id, on });
    ui.draw?.(true);
    return true;
  }
  /** A row back on returns to its place: modifiers fold in buy order, as they would after a reload. */
  function restack() {
    const seen = new Set(), next = [];
    for (const id of [run.mood, ...run.order]) {
      const s = id && scopes.get(id);
      if (s && !seen.has(id)) { seen.add(id); next.push(...s.mods); }
    }
    for (const s of scopes.values()) if (!seen.has(s.item.id)) next.push(...s.mods);
    mods.splice(0, mods.length, ...next);
  }

  /* ---- layered looks: the newest word wins, and a row switched back on takes its old place, never the top. ---- */
  let lookSeq = 0;
  const lookPlace = new Map();
  function seqFor(scope, key) {
    const k = `${scope.item.id}|${key}`;
    if (scope.restored && scope.mounting && lookPlace.has(k)) return lookPlace.get(k);   // later, a press is news and wins
    lookSeq += 1;
    lookPlace.set(k, lookSeq);
    return lookSeq;
  }
  /* ctx.vars: each row holds its own values, so letting go of one never restores a value another row has since let go of. */
  const varHolds = new Set();
  const varBase = new Map();
  function paintVar(key) {
    const root = ui.doc.documentElement.style;
    let top = null;
    for (const h of varHolds) if (key in h.map && (!top || h.seq[key] > top.seq[key])) top = h;
    const v = top ? top.map[key] : varBase.get(key);
    if (v) root.setProperty(key, v); else root.removeProperty(key);
    if (!top) varBase.delete(key);
  }
  function holdVars(map, scope) {
    const root = ui.doc.documentElement.style;
    if (!scope.varHold) {
      const fresh = { map: {}, seq: {} };
      scope.varHold = fresh;
      varHolds.add(fresh);
      scope.cleanups.add(() => { varHolds.delete(fresh); for (const k of Object.keys(fresh.map)) paintVar(k); });
    }
    const h = scope.varHold;
    for (const [k, v] of Object.entries(map)) {
      if (!varBase.has(k)) varBase.set(k, root.getPropertyValue(k));
      h.map[k] = v;
      h.seq[k] = seqFor(scope, `var:${k}`);
      paintVar(k);
    }
  }

  /* ---- events ---- */
  function emit(event, data) {
    const set = listeners.get(event);
    if (!set) return;
    for (const sub of [...set]) {
      if (sub.scope) guard(sub.scope, sub.fn, data);
      else sub.fn(data);
    }
  }
  function subscribe(event, fn, scope = null) {
    if (!listeners.has(event)) listeners.set(event, new Set());
    const sub = { fn, scope };
    listeners.get(event).add(sub);
    const off = () => { listeners.get(event)?.delete(sub); scope?.subs.delete(off); };
    scope?.subs.add(off);
    return off;
  }

  /* ---- the clock ---- */
  /** One frame of game time. Nothing an unlock made runs while paused, suspended or after the end. */
  function step(dtMs) {
    if (paused || suspended) return;
    const dt = Math.min(1000, Math.max(0, dtMs));
    if (!run.ended) E.tick(run, dt, mods);   // real time: income and route timing never see ctx.speed
    const scaled = dt * speedFactor();
    now += scaled;
    real += dt;
    if (stopped && !creditsUp) { const s = scopes.get(ENDING_SCOPE); if (s) runScope(s, scaled); return; }
    for (const scope of [...scopes.values()]) runScope(scope, scaled);
  }

  /** Time away with the page open (tab hidden, window minimised, frame off screen, a host focus suspend). Only the
   *  per-second pay accrues (E.away): no unlock runs, no timer or timed buff moves. Pause, panic and the end pay nothing. */
  function away(ms) {
    if (paused || stopped || run.ended || (suspended && suspendReason !== 'focus')) return 0;
    let gain = E.away(run, mods, ms);
    if (gain > 0 && P.lit(prestigeOf(), 'outside')) gain += E.lump(run, gain * 0.25, 'fps');   // Go Outside: away pay +25%
    if (gain > 0) emit('away', { gain, ms });
    return gain;
  }

  /* ---- ctx.speed: every hold multiplies the unlocks' clock, capped, restored when released ---- */
  function speedFactor() {
    let f = 1;
    for (const h of speeds) f *= h.m;
    return Math.min(CONFIG.SPEED_MAX, Math.max(CONFIG.SPEED_MIN, f));
  }
  function applySpeed() {
    const f = speedFactor();
    for (const scope of scopes.values()) for (const a of scope.anims) a.playbackRate = f;
  }
  function holdSpeed(m, scope) {
    if (!(m > 0) || !Number.isFinite(m)) return () => {};
    const h = { m };
    speeds.add(h);
    applySpeed();
    const release = () => { if (speeds.delete(h)) applySpeed(); scope.cleanups.delete(release); };
    scope.cleanups.add(release);
    return release;
  }

  function runScope(scope, dt) {
    for (const t of [...scope.timers]) {
      if (now < t.at) continue;
      if (t.every) { t.at = now + t.every; guard(scope, t.fn); }
      else { scope.timers.delete(t); guard(scope, t.fn); }
    }
    const sec = dt / 1000;
    for (const f of [...scope.frames]) guard(scope, f, sec, now);
    if (scope.ret?.tick) guard(scope, scope.ret.tick, sec, now);
  }

  function setPaused(v) {
    const next = !!v;
    if (next === paused) return;
    if (!suspended) ui.holdWorld?.(next);
    paused = next;
    audio?.hold(paused || suspended);
    emit(paused ? 'pause' : 'resume');
  }
  function setSuspended(v, reason) {
    const next = !!v;
    // Only a host focus suspend still pays away time; any other reason (panic, arcade) wins over it until released.
    if (!next) suspendReason = null;
    else if (!suspended || reason !== 'focus') suspendReason = reason || 'host';
    if (next === suspended) return;
    if (!paused) ui.holdWorld?.(next);
    suspended = next;
    audio?.hold(paused || suspended);  // first: panic never waits on a listener
    emit('suspend', { on: suspended, reason });
  }
  /* ---- verbs ---- */
  /* via: 'hand' = a trusted manual press on #btn (the only kind tap-rate achievements and the EMI curve read), 'right' = the
   * Double Wielding right hand, 'copy' = a bouncing copy, 'key' = the keyboard, '' = anything else. t = its event time. */
  function tap({ x, y, auto = false, via = '', mult = 1, t } = {}) {
    if (paused || suspended || stopped || ui.buttonLocked?.()) return 0;
    audio?.arm();
    const gain = E.tap(run, mods, now, auto, mult);
    ui.pressed?.();
    emit('tap', { gain, x, y, auto, via: auto ? '' : via, mult: auto ? 1 : mult, t });
    ui.draw?.();
    return gain;
  }

  function pay(amount, source = 'lump', at = null, owner = source) {
    if (stopped) return 0;
    const bank = journey ? takeActivity(run, owner) : 0;
    const extra = bank ? E.lump(run,bank,'activity_rank') : 0;
    const adF = source === 'ad' ? adClickFactor(run) * (P.lit(prestigeOf(), 'o_out') ? 2 : 1) : 1;   // Living Ad: ads pay double
    const got = extra + E.sideLump(run, amount * run.pace * adF, source);
    ui.paid?.(got, at);
    ui.draw?.();
    return got;
  }

  function spend(amount, reason) {
    if (stopped) return 0;
    const took = E.spend(run, amount, reason);
    ui.draw?.();
    return took;
  }

  /* ---- ctx.guardBuy: a guard may refuse a row a few times. A fork or ending row is never refused, a row goes through
   * after GUARD_MAX_REFUSALS whatever the guard says, and a guard that throws lets it through. Never an impossible run. */
  function refusal(id) {
    const it = BY_ID[id];
    if (!it || it.kind === 'fork' || it.kind === 'end' || !guards.size) return null;
    if (!E.isOpen(run, it) || run.fuzz < E.price(run, id, mods)) return null;  // would fail anyway: nothing to refuse
    const key = `${id}:${E.count(run, id)}`;
    const n = refusals.get(key) || 0;
    if (n >= CONFIG.GUARD_MAX_REFUSALS) return null;
    for (const g of [...guards]) {
      const ans = guard(g.scope, g.fn, it);
      if (ans?.refuse) { refusals.set(key, n + 1); return ans; }
    }
    return null;
  }
  function addGuard(fn, scope) {
    const g = { fn, scope };
    guards.add(g);
    const off = () => { guards.delete(g); scope.cleanups.delete(off); };
    scope.cleanups.add(off);
    return off;
  }

  function wallHit({ x, y, corner = false } = {}, owner = 'stray') {
    if (paused || suspended || stopped) return 0;
    const gain = E.wallHit(run, mods, now, owner, corner);
    emit('wall', { gain, x, y, corner });
    return gain;
  }

  function afterGain(id, res) {
    noteSeen(life, run);
    const item = res.item;
    /* One purchase, one 'buy': a bundle's riders ride along in `also` (data/folds.js). */
    const also = (res.riders || []).map((r) => BY_ID[r]).filter(Boolean);
    emit('buy', { item, count: res.count, cost: res.cost, also });
    const scope = scopes.get(id);
    delete run.off[id];   // buying a row again means wanting it
    if (scope && scope.ret?.grow) guard(scope, scope.ret.grow, res.count);
    else if (liveOn(run.off, id)) mount(id);
    emit('switch', { id, on: true });
    for (const r of also) {
      delete run.off[r.id];
      if (liveOn(run.off, r.id)) mount(r.id);
      emit('switch', { id: r.id, on: true });
    }
    PE.tickerHeadStart(run, prestigeOf());   // Unit Complete ending (dr_end): Scrolling Thoughts arrives at Rank 2 (Journey)
    syncQuiet();
    save();
    ui.bought?.(item, res);
    ui.draw?.(true);
  }

  function buy(id, option) {
    if (stopped) return { ok: false, reason: 'ended' };
    const refused = refusal(id);
    if (refused) {
      ui.denied?.(id, refused);
      if (typeof refused.refuse === 'string') ui.toast?.(brandText(refused.refuse));
      return { ok: false, reason: 'guarded', line: refused.refuse };
    }
    /* Free Sample / Free Trial (head_2, head_5): the run's first purchases cost nothing. The bank is handed the price for
     * the buy and keeps nothing if it fails. */
    const gift = freeBuy(id) ? rawPrice(id) : 0;
    if (gift) run.fuzz += gift;
    const res = journey ? journey.buy(id, option, mods) : E.buy(run, id, mods);
    if (gift) { if (res.ok) stamp.free -= 1; else run.fuzz -= gift; }
    if (!res.ok) { ui.denied?.(id, res); return res; }
    afterGain(id, res);
    if (run.ended) end(id);
    else if (id === 'onemore') { recordEnding(life, run, id); save(); }
    return res;
  }

  function choose(forkId, lane) {
    const res = journey ? journey.choose(forkId, lane, mods) : E.choose(run, forkId, lane, mods);
    if (!res.ok) return res;
    emit('fork', { fork: forkId, lane });
    afterGain(forkId, { item: res.item, count: 1, cost: 0 });
    return res;
  }

  /** One ending flow for all thirteen. Called with the run already marked ended. A registry entry may add
   *  ending(ctx) -> Promise: its own show between the fade and the credits. */
  async function end(id, { restored = false } = {}) {
    if (endingBegun) return;
    endingBegun = true;
    stopped = true;
    /* take(ctx) -> Promise: an optional phase BEFORE the fade, while the page is still mounted (and frozen: `stopped`
     * stops every unlock's frames). Streams keep playing until it resolves. Capped on the game clock. */
    const take = !restored && registry[id]?.take;
    if (!take) audio?.stopStreams?.();
    emit('ending', { id });
    if (!restored) {
      recordEnding(life, run, id);
      save();
      host.send?.('fuzz-ended', {
        total: Math.round(run.total), minutes: Math.round(run.clock / 60000), items: run.order.length,
        ending: id, route: E.routeOf(run), dayUtc: new Date().toISOString().slice(0, 10),
      });
      if (take) {
        const scope = newScope(BY_ID[id]);
        scopes.set(ENDING_SCOPE, scope);
        scope.ctx = makeContext(api, scope);
        try {
          await Promise.race([take(scope.ctx), new Promise((r) => scope.timers.add({ at: now + CONFIG.ENDING_TAKE_MAX_MS, fn: r }))]);
        } catch (e) { console.error(e); }
        audio?.stopStreams?.();
      }
      audio?.setQuiet(true);
      await ui.ending?.(id, { colour: registry[id]?.fadeColour });
    }
    unmountAll();
    const show = !restored && registry[id]?.ending;
    if (show) {
      audio?.setQuiet(false);
      const scope = newScope(BY_ID[id]);
      scopes.set(ENDING_SCOPE, scope);
      scope.ctx = makeContext(api, scope);
      try {
        await Promise.race([registry[id].ending(scope.ctx), new Promise((r) => setTimeout(r, CONFIG.ENDING_SHOW_MAX_MS))]);
      } catch (e) { console.error(e); }
      unmount(ENDING_SCOPE);
      audio?.setQuiet(true);
    }
    ui.credits?.(id);
    creditsUp = true;
    for (const it of ITEMS) if (it.group === 'after') mount(it.id);
  }

  function syncQuiet() {
    const until = moodOf(run)?.silentUntil;
    audio?.setQuiet(!!until && !until.some((id) => E.has(run, id)));
  }

  /* ---- dev door ---- */
  function grant(id) {
    const added = E.grant(run, id);
    for (const a of added) mount(a);
    syncQuiet();
    ui.draw?.(true);
    return added;
  }

  function rawPrice(id) { return journey ? journey.price(id, mods) : E.price(run, id, mods, now); }
  function freeBuy(id) {
    if (!(stamp?.free > 0) || stopped) return false;
    const it = journey ? journey.item(id) : BY_ID[id];
    return !!it && it.kind !== 'fork' && it.kind !== 'end';
  }
  const prestigeView = P.prestigeView(prestigeOf, () => run.prestige || null);

  // The feed follows the run's themes: Pick a Theme, a Second Theme, or one of them switched off.
  for (const event of ['fork', 'taste2', 'switch']) subscribe(event, () => pictures.refreshNiches());

  const api = {
    get run() { return run; }, life, ui, audio, mods, host, rng, env, scopes, pictures, ads,
    get now() { return now; },
    /** Game time that ctx.speed never touches: for what must always run out on time. */
    get real() { return real; },
    speedFactor, holdSpeed, seqFor, holdVars,
    switches: { isOn, set: setOn, waitingOn: (id) => (run.off[parentOf(id)] ? parentOf(id) : null), groups: () => panelGroups(run, (id) => !!registry[id]?.mount) },
    get paused() { return paused; },
    get suspended() { return suspended; },
    get suspendReason() { return suspendReason; },
    get stopped() { return stopped; },
    mount, unmount, mountAll, unmountAll, emit, subscribe, step, away,
    awayCap: () => E.awayCap(run, mods),
    setPaused, setSuspended, tap, pay, spend, addGuard, wallHit, buy, choose, end, grant, syncQuiet, save,
    rates: () => E.rates(run, mods, now),
    item: (id) => journey ? journey.item(id) : BY_ID[id],
    funds: (id) => journey && FORKS[id] ? run.total : run.fuzz,
    price: (id) => freeBuy(id) ? 0 : rawPrice(id),
    /* Renewal Sky. prestige: the read-only view ctx.prestige shows; pstore(lane): that lane's bag in the life save;
     * renew(): finish + New run bookkeeping (sim/prestige.js renew), saved at once. */
    prestige: prestigeView,
    pstore: (lane) => { const p = prestigeOf(); return (p.store[lane] ||= {}); },
    renew() { const r = P.renew(life, run); save(); return r; },
    get freeBuys() { return stamp?.free || 0; },
    shop: (size) => journey ? journey.shop(size) : E.shopWindow(run, mods, size, env.featuredShop),
    endingsTotal: ENDING_IDS.length,
  };
  return api;
}
