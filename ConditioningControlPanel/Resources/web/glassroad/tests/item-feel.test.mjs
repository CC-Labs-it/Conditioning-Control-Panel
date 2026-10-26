// tests/item-feel.test.mjs - the thrower's seat (fx/itemFeelMath.js, audio plans, no camera move, photosafe, fx cues).
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { comboNext, confirmWord, hitToast, toastDecide, PRIORITY, BLOCKED_VERDICTS } from '../fx/itemFeelMath.js';
import { sfxPlan, confirmPlan, blockedPlan } from '../audio/sfxmap.js';
import { createKick } from '../render/cameraMath.js';
import { ITEM_CUES } from '../fx/reactions.js';
import { createFeel } from '../core/feel.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));

test('combo: hits inside the window climb, a late one starts over', () => {
  const st = { n: 0, t0: -1e9 };
  assert.equal(comboNext(st, 10, 0.6), 1);
  assert.equal(comboNext(st, 10, 0.6), 2);      // Fog catching two karts on one tick
  assert.equal(comboNext(st, 10.5, 0.6), 3);
  assert.equal(comboNext(st, 10.7, 0.6), 1);    // past the first hit's window
});

test('confirm words: per kind, then DOUBLE / TRIPLE / MULTI', () => {
  assert.equal(confirmWord('pendulum', 1), 'BONK!');
  assert.equal(confirmWord('nope', 1), 'HIT!');
  assert.equal(confirmWord('pink', 2), 'DOUBLE!');
  assert.equal(confirmWord('pink', 3), 'TRIPLE!');
  assert.equal(confirmWord('snap', 5), 'MULTI x5!');
});

const who = (i) => ({ name: ['You', 'Maya', 'Hush', 'Lull'][i], colour: i ? '#abc' : '', place: i });
const name = (k) => ({ pendulum: 'Pendulum', pink: 'Fog' })[k] || k;
const text = (m) => m.parts.map((p) => p.text).join('');

test('hit toasts: confirm, combo, blocked, taken, saved', () => {
  const c = hitToast({ victim: 1, by: 0, kind: 'pendulum', verdict: 'hit' }, who, name);
  assert.equal(c.tone, 'hit'); assert.equal(c.priority, PRIORITY.hit);
  assert.equal(text(c), 'BONK!Pendulum on Maya1st');
  assert.equal(c.parts.find((p) => p.cls === 'who').colour, '#abc');
  const d = hitToast({ victim: 2, by: 0, kind: 'pink', verdict: 'hit' }, who, name, { count: 2, names: [1, 2] });
  assert.equal(text(d), 'DOUBLE!Fog caught Maya & Hush');
  const t = hitToast({ victim: 3, by: 0, kind: 'pink', verdict: 'hit' }, who, name, { count: 3, names: [1, 2, 3] });
  assert.equal(text(t), 'TRIPLE!Fog caught Maya, Hush & Lull');
  for (const v of BLOCKED_VERDICTS) {
    const b = hitToast({ victim: 1, by: 0, kind: 'pendulum', verdict: v }, who, name);
    assert.equal(b.tone, 'blocked', v); assert.equal(b.priority, PRIORITY.blocked);
    assert.ok(text(b).includes('Maya'), v);
  }
  assert.equal(text(hitToast({ victim: 1, by: 0, verdict: 'glass' }, who, name)), "BLOCKEDMaya's shield took it");
  const o = hitToast({ victim: 0, by: 2, kind: 'pendulum', verdict: 'hit' }, who, name);
  assert.equal(o.tone, 'ouch'); assert.equal(text(o), 'OUCH!Pendulum from Hush');
  assert.equal(hitToast({ victim: 0, by: 2, kind: 'pendulum', verdict: 'glass' }, who, name).tone, 'saved');
  assert.equal(hitToast({ victim: 2, by: 3, kind: 'pendulum', verdict: 'hit' }, who, name), null);   // bots only
  assert.equal(hitToast({ victim: 0, by: 3, kind: 'pendulum', verdict: 'mercy' }, who, name), null);
});

test('toast policy: a hit replaces a fresh lap note, a combo replaces itself, a note waits', () => {
  const lap = { priority: PRIORITY.note, group: '', at: 0, until: 1.6 };
  assert.equal(toastDecide(null, { priority: 0 }, 0), 'now');
  assert.equal(toastDecide(lap, { priority: PRIORITY.hit, group: 'confirm' }, 0.1), 'now');
  const hit = { priority: PRIORITY.hit, group: 'confirm', at: 0, until: 1.6 };
  assert.equal(toastDecide(hit, { priority: PRIORITY.hit, group: 'confirm' }, 0.05), 'now');
  assert.equal(toastDecide(hit, { priority: PRIORITY.note }, 1.0), 'wait');
  assert.equal(toastDecide(hit, { priority: PRIORITY.note }, 1.7), 'now');     // the hit is gone
  assert.equal(toastDecide(hit, { priority: PRIORITY.hit, group: 'ouch' }, 0.1), 'wait');
  assert.equal(toastDecide(hit, { priority: PRIORITY.hit, group: 'ouch' }, 0.5, { minShowS: 0.45 }), 'now');
});

