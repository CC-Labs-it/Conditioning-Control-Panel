/* The Go Outside ending show (ui/unlocks/extras/outside-show.js): the maths that puts the meadow on the office monitor,
 * the beat plan, the skip, the reduced path, the fallbacks, and the silence under the show (ctx.audio.solo). The show
 * itself is driven in a browser (dev/play-ending.html?end=outside). */
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import {
  homography, applyMatrix, matrixCss, cropProjection, coverFit, holdFit, mapQuad, pullLayout, readTrack, fallbackTrack, frameAt, trackMs,
  outsideTimeline, skipTo, holdFrom, phaseAt, holdBeats, crackleGap, BEATS, FRAME,
} from '../ui/unlocks/extras/outside-show.js';
import { ENDINGS } from '../data/content/endings.js';
import { CLAUSES } from '../data/content/clauses.js';
import { BY_ID } from '../data/items.js';
import { CONFIG } from '../data/config.js';
import { EXIT_UNLOCKS } from '../ui/unlocks/extras/exits.js';
import { createEngine } from '../ui/engine.js';
import { createKit } from '../audio/kit.js';
import { createLife } from '../sim/save.js';
import * as E from '../sim/economy.js';

const near = (a, b, eps = 1e-6) => assert.ok(Math.abs(a - b) < eps, `${a} != ${b}`);
const nearPt = (p, q, eps = 1e-6) => { near(p[0], q[0], eps); near(p[1], q[1], eps); };
const VIEWS = [[1280, 800], [1920, 1080], [1366, 768], [390, 844], [844, 390], [360, 640]];

test('cropping the live camera preserves every sampled screen position before restoring the full pull frame', () => {
  for (const [vw, vh] of VIEWS) {
    const f = coverFit(vw, vh, FRAME), w = FRAME[0] * f.s, h = FRAME[1] * f.s;
    const crop = { x: (w - vw) / 2, y: (h - vh) / 2, width: vw, height: vh };
    for (const quad of [mapQuad(fallbackTrack().frames[0], f), [[-30, 8], [w + 12, -4], [w - 20, h], [-5, h + 18]]]) {
      const full = homography(w, h, quad), cropped = cropProjection(full, crop);
      for (const u of [0, .25, .5, .75, 1]) for (const v of [0, .25, .5, .75, 1]) {
        nearPt(applyMatrix(cropped, u * vw, v * vh), applyMatrix(full, crop.x + u * vw, crop.y + v * vh));
      }
    }
  }
});

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

test('the clip covers the view, and at frame 0 the canvas box is exactly the clip: no seam where the meadow meets it', () => {
  const tr = fallbackTrack();
  for (const [vw, vh] of VIEWS) {
    const f = coverFit(vw, vh, FRAME);
    assert.ok(FRAME[0] * f.s >= vw - 1e-6 && FRAME[1] * f.s >= vh - 1e-6, `${vw}x${vh} covered`);
    near(f.ox, (vw - FRAME[0] * f.s) / 2); near(f.oy, (vh - FRAME[1] * f.s) / 2);
    const l = pullLayout(tr, 0, vw, vh);
    const box = [FRAME[0] * f.s, FRAME[1] * f.s];
    const m = homography(box[0], box[1], l.quad);
    nearPt(applyMatrix(m, 0, 0), [f.ox, f.oy]);
    nearPt(applyMatrix(m, box[0], box[1]), [f.ox + box[0], f.oy + box[1]]);
    near(m[3], 0); near(m[7], 0); near(m[0], 1); near(m[5], 1);   // a plain move: the live canvas does not jump
    near(l.glass, 0);
  }
});

test('the hold shows the whole screen with a margin, edge to edge on a wide view and eased out on a tall phone', () => {
  const quad = [[505, 210], [775, 214], [772, 366], [503, 362]];
  for (const [vw, vh] of VIEWS) {
    const f = holdFit(vw, vh, quad, FRAME);
    const m = Math.max(12, Math.min(vw, vh) * 0.04);
    for (const [x, y] of mapQuad(quad, f)) assert.ok(x >= m - 1e-6 && x <= vw - m + 1e-6 && y >= m - 1e-6 && y <= vh - m + 1e-6, `${vw}x${vh} screen in view`);
    assert.ok(f.s <= coverFit(vw, vh).s + 1e-9 && f.s >= Math.min(vw / FRAME[0], vh / FRAME[1]) - 1e-9);
    if (vw / vh >= 1.5) near(f.s, coverFit(vw, vh).s);   // wide: no letterbox
  }
  const wide = [[420, 200], [860, 206], [856, 452], [424, 446]];
  const phone = holdFit(390, 844, wide);
  for (const [x, y] of mapQuad(wide, phone)) assert.ok(x >= 15 && x <= 375 && y > 0 && y < 844);
  assert.ok(phone.s < coverFit(390, 844).s, 'a tall phone eases out to keep the screen');
  assert.ok(phone.oy > 0, 'dark above and below');
});

