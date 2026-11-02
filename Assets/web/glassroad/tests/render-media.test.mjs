import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import {
  MEDIA_DEFAULTS, mediaLook, tierOf, roadSamples, sizeOf, axesFrom, rectPoints, tubeClash, placeBillboard, haloSlots,
  tunnelLayout, fitCover, fitContain, spinSafeCrop, safeSpin, createUploadBudget, urlAllowed, creditText, colourOf,
  slotColour, tierSlots, FALLBACK_SPECTRUM, V3,
} from '../render/mediaMath.js';
import { PROGRAMS, createPlaceholder, programFor, spinOf, warpOf, loomReady, loomParams } from '../render/mediaPlaceholders.js';

const json = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const layout = JSON.parse(readFileSync(new URL('../tracks/threshold.media.json', import.meta.url), 'utf8'));
const tr = loadTrack(json);
const M = mediaLook();
const samples = roadSamples(tr, M.checkStepM);
const fine = roadSamples(tr, 2);
const near = (a, b, e = 1e-6) => Math.abs(a - b) < e;
const unit = (v) => near(Math.hypot(...v), 1, 1e-6);

test('media: the layout follows the polish 3 corridors', () => {
  assert.equal(layout.track, 'threshold');
  const fs = layout.billboards.map((b) => b.f);
  // polish 6: refit to the stacked course (render/colossiMath.js corridorBoards)
  assert.deepEqual(fs, [0.06, 0.2, 0.27, 0.35, 0.37, 0.575, 0.68, 0.76, 0.808, 0.99]);
  layout.billboards.forEach((b, i) => {
    if (i) assert.notEqual(b.side, layout.billboards[i - 1].side, b.id + ' alternates sides');
    assert.ok(b.heightM >= 8 && b.heightM <= 14, b.id + ' height');
    assert.ok(b.offM >= 8 && b.offM <= 14, b.id + ' off the road edge');
    assert.ok(['portrait', 'landscape'].includes(b.kind));
  });
  assert.ok(layout.billboards.some((b) => b.kind === 'portrait') && layout.billboards.some((b) => b.kind === 'landscape'));
  assert.equal(layout.halo.f, 0);
  assert.ok(layout.halo.count >= 8 && layout.halo.count <= 12);
  assert.ok(layout.tunnel.from >= 0.88 && layout.tunnel.to <= 0.93 && layout.tunnel.to > layout.tunnel.from);
});

test('media: every billboard places, faces the oncoming kart and clears every road stretch with its stalk', () => {
  for (const b of layout.billboards) {
    const sl = placeBillboard(b, tr, samples, M);
    assert.ok(sl, b.id + ' placed');
    for (const v of [sl.X, sl.Y, sl.Z]) assert.ok(unit(v));
    assert.ok(near(V3.dot(sl.X, sl.Y), 0, 1e-6) && near(V3.dot(sl.Y, sl.Z), 0, 1e-6));
    const f = tr.frameAt(sl.s);
    assert.ok(V3.dot(sl.Z, f.t) < -0.7, b.id + ' faces back down the road');
    assert.ok(V3.dot(sl.Z, f.b) * sl.side < 0, b.id + ' turned toward the road');
    // lateral gap from the road edge to the nearest board point is at least offM - 1 m
    const lat = Math.min(...rectPoints(sl).map((P) => Math.abs(V3.dot(V3.sub(P, f.p), f.b))));
    assert.ok(lat >= f.w / 2 + Math.min(b.offM, 8) - 1, b.id + ' lateral ' + lat.toFixed(1));
    const stalk = [0.2, 0.4, 0.6, 0.8, 1].map((u) => V3.add(sl.stalk.top, V3.mul(V3.sub(sl.stalk.bottom, sl.stalk.top), u)));
    assert.equal(tubeClash([...rectPoints(sl, 6), ...stalk], fine, M), null, b.id + ' clear (2 m check)');
    const [w, h] = [sizeOf(b.kind, b.heightM).w, b.heightM];
    assert.ok(near(sl.w, w) && near(sl.h, h));
  }
});

