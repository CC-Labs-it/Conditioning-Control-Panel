// tests/render-juice3.test.mjs - polish 3 "spectacle" JUICE lane: the pure helpers behind bigger effects,
// the camera kick layer, head marks and the reaction numbers.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createState, S, BOOST, RESPAWN } from '../sim/state.js';
import {
  JUICE_LOOK, juiceLook, JUICE_HUES, spreadHues, hueOf, toHex, spectrumHexes, juiceHues, burstBoost, gentleGrow, tierShare, FX2_LOOK,
} from '../render/atlas.js';
import { LOOK } from '../render/look.js';
import { createKick, kickEdges, boostPunch, KICK_DEFAULTS, kickNumbers, createCamRig } from '../render/cameraMath.js';
import { REACT_DEFAULTS as R, createReact, reactTo, stepReact, headMark, stepMark } from '../render/kartPose.js';
import { MARK_IDS, markCell, drawMark, FACE_LOOK, MARK_LOOK } from '../render/faces.js';
import { ITEM_LOOK } from '../render/items/look.js';

const DT = 1 / 60;
const feel = (over = {}) => {
  const f = createFeel(JSON.parse(readFileSync(new URL('../feel.json', import.meta.url))));
  for (const [k, v] of Object.entries(over)) f.set(k, v);
  return f;
};
const moving = () => { const st = createState(); st[S.speedPct] = 0.8; st[S.grounded] = 1; return st; };

test('juice3: every feedback effect grows 2 to 4 times, phone counts stay about half', () => {
  const J = JUICE_LOOK;
  for (const k of ['sparkSizeMul', 'flameSizeMul', 'wispSizeMul', 'dustSizeMul', 'hitSizeMul', 'ringSizeMul']) {
    assert.ok(J[k] >= 2 && J[k] <= 4, k + ' ' + J[k]);
  }
  for (const [fam, b] of Object.entries(J.burst)) assert.ok(b.size >= 2 && b.size <= 4, fam);
  assert.ok(J.lineWidthM >= 0.04 * 2 && J.lineAlpha >= LOOK.lineAlpha * 1.5, 'speed lines wider and brighter');
  assert.ok(J.lineLenMul >= 1.5 && J.lineCountMul > 1);
  assert.ok(J.phoneMul <= 0.5 && J.midMul < 1 && J.gentleMul <= 0.5);
  assert.equal(tierShare('phone'), J.phoneMul); assert.equal(tierShare('mid'), J.midMul); assert.equal(tierShare('high'), 1);
  assert.ok(FX2_LOOK.phoneExtraMul <= 0.5);
  assert.ok(ITEM_LOOK.confetti.high >= 30 && ITEM_LOOK.confetti.phone <= ITEM_LOOK.confetti.high / 2);
  assert.ok(ITEM_LOOK.impactRingM[1] >= 10 && ITEM_LOOK.confettiSizeM >= 0.44);
});

test('juice3: Gentle keeps half of a growth and never shrinks below 1', () => {
  assert.equal(gentleGrow(2.6, false), 2.6);
  assert.ok(Math.abs(gentleGrow(2.6, true) - 1.8) < 1e-9);
  assert.equal(gentleGrow(0.8, true), 0.8);
  assert.equal(juiceLook({ juice: { sparkSizeMul: 3 } }).sparkSizeMul, 3);
  assert.equal(juiceLook({}).sparkSizeMul, JUICE_LOOK.sparkSizeMul);
});