test('the pull follows the track frame by frame and ends on the hold layout', () => {
  const tr = fallbackTrack();
  const n = tr.frames.length, vw = 390, vh = 844;
  const end = pullLayout(tr, n - 1, vw, vh);
  const hf = holdFit(vw, vh, tr.frames[n - 1]);
  near(end.fit.s, hf.s); near(end.fit.ox, hf.ox); near(end.fit.oy, hf.oy);
  end.quad.forEach((p, i) => nearPt(p, mapQuad(tr.frames[n - 1], hf)[i]));
  near(end.glass, 1);
  let w = Infinity;
  for (let i = 0; i < n; i++) { const q = pullLayout(tr, i, vw, vh).quad; const qw = q[1][0] - q[0][0]; assert.ok(qw <= w + 1e-6); w = qw; }
});

test('the track: checked, frame of the moment, length; a broken one falls back to a centred 16:9 screen', () => {
  const ok = { version: 1, fps: 30, size: [1280, 720], frames: [[[0, 0], [1280, 0], [1280, 720], [0, 720]], [[10, 10], [1270, 10], [1270, 710], [10, 710]]] };
  assert.equal(readTrack(ok).frames.length, 2);
  assert.equal(readTrack({ ...ok, version: 2 }), null);
  assert.equal(readTrack({ ...ok, frames: [[[0, 0]]] }), null);
  assert.equal(readTrack(null), null);
  const tr = fallbackTrack();
  assert.deepEqual(tr.frames[0], [[0, 0], [1280, 0], [1280, 720], [0, 720]]);
  const last = tr.frames[tr.frames.length - 1];
  near((last[0][0] + last[1][0]) / 2, 640); near((last[0][1] + last[3][1]) / 2, 360);
  near((last[1][0] - last[0][0]) / (last[3][1] - last[0][1]), 16 / 9);
  assert.deepEqual(tr.frames[tr.frames.length - 1], tr.frames[tr.frames.length - 15], 'the last half second holds');
  assert.equal(frameAt(tr, 0), 0);
  assert.equal(frameAt(tr, 1 / 30), 1);
  assert.equal(frameAt(tr, 0.99 / 30), 0);
  assert.equal(frameAt(tr, 99), tr.frames.length - 1);
  assert.equal(trackMs(tr), 5000);
});

/* Lane B's real files, once they exist: the contract (frame 0 = the frame corners within 0.001, one quad a frame). */
const TRACK_URL = new URL('../assets/outside/office-track.json', import.meta.url);
test('the office track, when present, keeps the contract', { skip: !existsSync(TRACK_URL) && 'office-track.json not delivered yet' }, () => {
  const tr = readTrack(JSON.parse(readFileSync(TRACK_URL, 'utf8')));
  assert.ok(tr, 'the track reads');
  assert.equal(tr.fps, 30);
  assert.deepEqual(tr.size, [1280, 720]);
  const [w, hh] = tr.size;
  tr.frames[0].forEach(([x, y], i) => {
    const [cx, cy] = [[0, 0], [w, 0], [w, hh], [0, hh]][i];
    assert.ok(Math.abs(x - cx) / w <= 0.001 && Math.abs(y - cy) / hh <= 0.001, `frame 0 corner ${i}`);
  });
  assert.ok(tr.frames.length >= 120 && tr.frames.length <= 210, 'about 5 s');
  const last = tr.frames[tr.frames.length - 1];
  for (const [vw, vh] of VIEWS) for (const [x, y] of mapQuad(last, holdFit(vw, vh, last))) assert.ok(x > 0 && x < vw && y > 0 && y < vh);
});