test('media: the halo rings the start/finish overhead, faces in and down, and clears the road', () => {
  const f0 = tr.frameAt(0);
  for (const orbit of [0, 0.7, 2.1]) {
    const H = haloSlots(layout.halo, f0, layout.halo.count, orbit, M);
    assert.equal(H.length, layout.halo.count);
    for (const sl of H) {
      const toAxis = V3.sub(H.centre, sl.C);
      assert.ok(near(Math.hypot(...toAxis), layout.halo.radiusM, 1e-6));
      assert.ok(V3.dot(sl.Z, toAxis) > 0, 'faces the axis');
      assert.ok(V3.dot(sl.Z, f0.n) < -0.3, 'tipped down');
      assert.ok(V3.dot(V3.sub(sl.C, f0.p), f0.n) > 12, 'well over the karts');
    }
    assert.equal(tubeClash(H.flatMap((s) => rectPoints(s, 4)), fine, M), null);
  }
});

test('media: the tunnel lines its corridor with screens facing in, and its glass clears every other stretch', () => {
  const T = tunnelLayout(layout.tunnel, tr, M, 1);
  const L = tr.lengthM;
  assert.ok(T.rings.length >= 6 && T.slots.length === T.rings.length * 3);
  for (const sl of T.slots) {
    assert.ok(sl.s > T.fromS && sl.s < T.toS, 'screen lies inside the actual mapped tunnel corridor');
    const f = tr.frameAt(sl.s), axis = V3.add(f.p, V3.mul(f.n, T.heightM * 0.4));
    assert.ok(V3.dot(sl.Z, V3.sub(axis, sl.C)) > 0, sl.id + ' faces in');
    assert.ok(V3.dot(sl.Z, f.t) < 0, sl.id + ' turned toward oncoming karts');
    const q = V3.sub(sl.C, f.p);
    assert.ok(V3.dot(q, f.n) > 1.5, sl.id + ' above the karts');
  }
  const own = (f) => f.s > T.fromS - 30 && f.s < T.toS + 30;
  const pts = [];
  for (let i = 0; i < T.shell.positions.length; i += 3) pts.push(T.shell.positions.slice(i, i + 3));
  assert.equal(tubeClash(pts, fine, M, own), null, 'shell clear of the stretches that pass near it');
  assert.equal(T.shell.indices.length % 3, 0);
  assert.ok(Math.max(...T.shell.indices) < pts.length);
  assert.ok(Math.max(...T.ribs.indices) < T.ribs.positions.length / 3);
  const phone = tunnelLayout(layout.tunnel, tr, M, tierOf('phone').tunnelStepMul);
  assert.ok(phone.slots.length <= Math.ceil(T.slots.length / 2) + 3, 'phone tier builds about half the tunnel screens');
});

test('media: phone keeps fewer slots and smaller canvases', () => {
  const hi = tierOf('high'), ph = tierOf('phone');
  assert.ok(ph.canvasPx < hi.canvasPx && ph.programs < hi.programs && ph.sourceFps <= hi.sourceFps && hi.placeholderFps === 0 && ph.maxUploads <= 1 && hi.maxUploads <= 3);
  const all = layout.billboards.map((b) => placeBillboard(b, tr, samples, M));
  const kept = tierSlots(all, 'phone');
  assert.ok(kept.length < all.length && kept.length >= 5);
  assert.equal(tierSlots(all, 'high').length, all.length);
  assert.ok(Math.round(layout.halo.count * ph.haloShare) <= 8);
});

