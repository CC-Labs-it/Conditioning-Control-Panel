/* ============================================================================
 * ui/preview-bar.js - the dev preview bar (owner 2026-11-21: "i wanna fast forward in the preview build and even skip to the
 * salient points or ending"; handoff-1121/TC-PREVIEW-BAR-BRIEF.md). Bottom left, folds to one chip. main.js imports it
 * only when ui/preview-bar-model.js previewBarAllowed() says so: local and *.pages.dev previews, never tc.cclabs.app,
 * never framed by app.cclabs.app, never in a CCP host. Nothing here is saved with the run; its clicks stop at the bar.
 *   Time     1x / 4x / 16x / 60x on the engine's per-frame dt (income, timers, journey minutes, golden buttons all follow)
 *   Skip     +5 / +15 min: the reference player (dev/journey-timing.mjs) plays those minutes on the real run
 *   Jump to  the same player, stopped where a salient point shows on the shelf
 *   Endings  every ending, started the way buying its tile starts it
 *   Setup    herd 3-12, Renewal Depth 0-10, Reset save
 * ==========================================================================*/

import { BY_ID, ITEMS } from '../data/items.js';
import * as E from '../sim/economy.js';
import { noteSeen, recordEnding } from '../sim/save.js';
import { liveOn } from '../sim/switches.js';
import { prestigeNow, PRESTIGE_EVENT } from './prestige-sky.js';
import { WARPS, SKIPS, HERD_MIN, HERD_MAX, TARGETS, warpSlices, runClock, playAhead, journeyShop, plainShop, depthSetup, startsOpen } from './preview-bar-model.js';
import { MAX_DEPTH } from '../data/prestige.js';

const OPEN_KEY = 'tc.previewBar.open';
/** Endless Mode is a row that keeps the run going, not an ending to watch. */
export const BAR_ENDINGS = Object.freeze(ITEMS.filter((it) => it.kind === 'end' && it.id !== 'onemore').map((it) => it.id));
/** The endings the bar offers on this run: small never jumps to a cut ending (the Expo). */
export const barEndings = (s) => BAR_ENDINGS.filter((id) => !E.asleep(s, id));
export const JUMPS = Object.freeze([
  ['theme', TARGETS.theme.label],
  ['taste2', TARGETS.taste2.label],
  ['fate', TARGETS.fate.label],
  ['end', TARGETS.end.label],
  ['credits', 'Credits card'],
  ['renew', 'Credits, then Renew'],
]);
const endName = (id) => String(BY_ID[id]?.name || id).replace(/^Ending:\s*/, '');

const CSS = `
#tc-preview-bar{position:fixed;left:max(8px,env(safe-area-inset-left));bottom:calc(env(safe-area-inset-bottom,0px) + var(--dock-h,34px) + 8px);z-index:2147483200;
  font:12px/1.3 system-ui,'Segoe UI',sans-serif;color:#f4eef6;max-width:min(430px,calc(100vw - 16px));pointer-events:auto;user-select:none}
#tc-preview-bar *{box-sizing:border-box;animation:none!important;transition:none!important}
#tc-preview-bar button,#tc-preview-bar select,#tc-preview-bar input{font:inherit;color:inherit}
#tc-preview-bar .pb-chip{display:flex;gap:6px;align-items:center;background:#211a26;border:1px solid #6d5a78;border-radius:999px;
  padding:4px 10px;min-height:28px;cursor:pointer;opacity:.85}
#tc-preview-bar .pb-chip:hover,#tc-preview-bar .pb-chip:focus-visible{opacity:1}
#tc-preview-bar .pb-chip b{font-variant-numeric:tabular-nums;font-weight:600}
#tc-preview-bar .pb-panel{margin-bottom:6px;background:#211a26f2;border:1px solid #6d5a78;border-radius:10px;padding:8px;
  box-shadow:0 6px 20px #0008;max-height:min(70vh,520px);overflow:auto}
#tc-preview-bar .pb-row{display:flex;flex-wrap:wrap;gap:4px;align-items:center;margin:0 0 6px}
#tc-preview-bar .pb-k{width:58px;color:#c9b6d3;font-weight:600}
#tc-preview-bar .pb-b{background:#352a3d;border:1px solid #6d5a78;border-radius:6px;padding:3px 8px;min-height:24px;cursor:pointer}
#tc-preview-bar .pb-b:hover{background:#463650}
#tc-preview-bar .pb-b[aria-pressed=true]{background:#f2c84b;color:#211a26;border-color:#f2c84b}
#tc-preview-bar .pb-b:disabled{opacity:.45;cursor:default}
#tc-preview-bar select,#tc-preview-bar input[type=number]{background:#352a3d;border:1px solid #6d5a78;border-radius:6px;padding:2px 4px;min-height:24px}
#tc-preview-bar input[type=number]{width:52px}
#tc-preview-bar .pb-clock{margin-left:auto;font-variant-numeric:tabular-nums;font-weight:600}
#tc-preview-bar .pb-ends{display:flex;flex-wrap:wrap;gap:4px;flex:1 1 300px}
#tc-preview-bar .pb-note{color:#e8dcee;min-height:1.3em;margin:2px 0 0}
#tc-preview-bar label{display:inline-flex;gap:4px;align-items:center;cursor:pointer}
`;

