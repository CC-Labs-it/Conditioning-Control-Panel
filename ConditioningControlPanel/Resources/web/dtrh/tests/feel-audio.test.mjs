import test from 'node:test';
import assert from 'node:assert/strict';

import {
  rungForStreak, freqForRung, gainForRung, depthCutoffHz, depthDroneMul, createFeelAudio,
  LADDER_RUNGS, LADDER_ROOT_HZ, POP_GAIN, DEPTH_OPEN_HZ, DEPTH_DEEP_HZ, DEPTH_DRONE_LIFT,
  VOICE_DUCK, POP_GAP_MS, RESOLVE_HZ,
} from '../game/feelAudio.js';

const near = (a, b, eps = 0.01) => assert.ok(Math.abs(a - b) <= eps, `${a} vs ${b}`);

test('the ladder starts on the root, climbs one rung a pop, and caps', () => {
  assert.equal(rungForStreak(1), 0);
  assert.equal(rungForStreak(2), 1);
  assert.equal(rungForStreak(LADDER_RUNGS), LADDER_RUNGS - 1);
  assert.equal(rungForStreak(500), LADDER_RUNGS - 1);
});

test('a broken streak, or nonsense, is the root', () => {
  for (const v of [0, -3, NaN, undefined, null, 'x']) assert.equal(rungForStreak(v), 0);
});

test('rungs are the C pentatonic: C D E G A, then the octave', () => {
  near(freqForRung(0), 261.63);
  near(freqForRung(1), 293.66);
  near(freqForRung(2), 329.63);
  near(freqForRung(3), 392.0);
  near(freqForRung(4), 440.0);
  near(freqForRung(5), 523.25, 0.02);
  near(freqForRung(7), 659.26, 0.02);
});

test('no two rungs sit a semitone apart, and the ladder only rises', () => {
  for (let r = 1; r < 12; r++) {
    const ratio = freqForRung(r) / freqForRung(r - 1);
    assert.ok(ratio > Math.pow(2, 1.5 / 12), `rung ${r} is too close to the one below`);
  }
});

test('the ladder tilts down in level as it climbs', () => {
  assert.equal(gainForRung(0), POP_GAIN);
  for (let r = 1; r < LADDER_RUNGS; r++) assert.ok(gainForRung(r) < gainForRung(r - 1));
  assert.ok(gainForRung(LADDER_RUNGS - 1) > POP_GAIN * 0.5);
});

test('depth closes the low-pass exponentially and lifts the drone linearly', () => {
  near(depthCutoffHz(0), DEPTH_OPEN_HZ);
  near(depthCutoffHz(1), DEPTH_DEEP_HZ);
  near(depthCutoffHz(0.5), Math.sqrt(DEPTH_OPEN_HZ * DEPTH_DEEP_HZ), 0.5);
  near(depthCutoffHz(7), DEPTH_DEEP_HZ);
  near(depthCutoffHz(NaN), DEPTH_OPEN_HZ);
  assert.equal(depthDroneMul(0), 1);
  near(depthDroneMul(0.5), 1 + DEPTH_DRONE_LIFT / 2);
  near(depthDroneMul(2), 1 + DEPTH_DRONE_LIFT);
});

/** A context that records what a voice asked of it. */
function fakeCtx() {
  const log = { oscs: [], peaks: [] };
  const param = (onPeak) => ({
    value: 0,
    setValueAtTime() {},
    linearRampToValueAtTime(v) { if (onPeak) onPeak(v); },
    exponentialRampToValueAtTime() {},
  });
  return {
    log,
    state: 'running',
    currentTime: 1,
    destination: {},
    createGain() { return { gain: param((v) => log.peaks.push(v)), connect() {}, disconnect() {} }; },
    createOscillator() {
      const o = { type: '', frequency: { value: 0 }, connect() {}, disconnect() {}, start() {}, stop() {} };
      log.oscs.push(o);
      return o;
    },
  };
}

