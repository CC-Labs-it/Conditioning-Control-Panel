/* ============================================================================
 * smoke/reactions-smoke.mjs - the crowd's polish pass (2026-10-14), in node:
 *
 *   node smoke/reactions-smoke.mjs
 *
 * Checked: the anti-spam guard (three in a row, one back every 1.5 s); the six
 * sound plans (every id has one, quiet, short, no speech: oscillators and noise
 * only) and that each one schedules on a fake AudioContext, also through
 * sfx.js `react`; the room's sounds (one per id, at most three, spaced); and
 * the crowd on a tiny fake DOM: a press emits watch-react, sounds and flies,
 * the fourth quick press is kept back and greys the bar, the mute button and
 * the Options rows flip the prefs, a seat with "Play watchers' reactions" off
 * neither shows nor plays the room, the stands always do.
 * ==========================================================================*/

import { createBus } from '../game/events.js';
import { createGuard, GUARD } from '../ui/reactions-guard.js';
import { reactCuePlan, createReactCues, REACT_SOUND } from '../audio/react-cues.js';
import { createSfx } from '../audio/sfx.js';
import { REACTION_IDS, bucketSounds, floatPlan, muteLabel, createReactions, REACT_TUNING } from '../ui/reactions.js';
import { reactPrefs, setReactPrefs } from '../ui/reactions-prefs.js';

let passed = 0;
const failures = [];
const ok = (what, cond) => { if (cond) passed++; else failures.push(what); };
const eq = (what, got, want) => ok(`${what} (got ${JSON.stringify(got)}, wanted ${JSON.stringify(want)})`, JSON.stringify(got) === JSON.stringify(want));
const DASH = new RegExp('[' + String.fromCharCode(0x2013, 0x2014) + ']|\\s-\\s|!');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// ---- the guard
{
  let t = 0;
  const g = createGuard({ now: () => t });
  eq('burst and pace', [GUARD.burst, GUARD.everyMs], [3, 1500]);
  const a = g.take(), b = g.take(), c = g.take();
  ok('three in a row go out', a.ok && b.ok && c.ok);
  ok('the third rests the bar', c.waitMs > 0 && c.waitMs <= 1500);
  const d = g.take();
  ok('the fourth is kept back', !d.ok && d.waitMs > 0);
  t += 1499;
  ok('...still kept back just before the refill', !g.take().ok);
  t += 2;
  ok('one back after 1.5 s', g.take().ok);
  ok('...and only one', !g.take().ok);
  t += 10000;
  eq('a long rest refills to the burst, no more', g.left(), 3);
  g.take(); g.reset();
  eq('reset', g.left(), 3);
}

// ---- the sound plans, pure
{
  const rand = () => 0.5;
  for (const id of REACTION_IDS) {
    const plan = reactCuePlan(id, rand);
    ok(`${id} has a plan`, plan.length > 0);
    ok(`${id} is quiet`, plan.every((v) => v.gain > 0 && v.gain <= REACT_SOUND.maxGain));
    ok(`${id} is short`, plan.every((v) => v.at >= 0 && v.at + v.sec <= REACT_SOUND.maxSec));
    ok(`${id} is tones and noise only`, plan.every((v) => ['sine', 'triangle', 'square', 'sawtooth', 'noise'].includes(v.wave)));
    ok(`${id} ramps stay positive`, plan.every((v) => v.hz > 0 && v.to > 0));
  }
  eq('an unknown id plays nothing', reactCuePlan('boo'), []);
  const sigs = REACTION_IDS.map((id) => JSON.stringify(reactCuePlan(id, rand).map((v) => [v.wave, Math.round(v.hz)])));
  eq('six different cues', new Set(sigs).size, 6);
}