test('juice3: spectrum colours read from any shape, the fallback keeps pink, magenta and violet near 40 percent', () => {
  assert.equal(toHex('#FF00aa'), '#ff00aa');
  assert.equal(toHex([1, 0, 0]), '#ff0000');
  assert.equal(toHex([0, 255, 0]), '#00ff00');
  assert.equal(toHex({ hex: '#123456' }), '#123456');
  assert.match(toHex(0.5), /^#[0-9a-f]{6}$/);
  assert.equal(toHex('nope'), null);
  assert.deepEqual(spectrumHexes({ dawn: ['#ff0000', '#00ff00'], deep: { a: [0, 0, 1] }, x: ['#ff0000'] }), ['#ff0000', '#00ff00', '#0000ff']);
  assert.deepEqual(spectrumHexes(null), []);
  assert.ok(juiceHues().length >= 6);
  const warm = JUICE_HUES.filter((h) => ['#ff4fb0', '#ff2bd6', '#9b4dff'].includes(h)).length / JUICE_HUES.length;
  assert.ok(warm > 0.3 && warm < 0.55, 'dominant share ' + warm);
  assert.ok(new Set(JUICE_HUES).size >= 9, 'cyan, teal, blue, lime, gold, orange and red appear too');
});

test('juice3: bursts grow by family, item hits and head sprays only by their own numbers', () => {
  const bump = burstBoost({ colours: LOOK.bumpColours });
  assert.equal(bump.family, 'bump'); assert.ok(bump.size >= 2 && bump.count > 1 && bump.spectrum);
  assert.equal(burstBoost({ colours: LOOK.boxColours }).family, 'box');
  assert.equal(burstBoost({ colours: LOOK.popColours }).family, 'pop');
  const hit = burstBoost({ family: 'freeze', colours: LOOK.popColours });
  assert.equal(hit.family, null); assert.equal(hit.size, 1);
  assert.equal(burstBoost({ family: 'golden', colours: ['#fff'] }).size, 1);
  assert.equal(burstBoost({ boost: 'bump', colours: [] }).family, 'bump');
});

test('kick: one lobe that peaks near its amplitude and settles; Gentle halves it and never swings back', () => {
  const run = (gentle) => {
    const k = createKick();
    k.hit('land', 3, 0, gentle);
    let lo = 0, hi = 0;
    for (let t = 0; t < 2; t += DT) { const o = k.update(DT, gentle); lo = Math.min(lo, o.y); hi = Math.max(hi, o.y); }
    return { lo, hi, end: k.out.y };
  };
  const a = run(false), g = run(true), A = KICK_DEFAULTS.landM[2];
  assert.ok(a.lo < -A * 0.8 && a.lo > -A * 1.6, 'peak ' + a.lo);
  assert.ok(a.hi < A * 0.4, 'a small overshoot at most');
  assert.ok(Math.abs(a.end) < 0.01, 'settled');
  assert.ok(g.lo > a.lo * 0.75, 'Gentle about half');
  assert.ok(g.hi <= 1e-6, 'no swing back under Gentle');
});

test('kick: bumps push away from the hit side, small brushes do nothing, boost punches the fov', () => {
  const k = createKick();
  assert.equal(k.hit('bump', 0.5, 1), false);
  k.hit('bump', 9, 1);
  let x = 0, f = 0;
  for (let t = 0; t < 0.3; t += DT) { const o = k.update(DT); x = Math.min(x, o.x); f = Math.max(f, o.fov); }
  assert.ok(x < -0.2, 'a hit from the right throws the view left ' + x);
  assert.ok(f > 1);
  const b = createKick(); b.hit('boost', boostPunch(BOOST.DRIFT3));
  let fb = 0; for (let t = 0; t < 0.5; t += DT) fb = Math.max(fb, b.update(DT).fov);
  assert.ok(fb > 3 && fb < KICK_DEFAULTS.maxFovDeg, 'slight punch ' + fb);
  assert.equal(boostPunch(BOOST.NONE), 0);
  assert.ok(boostPunch(BOOST.LAUNCH) >= boostPunch(BOOST.DRIFT1));
  assert.equal(kickNumbers({ camKick: { hz: 5 } }).hz, 5);
});

test('kick: state edges find landings by fall, boost starts and unexplained velocity jumps', () => {
  const w = {}, st = moving();
  assert.deepEqual(kickEdges(w, st, DT), []);
  st[S.airborne] = 1; kickEdges(w, st, DT);
  st[S.velY] = -9; kickEdges(w, st, DT);
  st[S.airborne] = 0; st[S.velY] = 0;
  assert.deepEqual(kickEdges(w, st, DT), [['land', 2, 0]]);
  st[S.boostKind] = BOOST.PAD;
  assert.deepEqual(kickEdges(w, st, DT), [['boost', boostPunch(BOOST.PAD), 0]]);
  assert.deepEqual(kickEdges(w, st, DT), [], 'a held boost punches once');
  st[S.velX] = 6;                                   // a shove to the right in one frame: hit from the left
  const e = kickEdges(w, st, DT);
  assert.equal(e.length, 1); assert.equal(e[0][0], 'bump'); assert.equal(e[0][2], -1);
  st[S.velX] = 12; assert.deepEqual(kickEdges(w, st, DT, KICK_DEFAULTS, false), [], 'the bus stands the detector down');
  const r = createState(); r[S.respawnPhase] = RESPAWN.PULL; r[S.airborne] = 1; const wr = {};
  kickEdges(wr, r, DT); r[S.airborne] = 0; assert.deepEqual(kickEdges(wr, r, DT), [], 'no landing kick from a rescue');
});

test('kick: boost springs never change the camera FOV and snap clears contact offsets', () => {
  const f = feel(), rig = createCamRig(f), k = moving();
  rig.snap(k, 16 / 9); rig.update(k, null, DT, 16 / 9);
  const base = rig.vFov;
  k[S.boostKind] = BOOST.DRIFT3;
  let top = 0; for (let t = 0; t < 0.4; t += DT) { rig.update(k, null, DT, 16 / 9); top = Math.max(top, rig.vFov); }
  assert.equal(top, base, 'a boost must not pump zoom or anchor pitch');
  for (let t = 0; t < 3; t += DT) rig.update(k, null, DT, 16 / 9);
  assert.ok(Math.abs(rig.vFov - base) < 0.05);
  rig.hit('bump', 9, 1); rig.update(k, null, DT, 16 / 9); rig.snap(k, 16 / 9);
  assert.equal(rig.kick.out.x, 0);
});

test('react3: a hard bump spins the saucer a full turn, marks the head, Gentle halves the squash', () => {
  const r = createReact(0), st = moving();
  reactTo(r, 'bump', { dv: R.bumpSpinMps + 1, lx: 1, lz: 0 }, R);
  let most = 0; for (let t = 0; t < R.bumpSpinS * 0.95; t += DT) most = Math.max(most, Math.abs(stepReact(r, st, DT, R).yaw));
  assert.ok(most > 5.5, 'full turn ' + most);
  assert.equal(r.out.mark, 'swirl');
  const small = createReact(0); reactTo(small, 'bump', { dv: 3, lx: 1 }, R);
  for (let t = 0; t < 0.2; t += DT) stepReact(small, st, DT, R);
  assert.equal(small.out.mark, 'sweat'); assert.ok(small.out.markK > 0.9);
  for (let t = 0; t < R.markS + 0.6; t += DT) stepReact(small, st, DT, R);
  assert.equal(small.out.markK, 0, 'the mark goes out');
  const land = (gentle) => { const x = createReact(0); reactTo(x, 'land', { fall: 12 }, R, gentle); return stepReact(x, st, DT, R, { gentle }).sq; };
  assert.ok(Math.abs(land(true) - land(false) * R.gentleSquash) < 1e-9);
  assert.ok(land(false) > 0.3, 'a big landing squash reads');
  const w = createReact(0); reactTo(w, 'wall', { dv: 5, side: 1 }, R);
  assert.equal(headMark(w, 'x-eyes'), 'anger');
});

test('react3: mood marks follow the face; a new mark waits for the old to go out', () => {
  const r = createReact(0);
  assert.equal(headMark(r, 'sleepy'), 'zz'); assert.equal(headMark(r, 'frozen'), 'ice');
  assert.equal(headMark(r, 'heart'), 'heart'); assert.equal(headMark(r, 'neutral'), null);
  stepMark(r, 'zz', DT, R); for (let i = 0; i < 30; i++) stepMark(r, 'zz', DT, R);
  assert.ok(r.markK > 0.9 && r.markShown === 'zz');
  stepMark(r, 'heart', DT, R);
  assert.equal(r.markShown, 'zz', 'fading the old one first');
  for (let i = 0; i < 40; i++) stepMark(r, 'heart', DT, R);
  assert.equal(r.markShown, 'heart');
});

test('marks: every id has a cell and draws inked and coloured; faces draw bigger and bolder', () => {
  assert.equal(MARK_IDS.length, 8);
  assert.deepEqual(markCell('swirl'), { col: 0, row: 0 });
  assert.deepEqual(markCell('stars'), { col: 3, row: 1 });
  assert.equal(markCell('nope'), null);
  const calls = [];
  const g = new Proxy({}, { get: (o, k) => (k in o ? o[k] : (...a) => calls.push(k)), set: (o, k, v) => { o[k] = v; return true; } });
  for (const id of MARK_IDS) {
    calls.length = 0;
    assert.equal(drawMark(g, id), id);
    assert.ok(calls.filter((c) => c === 'fill' || c === 'stroke').length >= 2, id + ' draws twice');
  }
  assert.equal(drawMark(g, 'nope'), null);
  assert.ok(FACE_LOOK.zoom > 1 && FACE_LOOK.zoom <= 1.25 && FACE_LOOK.weight > 1.7 && FACE_LOOK.gain > 1);
  assert.ok(MARK_LOOK.sizeM >= 0.6 && MARK_LOOK.upM > R.sprayUpM);
  assert.ok(R.starSizeM >= 0.17 * 2 && R.sweatSizeM >= 0.09 * 2, 'head sprays two times bigger at least');
});

test('juice3: the mote wheel spreads round the hue circle, not two neighbours', () => {
  const list = ['#ff1a8c', '#00e0a8', '#ff1a80', '#00d49c', '#2a5cff', '#ffcc0a', '#8cff1a', '#ff6a0a', '#8f1fff', '#00dcff'];
  const w = spreadHues(list, 6), hs = w.map(hueOf);
  assert.equal(w.length, 6);
  assert.ok(new Set(w).size === 6);
  assert.ok(Math.max(...hs) - Math.min(...hs) > 200, 'covers the wheel');
  assert.ok(Math.abs(hueOf('#00ff00') - 120) < 1e-9 && hueOf('#808080') === 0);
});