test('the beats run white, live (30 s of controls), settle, pull (the clip), hold, card, and stay under the show cap', () => {
  const tl = outsideTimeline({ credits: ENDINGS.outside.credits.length, pullMs: 5000 });
  assert.equal(tl.lockAt - tl.liveAt, 30000);
  assert.equal(tl.pullEnd - tl.pullAt, 5000);
  assert.ok(tl.liveAt < tl.lockAt && tl.lockAt < tl.pullAt && tl.pullAt < tl.holdAt && tl.holdAt < tl.closingAt && tl.closingAt < tl.finishAt);
  assert.equal(phaseAt(tl, 0), 'veil');
  assert.equal(phaseAt(tl, tl.liveAt + 1), 'live');
  assert.equal(phaseAt(tl, tl.lockAt + 1), 'settle');
  assert.equal(phaseAt(tl, tl.pullAt + 1), 'pull');
  assert.equal(phaseAt(tl, tl.holdAt + 1), 'hold');
  assert.equal(phaseAt(tl, tl.finishAt), 'done');
  assert.deepEqual(tl.lines.map((l) => l.kind), ['result', ...ENDINGS.outside.credits.map(() => 'credit'), 'closing']);
  /* The worst case: a clip that stalls to the cap, plus the wait for it to load. */
  const late = holdFrom(tl, tl.holdAt + BEATS.pullCapMs + BEATS.videoWaitMs);
  assert.ok(late.finishAt < CONFIG.ENDING_SHOW_MAX_MS, 'the engine never cuts it short');
  assert.equal(late.finishAt - late.holdAt, tl.finishAt - tl.holdAt, 'a late hold keeps the whole card');
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
  assert.ok(row.showMaxMs > CONFIG.ENDING_SHOW_MAX_MS, 'time to replay or export the poster');
  assert.equal(BY_ID.outside.cost, 1500000);
  assert.equal(ENDINGS.outside.closesTab, undefined);
  const words = [BY_ID.outside.what, ...CLAUSES.outside, ENDINGS.outside.result, ENDINGS.outside.closing, ...ENDINGS.outside.credits].join(' ');
  assert.doesNotMatch(words, /closes? (the|this) tab|close this/i);
});

