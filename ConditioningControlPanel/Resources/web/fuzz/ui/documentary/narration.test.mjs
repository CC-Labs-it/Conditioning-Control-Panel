import test from 'node:test';
import assert from 'node:assert/strict';
import { createDocumentaryNarration } from './narration.js';
import { NARRATION_CLIPS, STAGE_SCHEDULE, VERIFIED_RELEASE_SECONDS, RIDE_RECORDINGS } from './narration-data.js';
import { NARRATION_SUBTITLES, SUBTITLE_TIMING } from './subtitles.js';
import { projectionTimeline } from './narration-slides.js';

function bus() {
  const listeners = new Map();
  return {
    on(id, fn) { if (!listeners.has(id)) listeners.set(id, new Set()); listeners.get(id).add(fn); return () => listeners.get(id)?.delete(fn); },
    emit(id, value) { for (const fn of [...(listeners.get(id) || [])]) fn(value); },
    count() { return [...listeners.values()].reduce((n, set) => n + set.size, 0); },
  };
}
function harness(options = {}) {
  let clock = 0, serial = 0, concurrent = 0, maxConcurrent = 0, blocked = false, paceProvider = null;
  const timers = new Map(), streams = [], beats = [], statuses = [], speaking = [], holds = new Set(), holdPoints = new Map();
  const events = bus(), host = bus(), doc = new EventTarget(); doc.hidden = false;
  const ctx = { paused: false, caps: options.caps || {}, state: {}, on: host.on, cleanup() {} };
  const state = { z: options.at || 0, locked: !options.at, docked: options.at === 580 };
  const tunnel = {
    state, ready: Promise.resolve(), captions: { ownerDocument: doc, textContent: '', hidden: true, dataset: {} }, on: events.on, addCues() {},
    hold(id, at) { holds.add(id); holdPoints.set(id, at); return () => { holds.delete(id); holdPoints.delete(id); }; },
    setPace(provider) { paceProvider = provider; return () => { if (paceProvider === provider) paceProvider = null; }; },
    beat(id, data) { beats.push({ id, data }); if (id === 'booth.unlock') state.locked = false; },
  };
  function createStream(url, settings) {
    assert.equal(settings.bus, 'voice'); assert.equal(settings.autoplay, false);
    const el = new EventTarget(); Object.assign(el, { currentTime: 0, duration: NaN, readyState: 4, paused: true });
    const stream = {
      el, id: /([^/]+)\.mp3$/.exec(url)[1], stopped: false, gains: [settings.gain],
      play() {
        if (blocked) return Promise.reject(Object.assign(new Error('Gesture required'), { name: 'NotAllowedError' }));
        if (el.paused) { el.paused = false; concurrent++; maxConcurrent = Math.max(maxConcurrent, concurrent); }
        el.dispatchEvent(new Event('playing')); return Promise.resolve();
      },
      pause() { if (!el.paused) { el.paused = true; concurrent--; el.dispatchEvent(new Event('pause')); } },
      setGain(value) { this.gains.push(value); },
      stop() { this.pause(); this.stopped = true; },
      time(seconds) { el.currentTime = seconds; el.dispatchEvent(new Event('timeupdate')); },
      end() { this.pause(); el.dispatchEvent(new Event('ended')); },
      error() { el.dispatchEvent(new Event('error')); },
    };
    streams.push(stream); return stream;
  }
  const narration = createDocumentaryNarration(ctx, tunnel, {
    now: () => clock, setTimer(fn, delay) { const id = ++serial; timers.set(id, { at: clock + delay, fn }); return id; },
    clearTimer(id) { timers.delete(id); }, createStream, onStatus: value => statuses.push(value), onSpeaking: value => speaking.push(value), ...options,
  });
  return {
    narration, streams, beats, statuses, speaking, holds, holdPoints, doc, ctx, events, host, timers, captions: tunnel.captions,
    setBlocked(value) { blocked = value; }, get concurrent() { return concurrent; }, get maxConcurrent() { return maxConcurrent; },
    pace() { return paceProvider?.(state) || null; },
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
  h.cue('narration.r2', 92);
  assert.equal(h.streams.length, 1);
  h.streams[0].end();
  assert.equal(h.ctx.paused, false);
  assert.ok(h.beats.some(beat => beat.id === 'booth.stamp'));
  assert.ok(h.beats.some(beat => beat.id === 'booth.unlock'));
  assert.equal(h.streams.length, 1);
  h.tick(1500);
  assert.equal(h.streams[1].id, 'r2');
  assert.equal(h.maxConcurrent, 1);
  h.streams[1].end(); assert.equal(h.holds.size, 0);
  h.narration.dispose(); assert.equal(h.concurrent, 0); assert.equal(h.timers.size, 0);
});

