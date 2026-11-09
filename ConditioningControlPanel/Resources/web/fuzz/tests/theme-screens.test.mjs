// Owner 2026-11-08: a theme pick brings exactly three movable picture screens that swap about every 10 s, never picture walls.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { THEME_SCREEN_MS } from '../ui/unlocks/themes-a/theme-screens.js';

const HEADS = {
  'themes-a/sissy.js': 'si_head', 'themes-a/bimbo.js': 'bi_head', 'themes-a/drone.js': 'dr_head', 'themes-a/trance.js': 'tr_head',
  'themes-b/goon.js': 'go_head', 'themes-b/locked.js': 'lo_head', 'themes-b/censored.js': 'ce_head', 'themes-b/infection.js': 'in_head',
};

/** The source of one top-level unlock in a theme file: from its key to the next top-level key. */
function block(src, id) {
  const lines = src.split(/\r?\n/);
  const at = lines.findIndex((l) => l.startsWith(`  ${id}: {`));
  assert.ok(at >= 0, `${id} is in its file`);
  const end = lines.findIndex((l, i) => i > at && /^  [a-z_]+: \{/.test(l));
  return lines.slice(at, end < 0 ? undefined : end).join('\n');
}

test('every theme head mounts the three screens and no picture wall', () => {
  for (const [file, id] of Object.entries(HEADS)) {
    const body = block(readFileSync(new URL(`../ui/unlocks/${file}`, import.meta.url), 'utf8'), id);
    assert.equal(body.match(/themeScreens\(/g)?.length, 1, `${id} mounts the screens once`);
    for (const wall of ['gallery(', 'sideFrames(', 'mountTranceGallery(', 'frame(ctx']) assert.ok(!body.includes(wall), `${id} has no ${wall}`);
  }
});

test('the screens swap about every ten seconds', () => {
  assert.equal(THEME_SCREEN_MS, 10000);
});

// Owner 2026-11-08: "The starting 3 screens never show videos, just stills, nor gifs. 1 in 3 should be a vid or gif."
test('one of the three screens is the motion screen, picked once and kept', async () => {
  const { motionIndex } = await import('../ui/unlocks/themes-a/theme-screens.js');
  const bag = {};
  const store = { get: (k) => bag[k], set: (k, v) => { bag[k] = v; } };
  assert.equal(motionIndex(store, 3, () => 0.99), 2);
  assert.equal(bag.motionSpot, 2);
  assert.equal(motionIndex(store, 3, () => 0), 2, 'a reload keeps the same screen');
  bag.motionSpot = 7;
  assert.equal(motionIndex(store, 3, () => 0.4), 1, 'a bad stored value is dealt again');
  const src = readFileSync(new URL('../ui/unlocks/themes-a/theme-screens.js', import.meta.url), 'utf8');
  assert.match(src, /kind: motion \? 'gif' : 'picture', stillFallback: motion/);
});

test('a clip keeps its turn until its first play is through, at most 20 s; stills and gifs never hold', async () => {
  const { holdsClip, CLIP_HOLD_MS } = await import('../ui/unlocks/themes-a/theme-screens.js');
  assert.equal(CLIP_HOLD_MS, 20000);
  const clip = (duration) => ({ tagName: 'VIDEO', duration });
  assert.equal(holdsClip(clip(15), 1000, 11000), true, '10 s into a 15 s clip');
  assert.equal(holdsClip(clip(15), 1000, 17000), false, 'played through');
  assert.equal(holdsClip(clip(60), 1000, 20000), true);
  assert.equal(holdsClip(clip(60), 1000, 21000), false, 'never past 20 s');
  assert.equal(holdsClip(clip(6), 1000, 11000), false, 'a short loop swaps on its turn');
  assert.equal(holdsClip(clip(NaN), 1000, 2000), false);
  assert.equal(holdsClip({ tagName: 'IMG' }, 1000, 2000), false);
  assert.equal(holdsClip(null, 1000, 2000), false);
});

test('the motion screen draws a gif or a clip from own files, and a still when there is no motion', async () => {
  const { createPictures } = await import('../ui/pictures.js');
  const file = (type) => new Blob(['x'], { type });
  const p = createPictures({ source: 'own' });
  p.addOwn([file('image/png')]);
  assert.equal(await p.next({ kind: 'gif' }), null, 'no motion: the slot falls back');
  assert.equal((await p.next({ still: true })).kind, 'picture', 'the fallback still');
  p.addOwn([file('image/gif'), file('video/mp4')]);
  const kinds = new Set();
  for (let i = 0; i < 8; i++) kinds.add((await p.next({ kind: 'gif' })).kind);
  assert.deepEqual([...kinds].sort(), ['clip', 'gif']);
});

test('the slot asks for a still when a motion screen finds no motion', () => {
  const src = readFileSync(new URL('../ui/screens.js', import.meta.url), 'utf8');
  assert.match(src, /if \(!pic && \(kind === 'picture' \|\| stillFallback\)\)/);
});

test('on the web host (no media origin) a Scrolller mp4 row reaches the motion screen as a clip, with the credit', async () => {
  const { createPictures } = await import('../ui/pictures.js');
  const { scrolllerMedia } = await import('../ui/scrolller-pictures.js');
  const caps = { consent: true, clips: true };
  const frame = { v: 1, rows: [
    { url: 'https://images.scrolller.com/pico/a-480x640.jpg', kind: 'image' },
    { url: 'https://octa.scrolller.com/Clip-mobile.mp4', kind: 'video', mime: 'video/mp4' },
  ] };
  assert.deepEqual(scrolllerMedia(frame, caps, 'https://tc.cclabs.app').map((r) => r.kind), ['picture', 'clip']);
  const p = createPictures({ scrolller: caps, source: 'scrolller' });
  p.receiveScrolller(frame);
  const motion = await p.next({ kind: 'gif', allowScrolller: true });
  assert.equal(motion.kind, 'clip');
  assert.equal(motion.url, 'https://octa.scrolller.com/Clip-mobile.mp4');
  assert.equal(motion.credit, 'via Scrolller');
  const still = await p.next({ kind: 'picture', allowScrolller: true });
  assert.equal(still.kind, 'picture', 'the two still screens never get the clip');
});

// Owner 2026-11-09 (Pixel 7, "no pictures"): on a phone every new dock object starts folded, so the screens ask to open.
test('the three theme screens ask the dock to open them; other screens keep the dock default', async () => {
  const { themeScreens } = await import('../ui/unlocks/themes-a/theme-screens.js');
  const { screen } = await import('../ui/screens.js');
  const docOf = { getElementById: () => ({}), addEventListener() {}, removeEventListener() {}, hidden: false, head: { appendChild() {} } };
  const node = () => { const n = { style: {}, dataset: {}, classList: { toggle() {}, add() {} }, children: [], ownerDocument: docOf, isConnected: true,
    appendChild: (c) => c, insertBefore: (c) => c, remove() {}, addEventListener() {}, getBoundingClientRect: () => ({ left: 0, top: 0, width: 0, height: 0 }) }; return n; };
  const docks = [];
  const ctx = {
    layer: () => node(), bounds: () => ({ w: 412, h: 892 }), store: { get: () => undefined, set() {} }, pick: () => null, rng: () => 0.3,
    dock: (opts) => { docks.push(opts); return node(); }, el: () => node(), after() {}, every() {}, cleanup() {}, on() {}, emit() {},
    item: { fps: 0 }, state: { ended: false, pace: 1 }, fmt: String, paused: false, gentle: false, reduced: false, anim() {},
    pictures: { next: async () => null, source: 'scrolller' }, palette: () => ({ themes: [] }),
  };
  const list = themeScreens(ctx, { niches: ['pretty'] });
  assert.equal(list.length, 3);
  assert.deepEqual(docks.map((o) => o.min), [false, false, false], 'each theme screen asks to open');
  docks.length = 0;
  screen(ctx, { kind: null });
  assert.equal(docks[0].min, undefined, 'a plain screen leaves the phone default alone');
});