// ---- a fake Web Audio graph (the same shape as sfx-smoke's)
class Param {
  constructor(v) { this.value = v; }
  setValueAtTime(v) { this.value = v; return this; }
  linearRampToValueAtTime() { return this; }
  exponentialRampToValueAtTime(v) { if (!(v > 0)) throw new Error('exponential ramp to ' + v); return this; }
  setTargetAtTime(v) { this.value = v; return this; }
  cancelScheduledValues() { return this; }
}
class FakeNode {
  constructor(ctx, kind) { this.ctx = ctx; this.kind = kind; }
  connect(to) { this.ctx.wires.push([this.kind, (to && to.kind) || 'destination']); return to; }
  disconnect() {}
  start(t) { this.ctx.started.push({ kind: this.kind, at: t }); }
  stop() {}
}
class FakeContext {
  constructor() { this.state = 'running'; this.currentTime = 0; this.sampleRate = 48000; this.started = []; this.wires = []; this.destination = { kind: 'destination' }; }
  createGain() { const n = new FakeNode(this, 'gain'); n.gain = new Param(1); return n; }
  createOscillator() { const n = new FakeNode(this, 'osc'); n.frequency = new Param(440); n.detune = new Param(0); return n; }
  createBiquadFilter() { const n = new FakeNode(this, 'filter'); n.frequency = new Param(1000); n.Q = new Param(1); return n; }
  createBufferSource() { const n = new FakeNode(this, 'noise'); n.detune = new Param(0); return n; }
  createStereoPanner() { const n = new FakeNode(this, 'pan'); n.pan = new Param(0); return n; }
  createDelay() { const n = new FakeNode(this, 'delay'); n.delayTime = new Param(0); return n; }
  createBuffer(ch, len) { return { getChannelData: () => new Float32Array(len) }; }
  resume() { return Promise.resolve(); }
  close() { return Promise.resolve(); }
}
{
  const ctx = new FakeContext();
  const master = ctx.createGain();
  const cues = createReactCues({ ctx, master, noise: {}, rand: () => 0.3 });
  for (const id of REACTION_IDS) {
    const before = ctx.started.length;
    let n = 0, threw = null;
    try { n = cues.play(id, { soft: true, pan: 0.35 }); } catch (e) { threw = e.message; }
    ok(`${id} schedules${threw ? ' (' + threw + ')' : ''}`, !threw && n > 0 && ctx.started.length - before === n);
  }
  ok('the room pans', ctx.wires.some(([k]) => k === 'pan'));
  eq('an unknown id starts nothing', cues.play('spam'), 0);
}
{
  const ctx = new FakeContext();
  const win = { PBP: { settings: { sfxVolume: 0.6 } } };
  const sfx = createSfx({ bus: createBus(), doc: null, win, context: () => ctx });
  ok('sfx plays react', sfx.play('react', { id: 'clap' }));
  eq('...and logs it', sfx.log().at(-1).cues, ['clap']);
  ok('...on real voices', ctx.started.length >= 6);
  sfx.dispose();
}

// ---- the room's sounds, pure
{
  const plan = [{ id: 'fire', atMs: 0 }, { id: 'fire', atMs: 100 }, { id: 'wow', atMs: 120 }, { id: 'clap', atMs: 900 }, { id: 'eyes', atMs: 950 }];
  const s = bucketSounds(plan);
  eq('one per id, three at most', s.map((x) => x.id), ['fire', 'wow', 'clap']);
  ok('spaced out', s[1].atMs - s[0].atMs >= 240);
  eq('nothing for nothing', bucketSounds([]), []);
  let k = 7;
  const rand = () => { k = (k * 16807) % 2147483647; return (k - 1) / 2147483646; };
  ok('a busy bucket stays at three', bucketSounds(floatPlan({ fire: 50, wow: 9, laugh: 3, clap: 4, heart: 1, eyes: 2 }, { rand })).length === 3);
  for (const line of [muteLabel(true), muteLabel(false)]) ok(`no dash or bang: ${line}`, !DASH.test(line));
}

// ---- the prefs (no storage in node: in memory)
{
  eq('both on by default', reactPrefs(), { show: true, sound: true });
  setReactPrefs({ show: false, sound: 'loud' });
  eq('only booleans are taken', reactPrefs(), { show: false, sound: true });
  setReactPrefs({ show: true });
}