function rig(over = {}) {
  const c = fakeCtx();
  let t = 0;
  const io = {
    getCtx: () => c, getOut: () => c.destination, silent: () => false, level: () => 0.48,
    voiceActive: () => false, now: () => t, ...over,
  };
  return { c, feel: createFeelAudio(io), tick: (ms) => { t += ms; } };
}

test('a pop sounds its rung as a sine with one octave partial', () => {
  const { c, feel, tick } = rig();
  tick(1000);
  assert.equal(feel.pop(3), true);
  assert.equal(c.log.oscs.length, 2);
  near(c.log.oscs[0].frequency.value, freqForRung(2));
  near(c.log.oscs[1].frequency.value, freqForRung(2) * 2);
  assert.ok(c.log.oscs.every((o) => o.type === 'sine'));
  near(c.log.peaks[0], gainForRung(2), 1e-9);
});

test('a sweep of pops inside the gap is one note, not a cluster', () => {
  const { c, feel, tick } = rig();
  tick(1000);
  feel.pop(1); feel.pop(2); feel.pop(3);
  assert.equal(c.log.oscs.length, 2);
  tick(POP_GAP_MS + 1);
  feel.pop(4);
  assert.equal(c.log.oscs.length, 4);
});

test('muted, a zeroed slider or a sleeping context play nothing', () => {
  let r = rig({ silent: () => true }); r.tick(1000);
  assert.equal(r.feel.pop(1), false);
  r = rig({ level: () => 0 }); r.tick(1000);
  assert.equal(r.feel.pop(1), false);
  r = rig(); r.c.state = 'suspended'; r.tick(1000);
  assert.equal(r.feel.pop(1), false);
  assert.equal(r.c.log.oscs.length, 0);
});

test('every voice sits under a speaking voice line', () => {
  const { c, feel, tick } = rig({ voiceActive: () => true });
  tick(1000);
  feel.pop(1);
  near(c.log.peaks[0], POP_GAIN * VOICE_DUCK, 1e-9);
});

test('the slider can lift a note to at most twice its quoted gain', () => {
  const { c, feel, tick } = rig({ level: () => 1 });
  tick(1000);
  feel.pop(1);
  assert.ok(c.log.peaks[0] <= POP_GAIN * 2 + 1e-9 && c.log.peaks[0] > POP_GAIN);
});

test('a gold pop does not climb: the current rung an octave up', () => {
  const { c, feel, tick } = rig();
  tick(1000);
  feel.pop(2, { gold: true });
  near(c.log.oscs[0].frequency.value, freqForRung(1) * 2);
});

test('the landing resolves on the low root, an octave under the ladder', () => {
  const { c, feel } = rig();
  feel.resolve();
  near(c.log.oscs[0].frequency.value, RESOLVE_HZ);
  near(RESOLVE_HZ * 2, LADDER_ROOT_HZ);
});

// ---- wave 2: hold tone, moment figures, ticks, countdown, the seam ---------------
import {
  holdRung, tickHz, countNotes, momentNotes, attachFeelEvents,
  HOLD_TOP_RUNG, HOLD_GAIN, HOLD_RESOLVE_RUNG, HOLD_RESOLVE_GAP_MS, MOMENT_GAIN, MOMENT_GAP_MS,
  TICK_BASE_RUNG, TICK_TOP_RUNG, TICK_GAP_MS, TICK_GAIN, COUNT_GAIN, COUNT_GO_GAIN, COUNT_GAP_MS,
  CURSE_THIRD_SEMIS, MILESTONE_FIFTH_RUNGS,
} from '../game/feelAudio.js';

const QUIET_MAX = 0.10;   // the brief: new sounds sit between 0.03 and 0.10
const onLadder = (hz) => {
  for (let oct = -2; oct <= 2; oct++) {
    for (let r = 0; r < 5; r++) if (Math.abs(freqForRung(r) * Math.pow(2, oct) - hz) < 0.05) return true;
  }
  return false;
};

test('the hold tone climbs the ladder with progress and never leaves it', () => {
  assert.equal(holdRung(0), 0);
  assert.equal(holdRung(1), HOLD_TOP_RUNG);
  assert.equal(holdRung(9), HOLD_TOP_RUNG);
  for (const v of [NaN, undefined, null, -1, 'x']) assert.equal(holdRung(v), 0);
  let last = 0;
  for (let t = 0; t <= 1.0001; t += 0.05) { const r = holdRung(t); assert.ok(r >= last); last = r; }
});