test('camera: items never move the chase camera (owner camera rule 2026-10-26)', () => {
  assert.equal(createKick().nudge, undefined);
  const cam = readFileSync(new URL('../render/camera.js', import.meta.url), 'utf8');
  assert.ok(!/'item-(fire|hit)'/.test(cam), 'render/camera.js must not listen to item events');
  for (const k of Object.keys(feelJson.itemFeel)) assert.ok(!/KickM$|KickMul$/.test(k), k);
});

test('edge flash: asks the shared photosafe budget, a refusal keeps the stamp', async () => {
  const { createItemFeel } = await import('../hud/itemFeel.js');
  const node = () => ({ textContent: '', offsetWidth: 0, classList: { set: new Set(), add(...c) { c.forEach((x) => this.set.add(x)); }, remove(...c) { c.forEach((x) => this.set.delete(x)); } } });
  const make = (grant) => {
    const el = { '.if-edge': node(), '.if-mark': node(), '.if-stamp': node() }, asked = [];
    const f = createItemFeel({ querySelector: (s) => el[s] }, { doc: null, feel: createFeel(feelJson), now: () => 5000,
      toast() {}, names: (i) => 'K' + i, fetchFn: () => Promise.reject(new Error('offline')),
      photosafe: { request: (k) => { asked.push(k); return grant; } } });
    f.hit({ victim: 2, by: 0, kind: 'pendulum', verdict: 'hit' });
    return { el, asked };
  };
  const yes = make(true), no = make(false);
  assert.deepEqual(yes.asked, ['flash']); assert.ok(yes.el['.if-edge'].classList.set.has('on'));
  assert.deepEqual(no.asked, ['flash']); assert.ok(!no.el['.if-edge'].classList.set.has('on'));
  assert.equal(no.el['.if-stamp'].textContent, 'BONK!');
});

test('audio: a landed hit chimes (climbing with the chain), a blocked one clanks, the victim keeps its own', () => {
  const one = confirmPlan(1, 3), three = confirmPlan(3, 3);
  assert.ok(one.some((x) => x.id === 'menu-ok'));
  const chime = (p) => p.find((x) => x.id === 'menu-ok').rate;
  assert.ok(chime(three) > chime(one));
  assert.equal(three.length, one.length + 1);
  assert.deepEqual(sfxPlan('item-hit', { victim: 2, by: 0, verdict: 'hit', combo: 1 }), confirmPlan(1, 3));
  assert.deepEqual(sfxPlan('item-hit', { victim: 2, by: 0, verdict: 'glass' }), blockedPlan('glass'));
  assert.ok(blockedPlan('mercy').every((x) => x.rate < 1));
  assert.deepEqual(sfxPlan('item-hit', { victim: 0, by: 2, verdict: 'hit' }).map((x) => x.id), ['item-hit']);
  assert.equal(sfxPlan('item-hit', { victim: 0, by: 2, verdict: 'glass' }), null);
  assert.equal(sfxPlan('item-hit', { victim: 3, by: 2, verdict: 'glass' }), null);
});

function fakeFx(gentleOn = false) {
  const feel = createFeel(feelJson), fired = [];
  const ch = { lines: { on: false, held: false, v: 0 }, thud: { on: false, peak: 0 } };
  return { fired, ch, feel, gentle: { kick: (v) => (gentleOn ? 0 : v), scroll: (v) => v * (gentleOn ? 0.5 : 1) },
    fire: (...a) => { fired.push(a); if (ch[a[0]]) Object.assign(ch[a[0]], { on: true, peak: a[1] }); } };
}

test('fx: Snap and Fog hit before their item-fire, and the tick keeps the stronger confirm rumble', () => {
  const fx = fakeFx();
  ITEM_CUES['item-hit'](fx, { victim: 2, by: 0, verdict: 'hit' });
  ITEM_CUES['item-fire'](fx, { racer: 0, mode: 'self' });
  assert.deepEqual(fx.fired.map((a) => a[0]), ['thud']);
  assert.equal(fx.ch.thud.peak, feelJson.itemFeel.confirmRumble);
});

test('fx: a use ticks the pad and flicks the lines, a landed hit rumbles, Gentle zeroes the rumble', () => {
  const fx = fakeFx();
  ITEM_CUES['item-fire'](fx, { racer: 0, mode: 'throw' });
  assert.deepEqual(fx.fired.map((a) => a[0]), ['thud', 'lines']);
  assert.ok(fx.fired[0][1] > 0);
  const boost = fakeFx(); boost.ch.lines.held = true;
  ITEM_CUES['item-fire'](boost, { racer: 0, mode: 'throw' });
  assert.deepEqual(boost.fired.map((a) => a[0]), ['thud']);           // a boost's held lines are left alone
  const bot = fakeFx(); ITEM_CUES['item-fire'](bot, { racer: 3, mode: 'throw' });
  assert.equal(bot.fired.length, 0);
  const hit = fakeFx(); ITEM_CUES['item-hit'](hit, { victim: 2, by: 0, verdict: 'hit' });
  assert.equal(hit.fired[0][0], 'thud'); assert.ok(hit.fired[0][1] > 0);
  const g = fakeFx(true); ITEM_CUES['item-hit'](g, { victim: 2, by: 0, verdict: 'hit' });
  assert.equal(g.fired[0][1], 0);
});
