/* ui/stage.js - small buys get the stage (owner 2026-11-10). The registry ambient look unlocks join through
 * `ctx.stage`, and the per-frame runner of ui/stage-model.js: the solo after a look buy (the others ease down, a paper tag
 * names the new one, a few small pays float up) and the cast of three (the rest idle and rotate in).
 *
 * A target is an element (the stage writes its inline opacity, so give it a holder nothing else animates: `ctx.stage.holder`)
 * or a function `setStrength(0..1)` that turns the effect's own knob. Only opacity or that knob, never a filter, and no
 * layout reads per frame: the tag measures the counter once when a solo starts. State the player chose (pins, "everything
 * at once") lives in run.store.stage; the solo itself is not saved, so a reload mid-solo just drops it. */

import { brandText } from '../data/brand.js';
import { BY_ID } from '../data/items.js';
import { LITE } from './perf.js';
import {
  approach, initialStage, soloAsk, soloPaysDue, soloTag, stageIdOf, stageTargets, startSolo, STEADY, SOLO_MS,
} from './stage-model.js';

const TAG_CSS = `
.stage-tag { position:absolute;left:0;top:0;pointer-events:none;white-space:nowrap;padding:5px 12px 6px;
  font:700 14px/1.1 var(--shop-font, var(--font));color:#2b2230;background:#fffaf0;border:1.5px solid #2b2230;border-radius:3px 6px 4px 7px;
  box-shadow:2px 3px 0 rgba(43,34,48,.28);transform-origin:30% 100%; }
.stage-tag::before { content:'';position:absolute;left:50%;top:-6px;width:26px;height:10px;margin-left:-13px;
  background:color-mix(in srgb,var(--accent-soft,#ffd3ea) 75%,transparent);border:1px solid rgba(43,34,48,.25);transform:rotate(-4deg); }
`;

const isEl = (t) => !!t && typeof t === 'object' && 'style' in t;