test('recordings continue through every former distance gate without installing stationary holds', async () => {
  for (const [at, id, offset] of [[92, 'r2', 23], [268, 'r3', 22], [370, 'r4', 10]]) {
    const h = harness({ at }); await h.narration.ready;
    const recording = h.streams[0]; assert.equal(recording.id, id);
    recording.time(offset);
    assert.equal(h.narration.debug().gated, false); assert.equal(recording.el.paused, false);
    assert.equal(recording.el.currentTime, offset, 'narration never seeks backwards at a route cue');
    assert.equal(h.holds.size, 0);
    if (id === 'r3') { recording.time(35); assert.ok(h.beats.some(beat => beat.id === 'floor.replay')); }
    recording.end(); assert.equal(h.holds.size, 0);
    if (id === 'r3') assert.ok(h.beats.some(beat => beat.id === 'floor.release'));
    h.narration.dispose();
  }
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
  assert.equal(h.narration.debug().current, 'r1');
  assert.ok(!h.beats.some(beat => beat.id === 'booth.stamp'));
  h.pause(false); h.tick(50000);
  assert.ok(h.beats.some(beat => beat.id === 'booth.unlock')); assert.equal(h.streams.length, 1);
  h.narration.dispose(); assert.equal(h.timers.size, 0);
});

test('autoplay denial exposes retry and resumes on a gesture', async () => {
  const h = harness(); h.setBlocked(true); await h.narration.ready; await Promise.resolve();
  assert.equal(h.narration.debug().status, 'blocked'); assert.equal(h.concurrent, 0);
  assert.equal(h.beats.filter(beat => beat.id === 'projector.show').length, 0);
  h.tick(3000);
  assert.equal(h.beats.filter(beat => beat.id === 'projector.show').length, 0);
  h.setBlocked(false); h.doc.dispatchEvent(new Event('pointerdown'));
  assert.equal(h.narration.debug().status, 'playing'); assert.equal(h.concurrent, 1);
  assert.equal(h.beats.filter(beat => beat.id === 'projector.show').length, 1);
  assert.equal(h.beats.find(beat => beat.id === 'projector.show').data.id, 'booth');
  h.pause(true); h.pause(false);
  assert.equal(h.beats.filter(beat => beat.id === 'projector.show').length, 1, 'resume does not redeploy the opening screen');
  h.narration.dispose();
});

test('an unavailable opening clip starts one finite visual fallback after pause clears', async () => {
  const h = harness(); h.setBlocked(true); await h.narration.ready; await Promise.resolve();
  h.pause(true); h.streams[0].error();
  assert.equal(h.narration.debug().missing, true);
  h.tick(120000);
  assert.equal(h.beats.filter(beat => beat.id === 'projector.show').length, 0);
  assert.equal(h.narration.debug().current, 'r1');
  h.pause(false);
  assert.equal(h.beats.filter(beat => beat.id === 'projector.show').length, 1);
  h.tick(NARRATION_CLIPS.r1.duration * 1000 + 1);
  assert.equal(h.narration.debug().current, null);
  assert.ok(h.beats.some(beat => beat.id === 'projector.hide' && beat.data.id === 'booth'));
  h.narration.dispose(); assert.equal(h.timers.size, 0);
});

