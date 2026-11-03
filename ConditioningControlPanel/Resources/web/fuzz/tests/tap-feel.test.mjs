import { test } from 'node:test';
import assert from 'node:assert/strict';
import { buttonImpulse, contactSparks, createTapPhrase } from '../ui/unlocks/basics/tap-feel.js';

function fixture() {
  const nodes = [], animations = [], frames = [], events = {};
  const doc = {
    defaultView: { getComputedStyle: () => ({ scale: '1.02 .97' }) },
    createElement() {
      const el = { style: {}, dataset: {}, remove() { const i = nodes.indexOf(this); if (i >= 0) nodes.splice(i, 1); } };
      return el;
    },
  };
  const button = { ownerDocument: doc, getBoundingClientRect: () => ({ left: 100, top: 100, width: 80, height: 40 }) };
  const ctx = {
    button: { el: button }, state: { clock: 0 }, gentle: false,
    on(name, fn) { (events[name] ||= []).push(fn); },
    frame(fn) { frames.push(fn); },
    layer: () => ({ appendChild(el) { nodes.push(el); } }),
    rng() { throw new Error('cosmetic feedback must not consume game RNG'); },
    anim(el, frames, opts) {
      const a = { el, frames, opts, cancelled: false, cancel() { this.cancelled = true; this.oncancel?.(); } };
      animations.push(a);
      return a;
    },
  };
  return { ctx, nodes, animations, frames, events };
}

test('rapid button presses replace the old spring and keep the current scale', () => {
  const { ctx, animations } = fixture();
  buttonImpulse(ctx);
  ctx.state.clock = 80;
  buttonImpulse(ctx);
  assert.equal(animations[0].cancelled, true);
  assert.equal(animations[1].frames[0].scale, '1.02 .97');
  assert.equal(animations.filter((a) => !a.cancelled).length, 1);
});

test('a lucky spring survives the ordinary tap callback; Gentle removes it', () => {
  const { ctx, animations } = fixture();
  buttonImpulse(ctx, 1.7, true);
  buttonImpulse(ctx);
  assert.equal(animations.length, 1);
  ctx.gentle = true;
  buttonImpulse(ctx);
  assert.equal(animations[0].cancelled, true);
  assert.equal(animations.length, 1);
});

test('contact sparks have a hard live cap and cancelled animations release their nodes', () => {
  const { ctx, nodes, animations } = fixture();
  for (let i = 0; i < 100; i++) { ctx.state.clock += 120; contactSparks(ctx, { x: 150, y: 130 }, i % 3 === 0); }
  assert.equal(nodes.length, 32);
  animations.forEach((a) => a.cancel());
  assert.equal(nodes.length, 0);
  contactSparks(ctx, { x: 150, y: 130 }, true);
  assert.equal(nodes.length, 18);
});

test('autotaps and Gentle cannot flood the page with ordinary contact sparks', () => {
  const { ctx, nodes } = fixture();
  contactSparks(ctx, { auto: true });
  ctx.gentle = true;
  contactSparks(ctx, {}, true);
  assert.equal(nodes.length, 0);
});

test('changing motion preference clears active effects, including on resume', () => {
  const { ctx, nodes, animations, frames, events } = fixture();
  buttonImpulse(ctx);
  contactSparks(ctx, { x: 150, y: 130 });
  assert.equal(frames.length, 1);
  ctx.gentle = true;
  events.resume.forEach((fn) => fn());
  assert.equal(nodes.length, 0);
  assert.ok(animations.every((a) => a.cancelled));
  ctx.gentle = false;
  ctx.state.clock = 200;
  contactSparks(ctx, { x: 150, y: 130 });
  ctx.gentle = true;
  frames.forEach((fn) => fn());
  assert.equal(nodes.length, 0);
});

test('tap notes form a bounded phrase and restart after a break', () => {
  const phrase = createTapPhrase();
  const cues = Array.from({ length: 16 }, (_, i) => phrase.tap(i * 120));
  assert.deepEqual(cues.filter((c) => c.note != null).map((c) => c.note), [3, 5, 6, 8, 6, 5, 3, 1]);
  assert.equal(phrase.tap(3500).note, 3);
  assert.equal(phrase.tap(3501), null);
  phrase.reset();
  assert.equal(phrase.tap(3501).note, 3);
});

test('autotaps are quieter and rate limited without adding melody', () => {
  const phrase = createTapPhrase();
  assert.deepEqual(phrase.tap(0, true), { note: null, gain: .18 });
  assert.equal(phrase.tap(100, true), null);
  assert.deepEqual(phrase.tap(240, true), { note: null, gain: .18 });
});