// ---- the crowd on a tiny fake DOM
class El {
  constructor(tag) { this.tagName = tag; this.children = []; this.parent = null; this.dataset = {}; this.attrs = {}; this.style = { setProperty: (k, v) => { this.style[k] = v; } };
    this.handlers = {}; this.hidden = false; this._text = ''; this.checked = false;
    const set = new Set(); this.classList = { add: (...c) => c.forEach((x) => set.add(x)), remove: (...c) => c.forEach((x) => set.delete(x)), contains: (x) => set.has(x), toggle: (x, on) => { const v = on === undefined ? !set.has(x) : !!on; if (v) set.add(x); else set.delete(x); return v; } }; }
  set className(v) { for (const c of String(v).split(/\s+/).filter(Boolean)) this.classList.add(c); }
  set textContent(v) { this._text = String(v); if (v === '') { for (const c of this.children) c.parent = null; this.children = []; } }
  get textContent() { return this._text; }
  get offsetWidth() { return 10; }
  setAttribute(k, v) { this.attrs[k] = String(v); }
  getAttribute(k) { return this.attrs[k]; }
  appendChild(c) { c.parent = this; this.children.push(c); return c; }
  remove() { if (this.parent) { this.parent.children = this.parent.children.filter((c) => c !== this); this.parent = null; } }
  addEventListener(t, h) { (this.handlers[t] ||= []).push(h); }
  removeEventListener(t, h) { this.handlers[t] = (this.handlers[t] || []).filter((x) => x !== h); }
  getBoundingClientRect() { return { left: 600, top: 760, width: 38, height: 38 }; }
  all() { return this.children.flatMap((c) => [c, ...c.all()]); }
  matches(sel) {
    const m = /^\[data-([a-z-]+)\]$/.exec(sel);
    if (m) { const key = m[1].replace(/-([a-z])/g, (_, ch) => ch.toUpperCase()); return this.dataset[key] !== undefined; }
    return sel.split(',').map((x) => x.trim()).some((x) => x.startsWith('.') && this.classList.contains(x.slice(1)));
  }
  querySelectorAll(sel) { return this.all().filter((e) => e.matches(sel)); }
  closest(sel) { let e = this; while (e) { if (e.matches(sel)) return e; e = e.parent; } return null; }
}
globalThis.document = { body: new El('body'), createElement: (t) => new El(t) };
const fire = (node, type) => { const target = node; let e = node; while (e) { for (const h of e.handlers[type] || []) h({ target, preventDefault() {} }); e = e.parent; } };
{
  const bus = createBus();
  const sent = [];
  bus.on('watch-react', (p) => sent.push(p.id));
  const sounds = [];
  let pv = { show: true, sound: true };
  const subs = new Set();
  const prefs = { get: () => pv, set: (patch) => { pv = { ...pv, ...patch }; for (const fn of subs) fn(pv); return pv; }, on: (fn) => { subs.add(fn); fn(pv); return () => subs.delete(fn); } };
  let t = 0;
  const bar = new El('div'), floats = new El('div');
  const show = new El('input'), soundBox = new El('input');
  const crowd = createReactions({ bus, bar, floats, prefs, guard: createGuard({ now: () => t }), toggles: { show, sound: soundBox },
    sound: (id, o) => sounds.push([id, !!(o && o.soft)]), rand: () => 0.5 });
  const btns = bar.querySelectorAll('[data-react]');
  eq('six buttons', btns.length, 6);
  const mute = bar.querySelectorAll('[data-react-mute]')[0];
  ok('a mute button', !!mute && mute.textContent === '\u{1F50A}');
  eq('the Options rows paint from the prefs', [show.checked, soundBox.checked], [true, true]);

  fire(btns[0], 'click');
  eq('a seat cannot press', sent, []);
  crowd.setWatching(true);
  fire(btns[0], 'click');
  eq('a press goes out', sent, ['fire']);
  eq('...sounds at once, full', sounds, [['fire', false]]);
  eq('...and flies into the board', crowd.debug().flown, 1);
  ok('...from where the button is', document.body.children.some((n) => n.classList.contains('react-fly') && n.style.left === '619px'));
  fire(btns[3], 'click'); fire(btns[4], 'click');
  eq('three in a row', sent, ['fire', 'clap', 'heart']);
  ok('the bar rests after the burst', crowd.debug().cooling && bar.classList.contains('cooling'));
  fire(btns[1], 'click');
  eq('the fourth is kept back', [sent.length, crowd.debug().refused], [3, 1]);
  ok('...and the bar shakes', bar.classList.contains('nope'));
  t += 1600;
  fire(btns[1], 'click');
  eq('a press is back after the rest', sent.at(-1), 'wow');

  fire(mute, 'click');
  eq('mute flips the sound pref', pv.sound, false);
  ok('...and the button says so', mute.textContent === '\u{1F507}' && mute.getAttribute('aria-pressed') === 'true' && !soundBox.checked);
  t += 5000;
  const n = sounds.length;
  fire(btns[2], 'click');
  ok('a muted press still goes out, silent', sent.at(-1) === 'laugh' && sounds.length === n);
  soundBox.checked = true; fire(soundBox, 'change');
  eq('the Options row brings it back', pv.sound, true);

  // the room, in the stands: shown and heard whatever a seat would choose
  prefs.set({ show: false });
  const h0 = sounds.length;
  bus.emit('watch-reactions', { counts: { fire: 4, eyes: 1 } });
  await sleep(REACT_TUNING.bucketMs + 150);
  ok('the stands see their room', crowd.debug().shown >= 2);
  eq('...and hear it, softly, one per id', sounds.slice(h0).map((x) => x.join(':')).sort(), ['eyes:true', 'fire:true']);

  // a seat with the crowd off: nothing
  crowd.setWatching(false); crowd.setStands(false);
  const s0 = crowd.debug().shown, h1 = sounds.length;
  bus.emit('watch-reactions', { counts: { clap: 3 } });
  await sleep(REACT_TUNING.bucketMs + 150);
  eq('a seat with the crowd off sees nothing', crowd.debug().shown, s0);
  eq('...and hears nothing', sounds.length, h1);
  show.checked = true; fire(show, 'change');
  bus.emit('watch-reactions', { counts: { clap: 3 } });
  await sleep(REACT_TUNING.bucketMs + 150);
  ok('turned back on, the seat sees the room', crowd.debug().shown > s0);
  eq('...and hears it', sounds.slice(h1).map((x) => x[0]), ['clap']);
  crowd.dispose();
  ok('dispose clears the flights', !document.body.children.some((c) => c.classList.contains('react-fly')));
}
// reduced motion: no flight, the button lights; the room is one still row and one sound
{
  const bus = createBus();
  const sounds = [];
  const bar = new El('div'), floats = new El('div');
  let pv = { show: true, sound: true };
  const prefs = { get: () => pv, set: (x) => (pv = { ...pv, ...x }), on: (fn) => { fn(pv); return () => {}; } };
  const crowd = createReactions({ bus, bar, floats, prefs, still: () => true, toggles: {}, sound: (id) => sounds.push(id) });
  crowd.setWatching(true);
  const b = bar.querySelectorAll('[data-react]')[5];
  fire(b, 'click');
  ok('still: the button lights instead', b.classList.contains('sent-still') && crowd.debug().flown === 0);
  bus.emit('watch-reactions', { counts: { heart: 2, laugh: 1 } });
  ok('still: one row', floats.children.length === 1 && floats.children[0].classList.contains('react-still'));
  eq('still: one sound for the row', sounds, ['eyes', 'laugh']);
  crowd.dispose();
}

if (failures.length) {
  console.error(`reactions-smoke: ${failures.length} failed, ${passed} passed`);
  for (const f of failures) console.error('  FAIL ' + f);
  process.exit(1);
}
console.log(`reactions-smoke: ${passed} passed`);
process.exit(0);