test('stage chains in a hidden document, has one bounded timer, and respects Wake pause', async () => {
  const h = harness({ at: 580, schedule: [{ id: 'n0a', at: 20 }, { id: 'n0b', at: 35 }] }); await h.narration.ready;
  assert.equal(h.streams[0].id, 'p1'); h.streams[0].end(); h.tick(1500); assert.equal(h.streams[1].id, 'p2'); h.streams[1].end();
  assert.ok(h.beats.some(beat => beat.id === 'stage.buttons'));
  h.doc.hidden = true; h.doc.dispatchEvent(new Event('visibilitychange'));
  h.tick(1500);
  if (h.narration.debug().current) h.streams.at(-1).end();
  h.tick(18500); assert.equal(h.streams.at(-1).id, 'n0a');
  h.streams.at(-1).end(); h.pause(true); h.tick(90000);
  assert.equal(h.narration.debug().elapsed, 20);
  h.pause(false); h.tick(15000); assert.equal(h.streams.at(-1).id, 'n0b');
  assert.equal(h.maxConcurrent, 1); h.narration.dispose(); assert.equal(h.timers.size, 0);
});

test('New run waits for commentary and a speech gap; panic resolves any wait immediately', async () => {
  const h = harness({ at: 580 }); await h.narration.ready;
  let done = false; const wait = h.narration.finishNewRun().then(() => { done = true; });
  assert.equal(h.streams.at(-1).id, 'p1'); assert.equal(h.streams[0].stopped, false);
  h.streams[0].time(20); h.tick(12000); assert.equal(done, false);
  h.streams[0].end(); h.tick(1499); assert.equal(h.streams.length, 1);
  h.tick(1); assert.equal(h.streams.at(-1).id, 'nr0'); assert.equal(h.maxConcurrent, 1);
  h.host.emit('suspend', { on: true, reason: 'panic' }); await wait;
  assert.equal(done, true); assert.equal(h.concurrent, 0); assert.equal(h.timers.size, 0);
});

test('New run does not start its exit deadline while waiting for the current line', async () => {
  const h = harness({ at: 580 }); await h.narration.ready;
  let done = false; const wait = h.narration.finishNewRun().then(() => { done = true; });
  h.streams[0].time(1);
  h.tick(NARRATION_CLIPS.nr0.duration * 1000 + 2500);
  assert.equal(done, false); assert.equal(h.streams[0].stopped, false); assert.equal(h.streams.length, 1);
  h.streams[0].end(); h.tick(1500);
  assert.equal(h.streams[1].id, 'nr0'); assert.equal(done, false);
  h.streams[1].end(); await wait; assert.equal(done, true); assert.equal(h.maxConcurrent, 1);
  h.narration.dispose();
});

test('closing watchdog never truncates healthy audio when decoded duration exceeds metadata', async () => {
  const h = harness({ at: 580 }); await h.narration.ready;
  let done = false; const wait = h.narration.finishNewRun().then(() => { done = true; });
  h.streams[0].end(); h.tick(1500);
  const closing = h.streams[1]; closing.el.duration = NARRATION_CLIPS.nr0.duration + 40; closing.time(1);
  h.tick(NARRATION_CLIPS.nr0.duration * 1000 + 2500);
  assert.equal(closing.stopped, false); assert.equal(done, false);
  h.pause(true); h.tick(60000); assert.equal(closing.stopped, false);
  h.pause(false); closing.end(); await wait; assert.equal(done, true);
  h.narration.dispose();
});

test('host focus can hold the mixer without freezing the stage wall clock', async () => {
  const h = harness({ at: 580, schedule: [{ id: 'n0a', at: 20 }] }); await h.narration.ready;
  h.streams[0].end(); h.tick(1500); h.streams[1].end();
  h.ctx.paused = true; h.host.emit('suspend', { on: true, reason: 'focus' }); h.tick(30000);
  assert.equal(h.narration.debug().elapsed, 30); assert.equal(h.narration.debug().current, null);
  h.ctx.paused = false; h.host.emit('suspend', { on: false, reason: 'focus' });
  assert.equal(h.narration.debug().current, 'n0a');
  h.narration.dispose();
});

