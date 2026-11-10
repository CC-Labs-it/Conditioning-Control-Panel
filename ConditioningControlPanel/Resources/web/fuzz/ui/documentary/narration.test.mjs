import test from 'node:test';
import assert from 'node:assert/strict';
import { createDocumentaryNarration } from './narration.js';
import { NARRATION_CLIPS, STAGE_SCHEDULE } from './narration-data.js';

function bus() {
  const listeners = new Map();
  return {
    on(id, fn) { if (!listeners.has(id)) listeners.set(id, new Set()); listeners.get(id).add(fn); return () => listeners.get(id)?.delete(fn); },
    emit(id, value) { for (const fn of [...(listeners.get(id) || [])]) fn(value); },
    count() { return [...listeners.values()].reduce((n, set) => n + set.size, 0); },
  };
}
function harness(options = {}) {
  let clock = 0, serial = 0, concurrent = 0, maxConcurrent = 0, blocked = false;
  const timers = new Map(), streams = [], beats = [], statuses = [], holds = new Set();
  const events = bus(), host = bus(), doc = new EventTarget(); doc.hidden = false;
  const ctx = { paused: false, caps: options.caps || {}, state: {}, on: host.on, cleanup() {} };
  const state = { z: options.at || 0, locked: !options.at, docked: options.at === 580 };
  const tunnel = {
    state, ready: Promise.resolve(), captions: { ownerDocument: doc }, on: events.on, addCues() {},
    hold(id) { holds.add(id); return () => holds.delete(id); },
    beat(id, data) { beats.push({ id, data }); if (id === 'booth.unlock') state.locked = false; },
  };
  function createStream(url, settings) {
    assert.equal(settings.bus, 'voice'); assert.equal(settings.autoplay, false);
    const el = new EventTarget(); Object.assign(el, { currentTime: 0, readyState: 4, paused: true });
    const stream = {
      el, id: /([^/]+)\.mp3$/.exec(url)[1], stopped: false,
      play() {
        if (blocked) return Promise.reject(Object.assign(new Error('Gesture required'), { name: 'NotAllowedError' }));
        if (el.paused) { el.paused = false; concurrent++; maxConcurrent = Math.max(maxConcurrent, concurrent); }
        el.dispatchEvent(new Event('playing')); return Promise.resolve();
      },
      pause() { if (!el.paused) { el.paused = true; concurrent--; } },
      stop() { this.pause(); this.stopped = true; },
      time(seconds) { el.currentTime = seconds; el.dispatchEvent(new Event('timeupdate')); },
      end() { this.pause(); el.dispatchEvent(new Event('ended')); },
      error() { el.dispatchEvent(new Event('error')); },
    };
    streams.push(stream); return stream;
  }
  const narration = createDocumentaryNarration(ctx, tunnel, {
    now: () => clock, setTimer(fn, delay) { const id = ++serial; timers.set(id, { at: clock + delay, fn }); return id; },
    clearTimer(id) { timers.delete(id); }, createStream, onStatus: value => statuses.push(value), ...options,
  });
  return {
    narration, streams, beats, statuses, holds, doc, ctx, events, host, timers,
    setBlocked(value) { blocked = value; }, get concurrent() { return concurrent; }, get maxConcurrent() { return maxConcurrent; },
    cue(id, z) { state.z = z; events.emit('cue', { id, at: z }); },
    pause(value) { ctx.paused = value; host.emit(value ? 'pause' : 'resume'); },
    tick(ms) {
      const end = clock + ms;
      for (let safety = 0; safety < 1000; safety++) {
        const due = [...timers].filter(([, timer]) => timer.at <= end).sort((a, b) => a[1].at - b[1].at)[0];
        if (!due) { clock = end; return; }
        clock = due[1].at; timers.delete(due[0]); due[1].fn();
      }
      throw new Error('Timer loop did not settle');
    },
  };
}

test('recording allowlist is complete and schedule has no placeholder clips', () => {
  assert.equal(Object.keys(NARRATION_CLIPS).length, 217);
  assert.ok(!NARRATION_CLIPS.c6t && !NARRATION_CLIPS.c11t);
  for (const row of STAGE_SCHEDULE) assert.ok(NARRATION_CLIPS[row.id]?.duration > 0, row.id);
  assert.ok(STAGE_SCHEDULE.at(-1).at >= 10800);
});

test('ride queue cannot overlap; ended applies terminal beats and releases before the next recording', async () => {
  const h = harness(); await h.narration.ready;
  assert.equal(h.streams[0].id, 'r1');
  h.cue('narration.r2', 12);
  assert.equal(h.streams.length, 1);
  h.streams[0].end();
  assert.equal(h.ctx.paused, false);
  assert.ok(h.beats.some(beat => beat.id === 'booth.stamp'));
  assert.ok(h.beats.some(beat => beat.id === 'booth.unlock'));
  assert.equal(h.streams[1].id, 'r2');
  assert.equal(h.maxConcurrent, 1);
  h.streams[1].end(); assert.equal(h.holds.size, 0);
  h.narration.dispose(); assert.equal(h.concurrent, 0); assert.equal(h.timers.size, 0);
});

