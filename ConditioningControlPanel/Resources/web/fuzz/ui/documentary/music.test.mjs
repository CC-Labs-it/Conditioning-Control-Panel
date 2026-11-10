import test from 'node:test';
import assert from 'node:assert/strict';
import { createDocumentaryMusic } from './music.js';

function bus() {
  const map = new Map();
  return {
    on(id, fn) { if (!map.has(id)) map.set(id, new Set()); map.get(id).add(fn); return () => map.get(id)?.delete(fn); },
    emit(id, data) { for (const fn of [...(map.get(id) || [])]) fn(data); },
  };
}
function harness() {
  let clock = 0, serial = 0;
  const timers = new Map(), streams = [], host = bus(), events = bus(), doc = new EventTarget();
  doc.hidden = false;
  const ctx = { paused: false, on: host.on, cleanup() {} };
  const tunnel = { state: { docked: false }, captions: { ownerDocument: doc }, ready: Promise.resolve(), on: events.on };
  const music = createDocumentaryMusic(ctx, tunnel, {
    now: () => clock,
    setTimer(fn, ms) { const id = ++serial; timers.set(id, { at: clock + ms, fn }); return id; },
    clearTimer(id) { timers.delete(id); },
    createStream(url, settings) {
      assert.equal(settings.source, 'documentary-music'); assert.equal(settings.bus, 'podcast');
      const el = new EventTarget(); Object.assign(el, { paused: true, currentTime: 0, duration: 100 });
      const source = {
        el, url, gain: settings.gain, stopped: false,
        play() { if (el.paused) { el.paused = false; el.dispatchEvent(new Event('playing')); } return Promise.resolve(); },
        pause() { el.paused = true; }, stop() { this.pause(); this.stopped = true; }, setGain(value) { this.gain = value; },
        time(time) { el.currentTime = time; el.dispatchEvent(new Event('timeupdate')); },
        end() { el.paused = true; el.dispatchEvent(new Event('ended')); },
        error() { el.dispatchEvent(new Event('error')); },
      };
      streams.push(source); return source;
    },
  });
  return { music, streams, host, events, doc, ctx, tunnel, timers,
    tick(ms) {
      const end = clock + ms;
      for (let n=0;n<1000;n++) {
        const due = [...timers].filter(([, job]) => job.at <= end).sort((a,b) => a[1].at-b[1].at)[0];
        if (!due) { clock = end; return; }
        clock = due[1].at; timers.delete(due[0]); due[1].fn();
      }
      throw new Error('Envelope did not settle');
    },
  };
}

test('three music tracks loop sequentially across arrival without parallel streams', async () => {
  const h = harness(); await h.music.ready;
  assert.equal(h.music.debug().current, 'breathe-in');
  h.streams[0].time(25); h.tunnel.state.docked = true; h.events.emit('arrive');
  assert.equal(h.streams.length, 1); assert.equal(h.streams[0].el.currentTime, 25);
  for (const id of ['idle-cycle', 'sealed', 'breathe-in']) {
    h.streams.at(-1).end();
    assert.equal(h.music.debug().current, id);
    assert.equal(h.streams.filter(source => !source.stopped).length, 1);
  }
  h.music.dispose(); assert.equal(h.timers.size, 0);
});

test('music fades at track boundaries and ducks smoothly under narration', async () => {
  const h = harness(); await h.music.ready;
  const source = h.streams[0]; assert.equal(source.gain, 0);
  source.time(.7); assert.ok(source.gain > .2 && source.gain < .24);
  source.time(3); h.tick(100); assert.ok(source.gain > .43);
  h.music.setSpeaking(true); h.tick(1000); assert.ok(source.gain < .15);
  h.music.setSpeaking(false); h.tick(3500); assert.ok(source.gain > .43);
  source.time(99); assert.ok(source.gain > .19 && source.gain < .21);
  source.time(100); assert.equal(source.gain, 0);
  h.music.dispose(); assert.equal(h.timers.size, 0);
});

test('Wake Up pauses music, hidden ending continues, panic disposes it', async () => {
  const h = harness(); await h.music.ready;
  h.streams[0].time(3); h.doc.hidden = true; h.doc.dispatchEvent(new Event('visibilitychange'));
  assert.equal(h.streams[0].el.paused, true);
  h.tunnel.state.docked = true; h.events.emit('arrive'); assert.equal(h.streams[0].el.paused, false);
  h.ctx.paused = true; h.host.emit('pause'); assert.equal(h.streams[0].el.paused, true);
  assert.equal(h.timers.size, 0);
  h.ctx.paused = false; h.host.emit('resume'); assert.equal(h.streams[0].el.paused, false);
  h.host.emit('suspend', { on: true, reason: 'panic' });
  assert.equal(h.streams[0].stopped, true); assert.equal(h.timers.size, 0);
});

test('unavailable music stops after one attempt per track', async () => {
  const h = harness(); await h.music.ready;
  for (let n=0;n<3;n++) h.streams.at(-1).error();
  assert.equal(h.streams.length, 3); assert.equal(h.music.debug().current, null);
  assert.equal(h.timers.size, 0); h.music.dispose();
});