test('media: cover, contain and the spin-safe crop', () => {
  assert.deepEqual(fitCover(16 / 9, 16 / 9), { cx: 0.5, cy: 0.5, sx: 1, sy: 1 });
  const p = fitCover(16 / 9, 9 / 16);
  assert.ok(near(p.sy, 1) && near(p.sx, (9 / 16) / (16 / 9)));
  const l = fitCover(9 / 16, 16 / 9);
  assert.ok(near(l.sx, 1) && near(l.sy, (9 / 16) / (16 / 9)));
  const c = fitContain(1, 2);
  assert.ok(near(c.sx, 2) && near(c.sy, 1));
  for (const a of [9 / 16, 1, 16 / 9]) {
    const s = spinSafeCrop(a);
    assert.ok(near(Math.hypot(s.sx, s.sy), 1, 1e-9), 'half diagonal 0.5 at every turn');
    assert.ok(near(s.sx / s.sy, a, 1e-9), 'keeps the slot aspect');
  }
  assert.deepEqual(fitCover(0, 1), { cx: 0.5, cy: 0.5, sx: 1, sy: 1 });
});

test('media: Loom turns never pass maxBandHz bands a second', () => {
  for (const arms of [1, 4, 6, 12]) {
    const r = safeSpin(arms, 50);
    assert.ok((Math.abs(r) * arms) / (Math.PI * 2) <= M.maxBandHz + 1e-9);
    assert.ok(safeSpin(arms, -50) < 0);
  }
  assert.equal(safeSpin(4, 0.3), 0.3);
  assert.ok(M.maxBandHz < 3);
  for (let p = 0; p < PROGRAMS.length; p++) {
    const arms = PROGRAMS[p].arms || 1;
    assert.ok((Math.abs(spinOf(p)) * arms) / (Math.PI * 2) <= M.maxBandHz + 1e-9, PROGRAMS[p].id);
  }
  assert.ok(spinOf(0) !== 0 && warpOf(1) > 0 && warpOf(1) < 0.1);
});

test('media: the upload budget takes at most max, overdue first, and skips far or off sources', () => {
  const b = createUploadBudget();
  const a = b.add({ every: 0.1, near: 50 }), c = b.add({ every: 0.1, near: 10 }), d = b.add({ every: 0.1, near: 5000 }), e = b.add({ every: 0.1, near: 1, on: false });
  b.add({ every: 0.1, near: 20 });
  let got = b.pick(0, 2, 900);
  assert.equal(got.length, 2);
  assert.deepEqual(got, [c, 4]);
  assert.ok(!got.includes(d) && !got.includes(e));
  for (const i of got) b.done(i, 0);
  got = b.pick(0.01, 2, 900);
  assert.deepEqual(got, [a]);
  b.done(a, 0.01);
  assert.equal(b.pick(0.05, 2, 900).length, 0, 'nothing due yet');
  assert.ok(b.pick(0.2, 2, 900).length <= 2);
  // a long frame never builds a burst: due steps to now + every / 2 at least
  b.done(c, 10);
  assert.ok(b.list[c].due >= 10.05);
});

test('media: setSource takes only local or same-origin urls', () => {
  const page = 'https://app.cclabs.app';
  assert.ok(urlAllowed('https://ccp.assets/scrolller/abc.webp', page));
  assert.ok(urlAllowed('/relay/media/abc.webp', page));
  assert.ok(urlAllowed('https://app.cclabs.app/relay/abc.mp4', page));
  assert.ok(urlAllowed('blob:https://app.cclabs.app/1234', page));
  assert.ok(urlAllowed('data:image/png;base64,AAAA', page));
  assert.ok(!urlAllowed('https://images.scrolller.com/abc.jpg', page));
  assert.ok(!urlAllowed('http://ccp.assets/abc.jpg', page));
  assert.ok(!urlAllowed('https://example.com/x.png', page));
  assert.ok(!urlAllowed('data:text/html,hi', page));
  assert.ok(!urlAllowed('', page) && !urlAllowed(null, page));
});

test('media: credit text', () => {
  assert.equal(creditText('scrolller'), 'via Scrolller');
  assert.equal(creditText({ source: 'Scrolller' }), 'via Scrolller');
  assert.equal(creditText(null), '');
  assert.equal(creditText('somebody'), '');
  assert.equal(creditText({ text: 'via the host' }), 'via the host');
});

