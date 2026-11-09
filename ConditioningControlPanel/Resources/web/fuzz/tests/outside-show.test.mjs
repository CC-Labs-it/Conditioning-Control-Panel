/* The Go Outside ending show (ui/unlocks/extras/outside-show.js): the maths that puts the meadow in the monitor, the beat
 * plan, the skip, the reduced path and the fallbacks. The show itself is driven in a browser (dev/play-ending.html?end=outside). */
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  homography, applyMatrix, matrixCss, roomFit, pullFrame, startZoom, outsideTimeline, skipTo, phaseAt, holdBeats,
  nicheFor, roomChoice, crackleGap, BEATS, ROOM_SIZE, NICHES,
} from '../ui/unlocks/extras/outside-show.js';
import { ENDINGS } from '../data/content/endings.js';
import { CLAUSES } from '../data/content/clauses.js';
import { BY_ID } from '../data/items.js';
import { CONFIG } from '../data/config.js';
import { EXIT_UNLOCKS } from '../ui/unlocks/extras/exits.js';

const near = (a, b, eps = 1e-6) => assert.ok(Math.abs(a - b) < eps, `${a} != ${b}`);
const nearPt = (p, q, eps = 1e-6) => { near(p[0], q[0], eps); near(p[1], q[1], eps); };

test('the homography sends the box corners onto a perspective quad, and the middle inside it', () => {
  const quad = [[412, 230], [790, 248], [772, 470], [425, 455]];
  const m = homography(1280, 720, quad);
  assert.equal(m.length, 16);
  nearPt(applyMatrix(m, 0, 0), quad[0]);
  nearPt(applyMatrix(m, 1280, 0), quad[1]);
  nearPt(applyMatrix(m, 1280, 720), quad[2]);
  nearPt(applyMatrix(m, 0, 720), quad[3]);
  const [x, y] = applyMatrix(m, 640, 360);
  assert.ok(x > 425 && x < 790 && y > 230 && y < 470, 'centre lands inside');
  assert.match(matrixCss(m), /^matrix3d\((-?[\d.e-]+,){15}-?[\d.e-]+\)$/);
});

test('a rectangle quad is a plain scale and move', () => {
  const m = homography(200, 100, [[10, 20], [110, 20], [110, 70], [10, 70]]);
  near(m[3], 0); near(m[7], 0);
  nearPt(applyMatrix(m, 100, 50), [60, 45]);
});

test('the room covers the view, centred on its screen, and never shows past its own edge on a wide screen', () => {
  const quad = [[400, 220], [780, 230], [770, 470], [410, 460]];
  for (const [vw, vh] of [[1920, 1080], [1280, 720], [1366, 768]]) {
    const f = roomFit(vw, vh, quad);
    assert.ok(ROOM_SIZE[0] * f.s >= vw - 1e-6 && ROOM_SIZE[1] * f.s >= vh - 1e-6, `${vw}x${vh} covered`);
    assert.ok(f.ox <= 1e-6 && f.oy <= 1e-6 && f.ox + ROOM_SIZE[0] * f.s >= vw - 1e-6 && f.oy + ROOM_SIZE[1] * f.s >= vh - 1e-6);
    for (const [x, y] of f.quad) assert.ok(x >= 0 && x <= vw && y >= 0 && y <= vh, 'screen on the view');
  }
  /* A tall phone keeps more of the room (dark above and below) and still the whole screen. */
  const p = roomFit(390, 844, quad);
  for (const [x, y] of p.quad) assert.ok(x >= 0 && x <= 390 && y >= 0 && y <= 844, 'phone screen on the view');
  assert.ok(ROOM_SIZE[0] * p.s >= 390);
});

test('the pull back starts with the meadow filling the view and ends exactly in the hole', () => {
  const vw = 1280, vh = 720, quad = roomFit(vw, vh, [[400, 220], [780, 230], [770, 470], [410, 460]]).quad;
  const a = pullFrame(0, vw, vh, quad);
  assert.deepEqual(a.corners, [[0, 0], [vw, 0], [vw, vh], [0, vh]]);
  near(a.z, startZoom(vw, vh, quad)); assert.ok(a.z > 1);
  near(a.room, 0);
  const b = pullFrame(1, vw, vh, quad);
  near(b.z, 1); near(b.room, 1);
  b.corners.forEach((c, i) => nearPt(c, quad[i]));
  /* The zoom never overshoots and only falls. */
  let z = Infinity;
  for (let p = 0; p <= 1.0001; p += 0.05) { const f = pullFrame(p, vw, vh, quad); assert.ok(f.z <= z + 1e-9 && f.z >= 1 - 1e-9); z = f.z; }
});