test('unknown accepted clauses skip Terms and their linking lines', async () => {
  const h = harness({ at: 580, schedule: [{ id: 'c6', at: 0 }, { id: 'term-start-1', at: 0 }, { id: 'c7', at: 0 }, { id: 'b1', at: 0 }] });
  await h.narration.ready; h.streams[0].end(); h.tick(1500); h.streams[1].end(); h.tick(1500);
  assert.equal(h.streams[2].id, 'b1'); assert.equal(h.narration.debug().acceptedTerms, 0);
  h.narration.dispose();
});

test('subtitle delivery covers each recording with readable two-line phrases and mixed timing provenance', () => {
  assert.equal(SUBTITLE_TIMING, 'mixed');
  assert.deepEqual(Object.keys(NARRATION_SUBTITLES).sort(), Object.keys(NARRATION_CLIPS).sort());
  for (const [id, cues] of Object.entries(NARRATION_SUBTITLES)) {
    let previous = 0;
    for (const [from, to, text] of cues) {
      assert.ok(from >= previous && to > from && to <= 1, id);
      assert.ok(text.split('\n').length <= 2, id);
      assert.ok(text.split('\n').every(line => line.length <= 36), id);
      previous = to;
    }
  }
  assert.equal(NARRATION_SUBTITLES.r1[0][2], 'This is the story of a person');
});

test('subtitles follow actual media duration and seeks, toggle immediately, and clear on pause and disposal', async () => {
  const h = harness(); await h.narration.ready; const recording = h.streams[0]; recording.el.duration = 60;
  recording.time(4); assert.equal(h.captions.textContent, 'This is the story of a person');
  recording.time(18); assert.match(h.captions.textContent, /^I would tell you their name/);
  assert.equal(h.captions.dataset.timing, 'aligned');
  h.narration.setSubtitles(false); assert.equal(h.captions.textContent, '');
  recording.time(30); assert.equal(h.captions.textContent, '');
  h.narration.setSubtitles(true); assert.ok(h.captions.textContent.length > 0);
  h.pause(true); assert.equal(h.captions.textContent, '');
  h.pause(false); assert.ok(h.captions.textContent.length > 0);
  h.narration.dispose(); assert.equal(h.captions.textContent, ''); assert.equal(h.captions.hidden, true);
});

test('voice fades use actual media time and ducking clears during the gap and immediately on panic', async () => {
  const h = harness({ at: 268 }); await h.narration.ready; const recording = h.streams[0]; recording.el.duration = 60;
  assert.equal(recording.gains[0], 0);
  recording.time(0.06); assert.ok(Math.abs(recording.gains.at(-1) - 0.5) < 0.001);
  recording.time(0.12); assert.equal(recording.gains.at(-1), 1);
  recording.time(60 - VERIFIED_RELEASE_SECONDS.r3 / 2); assert.ok(Math.abs(recording.gains.at(-1) - 0.5) < 0.001);
  h.cue('narration.r2', 92); recording.end();
  assert.equal(h.speaking.at(-1), false); assert.equal(h.captions.textContent, '');
  h.tick(1499); assert.equal(h.streams.length, 1);
  h.tick(1); assert.equal(h.speaking.at(-1), true); assert.equal(h.maxConcurrent, 1);
  h.host.emit('suspend', { on: true, reason: 'panic' });
  assert.equal(h.speaking.at(-1), false); assert.equal(h.timers.size, 0); assert.equal(h.concurrent, 0);
});

test('fade limits cover the full audited delivery and preserve voiced final samples', () => {
  assert.equal(Object.keys(VERIFIED_RELEASE_SECONDS).length, 217);
  assert.equal(VERIFIED_RELEASE_SECONDS.r4, 0, 'a low RMS tail with an audible peak is not safe');
  assert.equal(VERIFIED_RELEASE_SECONDS.r1, 0);
  assert.equal(VERIFIED_RELEASE_SECONDS.r3, 0.079);
  assert.equal(Object.values(VERIFIED_RELEASE_SECONDS).filter(seconds => seconds > 0).length, 26);
  assert.ok(Object.values(VERIFIED_RELEASE_SECONDS).every(seconds => seconds >= 0 && seconds <= 0.25));
});