test('media: colour reader takes every shape spectrum.js may hand back', () => {
  assert.deepEqual(colourOf('#ff0000'), [1, 0, 0]);
  assert.deepEqual(colourOf('#0f0'), [0, 1, 0]);
  assert.deepEqual(colourOf([0, 0, 255]), [0, 0, 1]);
  assert.deepEqual(colourOf([0.5, 0.5, 0.5]), [0.5, 0.5, 0.5]);
  assert.deepEqual(colourOf({ r: 1, g: 0, b: 0 }), [1, 0, 0]);
  assert.deepEqual(colourOf(0), [1, 0, 0]);
  assert.deepEqual(colourOf(120).map((v) => Math.round(v)), [0, 1, 0]);
  assert.deepEqual(colourOf({ h: 240 }).map((v) => Math.round(v)), [0, 0, 1]);
  assert.deepEqual(colourOf('nope', [9, 9, 9]), [9, 9, 9]);
  // the fallback ring is a spectrum: pink and violet lead, cyan, lime, gold, orange and red all appear
  const hues = FALLBACK_SPECTRUM.map((h) => colourOf(h));
  assert.ok(hues.some(([r, g, b]) => b > 0.8 && g > 0.8 && r < 0.2), 'cyan');
  assert.ok(hues.some(([r, g, b]) => r > 0.9 && g < 0.3 && b < 0.3), 'red');
  assert.ok(hues.some(([r, g, b]) => r > 0.5 && g > 0.9 && b < 0.3), 'lime');
  assert.deepEqual(slotColour(3, { propColour: () => '#00ff00' }), [0, 1, 0]);
  assert.deepEqual(slotColour(3, { propColour: () => { throw new Error('x'); } }), colourOf(FALLBACK_SPECTRUM[3]));
});

test('media: picture axes are orthonormal for any normal', () => {
  for (const Z of [[0, 0, 1], [0, -1, 0], [0.3, -0.8, 0.2]]) {
    const a = axesFrom(Z, [0, 1, 0]);
    assert.ok(unit(a.X) && unit(a.Y) && unit(a.Z));
    assert.ok(near(V3.dot(a.X, a.Y), 0, 1e-9) && near(V3.dot(a.X, a.Z), 0, 1e-9));
  }
});

test('media: every placeholder program draws into a 2D context, and the Loom comes from its own tree', () => {
  const calls = { n: 0 };
  const grad = { addColorStop() { calls.n++; } };
  const stub = new Proxy({}, {
    get: (o, k) => (k in o ? o[k] : k === 'createLinearGradient' || k === 'createRadialGradient' ? () => grad : () => { calls.n++; }),
    set: (o, k, v) => { o[k] = v; return true; },
  });
  const make = (w, h) => ({ width: w, height: h, getContext: () => stub });
  const cols = [0, 1, 2, 3, 4, 5].map((i) => slotColour(i));
  assert.ok(loomReady(), 'arcademy/engine/loom/loomField.js imported');
  const q = loomParams('loom-a', cols);
  assert.equal(q.schema, 2);
  assert.equal(q.centerpiece.kind, 'none', 'no flashing centrepiece');
  for (let p = 0; p < PROGRAMS.length; p++) {
    const still = createPlaceholder(p, 64, cols, { make });
    assert.equal(still.static, true, 'drawn once by default');
    assert.equal(still.draw(0), true);
    assert.equal(still.draw(5), false);
    const ph = createPlaceholder(p, 64, cols, { make, animate: true });
    const before = calls.n;
    assert.equal(ph.draw(0), true, PROGRAMS[p].id + ' draws');
    assert.ok(calls.n > before);
    if (PROGRAMS[p].loom) assert.equal(ph.draw(5), false, 'a Loom frame is drawn once');
    else assert.equal(ph.draw(0.1), true, 'animate redraws');
  }
  assert.equal(programFor(7, 4), 3);
  assert.equal(programFor(-1, 6), 5);
  assert.equal(MEDIA_DEFAULTS.tiers.high.maxUploads, 3);
});