test('recap ticks rise with the count and stay on the ladder', () => {
  near(tickHz(0), freqForRung(TICK_BASE_RUNG));
  near(tickHz(1), freqForRung(TICK_TOP_RUNG));
  near(tickHz(NaN), freqForRung(TICK_BASE_RUNG));
  let last = 0;
  for (let t = 0; t <= 1.0001; t += 0.1) { const hz = tickHz(t); assert.ok(hz >= last && onLadder(hz)); last = hz; }
});

test('the countdown walks down three low notes to the root on GO', () => {
  const hz = (n) => countNotes(n)[0].hz;
  assert.ok(hz(3) > hz(2) && hz(2) > hz(1) && hz(1) > hz(0));
  near(hz(0), RESOLVE_HZ);
  for (const n of [3, 2, 1, 0]) {
    for (const k of countNotes(n)) assert.ok(onLadder(k.hz) && k.hz < LADDER_ROOT_HZ + 1 && k.gain <= QUIET_MAX);
  }
  assert.equal(countNotes(3)[0].gain, COUNT_GAIN);
  assert.equal(countNotes(0)[0].gain, COUNT_GO_GAIN);
  for (const v of [4, -1, NaN, undefined, 'go']) assert.equal(countNotes(v), null);
});

test('every moment figure is quiet, short, eased in order, and in key', () => {
  const events = ['rabbit-caught', 'darter-caught', 'gold-first', 'combo-milestone', 'combo-big', 'boon-picked', 'wave-cleared'];
  for (const ev of events) {
    const notes = momentNotes(ev, { combo: 12 });
    assert.ok(notes.length >= 1 && notes.length <= 3, ev);
    for (const n of notes) {
      assert.ok(onLadder(n.hz), `${ev} ${n.hz} is off the ladder`);
      assert.ok(n.gain > 0 && n.gain <= QUIET_MAX && n.dur <= 0.7 && n.at >= 0 && n.at < 0.3, ev);
    }
    assert.ok(notes[0].gain <= MOMENT_GAIN);
  }
  assert.deepEqual(momentNotes('junction-near'), []);
  assert.deepEqual(momentNotes(undefined), []);
});

test('the figures have the shapes the brief asks for', () => {
  const [r1, r2] = momentNotes('rabbit-caught');
  assert.ok(r2.hz > r1.hz && r2.at > r1.at);                       // two notes, rising
  const [w1, w2] = momentNotes('wave-cleared');
  assert.ok(w2.hz < w1.hz && w2.at > w1.at);                       // a descending pair
  const [m1, m2] = momentNotes('combo-milestone', { combo: 4 });   // the rung plus its ladder fifth
  near(m1.hz, freqForRung(3));
  near(m2.hz, freqForRung(3 + MILESTONE_FIFTH_RUNGS));
  near(momentNotes('combo-milestone', {})[0].hz, LADDER_ROOT_HZ);  // no combo given: the root
  const [b1, b2] = momentNotes('boon-picked');
  near(b1.hz, LADDER_ROOT_HZ);
  near(b2.hz / b1.hz, Math.pow(2, 4 / 12), 1e-6);                  // major third
  const [c1, c2] = momentNotes('curse-picked');
  near(c1.hz, RESOLVE_HZ);
  near(c2.hz / c1.hz, Math.pow(2, CURSE_THIRD_SEMIS / 12), 1e-6);  // minor third, darker and lower
  assert.ok(c1.hz < b1.hz && c1.gain <= QUIET_MAX);
});

