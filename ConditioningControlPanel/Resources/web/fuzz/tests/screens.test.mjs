import { test } from 'node:test';
import assert from 'node:assert/strict';
import { clampSpot, defaultSpot, fromShare, GRIP, holdMedia, screenSize, shopContent, themeNiche, toShare } from '../ui/screens.js';
import { createPictures, ownKind } from '../ui/pictures.js';
import { BY_ID, ITEMS } from '../data/items.js';

const rect = (left, top, right, bottom) => ({ left, top, right, bottom, width: right - left, height: bottom - top });
const DESK = { W: 1280, H: 800, shop: rect(260, 220, 1020, 800) };
const PHONE = { W: 390, H: 800, shop: rect(16, 200, 374, 800) };

test('an empty first-frame shop does not reserve the empty lower half of a phone', () => {
  const box = { getBoundingClientRect: () => PHONE.shop, children: [] };
  const shop = shopContent(box);
  assert.equal(shop.bottom, 200);
  const initial = defaultSpot({ ...PHONE, railW: 0, floor: 750 }, { w: 164, h: 180 });
  assert.deepEqual(clampSpot({ ...initial, w: 164, h: 180 }, { ...PHONE, shop }), initial);
  box.children.push({ getBoundingClientRect: () => rect(16, 200, 374, 425) });
  assert.equal(shopContent(box).bottom, 425);
  assert.deepEqual(clampSpot({ ...initial, w: 164, h: 180 }, { ...PHONE, shop: shopContent(box) }), initial);
});

test('a spot stays in the window', () => {
  assert.deepEqual(clampSpot({ x: -50, y: -9, w: 200, h: 100 }, { W: 1280, H: 800 }), { x: 0, y: 0 });
  assert.deepEqual(clampSpot({ x: 2000, y: 2000, w: 200, h: 100 }, { W: 1280, H: 800 }), { x: 1080, y: 700 });
});

test('a screen can never end up all the way behind the shop', () => {
  const stands = (p, w, h, s) => Math.max(s.top - p.y, s.left - p.x, p.x + w - s.right);
  const d = clampSpot({ x: 500, y: 500, w: 200, h: 200 }, DESK);
  assert.ok(stands(d, 200, 200, DESK.shop) >= GRIP, JSON.stringify(d));
  const p = clampSpot({ x: 100, y: 600, w: 100, h: 100 }, PHONE);
  assert.ok(stands(p, 100, 100, PHONE.shop) >= GRIP, JSON.stringify(p));
  assert.equal(p.x, 100, 'on a phone the only way out is up');
  assert.equal(p.y, 200 - GRIP);
});

test('a screen half out of the shop, or in a rail, stays where it was put', () => {
  assert.deepEqual(clampSpot({ x: 200, y: 400, w: 200, h: 200 }, DESK), { x: 200, y: 400 });
  assert.deepEqual(clampSpot({ x: 8, y: 500, w: 200, h: 200 }, DESK), { x: 8, y: 500 });
});

test('a hidden shop keeps nothing out', () => {
  assert.deepEqual(clampSpot({ x: 500, y: 500, w: 200, h: 200 }, { W: 1280, H: 800, shop: rect(0, 0, 0, 0) }), { x: 500, y: 500 });
});

test('a spot is a share of the free room, so another window keeps the same corner', () => {
  const a = { x: 1072, y: 600, w: 200, h: 100 };
  const share = toShare(a, { W: 1280, H: 800 });
  assert.deepEqual(fromShare(share, a, { W: 1280, H: 800 }), { x: 1072, y: 600 });
  const big = fromShare(share, a, { W: 1920, H: 1080 });
  assert.ok(big.x > 1700, 'still on the right');
  const small = fromShare(share, a, { W: 390, H: 800 });
  assert.ok(small.x + a.w <= 390 && small.y + a.h <= 800, 'still inside');
  assert.deepEqual(toShare({ x: 0, y: 0, w: 500, h: 500 }, { W: 400, H: 400 }), { fx: 0, fy: 0 }, 'bigger than the window does not divide by zero');
});

test('sizes: in the rails when there is room, beside the button when there is not, and never past the free room', () => {
  const rail = screenSize({ W: 1280, railW: 240, flank: 240, maxH: 200 }, { ratio: 1 });
  assert.deepEqual(rail, { w: 224, h: 224 });
  const phone = screenSize({ W: 390, railW: -205, flank: 102, maxH: 90 }, { ratio: 1.15 });
  assert.equal(phone.w, 102);
  assert.equal(phone.h, 90);
  assert.ok(screenSize({ W: 390, railW: -205, flank: 102, maxH: 200 }, { ratio: 0.56, wide: true }).w > 200, 'a wide one takes more of a phone');
});