test('the beats run white, live (6.5 s of controls), settle, pull (4 s), hold, card, and stay under the show cap', () => {
  const tl = outsideTimeline({ credits: ENDINGS.outside.credits.length });
  assert.equal(tl.lockAt - tl.liveAt, 6500);
  assert.equal(tl.pullEnd - tl.pullAt, 4000);
  assert.ok(tl.liveAt < tl.lockAt && tl.lockAt < tl.pullAt && tl.pullAt < tl.holdAt && tl.holdAt < tl.closingAt && tl.closingAt < tl.finishAt);
  assert.equal(phaseAt(tl, 0), 'veil');
  assert.equal(phaseAt(tl, tl.liveAt + 1), 'live');
  assert.equal(phaseAt(tl, tl.lockAt + 1), 'settle');
  assert.equal(phaseAt(tl, tl.pullAt + 1), 'pull');
  assert.equal(phaseAt(tl, tl.holdAt + 1), 'hold');
  assert.equal(phaseAt(tl, tl.finishAt), 'done');
  assert.equal(tl.lines.length, ENDINGS.outside.credits.length + 2);
  assert.deepEqual(tl.lines.map((l) => l.kind), ['result', ...ENDINGS.outside.credits.map(() => 'credit'), 'closing']);
  assert.ok(tl.finishAt < CONFIG.ENDING_SHOW_MAX_MS, 'the engine never cuts it short');
});

test('a skip lands on the hold, never past it; the card still plays in full', () => {
  const tl = outsideTimeline({ credits: 5 });
  for (const t of [100, tl.liveAt + 2000, tl.pullAt + 500]) {
    const s = skipTo(tl, t);
    assert.equal(s.holdAt, t);
    assert.equal(phaseAt(s, t), 'hold');
    assert.equal(s.lines.length, tl.lines.length);
    assert.equal(s.finishAt - s.holdAt, tl.finishAt - tl.holdAt, 'the hold is not shortened');
  }
  assert.equal(skipTo(tl, tl.holdAt + 10), tl, 'a skip at the hold changes nothing');
});

test('reduced motion and Gentle: no live part, a cross-fade instead of the pull', () => {
  const r = outsideTimeline({ reduced: true, credits: 5 });
  assert.equal(r.reduced, true);
  assert.equal(r.liveAt, null); assert.equal(r.lockAt, null);
  assert.equal(r.pullEnd - r.pullAt, BEATS.fadeMs);
  assert.equal(phaseAt(r, BEATS.veilMs + 10), 'veil');
  assert.equal(phaseAt(r, r.pullAt + 1), 'pull');
  assert.equal(skipTo(r, 50).holdAt, 50);
});

test('the drawing follows the theme, Locked draws as chastity, and a missing piece falls back', () => {
  assert.equal(nicheFor('locked'), 'chastity');
  assert.equal(nicheFor('bimbo'), 'bimbo');
  assert.equal(nicheFor(null), 'trance');
  assert.equal(nicheFor('nonsense'), 'trance');
  for (const n of NICHES) assert.equal(nicheFor(n), n);
  const q = [[1, 2], [3, 2], [3, 4], [1, 4]];
  const screens = { version: 1, size: ROOM_SIZE, screens: { trance: { quad: q }, goon: { quad: [[1, 2]] } } };
  assert.deepEqual(roomChoice(screens, 'trance'), { niche: 'trance', quad: q });
  assert.deepEqual(roomChoice(screens, 'sissy'), { niche: 'trance', quad: q }, 'no drawing -> trance');
  assert.equal(roomChoice(screens, 'goon').niche, 'trance', 'a broken quad -> trance');
  assert.equal(roomChoice({ screens: {} }, 'sissy'), null, 'nothing usable -> the plain room');
  assert.equal(roomChoice(null, 'sissy'), null);
  assert.equal(roomChoice({}, 'sissy'), null);
});

test('crackles come every 3 to 7 seconds', () => {
  assert.equal(crackleGap(0), 3000);
  assert.equal(crackleGap(1), 7000);
  for (let i = 0; i < 50; i++) { const g = crackleGap(); assert.ok(g >= 3000 && g <= 7000); }
});

test('the hold card order is fixed by holdBeats', () => {
  const b = holdBeats(1000, 2);
  assert.deepEqual(b.lines.map((l) => l.at), [1900, 2800, 3700, 5000]);
  assert.equal(b.finishAt, 5000 + BEATS.tailMs);
});

test('the row: a white fade, the new show, 1.5M, and the tab stays open', () => {
  const row = EXIT_UNLOCKS.outside;
  assert.equal(row.fadeColour, '#fff');
  assert.equal(typeof row.ending, 'function');
  assert.equal(BY_ID.outside.cost, 1500000);
  assert.equal(ENDINGS.outside.closesTab, undefined);
  const words = [BY_ID.outside.what, ...CLAUSES.outside, ENDINGS.outside.result, ENDINGS.outside.closing, ...ENDINGS.outside.credits].join(' ');
  assert.doesNotMatch(words, /closes? (the|this) tab|close this/i);
});

test('nothing of Mojang\'s and no recorded birds: the show synthesises its sounds and names no sfx-mc file', () => {
  const src = readFileSync(new URL('../ui/unlocks/extras/outside-show.js', import.meta.url), 'utf8');
  assert.doesNotMatch(src, /sfx-mc|minecraft|mojang/i);
  assert.doesNotMatch(src, /new Audio\(|<audio/);
  assert.match(src, /createOscillator/);
});