/** The wave 1 fake, plus the automation calls a held tone needs. */
function holdRig(over = {}) {
  const r = rig(over);
  const log = r.c.log;
  log.targets = []; log.gainTargets = []; log.stops = 0; log.cancels = 0;
  const gain0 = r.c.createGain.bind(r.c), osc0 = r.c.createOscillator.bind(r.c);
  r.c.createGain = () => {
    const g = gain0();
    g.gain.setTargetAtTime = (v) => log.gainTargets.push(v);
    g.gain.cancelScheduledValues = () => { log.cancels++; };
    return g;
  };
  r.c.createOscillator = () => {
    const o = osc0();
    o.frequency.setTargetAtTime = (v) => log.targets.push(v);
    o.stop = () => { log.stops++; };
    return o;
  };
  return r;
}

test('a hold is one quiet sine that glides up and resolves on the octave root', () => {
  const { c, feel, tick } = holdRig();
  tick(1000);
  assert.equal(feel.holdStart(0), true);
  assert.equal(feel.holdActive(), true);
  assert.equal(c.log.oscs.length, 1);
  near(c.log.oscs[0].frequency.value, LADDER_ROOT_HZ);
  near(c.log.peaks[0], HOLD_GAIN, 1e-9);
  assert.equal(feel.holdStart(0.5), true);            // a second start is a progress, not a second tone
  assert.equal(c.log.oscs.length, 1);
  feel.holdProgress(1);
  near(c.log.targets.at(-1), freqForRung(HOLD_TOP_RUNG));
  assert.equal(feel.holdEnd(true), true);
  assert.equal(feel.holdActive(), false);
  assert.equal(c.log.cancels, 1);
  near(c.log.gainTargets.at(-1), 0.0001, 1e-9);       // the OUT is a fade, never a cut
  near(c.log.oscs[1].frequency.value, freqForRung(HOLD_RESOLVE_RUNG));
  assert.equal(feel.holdEnd(true), false);            // nothing held: no second resolve inside the gap
  assert.equal(c.log.oscs.length, 3);
});

test('a hold honours mute, the voice and dispose', () => {
  let r = holdRig({ silent: () => true }); r.tick(1000);
  assert.equal(r.feel.holdStart(0), false);
  assert.equal(r.c.log.oscs.length, 0);

  let muted = false;
  r = holdRig({ silent: () => muted }); r.tick(1000);
  r.feel.holdStart(0);
  muted = true;
  assert.equal(r.feel.holdProgress(0.5), false);      // muted mid-hold: it lets go
  assert.equal(r.feel.holdActive(), false);

  let speaking = true;
  r = holdRig({ voiceActive: () => speaking }); r.tick(1000);
  r.feel.holdStart(0);
  near(r.c.log.peaks[0], HOLD_GAIN * VOICE_DUCK, 1e-9);
  speaking = false;
  r.feel.holdProgress(0.4);
  near(r.c.log.gainTargets.at(-1), HOLD_GAIN, 1e-9);  // the voice stopped: back up, eased

  r = holdRig(); r.tick(1000);
  r.feel.holdStart(0);
  r.feel.dispose();
  assert.equal(r.feel.holdActive(), false);
  assert.equal(r.feel.holdStart(0), false);
  assert.equal(r.feel.moment('rabbit-caught'), false);
});

test('ticks, counts and moments are rate-limited and sit under the voice', () => {
  const { c, feel, tick } = holdRig({ voiceActive: () => true });
  tick(1000);
  assert.equal(feel.tick(0), true);
  assert.equal(feel.tick(0.1), false);
  near(c.log.peaks[0], TICK_GAIN * VOICE_DUCK, 1e-9);
  tick(TICK_GAP_MS + 1);
  assert.equal(feel.tick(0.2), true);
  assert.equal(feel.count(3), true);
  assert.equal(feel.count(2), false);
  tick(COUNT_GAP_MS + 1);
  assert.equal(feel.count(2), true);
  assert.equal(feel.count(7), false);
  assert.equal(feel.moment('rabbit-caught'), true);
  assert.equal(feel.moment('rabbit-caught'), false);
  assert.equal(feel.moment('wave-cleared'), true);    // a different moment is not held back
  tick(MOMENT_GAP_MS + 1);
  assert.equal(feel.moment('rabbit-caught'), true);
  assert.equal(feel.moment('nonsense'), false);
});