export function createStage({ doc = null, engine, lite = LITE, gentle = () => false, reduced = () => false } = {}) {
  /** id -> { id, members: Map(scope -> { item, targets: Set }), cur, written: Map(target -> value) } */
  const entries = new Map();
  let solo = null, tag = null, cssDone = false;

  function state() {
    const run = engine.run;
    if (!run) return initialStage();
    const st = (run.store.stage ||= initialStage(run.owned));
    st.pins ||= []; st.at ||= {};
    return st;
  }
  const emit = () => engine.emit?.('stage', {});
  const orderOf = (itemId) => engine.run?.order?.indexOf(itemId) ?? -1;
  /** The family's face: its newest owned row. */
  function face(e) {
    let best = null;
    for (const m of e.members.values()) if (!best || orderOf(m.item.id) > orderOf(best.id)) best = m.item;
    return best;
  }
  const liveOf = (e) => [...e.members.values()].some((m) => m.targets.size > 0);

  function write(e, target, s) {
    const last = e.written.get(target);
    if (last !== undefined && Math.abs(last - s) < 0.01 && !(s === 1 && last !== 1) && !(s === 0 && last !== 0)) return;
    e.written.set(target, s);
    try {
      if (isEl(target)) target.style.opacity = s >= 0.999 ? '' : s.toFixed(3);
      else target(s);
    } catch (err) { console.error('stage target failed', err); }
  }
  function release(e, target) {
    e.written.delete(target);
    try { if (isEl(target)) target.style.opacity = ''; else target(1); } catch {}
  }

  function css() {
    if (cssDone || !doc?.head) return;
    cssDone = true;
    const s = doc.createElement('style');
    s.textContent = TAG_CSS;
    doc.head.appendChild(s);
  }
  function dropTag() { tag?.el?.remove(); tag = null; }
  function showTag(name) {
    dropTag();
    const layer = doc?.getElementById?.('l-fx');
    if (!layer || !doc.createElement) return;
    css();
    const t = doc.createElement('div');
    t.className = 'stage-tag';
    t.setAttribute?.('aria-hidden', 'true');
    t.textContent = soloTag(name);
    layer.appendChild(t);
    const win = doc.defaultView;
    /* A sticker on the button's top right corner, where the eye already is. Clicks go through it. */
    const r = doc.getElementById('btn')?.getBoundingClientRect?.();
    const w = t.offsetWidth || 120, h = t.offsetHeight || 26, vw = win?.innerWidth || 800, vh = win?.innerHeight || 600;
    const x = Math.max(8, Math.min(vw - w - 8, r ? r.right - w * 0.35 : vw / 2 - w / 2));
    const y = Math.max(52, Math.min(vh - h - 8, r ? r.top - h * 0.6 : 72));
    t.style.left = `${x}px`; t.style.top = `${y}px`;
    tag = { el: t, x: x + w / 2, y: y + 14 };
    const calm = gentle() || reduced();
    const frames = calm
      ? [{ opacity: 0 }, { opacity: 1, offset: 0.12 }, { opacity: 1, offset: 0.86 }, { opacity: 0 }]
      : [{ opacity: 0, transform: 'translateY(-8px) rotate(-7deg) scale(.7)' },
        { opacity: 1, transform: 'translateY(0) rotate(-1deg) scale(1.06)', offset: 0.07 },
        { opacity: 1, transform: 'rotate(-2deg) scale(1)', offset: 0.12 },
        { opacity: 1, transform: 'rotate(-2deg) scale(1)', offset: 0.88 },
        { opacity: 0, transform: 'translateY(4px) rotate(-3deg) scale(.96)' }];
    const a = t.animate?.(frames, { duration: SOLO_MS, easing: 'ease-out', fill: 'both' });
    if (a) a.onfinish = () => { if (tag?.el === t) dropTag(); else t.remove(); };
  }

  function join(scope, item, id) {
    let e = entries.get(id);
    if (!e) { e = { id, members: new Map(), cur: 1, written: new Map() }; entries.set(id, e); }
    let m = e.members.get(scope);
    if (!m) {
      m = { item, targets: new Set() };
      e.members.set(scope, m);
      scope.cleanups?.add(() => {
        for (const t of m.targets) release(e, t);
        e.members.delete(scope);
        if (!e.members.size) entries.delete(id);
        emit();
      });
      /* A real buy, not a reload or a switch back on: the new thing gets its solo. */
      const run = engine.run;
      if (!scope.restored && run && !run.ended) {
        state().at[id] = run.clock;
        solo = startSolo(id, brandText(item.name), run.clock);
        showTag(solo.name);
      }
      emit();
    }
    return { e, m };
  }

  function register(scope, item, a, b) {
    let id, target;
    if (typeof a === 'string') { id = a; target = b; } else { id = stageIdOf(item.id) || item.id; target = a; }
    const { e, m } = join(scope, item, id);
    if (!target) return () => {};
    m.targets.add(target);
    write(e, target, e.cur);
    return () => { if (m.targets.delete(target)) release(e, target); };
  }

  function modelEntries() {
    const st = state();
    return [...entries.values()].map((e) => {
      const f = face(e);
      return { id: e.id, order: f ? orderOf(f.id) : -1, at: st.at[e.id], steady: STEADY.has(e.id), live: liveOf(e) };
    });
  }

  function tick(dt) {
    const run = engine.run;
    if (!run || run.ended || engine.stopped) {
      for (const e of entries.values()) { e.cur = 1; for (const m of e.members.values()) for (const t of m.targets) write(e, t, 1); }
      solo = null; dropTag();
      return;
    }
    if (engine.paused || engine.suspended || !entries.size) return;
    const st = state(), now = run.clock;
    const { strengths } = stageTargets(modelEntries(), now, { solo, all: !!st.all, pins: st.pins, lite });
    for (const e of entries.values()) {
      e.cur = approach(e.cur, strengths[e.id] ?? 1, dt);
      for (const m of e.members.values()) for (const t of m.targets) write(e, t, e.cur);
    }
    if (solo) {
      for (let n = soloPaysDue(solo, now); n > 0; n--) {
        solo.paid += 1;
        const at = tag ? { x: tag.x + (Math.random() - 0.5) * 70, y: tag.y } : null;
        engine.pay?.(soloAsk(engine.rates?.() || {}), 'stage', at, 'stage');
      }
      if (now >= solo.until) { solo = null; dropTag(); }
    }
  }

  /** The Control Panel's view: one row per ambient effect. */
  function list() {
    const st = state();
    const model = modelEntries();
    const { cast } = stageTargets(model, engine.run?.clock || 0, { all: !!st.all, pins: st.pins, lite });
    return model.filter((m) => !m.steady && m.live).sort((a, b) => a.order - b.order).map((m) => {
      const item = face(entries.get(m.id)) || BY_ID[m.id];
      return { id: m.id, item, name: brandText(item?.name || m.id), pinned: st.pins.includes(m.id), cast: cast.has(m.id) };
    });
  }
  function pin(id, on) {
    const st = state();
    st.pins = st.pins.filter((p) => p !== id);
    if (on) st.pins.push(id);
    emit();
  }
  function setAll(on) { state().all = !!on; emit(); }

  function forScope(scope, item) {
    return Object.freeze({
      register: (a, b) => register(scope, item, a, b),
      list, pin, setAll,
      get all() { return !!state().all; },
      get lite() { return !!lite; },
    });
  }

  return { forScope, tick, list, pin, setAll, debug: () => ({ now: engine.run?.clock, model: modelEntries(), state: state() }), get all() { return !!state().all; }, get solo() { return solo; }, entries };
}
