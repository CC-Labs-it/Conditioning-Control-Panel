// tests/item-feel.test.mjs - the thrower's seat (fx/itemFeelMath.js, audio plans, camera nudge, fx cues, feel keys).
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { comboNext, confirmWord, hitToast, toastDecide, fireKickKey, scaleKick, PRIORITY, BLOCKED_VERDICTS } from '../fx/itemFeelMath.js';
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

test('fire kicks: every mode has a feel key, scaled for Gentle', () => {
  const feel = createFeel(feelJson);
  for (const m of ['throw', 'bounce', 'tether', 'lob', 'wide', 'drop', 'self', 'odd']) {
    const v = feel.get(fireKickKey(m));
    assert.equal(v.length, 3, m);
  }
  assert.equal(fireKickKey('bounce'), 'itemFeel.throwKickM');
  assert.deepEqual(scaleKick([0, 0.1, 0.2], 0.5), [0, 0.05, 0.1]);
  assert.deepEqual(scaleKick(null), [0, 0, 0]);
  assert.deepEqual(scaleKick([1, 'x', 2], -1), [0, 0, 0]);
});

test('camera nudge: a back kick moves the eye back, then settles', () => {
  const k = createKick();
  assert.equal(k.nudge([0, 0, 0.24]), true);
  let peak = 0;
  for (let i = 0; i < 30; i++) peak = Math.max(peak, k.update(1 / 60).z);
  assert.ok(peak > 0.1 && peak < 0.4, 'peak ' + peak);
  for (let i = 0; i < 120; i++) k.update(1 / 60);
  assert.ok(Math.abs(k.out.z) < 0.02);
  assert.equal(k.nudge(null), false);
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
  const ch = { lines: { on: false, held: false, v: 0 } };
  return { fired, ch, feel, gentle: { kick: (v) => (gentleOn ? 0 : v), scroll: (v) => v * (gentleOn ? 0.5 : 1) },
    fire: (...a) => fired.push(a) };
}

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
