import test from 'node:test';
import assert from 'node:assert/strict';
import { createProjectorEffects } from './projector-effects.js';

function harness({ armed = true, gentle = false, reduced = false } = {}) {
  const listeners = new Map(), inputs = [], sources = [], animations = [], buffers = [];
  const doc = new EventTarget(); doc.hidden = false;
  const ac = {
    state: 'running', currentTime: 8, sampleRate: 24000,
    createBuffer(channels, size, rate) {
      const data = new Float32Array(size);
      const buffer = { duration: size / rate, getChannelData: () => data };
      buffers.push(buffer); return buffer;
    },
    createBufferSource() {
      const source = { starts: [], stops: [], connected: false, disconnected: false,
        connect() { this.connected = true; }, disconnect() { this.disconnected = true; },
        start(at) { this.starts.push(at); }, stop(at) { this.stops.push(at); },
        end() { this.onended?.(); },
      };
      sources.push(source); return source;
    },
  };
  const ctx = {
    paused: false, gentle, reduced,
    audio: { input(settings) {
      if (!armed) return null;
      const input = { ac, node: {}, settings, stopped: false, stop() { this.stopped = true; } };
      inputs.push(input); return input;
    } },
    anim(node, frames, settings) {
      let resolve;
      const animation = { node, frames, settings, paused: false, cancelled: false,
        finished: new Promise(done => { resolve = done; }),
        pause() { this.paused = true; }, play() { this.paused = false; },
        cancel() { this.cancelled = true; resolve(); }, finish() { resolve(); },
      };
      animations.push(animation); return animation;
    },
    cleanup() {},
    on(id, fn) {
      if (!listeners.has(id)) listeners.set(id, new Set());
      listeners.get(id).add(fn); return () => listeners.get(id).delete(fn);
    },
  };
  const effects = createProjectorEffects(ctx, { document: doc });
  return { effects, ctx, ac, doc, inputs, sources, animations, buffers,
    arm() { armed = true; },
    emit(id, value) { for (const fn of [...(listeners.get(id) || [])]) fn(value); },
    node(parentNode = {}) { return { parentNode, removed: false, remove() { this.removed = true; this.parentNode = null; } }; },
  };
}

test('short mechanical sounds use one scoped mixer route and stop at their finite durations', () => {
  const h = harness();
  assert.equal(h.effects.slide('reflex:1'), true);
  assert.equal(h.effects.slide('reflex:1'), false);
  assert.equal(h.effects.unroll('reflex'), true);
  assert.equal(h.effects.unroll('reflex'), false);
  assert.equal(h.inputs.length, 2);
  for (let i = 0; i < h.sources.length; i++) {
    const source = h.sources[i], input = h.inputs[i];
    assert.equal(input.settings.bus, 'sfx');
    assert.equal(input.settings.source, 'documentary-projectors');
    assert.ok(input.settings.gain < 1);
    assert.equal(source.loop, false);
    assert.deepEqual(source.starts, [8]);
    assert.equal(source.stops[0], 8 + source.buffer.duration);
    assert.ok(source.buffer.duration <= 1.1);
    const samples = source.buffer.getChannelData(0);
    assert.ok(samples[0] === 0);
    assert.ok(Math.abs(samples.at(-1)) < .001);
    assert.ok(samples.every(value => Number.isFinite(value) && Math.abs(value) <= .5));
    source.end(); assert.equal(input.stopped, true);
  }
  assert.equal(h.effects.debug().sounds, 0);
  h.effects.resetUnroll('reflex'); assert.equal(h.effects.unroll('reflex'), true);
  assert.equal(h.buffers.length, 2, 'the finite PCM buffers are reused');
  h.effects.dispose();
});

test('preload and stale duplicate calls cannot produce cues before a gesture or accumulate suspended sounds', () => {
  const h = harness({ armed: false });
  const first = h.node();
  h.effects.transition(first, { initial: true, key: 'initial' });
  assert.equal(h.effects.debug().soundCount, 0); assert.equal(h.animations.length, 0);
  assert.equal(h.effects.slide('unarmed'), false); assert.equal(h.sources.length, 0);
  h.arm(); assert.equal(h.effects.slide('unarmed'), false, 'a missed cue is not replayed after unlocking');
  h.ac.state = 'suspended'; assert.equal(h.effects.slide('suspended'), false);
  assert.equal(h.sources.length, 0); assert.equal(h.inputs.at(-1).stopped, true);
  h.ac.state = 'running'; assert.equal(h.effects.slide('next'), true);
  h.effects.dispose();
});

test('decoded slide transitions crossfade for 220ms and superseded completion preserves the newest image', async () => {
  const h = harness(), parent = {}, old = h.node(parent), a = h.node(parent), b = h.node(parent);
  h.effects.transition(a, { outgoing: old, key: 'a' });
  assert.equal(h.animations.length, 2);
  assert.equal(h.animations[0].settings.duration, 220);
  assert.equal(h.animations[0].frames[0].opacity, 0);
  assert.match(h.animations[0].frames[0].transform, /translateX/);
  h.effects.transition(b, { outgoing: a, key: 'b' });
  await Promise.resolve(); await Promise.resolve();
  assert.equal(old.removed, true); assert.equal(b.removed, false);
  assert.equal(h.effects.debug().transitions, 1);
  h.animations.slice(-2).forEach(animation => animation.finish());
  await Promise.resolve(); await Promise.resolve();
  assert.equal(a.removed, true); assert.equal(b.removed, false);
  assert.equal(h.effects.debug().transitions, 0);
  h.effects.dispose();
});

test('gentle transitions only change opacity and reduced motion commits immediately', () => {
  const h = harness({ gentle: true }), parent = {};
  h.effects.transition(h.node(parent), { outgoing: h.node(parent), key: 'gentle' });
  assert.ok(h.animations.every(animation => animation.frames.every(frame => !('transform' in frame))));
  assert.equal(h.inputs[0].settings.gain, .3); h.effects.dispose();
  const r = harness({ reduced: true }), old = r.node();
  r.effects.transition(r.node(), { outgoing: old, key: 'reduced' });
  assert.equal(r.animations.length, 0); assert.equal(old.removed, true); r.effects.dispose();
});

test('pause and hidden document stop sound immediately; panic cancels all work without replay', async () => {
  const h = harness(), parent = {}, current = h.node(parent), old = h.node(parent);
  h.effects.transition(current, { outgoing: old, key: 'a' }); h.effects.unroll('booth');
  h.ctx.paused = true; h.emit('pause');
  assert.equal(h.effects.debug().sounds, 0); assert.ok(h.inputs.every(input => input.stopped));
  assert.ok(h.animations.every(animation => animation.paused));
  h.ctx.paused = false; h.emit('resume');
  assert.ok(h.animations.every(animation => !animation.paused)); assert.equal(h.effects.debug().sounds, 0);
  h.effects.slide('b'); h.doc.hidden = true; h.doc.dispatchEvent(new Event('visibilitychange'));
  assert.equal(h.effects.debug().sounds, 0); assert.equal(h.effects.slide('hidden'), false);
  h.doc.hidden = false; h.doc.dispatchEvent(new Event('visibilitychange'));
  h.effects.slide('c'); h.emit('suspend', { on: true, reason: 'panic' });
  await Promise.resolve();
  assert.equal(h.effects.debug().disposed, true); assert.equal(h.effects.debug().transitions, 0);
  assert.equal(h.effects.debug().sounds, 0); assert.equal(old.removed, true); assert.equal(current.removed, false);
  assert.equal(h.effects.slide('after-panic'), false); assert.equal(h.effects.unroll('after-panic'), false);
});
