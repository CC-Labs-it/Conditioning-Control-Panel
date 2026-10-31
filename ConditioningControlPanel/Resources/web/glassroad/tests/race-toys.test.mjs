import { test } from 'node:test';
import assert from 'node:assert/strict';
import { toyPulse, finishPulse, bedLevel, chargeStep, TOY_BED, TOY_SILENCE } from '../fx/toyPlan.js';
import { createRaceToys } from '../host/raceToys.js';
import { TRAP_EVENTS } from '../fx/cues.js';
import { cleanOptions, cycleOption, choiceLabel, OPTION_DEFAULTS } from '../hud/options.js';

function rig() {
  const subs = [], sent = []; let t = 1000;
  const bus = { on(name, fn) { subs.push(fn); return () => subs.splice(subs.indexOf(fn), 1); } };
  const kit = createRaceToys({ bus, send: m => sent.push(m), place: () => rig.place ?? 1, now: () => t });
  return { kit, sent, emit: (name, p = {}) => subs.slice().forEach(f => f(p, name)), tick: (ms = 100) => { t += ms; }, subs,
    pulses: () => sent.filter(m => m.type === 'toy'), beds: () => sent.filter(m => m.type === 'toy-bed'), stops: () => sent.filter(m => m.type === 'toy-stop') };
}
const race = (r, ms, o = {}) => { for (let i = 0; i < ms; i += 100) { r.tick(100); r.kit.update(.1, { racing: true, speedShare: 1, ...o }); } };

test('toy plan: only the player\'s own events make a pulse', () => {
  assert.equal(toyPulse('item-hit', { victim: 3, by: 2, verdict: 'hit' }), null);
  assert.equal(toyPulse('bump', { a: 2, b: 3, dv: 20 }), null);
  assert.equal(toyPulse('hitter-hit', { racer: 4 }), null);
  assert.equal(toyPulse(TRAP_EVENTS[0], { racer: 2 }), null);
  assert.equal(toyPulse('item-hit', { victim: 3, by: 0, verdict: 'hit' }).tag, 'landed');
});

test('toy plan: drift tiers climb, a hit and a trap go silent, a graze is nothing', () => {
  const [a, b, c] = [1, 2, 3].map(tier => toyPulse('boost-start', { kind: 'drift', tier }));
  assert.ok(a.level < b.level && b.level < c.level && a.ms < c.ms);
  assert.equal(toyPulse('item-hit', { victim: 0, verdict: 'hit' }).silenceS, TOY_SILENCE.hitS);
  assert.equal(toyPulse(TRAP_EVENTS[0], {}).silenceS, TOY_SILENCE.trapS);
  assert.equal(toyPulse('bump', { a: 0, b: 1, dv: .1 }, { bumpMinDv: 3 }), null);
  assert.ok(toyPulse('bump', { a: 0, b: 1, dv: 9 }, { bumpMinDv: 3 }));
  assert.equal(toyPulse('land', { tier: 1 }), null);
  assert.ok(toyPulse('word-pop', { lineDone: true }).level > toyPulse('word-pop', {}).level);
});

test('toy plan: first place feels best', () => {
  assert.ok(finishPulse(1).level > finishPulse(2).level && finishPulse(3).level > finishPulse(5).level);
  assert.equal(finishPulse(0).level, finishPulse(8).level);
});

test('toy plan: the bed stays in its range and the last lap lifts it', () => {
  assert.equal(bedLevel(0), TOY_BED.floor);
  assert.ok(Math.abs(bedLevel(1) - (TOY_BED.floor + TOY_BED.span)) < 1e-9);
  assert.ok(bedLevel(1, true) > bedLevel(1) && bedLevel(1, true) <= 1);
  assert.ok(chargeStep(0, 1, 0, 1) > 0 && chargeStep(0, 1, 0, 1) < 1, 'charge climbs, slowly');
  assert.ok(.8 - chargeStep(.8, 0, 0, 1) > chargeStep(0, .8, 0, 1), 'and falls faster than it climbs');
});

test('race toys: the bed builds while racing, at most three sends a second', () => {
  const r = rig();
  race(r, 3000);
  const b = r.beds();
  assert.ok(b.length > 0 && b.length <= 10, 'sends ' + b.length);
  assert.ok(b[b.length - 1].level > b[0].level, 'it builds');
});

test('race toys: the bed keeps sending at a steady level, so the host can tell the page is alive', () => {
  const r = rig();
  race(r, 40000);
  const before = r.beds().length;
  race(r, 2000);
  assert.ok(r.beds().length - before >= 2);
});