/** A window that is only an event target. */
function fakeTarget() {
  const subs = new Set();
  return {
    subs,
    addEventListener(name, fn) { if (name === 'dtrh:feel') subs.add(fn); },
    removeEventListener(name, fn) { subs.delete(fn); },
    send(detail) { for (const fn of [...subs]) fn({ detail }); },
  };
}

test('the seam voices moment, hold, tick and count, and leaves pop and arrive alone', () => {
  const { c, feel, tick } = holdRig();
  const win = fakeTarget();
  const detach = attachFeelEvents(feel, win);
  tick(1000);
  win.send({ kind: 'pop', streak: 3 });
  win.send({ kind: 'arrive', what: 'gold' });
  win.send({ kind: 'detonate' });
  assert.equal(c.log.oscs.length, 0);                 // chaosRun already voices those
  win.send({ kind: 'moment', event: 'rabbit-caught', gold: 2 });
  assert.equal(c.log.oscs.length, 4);
  win.send({ kind: 'tick', t: 1 });
  near(c.log.oscs[4].frequency.value, freqForRung(TICK_TOP_RUNG));
  win.send({ kind: 'count', n: 3 });
  assert.equal(c.log.oscs.length, 8);
  win.send({ kind: 'hold', phase: 'progress', t: 0.2 });   // a progress with no start still opens the tone
  assert.equal(feel.holdActive(), true);
  win.send({ kind: 'hold', phase: 'end', ok: true });
  assert.equal(feel.holdActive(), false);
  const afterHold = c.log.oscs.length;
  win.send({ kind: 'moment', event: 'defused', combo: 4 });   // same defuse: not a second resolve
  assert.equal(c.log.oscs.length, afterHold);
  tick(HOLD_RESOLVE_GAP_MS + 1);
  win.send({ kind: 'moment', event: 'defused', combo: 5 });   // no hold events at all: it still resolves
  assert.equal(c.log.oscs.length, afterHold + 2);
  win.send({ kind: 'hold', phase: 'start', t: 0 });
  win.send({ kind: 'phase', state: 'recap' });               // no hold outlives its phase
  assert.equal(feel.holdActive(), false);
  win.send({ kind: 'hold', phase: 'start', t: 0 });
  detach();
  assert.equal(feel.holdActive(), false);
  assert.equal(win.subs.size, 0);
});

test('GO sounds once: from count 0, or from the run starting if no count ever came', () => {
  let r = holdRig(); let win = fakeTarget(); attachFeelEvents(r.feel, win); r.tick(1000);
  win.send({ kind: 'phase', state: 'countdown' });
  win.send({ kind: 'phase', state: 'running' });
  assert.equal(r.c.log.oscs.length, 4);               // low root + ladder root
  near(r.c.log.oscs[0].frequency.value, RESOLVE_HZ);
  win.send({ kind: 'phase', state: 'running' });
  assert.equal(r.c.log.oscs.length, 4);

  r = holdRig(); win = fakeTarget(); attachFeelEvents(r.feel, win); r.tick(1000);
  win.send({ kind: 'phase', state: 'countdown' });
  win.send({ kind: 'count', n: 0 });
  win.send({ kind: 'phase', state: 'running' });
  assert.equal(r.c.log.oscs.length, 4);
});

test('the seam survives junk, a missing target and a feel that throws', () => {
  assert.equal(typeof attachFeelEvents(null, fakeTarget()), 'function');
  assert.equal(typeof attachFeelEvents({}, null), 'function');
  assert.equal(typeof attachFeelEvents({}, {}), 'function');
  attachFeelEvents({}, undefined)();                  // node has no window: a no-op detach
  const win = fakeTarget();
  const detach = attachFeelEvents({ moment() { throw new Error('boom'); } }, win);
  for (const d of [null, undefined, {}, { kind: 'moment' }, { kind: 'hold' }, { kind: 'tick' }, { kind: 'count' },
    { kind: 'phase' }, { kind: 'moment', event: 'rabbit-caught' }, { kind: 'nope' }]) {
    assert.doesNotThrow(() => win.send(d));
  }
  assert.doesNotThrow(detach);
});