test('new screens fill the emptier side, from the bottom of a rail, and do not sit on the button', () => {
  const r = { W: 1280, H: 800, railW: 240 };
  const size = { w: 224, h: 258 };
  const a = defaultSpot(r, size, []);
  assert.deepEqual(a, { x: 8, y: 800 - 8 - 258 });
  const b = defaultSpot(r, size, [{ ...a, ...size }]);
  assert.equal(b.x, 1280 - 224 - 8);
  const c = defaultSpot(r, size, [{ ...a, ...size }, { ...b, ...size }]);
  assert.equal(c.x, 8);
  assert.equal(c.y, a.y - 258 - 8);
  const phone = { W: 390, H: 800, railW: -205 };
  const p = defaultSpot({ ...phone, floor: 740 }, { w: 102, h: 110 }, []);
  assert.equal(p.y, 740 - 8 - 110, 'a phone starts in the open room above the dock strip');
  const q = defaultSpot(phone, { w: 102, h: 110 }, [{ ...p, w: 102, h: 110 }]);
  const btn = rect(110, 130, 280, 188);
  for (const s of [p, q]) assert.ok(s.x + 102 <= btn.left + 2 || s.x >= btn.right - 2, JSON.stringify(s));
});

test('the picked theme feeds the screen first, and the base niche stays in the mix', () => {
  assert.equal(themeNiche([], 'pretty', () => 0), 'pretty');
  assert.equal(themeNiche([['pink', 'heels']], 'pretty', () => 0), 'pink');
  assert.equal(themeNiche([['pink']], 'pretty', () => 0.9), 'pretty');
  const seq = [0, 0.99];
  assert.equal(themeNiche([['pink'], ['chrome']], 'pretty', () => seq.shift()), 'chrome', 'a second theme is in the mix');
});

test('own files sort into pictures, gifs and clips, by type and then by name', () => {
  assert.equal(ownKind({ type: 'image/png' }), 'picture');
  assert.equal(ownKind({ type: 'image/gif' }), 'gif');
  assert.equal(ownKind({ type: 'video/webm' }), 'clip');
  assert.equal(ownKind({ type: 'text/plain', name: 'a.png' }), null);
  assert.equal(ownKind({ type: '', name: 'A.GIF' }), 'gif');
  assert.equal(ownKind({ type: '', name: 'a.mov' }), 'clip');
  assert.equal(ownKind({ type: '', name: 'a.exe' }), null);
});

test('each kind asks for its own, a clip screen never gets a still, and a plain caller never gets a clip', async () => {
  const file = (type) => new Blob(['x'], { type });
  const p = createPictures({ source: 'own' });
  p.addOwn([file('video/mp4')]);
  assert.equal(await p.next(), null, 'a css background cannot show a clip');
  assert.equal((await p.next({ kind: 'clip' })).kind, 'clip');
  assert.equal((await p.next({ kind: 'gif' })).kind, 'clip', 'a gif screen may loop a clip');
  assert.equal(await p.next({ kind: 'picture' }), null);
  p.addOwn([file('image/png')]);
  assert.equal((await p.next({ kind: 'picture' })).kind, 'picture');
  assert.equal((await p.next({ kind: 'clip' })).kind, 'clip');
  const stills = createPictures({ source: 'own' });
  stills.addOwn([file('image/png')]);
  assert.equal(await stills.next({ kind: 'clip' }), null, 'no still pretends to be a clip');
  assert.equal(await stills.next({ kind: 'gif' }), null);
  assert.deepEqual(stills.ownCounts, { picture: 1, gif: 0, clip: 0 });
});

test('a plain caller never gets a gif: nothing can stop one in a css background or a plain img, for Gentle or for Wake Up', async () => {
  const p = createPictures({ source: 'own' });
  p.addOwn([new Blob(['g'], { type: 'image/gif' })]);
  assert.equal(await p.next(), null);
  assert.equal(await p.next({ kind: 'picture' }), null);
  assert.equal((await p.next({ kind: 'mixed' })).kind, 'gif');
});

test('Swipe Feed earns per second too, and every row has words', () => {
  assert.ok(BY_ID.d_scroll.fps > 0);
  assert.ok(BY_ID.d_scroll.click > 0);
  assert.deepEqual(ITEMS.filter((i) => !i.what && i.kind === 'item').map((i) => i.id), []);
  assert.equal(BY_ID.d_scroll.name, 'Swipe Feed');
});

test('a feed video starts once while play is pending and stops for hidden, paused and motion-off views', async () => {
  const ctx = {state:{}, paused:false, gentle:false, reduced:false};
  let plays = 0, pauses = 0, finish;
  const movie = {
    tagName:'VIDEO', paused:true, ownerDocument:{hidden:false},
    play() { plays++; return new Promise(resolve => { finish = resolve; }); },
    pause() { pauses++; this.paused = true; },
  };
  holdMedia(ctx,movie); holdMedia(ctx,movie);
  assert.equal(plays,1);
  holdMedia(ctx,movie,false); assert.equal(pauses,1);
  finish(); await new Promise(resolve=>setImmediate(resolve));
  for (const key of ['paused','gentle','reduced']) {
    ctx[key] = true; movie.paused = false;
    holdMedia(ctx,movie); assert.equal(movie.paused,true);
    ctx[key] = false;
  }
  movie.ownerDocument.hidden = true; movie.paused = false;
  holdMedia(ctx,movie); assert.equal(movie.paused,true); assert.equal(plays,1);
  movie.ownerDocument.hidden = false;
  holdMedia(ctx,movie); assert.equal(plays,2);
  finish();
});