test('race toys: not racing is silence', () => {
  const r = rig();
  race(r, 1000);
  race(r, 500, { racing: false });
  assert.equal(r.beds().at(-1).level, 0);
  const n = r.sent.length;
  race(r, 3000, { racing: false });
  assert.equal(r.sent.length, n, 'a silent page sends nothing');
});

test('race toys: a hit thuds, then the bed and other pulses stop for the silence, then it comes back weaker', () => {
  const r = rig();
  race(r, 8000);
  const before = r.beds().at(-1).level;
  r.emit('item-hit', { victim: 0, verdict: 'hit' });
  assert.equal(r.pulses().at(-1).tag, 'hit');
  race(r, 500);
  assert.equal(r.beds().at(-1).level, 0, 'silent after the thud');
  const n = r.pulses().length;
  r.emit('word-pop', {});
  r.emit('boost-start', { kind: 'drift', tier: 3 });
  assert.equal(r.pulses().length, n, 'nothing plays over the silence');
  race(r, 1600);
  const after = r.beds().at(-1).level;
  assert.ok(after > 0 && after < before, `back at ${after}, was ${before}`);
});

test('race toys: a second hit during the silence still lands', () => {
  const r = rig();
  r.emit('item-hit', { victim: 0, verdict: 'hit' });
  r.tick(600);
  r.emit(TRAP_EVENTS[0], {});
  assert.equal(r.pulses().length, 2);
});

test('race toys: one blow that fires two events is one thud and takes the charge once', () => {
  const r = rig();
  race(r, 8000);
  r.emit('hitter-hit', { racer: 0 });
  r.emit('item-hit', { victim: 0, verdict: 'hit' });
  assert.equal(r.pulses().filter(m => m.tag === 'hit').length, 1);
});

test('race toys: a weak pulse never cuts off a stronger one, a stronger one does', () => {
  const r = rig();
  r.emit('boost-start', { kind: 'drift', tier: 3 });
  r.tick(350);
  r.emit('word-pop', {});
  assert.equal(r.pulses().length, 1, 'the word waits under the boost');
  const s = rig();
  s.emit('word-pop', {});
  s.tick(50);
  s.emit('boost-start', { kind: 'drift', tier: 3 });
  assert.equal(s.pulses().length, 2);
});

test('race toys: pause stops at once, once', () => {
  const r = rig();
  race(r, 1000);
  r.emit('pause', { on: true });
  assert.equal(r.stops().length, 1);
  r.emit('pause', { on: true });
  assert.equal(r.stops().length, 1);
  r.emit('pause', { on: false });
  assert.equal(r.stops().length, 1);
});

test('race toys: Moments has pulses and no bed; Off has neither and stops what plays', () => {
  const m = rig();
  race(m, 2000, { mode: 'moments' });
  m.emit('word-pop', {});
  assert.equal(m.beds().length, 0);
  assert.equal(m.pulses().length, 1);
  const o = rig();
  race(o, 1000);
  race(o, 100, { mode: 'off' });
  assert.equal(o.stops().length, 1);
  const n = o.sent.length;
  o.emit('word-pop', {});
  race(o, 1000, { mode: 'off' });
  assert.equal(o.sent.length, n);
});

test('race toys: the last lap lifts the bed, a new run forgets it', () => {
  const r = rig();
  race(r, 8000);
  const plain = r.beds().at(-1).level;
  r.emit('lap', { final: true });
  race(r, 600);
  assert.ok(r.beds().at(-1).level > plain + .1);
  r.kit.reset();
  race(r, 400);
  assert.ok(r.beds().at(-1).level <= TOY_BED.floor + .1);
});

test('race toys: dispose stops the toy, leaves the bus and goes quiet', () => {
  const r = rig();
  race(r, 500);
  r.kit.dispose();
  assert.equal(r.stops().length, 1);
  assert.equal(r.subs.length, 0);
  const n = r.sent.length;
  r.kit.update(.1, { racing: true, speedShare: 1 });
  assert.equal(r.sent.length, n);
});

test('options: Toys defaults to Full and cycles through its three choices', () => {
  assert.equal(OPTION_DEFAULTS.toys, 'full');
  assert.equal(cleanOptions({ toys: 'nope' }).toys, 'full');
  assert.equal(cycleOption('toys', 'full'), 'moments');
  assert.equal(cycleOption('toys', 'moments'), 'off');
  assert.equal(choiceLabel('toys', 'moments'), 'Moments');
});
