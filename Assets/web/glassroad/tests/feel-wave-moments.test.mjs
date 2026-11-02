/* tests/feel-wave-moments.test.mjs - feel wave 2026-10-26 lane D: rocket start ladder (rev, launch), hitter-hit,
 * lap final flag, HUD pulses and the Motion option. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createRace, launchGrade } from '../sim/race.js';
import { createProgress } from '../sim/progress.js';
import { createState, S } from '../sim/state.js';
import { createActions } from '../core/actions.js';
import { makeFeel, DT } from './sim-fake.mjs';
import { hitterHitOf, bearingOf, wireHitterHit } from '../sim/hitterHit.js';
import { createBus } from '../core/events.js';
import { pulseFrames, playPulse } from '../hud/hudPulse.js';
import { OPTION_DEFAULTS, OPTION_CHOICES, cleanOptions, cycleOption, choiceLabel, OPTIONS_KEY } from '../hud/options.js';

const FEEL = JSON.parse(fs.readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));

test('feel keys: launch.* and hud.* with their defaults', () => {
  assert.deepEqual(FEEL.launch, { revFullBeats: 1, perfectS: 0.1, revStep: 0.05, perfectTier: 2, goodTier: 1, earlyStallS: 0.4 });
  assert.deepEqual(FEEL.hud, { hitterFlashMs: 300, lapPulseMs: 450 });
  assert.equal(FEEL.boost.launchWindowS, 0.15, 'the old window stays');
});

function run(pressAt) {
  const feel = makeFeel(), ev = [];
  const race = createRace(null, feel, { emit: (n, p) => ev.push([n, p]) });
  const st = createState(), a = createActions();
  race.start();
  for (let t = DT; race.phase === 'countdown'; t += DT) { a.throttle = pressAt != null && t >= pressAt ? 1 : 0; race.update(DT, a, st); }
  for (let i = 0; i < 30; i++) race.update(DT, a, st);
  return { feel, ev, st };
}

test('rev builds while held, in steps, overcharges past the sweet spot, only in the count', () => {
  const feel = makeFeel(), beat = 60 / feel.get('clock.bpm'), goT = feel.get('juice.countdownPulses') * beat;
  const { ev } = run(goT - 1.5 * beat);
  const revs = ev.filter((e) => e[0] === 'rev').map((e) => e[1]);
  assert.ok(revs.length >= 10, String(revs.length));
  for (let i = 1; i < revs.length; i++) assert.ok(revs[i].level >= revs[i - 1].level - 1e-9, 'level only rises while held');
  assert.ok(revs.every((r) => r.level >= 0 && r.level <= 1));
  assert.equal(revs.at(-1).over, true, 'held 1.5 beats: overcharged');
  const goIdx = ev.findIndex((e) => e[0] === 'go');
  assert.ok(ev.slice(goIdx).every((e) => e[0] !== 'rev'), 'no rev after go');
  assert.deepEqual(ev.find((e) => e[0] === 'launch')[1], { grade: 'early', tier: 0 });
  assert.equal(run(null).ev.filter((e) => e[0] === 'rev').length, 0, 'no throttle, no rev');
});

test('launch grades: perfect pays perfectTier, good goodTier, early stalls, none pays nothing', () => {
  const feel = makeFeel(), beat = 60 / feel.get('clock.bpm');
  assert.equal(launchGrade(0.05, beat, feel), 'good');
  assert.equal(launchGrade(0.15, beat, feel), 'good');
  assert.equal(launchGrade(beat, beat, feel), 'perfect');
  assert.equal(launchGrade(beat - 0.09, beat, feel), 'perfect');
  assert.equal(launchGrade(beat + 0.09, beat, feel), 'perfect');
  assert.equal(launchGrade(beat + 0.2, beat, feel), 'early');
  const goT = feel.get('juice.countdownPulses') * beat;
  const p = run(goT - beat);
  assert.deepEqual(p.ev.find((e) => e[0] === 'launch-boost')[1], { tier: 2 });
  assert.equal(p.ev.filter((e) => e[0] === 'launch').length, 1);
});

test('lap carries final on the lap that starts the last lap', () => {
  const ev = [], L = 100;
  const track = { lengthM: L, laps: 3, checklines: [{ s: 0 }, { s: 50 }], sectionAt: () => 0 };
  const pr = createProgress(track, { emit: (n, p) => ev.push([n, p]) });
  const st = createState(), race = { phase: 'racing', timeS: 0, lapStartS: 0, finish() { this.phase = 'finished'; } };
  st[S.s] = 1; st[S.grounded] = 1; pr.reset(st);
  for (let i = 0; i < 400 && race.phase === 'racing'; i++) { st[S.s] = (st[S.s] + 1) % L; race.timeS += DT; pr.update(st, race); }
  const laps = ev.filter((e) => e[0] === 'lap').map((e) => e[1]);
  assert.deepEqual(laps.map((l) => [l.lap, l.final]), [[1, false], [2, true], [3, false]]);
  assert.ok(laps.every((l) => 'lapTimeS' in l && 'timeS' in l));
});

const kart = (i, x, z, yaw = 0) => {
  const st = createState();
  st[S.posX] = x; st[S.posZ] = z; st[S.quatY] = Math.sin(yaw / 2); st[S.quatW] = Math.cos(yaw / 2);
  return { i, st };
};

test('bearing: ahead 0, right +PI/2, behind PI (forward -Z, right +X)', () => {
  const v = kart(0, 0, 0);
  assert.ok(Math.abs(bearingOf(v.st, 0, -5)) < 1e-9);
  assert.ok(Math.abs(bearingOf(v.st, 5, 0) - Math.PI / 2) < 1e-9);
  assert.ok(Math.abs(Math.abs(bearingOf(v.st, 0, 5)) - Math.PI) < 1e-9);
  assert.equal(bearingOf(v.st, 0, 0), null);
});

test('hitter-hit: landed item hits and traps only; hitter null for traps and self hits', () => {
  const racers = [kart(0, 0, 0), kart(1, 0, 5)];
  const h = hitterHitOf('item-hit', { victim: 0, by: 1, verdict: 'hit' }, racers);
  assert.equal(h.racer, 0); assert.equal(h.hitter, 1); assert.ok(Math.abs(Math.abs(h.from) - Math.PI) < 1e-9, 'from behind');
  assert.equal(hitterHitOf('item-hit', { victim: 0, by: 1, verdict: 'glass' }, racers), null);
  assert.deepEqual(hitterHitOf('item-hit', { victim: 1, by: 1, verdict: 'hit' }, racers), { racer: 1, hitter: null, from: null });
  assert.deepEqual(hitterHitOf('item-hit', { victim: 1, by: 0, verdict: 'hit' }), { racer: 1, hitter: 0, from: null });
  assert.deepEqual(hitterHitOf('moving-trap-hit', { racer: 2 }), { racer: 2, hitter: null, from: null });
  assert.deepEqual(hitterHitOf('keep-gate-hit', { id: 'g' }), { racer: 0, hitter: null, from: null });
  assert.equal(hitterHitOf('scan-hit', { racer: 0, effect: 'boost' }), null);
  assert.equal(hitterHitOf('bump', { a: 0, b: 1 }), null);
  const bus = createBus(), out = [];
  const off = wireHitterHit(bus, () => racers);
  bus.on('hitter-hit', (p) => out.push(p));
  bus.emit('item-hit', { victim: 1, by: 0, verdict: 'hit' });
  bus.emit('goonworks-hit', { racer: 1 });
  off(); bus.emit('goonworks-hit', { racer: 1 });
  assert.equal(out.length, 2); assert.equal(out[0].hitter, 0); assert.ok(Math.abs(out[0].from) < 1e-9, 'hitter ahead of the victim');
});

test('HUD pulses go in and back out; less is soft, off plays nothing', () => {
  for (const kind of ['hit', 'lap']) {
    const f = pulseFrames(kind, 'full');
    assert.deepEqual(f[0], f.at(-1), kind + ': ends where it started');
    assert.ok(!pulseFrames(kind, 'less').some((k) => k.transform), kind + ': less has no scale');
    assert.ok(!pulseFrames(kind, 'full', true).some((k) => k.transform), kind + ': Gentle has no scale');
    assert.equal(pulseFrames(kind, 'off'), null);
  }
  const calls = [];
  playPulse({ animate: (f, o) => calls.push(o.duration) }, 'hit', 300, 'full', false);
  assert.deepEqual(calls, [300]);
  assert.equal(playPulse({}, 'hit', 300, 'full', false), null, 'no animate(): nothing');
});

test('Motion option: full default, cycles Full/Less/Off, unknown reads full, same storage key', () => {
  assert.equal(OPTIONS_KEY, 'glassroad.options.v1');
  assert.equal(OPTION_DEFAULTS.motion, 'full');
  assert.deepEqual([...OPTION_CHOICES.motion], ['full', 'less', 'off']);
  assert.equal(cleanOptions({ motion: 'wild' }).motion, 'full');
  assert.equal(cleanOptions({ motion: 'less' }).motion, 'less');
  assert.equal(cleanOptions(null).motion, 'full');
  assert.equal(cycleOption('motion', 'full'), 'less');
  assert.equal(cycleOption('motion', 'off'), 'full');
  assert.deepEqual(['full', 'less', 'off'].map((m) => choiceLabel('motion', m)), ['Full', 'Less', 'Off']);
});