/**
 * engine/ui: the live game; life + hooks: main.js's (forget, renew). remountPen(): draws the llama pen again after a herd
 * change. Returns { el, warp, setWarp, skip, jump, playEnding, setHerd, setDepth, destroy }.
 */
export function mountPreviewBar({ doc = document, win = doc.defaultView, engine, ui, life, hooks = {}, remountPen = () => {} }) {
  doc.getElementById('tc-preview-bar')?.remove();
  if (!doc.getElementById('tc-preview-bar-css')) {
    const st = doc.createElement('style'); st.id = 'tc-preview-bar-css'; st.textContent = CSS; doc.head.append(st);
  }
  const run = () => engine.run;
  let warp = 1;

  /* ---- Time: the engine's step sees warp x the frame. ui/app.js calls engine.step each frame by name, so wrapping the
   * api's property is enough; the engine clamps one step at a second, so a warped frame goes in as several. ---- */
  const realStep = engine.step;
  engine.step = (dt) => { for (const d of warpSlices(dt, warp)) realStep(d); };

  const root = doc.createElement('div');
  root.id = 'tc-preview-bar';
  root.dataset.html2canvasIgnore = '';   // the Living Ad card export never prints it
  root.setAttribute('data-html2canvas-ignore', 'true');
  root.setAttribute('role', 'region');
  root.setAttribute('aria-label', 'Preview bar');
  const b = (label, cls = 'pb-b') => { const x = doc.createElement('button'); x.type = 'button'; x.className = cls; x.textContent = label; return x; };
  const row = (key) => { const r = doc.createElement('div'); r.className = 'pb-row'; const k = doc.createElement('span'); k.className = 'pb-k'; k.textContent = key; r.append(k); return r; };

  const panel = doc.createElement('div'); panel.className = 'pb-panel'; panel.hidden = true; panel.id = 'tc-preview-panel';
  const chip = b('', 'pb-chip'); chip.setAttribute('aria-controls', panel.id);
  const chipWord = doc.createElement('span'); chipWord.textContent = 'Preview';
  const chipClock = doc.createElement('b');
  const chipWarp = doc.createElement('span');
  chip.append(chipWord, chipClock, chipWarp);

  /* Time */
  const time = row('Time');
  const warpButtons = WARPS.map((w) => { const x = b(`${w}x`); x.dataset.warp = String(w); x.addEventListener('click', () => setWarp(w)); return x; });
  const clock = doc.createElement('span'); clock.className = 'pb-clock'; clock.title = 'Run time';
  time.append(...warpButtons, clock);
  /* Skip */
  const skipRow = row('Skip');
  for (const m of SKIPS) { const x = b(`+${m} min`); x.dataset.skip = String(m); x.addEventListener('click', () => skip(m)); skipRow.append(x); }
  /* Jump to */
  const jumpRow = row('Jump to');
  const jumpSel = doc.createElement('select'); jumpSel.setAttribute('aria-label', 'Jump to');
  for (const [k, label] of JUMPS) { const o = doc.createElement('option'); o.value = k; o.textContent = label; jumpSel.append(o); }
  const go = b('Go'); go.dataset.jump = '';
  go.addEventListener('click', () => jump(jumpSel.value));
  const herdBox = doc.createElement('label');
  const herdFull = doc.createElement('input'); herdFull.type = 'checkbox'; herdFull.id = 'pb-herd12';
  herdBox.append(herdFull, doc.createTextNode('Full herd (12)'));
  jumpRow.append(jumpSel, go, herdBox);
  /* Endings */
  const endRow = row('Endings');
  const ends = doc.createElement('div'); ends.className = 'pb-ends';
  for (const id of barEndings(run())) { const x = b(endName(id)); x.dataset.ending = id; x.title = id; x.addEventListener('click', () => playEnding(id)); ends.append(x); }
  endRow.append(ends);
  /* Setup */
  const setup = row('Setup');
  const num = (label, min, max, value, on) => {
    const l = doc.createElement('label'); const i = doc.createElement('input');
    i.type = 'number'; i.min = String(min); i.max = String(max); i.step = '1'; i.value = String(value);
    i.addEventListener('change', () => on(Math.max(min, Math.min(max, Math.floor(Number(i.value) || min)))));
    l.append(doc.createTextNode(label), i); return [l, i];
  };
  const [herdL, herdIn] = num('Herd', HERD_MIN, HERD_MAX, Math.max(HERD_MIN, Number(life.llamas) || HERD_MIN), (n) => setHerd(n));
  const [depthL, depthIn] = num('Depth', 0, MAX_DEPTH, Number(life.prestige?.depth) || 0, (n) => setDepth(n));
  const reset = b('Reset save');
  reset.addEventListener('click', () => {
    if (!reset.dataset.sure) { reset.dataset.sure = '1'; reset.textContent = 'Sure? Press again'; return; }
    hooks.forget?.();
  });
  setup.append(herdL, depthL, reset);
  const note = doc.createElement('p'); note.className = 'pb-note'; note.setAttribute('role', 'status');
  panel.append(time, skipRow, jumpRow, endRow, setup, note);
  root.append(panel, chip);
  /* Its clicks and keys never reach the game (a press, a purchase, the button's Space). Escape still pauses. */
  for (const t of ['pointerdown', 'pointerup', 'mousedown', 'mouseup', 'click', 'dblclick', 'touchstart', 'touchend', 'contextmenu']) root.addEventListener(t, (e) => e.stopPropagation());
  for (const t of ['keydown', 'keyup']) root.addEventListener(t, (e) => { if (e.key !== 'Escape') e.stopPropagation(); });
  doc.body.append(root);

  const say = (t) => { note.textContent = t; };
  const setOpen = (open) => {
    panel.hidden = !open;
    chip.setAttribute('aria-expanded', open ? 'true' : 'false');
    try { win.sessionStorage?.setItem(OPEN_KEY, open ? '1' : '0'); } catch { /* private window */ }
  };
  chip.addEventListener('click', () => setOpen(panel.hidden));
  let wasOpen = startsOpen(win.location);
  if (!wasOpen) { try { wasOpen = win.sessionStorage?.getItem(OPEN_KEY) === '1'; } catch { wasOpen = false; } }
  setOpen(wasOpen);

  function setWarp(w) {
    warp = WARPS.includes(w) ? w : 1;
    for (const x of warpButtons) x.setAttribute('aria-pressed', x.dataset.warp === String(warp) ? 'true' : 'false');
    chipWarp.textContent = warp > 1 ? `${warp}x` : '';
  }
  setWarp(1);
  const paint = () => {
    const t = runClock(run().clock);
    clock.textContent = t; chipClock.textContent = t;
    const over = engine.stopped || !!run().ended;
    for (const x of [...skipRow.querySelectorAll('button'), go]) x.disabled = over;
    /* A Renew or the Sky changes the depth under the bar: follow it unless the field is being typed in. */
    if (doc.activeElement !== depthIn) depthIn.value = String(Number(life.prestige?.depth) || 0);
    if (doc.activeElement !== herdIn) herdIn.value = String(Math.max(HERD_MIN, Number(life.llamas) || HERD_MIN));
  };
  paint();
  const painter = win.setInterval(paint, 250);

  /* ---- stepping the reference player on the real run, then letting the page catch up as a restored run would ---- */
  const shopFor = () => engine.journey ? journeyShop(engine.journey, engine.mods) : plainShop(run(), engine.mods, engine.env?.featuredShop || []);
  function settle(res) {
    const s = run();
    noteSeen(life, s);
    for (const [fork, lane] of res.forks) engine.emit('fork', { fork, lane });
    for (const id of new Set([...res.bought, ...res.forks.map(([f]) => f)])) {
      const it = BY_ID[id];
      if (!it || it.kind === 'end') continue;
      const scope = engine.scopes.get(id);
      if (scope?.ret?.grow) { try { scope.ret.grow(E.count(s, id)); } catch (e) { console.error(e); } }
      else if (!scope && liveOn(s.off, id)) engine.mount(id, true);
    }
    /* Riders and lane rows a fork opened: anything owned and live that nothing mounted yet. */
    for (const id of s.order) if (!engine.scopes.has(id) && liveOn(s.off, id) && BY_ID[id]?.kind !== 'end') engine.mount(id, true);
    engine.syncQuiet?.();
    ui.drawPhrase?.();
    ui.draw?.(true);
    engine.save?.();
  }
  function advance(opts) {
    if (engine.stopped || run().ended) { say('The run is over. Reset save for a new one.'); return null; }
    const res = playAhead(run(), shopFor(), { ...opts, mods: engine.mods });
    settle(res);
    paint();
    return res;
  }
  function skip(minutes) {
    const res = advance({ ms: minutes * 60000 });
    if (!res) return null;
    const what = `${res.bought.length} buys${res.forks.length ? `, ${res.forks.length} choices` : ''}`;
    say(`Played ${Math.round(res.ms / 60000)} min (${what}). Run at ${runClock(run().clock)}.`);
    return res;
  }
  function jump(key) {
    if (key === 'fate' && herdFull.checked) setHerd(HERD_MAX);
    const until = key === 'credits' || key === 'renew' ? 'end' : key;
    const res = advance({ until });
    if (!res) return null;
    const label = TARGETS[until].label;
    if (res.reason === 'past') { say(`Already past ${label}. Reset save to see it again.`); return res; }
    if (res.reason !== 'target') { say(`Did not reach ${label} (${res.reason}) at ${runClock(run().clock)}.`); return res; }
    if (key !== 'credits' && key !== 'renew') { say(`${label}, ready to buy. Run at ${runClock(run().clock)}.`); return res; }
    /* The ending tile, bought by the shop, then straight to the credits card (the ending show is the Endings row's job). */
    const s = run(), shop = shopFor(), sh = shop.shop(8);
    const tile = [sh.goal, ...sh.rows].find((it) => it?.kind === 'end');
    const bought = tile && shop.buy(tile.id);
    if (!bought?.ok || !s.ended) { say(`The ending tile did not sell (${bought?.reason || 'none'}).`); return res; }
    recordEnding(life, s, s.ended.id);
    engine.save?.();
    engine.end(s.ended.id, { restored: true });
    paint();
    say(`Credits for ${endName(s.ended.id)}. Run at ${runClock(s.clock)}.`);
    if (key === 'renew') pressRenew();
    return res;
  }
  function pressRenew(tries = 0) {
    const btn = doc.querySelector('.rr-renew');
    if (btn && !btn.hidden) { btn.click(); return; }
    if (tries < 40) win.setTimeout(() => pressRenew(tries + 1), 100);
    else say('No Renew button on the credits card.');
  }

  /* ---- Endings: the chain owned, then the ending begins as its tile's purchase begins it (fuzz.end) ---- */
  function playEnding(id) {
    const it = BY_ID[id];
    if (!it || it.kind !== 'end' || E.asleep(run(), id)) return false;   // small: a cut ending is never played
    if (engine.stopped || run().ended) { say('An ending already ran. Reset save first.'); return false; }
    if (id === 'll_end') setHerd(HERD_MAX);   // The Panel needs a full pen
    engine.audio?.arm?.();
    for (const q of E.needsOf(run(), it).req) engine.grant(q);   // through the edition (reqFor in small)
    const s = run();
    E.grant(s, id);
    s.ended = { id, at: s.clock };
    engine.end(id);
    setOpen(false);
    paint();
    return true;
  }

  /* ---- Setup ---- */
  function setHerd(n) {
    const k = Math.max(HERD_MIN, Math.min(HERD_MAX, Math.floor(Number(n) || HERD_MIN)));
    life.llamas = k;
    herdIn.value = String(k);
    if (k === HERD_MAX) herdFull.checked = true;
    try { remountPen(); } catch (e) { console.error(e); }
    ui.draw?.(true);
    engine.save?.();
    say(`Herd: ${k}.`);
    return k;
  }
  function setDepth(d) {
    const P = prestigeNow();
    const p = depthSetup(d, P.createPrestige());
    life.prestige = p;
    depthIn.value = String(p.depth);
    try { doc.dispatchEvent(new win.CustomEvent(PRESTIGE_EVENT)); } catch { /* nobody listens */ }
    ui.draw?.(true);
    engine.save?.();
    say(p.depth ? `Depth ${p.depth}, ${Object.keys(p.own).length} stars, ${p.dust} Stardust. The run's pay follows from the next run.` : 'Depth 0.');
    return p;
  }

  function destroy() {
    win.clearInterval(painter);
    engine.step = realStep;
    root.remove();
  }
  return { el: root, get warp() { return warp; }, setWarp, skip, jump, playEnding, setHerd, setDepth, setOpen, destroy };
}
