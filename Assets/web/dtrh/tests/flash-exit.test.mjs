// How a flash card leaves (engine/flashExit.js), and the moving-picture door (hostMedia.js).
import test from 'node:test';
import assert from 'node:assert/strict';
import { EXIT_POOL, EXIT_MS, pickExit, exitPlan, toKeyframes } from '../engine/flashExit.js';

test('a pop never draws the same leave twice in a row', () => {
  let last = null;
  for (let i = 0; i < 200; i++) {
    const s = pickExit(last, Math.random);
    assert.ok(EXIT_POOL.includes(s));
    assert.notEqual(s, last);
    last = s;
  }
  assert.equal(pickExit(null, () => 0.999999), EXIT_POOL[EXIT_POOL.length - 1]);
});

test('every style has a plan, and none of them ever brightens back up', () => {
  for (const style of EXIT_POOL) {
    for (const level of ['full', 'reduced']) {
      const p = exitPlan(style, level, 7);
      assert.equal(p.style, style);
      assert.ok(p.duration > 0 && p.duration <= EXIT_MS[style]);
      if (style === 'shatter') { assert.equal(p.shards.length, p.cols * p.rows); continue; }
      assert.ok(p.frames.length >= 2, style);
      assert.equal(p.frames[0].offset, 0);
      assert.equal(p.frames[p.frames.length - 1].offset, 1);
      assert.equal(p.frames[p.frames.length - 1].opacity, 0);
      for (let i = 1; i < p.frames.length; i++) {
        assert.ok(p.frames[i].opacity <= p.frames[i - 1].opacity, style + ' flickers');
        assert.ok(p.frames[i].offset > p.frames[i - 1].offset);
      }
    }
  }
});

test('motion off is the short fade whatever the pick; reduced drops the spin and the tumble', () => {
  for (const style of EXIT_POOL) {
    const p = exitPlan(style, 'off', 3);
    assert.equal(p.style, 'fade');
    assert.equal(p.duration, EXIT_MS.fade);
  }
  assert.ok(!/rotate\((-?[1-9])/.test(exitPlan('twirl', 'reduced', 3).frames.map((f) => f.t).join(' ')));
  assert.ok(/rotate\(-?300deg\)/.test(exitPlan('twirl', 'full', 3).frames.at(-1).t));
  assert.ok(exitPlan('shatter', 'reduced', 3).shards.every((s) => s.rot === 0));
  assert.ok(exitPlan('shatter', 'full', 3).shards.every((s) => s.dy > 0), 'pieces sink');
  assert.ok(exitPlan('pop', 'reduced', 3).duration < exitPlan('pop', 'full', 3).duration);
});

test('keyframes keep the card centred and scale to the opacity it had', () => {
  const k = toKeyframes(exitPlan('glitch', 'full', 5), 'translate(-50%, -50%)', 0.5);
  assert.ok(k.every((f) => f.transform.startsWith('translate(-50%, -50%) ')));
  assert.equal(k[0].opacity, 0.5);
  assert.ok(k.some((f) => f.clipPath));
});

test('moving pictures get their own door and stay out of the still and clip draws', async () => {
  globalThis.window = globalThis.window || {};
  const { createHostMediaSource } = await import('../hostMedia.js');
  const m = createHostMediaSource();
  m.setManifest({
    images: [{ name: 'online100:a-1.jpg', url: 'https://ccp.assets/.temp/a.jpg' }],
    videos: [
      { name: 'online100:loop:b-2.mp4', url: 'https://ccp.assets/.temp/b.mp4' },
      { name: 'online100:loop:c-3.mp4', url: 'https://cdn.example/c.mp4' },
      { name: 'online100:d-4.mp4', url: 'https://ccp.assets/.temp/d.mp4' },
    ],
  });
  assert.deepEqual(m.stats(), { images: 1, videos: 1, skipped: 0 });
  const loop = m.drawLoop();
  assert.equal(loop.kind, 'loop');
  assert.equal(loop.name, 'b-2.mp4');
  assert.equal((await loop.acquire()).url, 'https://ccp.assets/.temp/b.mp4');
  for (let i = 0; i < 12; i++) assert.notEqual(m.draw().name, 'b-2.mp4');
  assert.equal(m.drawRemoteDom('video').name, 'c-3.mp4', 'a moving picture with no copy stays a DOM-only clip');
  m.setManifest({ images: [], videos: [] });
  assert.equal(m.drawLoop(), null);
});