test('nothing of Mojang\'s and no recorded birds: the show synthesises its sounds, names no sfx-mc file, and asks for silence first', () => {
  const src = readFileSync(new URL('../ui/unlocks/extras/outside-show.js', import.meta.url), 'utf8');
  assert.doesNotMatch(src, /sfx-mc|minecraft|mojang/i);
  assert.doesNotMatch(src, /new Audio\(|<audio/);
  assert.match(src, /createOscillator/);
  const body = src.slice(src.indexOf('export function outsideShow'));
  const solo = body.indexOf('ctx.audio.solo?.(true)');
  assert.ok(solo > 0 && solo < body.indexOf('ctx.audio.input'), 'silence before the first own sound');
  assert.match(src, /video\.muted = true/, 'the clip itself is silent');
});

/* ---- the silence under the show: a real kit on a stand-in AudioContext, the real engine ending flow ---- */

function param(v = 1) {
  return { value: v, cancelScheduledValues() {}, setValueAtTime(x) { this.value = x; }, setTargetAtTime(x) { this.value = x; },
    exponentialRampToValueAtTime(x) { this.value = x; }, linearRampToValueAtTime(x) { this.value = x; } };
}
class FakeAc {
  constructor() { this.state = 'running'; this.currentTime = 0; this.destination = {}; this.gains = []; FakeAc.last = this; }
  createGain() { const g = { gain: param(1), connect: (n) => n, disconnect() {} }; this.gains.push(g); return g; }
  createBufferSource() { return { buffer: null, loop: false, connect: (n) => n, disconnect() {}, start() {}, stop() {} }; }
  createMediaElementSource() { return { connect: (n) => n, disconnect() {} }; }
  decodeAudioData() { return Promise.resolve({ duration: 2 }); }
  suspend() { return Promise.resolve(); }
  resume() { return Promise.resolve(); }
}
class FakeMedia {
  constructor() { this.paused = true; this.volume = 1; this.muted = false; this.listeners = {}; }
  addEventListener(t, f) { (this.listeners[t] ||= []).push(f); }
  play() { this.paused = false; return Promise.resolve(); }
  pause() { this.paused = true; }
  removeAttribute() {}
  load() {}
}
const tick = () => new Promise((r) => setTimeout(r, 0));
/* The route gain a sound gets: the kit makes the sound's envelope gain, then its source gain on the route to master. */
async function routeOf(start) {
  const ac = FakeAc.last, n = ac.gains.length;
  const out = start();
  await tick(); await tick();
  return { out, gain: ac.gains[n + 1].gain };
}

test('during the show every other sound reads gain 0 (a running loop and stream, and ones started under it), the show\'s own play, and all come back after', async () => {
  globalThis.window = { AudioContext: FakeAc };
  const fetchWas = globalThis.fetch;
  globalThis.fetch = async () => ({ ok: true, arrayBuffer: async () => new ArrayBuffer(8) });
  try {
    const kit = createKit({ makeMedia: () => new FakeMedia() });
    kit.arm();
    /* Already playing when the run ends: a hum loop (a loop outside any scope survives the end). */
    const hum = await routeOf(() => kit.loop('assets/audio/hum.ogg', { source: 'hum' }));
    assert.equal(hum.gain.value, 1);

    /* The engine kills every stream at the end, so the radio stream that is still playing when the show begins is one
     * started after that (a stream the end did not reach): it is running, routed and at full level a moment before. */
    let showCtx = null, release, radio = null, radioBefore = null;
    const startRadio = () => {
      const ac = FakeAc.last, n = ac.gains.length;
      const out = kit.stream('assets/lofi/audio/dose.mp3', { bus: 'ambient', source: 'lofi_radio' });
      radio = { out, gain: ac.gains[n + 1].gain };
      radioBefore = { gain: radio.gain.value, muted: out.el.muted, playing: out.playing };
    };
    const run = E.createRun();
    const ui = { layer: () => ({}), bounds: () => ({ w: 800, h: 600 }), gentle: () => false, reduced: () => false, doc: {} };
    const engine = createEngine({ run, life: createLife(), audio: kit, ui, registry: {
      outside: { mount() { return {}; }, ending(c) { startRadio(); showCtx = c; c.audio.solo(true); return new Promise((r) => { release = r; }); } },
    } });
    E.grant(run, 'outside'); run.ended = { id: 'outside', at: run.clock };
    const ending = engine.end('outside');
    for (let i = 0; i < 20 && !showCtx; i++) await tick();
    assert.ok(showCtx, 'the show is running');
    assert.deepEqual(radioBefore, { gain: 1, muted: false, playing: true }, 'the radio was playing');

    /* The show's own sound passes; everything else is silent, also what starts under it. */
    const own = showCtx.audio.input({ bus: 'ambient', sourceLabel: 'Birds' });
    const ownGain = FakeAc.last.gains[FakeAc.last.gains.length - 1].gain;
    const bed = await routeOf(() => showCtx.audio.stream('assets/lofi/audio/breathe-in.mp3', { bus: 'ambient', source: 'outside-bed', sourceLabel: 'Music' }));
    assert.ok(own);
    assert.equal(ownGain.value, 1, 'the birds play');
    assert.equal(bed.gain.value, 1, 'the bed plays');
    assert.equal(bed.out.el.muted, false);
    assert.equal(hum.gain.value, 0, 'the running hum loop reads gain 0');
    assert.equal(radio.gain.value, 0, 'the running radio stream reads gain 0');
    assert.equal(radio.out.el.muted, true);
    assert.equal(radio.out.playing, true, 'silenced, not paused: it keeps its place');
    const podcast = await routeOf(() => kit.stream('assets/podcast/ep.mp3', { bus: 'podcast', source: 'podcast' }));
    assert.equal(podcast.gain.value, 0, 'a podcast started under the show is silent');
    assert.equal(podcast.out.el.muted, true);
    const moans = await routeOf(() => kit.loop('assets/audio/moan.ogg', { source: 'moans' }));
    assert.equal(moans.gain.value, 0, 'a loop started under the show is silent');
    assert.equal(kit.heard('voices'), 0);

    release();
    await ending;
    assert.equal(hum.gain.value, 1, 'the hum is back after the show');
    assert.equal(radio.gain.value, 1);
    assert.equal(radio.out.el.muted, true, 'the end card is quiet (setQuiet), the source itself is back');
    assert.equal(kit.heard('lofi_radio'), 1);
    assert.equal(moans.gain.value, 1);
  } finally {
    globalThis.fetch = fetchWas;
  }
});