test('unverified recording tails retain full gain and do not end on estimated duration', async () => {
  const h = harness(); await h.narration.ready; const recording = h.streams[0]; recording.el.duration = 60;
  recording.time(59.99); assert.equal(recording.gains.at(-1), 1);
  assert.equal(recording.stopped, false); assert.equal(h.narration.debug().current, 'r1');
  recording.end(); assert.equal(h.narration.debug().current, null);
  h.narration.dispose();
});

test('missing audio still advances subtitle phrases and preserves pause timing', async () => {
  const h = harness(); await h.narration.ready; h.streams[0].error();
  h.tick(3000); assert.equal(h.captions.textContent, 'This is the story of a person');
  h.pause(true); h.tick(5000); assert.equal(h.captions.textContent, '');
  h.pause(false); assert.equal(h.captions.textContent, 'This is the story of a person');
  h.tick(6000); assert.match(h.captions.textContent, /^who sat down/);
  assert.equal(h.speaking.at(-1), false); h.narration.dispose();
});

test('hover speech is at least five minutes apart and capped at two played lines', async () => {
  const h = harness({ at: 580, schedule: [] }); await h.narration.ready;
  h.streams[0].end(); h.tick(1500); h.streams[1].end(); h.tick(1500);
  h.events.emit('stage-action', { id: 'hover-new-run' });
  assert.equal(h.streams.at(-1).id, 'hv1'); assert.equal(h.narration.debug().hoverPlayed, 1);
  h.streams.at(-1).end();
  for (let i = 0; i < 20; i++) h.events.emit('stage-action', { id: 'hover-new-run' });
  h.tick(299999); h.events.emit('stage-action', { id: 'hover-new-run' }); assert.equal(h.narration.debug().hoverPlayed, 1);
  h.tick(1); h.events.emit('stage-action', { id: 'hover-new-run' }); assert.equal(h.streams.at(-1).id, 'hv2');
  assert.equal(h.narration.debug().hoverPlayed, 2); h.streams.at(-1).end();
  h.tick(600000); h.events.emit('stage-action', { id: 'hover-new-run' });
  assert.equal(h.streams.filter(stream => stream.id.startsWith('hv')).length, 2); h.narration.dispose();
});

test('replacing a queued hover reaction does not consume its quota', async () => {
  const h = harness({ at: 580, schedule: [{ id: 'n1', at: 0 }] }); await h.narration.ready;
  h.streams[0].end(); h.tick(1500); h.streams[1].end(); h.tick(1500);
  assert.equal(h.streams.at(-1).id, 'n1');
  h.events.emit('stage-action', { id: 'hover-new-run' }); assert.equal(h.narration.debug().hoverPlayed, 0);
  h.streams.at(-1).time(10); h.tick(12000);
  h.events.emit('stage-action', { id: 'ad', opened: true });
  h.streams.at(-1).end(); h.tick(1500);
  assert.equal(h.streams.at(-1).id, 'ad0'); assert.equal(h.narration.debug().hoverPlayed, 0);
  h.narration.dispose();
});