test('distance gate pauses the recording, then replay hold releases only on audio ended', async () => {
  const h = harness({ at: 180 }); await h.narration.ready;
  const recording = h.streams[0]; assert.equal(recording.id, 'r3');
  recording.time(22); assert.equal(h.narration.debug().gated, true); assert.equal(recording.el.paused, true);
  h.cue('narration.gate.r3', 285);
  assert.equal(h.narration.debug().gated, false); assert.equal(recording.el.paused, false);
  recording.time(35); assert.ok(h.beats.some(beat => beat.id === 'floor.replay'));
  assert.ok(h.holds.has('narration.r3'));
  recording.end();
  assert.equal(h.holds.size, 0); assert.equal(h.beats.at(-3).id, 'floor.release');
  h.narration.dispose();
});

test('Wake pause preserves the current recording and panic disposes immediately', async () => {
  const h = harness(); await h.narration.ready;
  h.streams[0].time(12); h.pause(true);
  assert.equal(h.streams[0].el.paused, true); h.tick(60000);
  assert.equal(h.streams[0].el.currentTime, 12);
  h.pause(false); assert.equal(h.streams[0].el.paused, false);
  h.host.emit('suspend', { on: true, reason: 'panic' });
  assert.equal(h.narration.debug().disposed, true); assert.equal(h.concurrent, 0); assert.equal(h.timers.size, 0);
  assert.equal(h.events.count(), 0); assert.equal(h.host.count(), 0);
  h.streams[0].end(); assert.equal(h.streams.length, 1);
});

test('missing recording uses finite paused visual fallback, without a second voice', async () => {
  const h = harness(); await h.narration.ready; h.streams[0].error();
  assert.equal(h.narration.debug().missing, true); assert.equal(h.concurrent, 0);
  h.tick(5000); h.pause(true); h.tick(120000);
  assert.ok(!h.beats.some(beat => beat.id === 'booth.unlock'));
  h.pause(false); h.tick(50000);
  assert.ok(h.beats.some(beat => beat.id === 'booth.unlock')); assert.equal(h.streams.length, 1);
  h.narration.dispose(); assert.equal(h.timers.size, 0);
});

test('autoplay denial exposes retry and resumes on a gesture', async () => {
  const h = harness(); h.setBlocked(true); await h.narration.ready; await Promise.resolve();
  assert.equal(h.narration.debug().status, 'blocked'); assert.equal(h.concurrent, 0);
  h.setBlocked(false); h.doc.dispatchEvent(new Event('pointerdown'));
  assert.equal(h.narration.debug().status, 'playing'); assert.equal(h.concurrent, 1);
  h.narration.dispose();
});

test('stage chains in a hidden document, has one bounded timer, and respects Wake pause', async () => {
  const h = harness({ at: 580, schedule: [{ id: 'n0a', at: 20 }, { id: 'n0b', at: 35 }] }); await h.narration.ready;
  assert.equal(h.streams[0].id, 'p1'); h.streams[0].end(); assert.equal(h.streams[1].id, 'p2'); h.streams[1].end();
  assert.ok(h.beats.some(beat => beat.id === 'stage.buttons'));
  h.doc.hidden = true; h.doc.dispatchEvent(new Event('visibilitychange'));
  if (h.narration.debug().current) h.streams.at(-1).end();
  h.tick(20000); assert.equal(h.streams.at(-1).id, 'n0a');
  h.streams.at(-1).end(); h.pause(true); h.tick(90000);
  assert.equal(h.narration.debug().elapsed, 20);
  h.pause(false); h.tick(15000); assert.equal(h.streams.at(-1).id, 'n0b');
  assert.equal(h.maxConcurrent, 1); h.narration.dispose(); assert.equal(h.timers.size, 0);
});

test('New run preempts commentary, is bounded, and panic resolves its wait immediately', async () => {
  const h = harness({ at: 580 }); await h.narration.ready;
  let done = false; const wait = h.narration.finishNewRun().then(() => { done = true; });
  assert.equal(h.streams.at(-1).id, 'nr0'); assert.equal(h.maxConcurrent, 1);
  h.host.emit('suspend', { on: true, reason: 'panic' }); await wait;
  assert.equal(done, true); assert.equal(h.concurrent, 0); assert.equal(h.timers.size, 0);
});

test('host focus can hold the mixer without freezing the stage wall clock', async () => {
  const h = harness({ at: 580, schedule: [{ id: 'n0a', at: 20 }] }); await h.narration.ready;
  h.streams[0].end(); h.streams[1].end();
  h.ctx.paused = true; h.host.emit('suspend', { on: true, reason: 'focus' }); h.tick(30000);
  assert.equal(h.narration.debug().elapsed, 30); assert.equal(h.narration.debug().current, null);
  h.ctx.paused = false; h.host.emit('suspend', { on: false, reason: 'focus' });
  assert.equal(h.narration.debug().current, 'n0a');
  h.narration.dispose();
});

test('unknown accepted clauses skip Terms and their linking lines', async () => {
  const h = harness({ at: 580, schedule: [{ id: 'c6', at: 0 }, { id: 'term-start-1', at: 0 }, { id: 'c7', at: 0 }, { id: 'b1', at: 0 }] });
  await h.narration.ready; h.streams[0].end(); h.streams[1].end();
  assert.equal(h.streams[2].id, 'b1'); assert.equal(h.narration.debug().acceptedTerms, 0);
  h.narration.dispose();
});