test('slideshow order follows the same subtitle cue boundaries at actual media duration', async () => {
  const h = harness({ at: 268 }); await h.narration.ready;
  const recording = h.streams[0]; recording.el.duration = 80;
  const slides = projectionTimeline('r3').filter(cue => cue.slot);
  assert.deepEqual(slides.map(cue => cue.slot), ['reflex-cheetah', 'reflex-shrimp', 'subject-habitat', 'floorboard-warning', 'subject-spreadsheet', 'subject-spreadsheet']);
  assert.ok(slides.every(cue => cue.projector === 'reflex'), 'slow motion stays on the same physical projector');
  let previous;
  for (const slide of slides) {
    recording.time(slide.at * 80 - 0.0001);
    assert.equal(h.beats.filter(beat => beat.id === 'projector.show' && beat.data.slot).at(-1)?.data.slot, previous);
    recording.time(slide.at * 80 + 0.0001);
    const shown = h.beats.filter(beat => beat.id === 'projector.show' && beat.data.slot).at(-1).data;
    assert.equal(shown.slot, slide.slot); assert.equal(shown.id, slide.projector);
    assert.ok(h.captions.textContent.replace(/\s+/g, ' ').includes(slide.phrase));
    assert.equal(recording.el.paused, false);
    previous = slide.slot;
  }
  assert.equal(h.holds.size, 0);
  assert.equal(h.beats.some(beat => beat.id === 'projector.hide'), false, 'the narration never retracts between comparison and slow motion');
  recording.end(); assert.equal(h.holds.size, 0); h.narration.dispose();
});

test('adjusting a subtitle boundary moves the matching slide without an independent timing edit', async () => {
  const subtitleData = { ...NARRATION_SUBTITLES, r3: NARRATION_SUBTITLES.r3.map(cue => [...cue]) };
  const cheetah = subtitleData.r3.find(cue => cue[2].includes('The cheetah reaches'));
  cheetah[0] = 0.15;
  assert.equal(projectionTimeline('r3', subtitleData).find(cue => cue.slot === 'reflex-cheetah').at, 0.15);
  const h = harness({ at: 268, subtitleData }); await h.narration.ready;
  h.streams[0].el.duration = 40;
  h.streams[0].time(5.999); assert.ok(!h.beats.some(beat => beat.data?.slot === 'reflex-cheetah'));
  h.streams[0].time(6.001); assert.match(h.captions.textContent, /^The cheetah reaches/);
  assert.ok(h.beats.some(beat => beat.data?.slot === 'reflex-cheetah'));
  h.narration.setSubtitles(false);
  const shrimp = projectionTimeline('r3', subtitleData).find(cue => cue.slot === 'reflex-shrimp');
  h.streams[0].time(shrimp.at * 40 + 0.001);
  assert.equal(h.captions.textContent, '');
  assert.equal(h.beats.filter(beat => beat.data?.slot).at(-1).data.slot, 'reflex-shrimp');
  h.narration.dispose();
});

test('earlier commentary drives a moving route and has no end-of-recording distance hold', async () => {
  assert.deepEqual(RIDE_RECORDINGS.filter(row => row.id !== 'r1').map(row => [row.id, row.at, row.hold]), [['r2', 92, null], ['r3', 268, null], ['r4', 370, null]]);
  const h = harness({ at: 180 }); await h.narration.ready;
  assert.equal(h.narration.debug().current, null);
  h.cue('narration.r3', 268); assert.equal(h.narration.debug().current, 'r3');
  assert.equal(h.holds.size, 0);
  h.streams[0].time(22); assert.equal(h.pace().mode, 'narration');
  assert.ok(h.pace().speed > .3);
  h.streams[0].time(NARRATION_CLIPS.r3.duration + 1); assert.equal(h.holds.size, 0);
  h.streams[0].end(); assert.equal(h.holds.size, 0); h.narration.dispose();
});

test('intro releases the cart after the opening words and pacing detaches on panic', async () => {
  const h = harness(); await h.narration.ready;
  h.streams[0].time(1.5); assert.ok(h.beats.some(beat => beat.id === 'booth.unlock'));
  h.streams[0].time(20); assert.ok(h.pace().target > 20);
  assert.equal(h.streams[0].el.paused, false);
  h.host.emit('suspend', { on: true, reason: 'panic' }); assert.equal(h.pace(), null);
});

test('the later conversation image and its cleanup use the second wakeup projector', async () => {
  const h = harness({ at: 370 }); await h.narration.ready;
  h.streams[0].time(37);
  assert.ok(h.beats.some(beat => beat.id === 'projector.show' && beat.data.id === 'conversation'));
  h.streams[0].end();
  assert.ok(h.beats.some(beat => beat.id === 'projector.hide' && beat.data.id === 'conversation'));
  h.narration.dispose();
});
