/* tests/logbook-trial.test.mjs - Time Trial (Logbook TRIAL lane): ghost record, encode / decode, sample,
 * best-only storage, the medal table and the mode pick. */
import test from 'node:test';
import assert from 'node:assert/strict';
import { S, createState } from '../sim/state.js';
import { createGhostRecorder, encodeGhost, decodeGhost, sample, toState, lapGap, formatGap, loadGhost, saveBestGhost,
  ghostKey, GHOST_HZ, GHOST_MAX_CHARS } from '../meta/ghost.js';
import { MEDALS, medalFor } from '../meta/medals.js';
import { parseMode, loadMode, saveMode, resolveMode, MODE_KEY } from '../meta/mode.js';
import { makeRace, DT } from './sim-racers-harness.mjs';

const memStore = () => { const m = new Map(); return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), map: m }; };
const throwing = { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); } };

/** A kart on a 200 m circle at 30 m/s, yawing as it goes. */
function circleGhost(seconds, track = 'threshold') {
  const rec = createGhostRecorder({ track, laps: 3 }), st = createState(), R = 200, w = 30 / R;
  for (let t = 0; t <= seconds + 1e-9; t += DT) {
    const a = w * t;
    st[S.posX] = R * Math.sin(a); st[S.posY] = 3 + Math.sin(t); st[S.posZ] = R - R * Math.cos(a);
    st[S.quatX] = 0; st[S.quatY] = Math.sin(-a / 2); st[S.quatZ] = 0; st[S.quatW] = Math.cos(-a / 2);
    st[S.s] = (30 * t) % 5000; st[S.speedPct] = 0.95;
    rec.tick(t, st);
    if (t > 0 && Math.abs(t % 100) < DT) rec.lap(t);
  }
  return rec.finish(seconds);
}

test('ghost: records at 10 Hz on the race clock', () => {
  const g = circleGhost(60);
  assert.equal(g.hz, GHOST_HZ);
  assert.ok(Math.abs(g.n - 601) <= 1, 'one sample per tenth: ' + g.n);
  assert.equal(createGhostRecorder({ track: 'keep', laps: 2 }).finish(10), null, 'nothing recorded, no ghost');
});

test('ghost: encode / decode round trip is exact', () => {
  const g = circleGhost(120), str = encodeGhost(g), back = decodeGhost(str);
  assert.ok(back);
  assert.deepEqual([back.track, back.laps, back.n, back.hz], [g.track, g.laps, g.n, g.hz]);
  assert.equal(back.timeS, g.timeS);
  assert.deepEqual([...back.rows], [...g.rows]);
  assert.equal(back.splits.length, g.splits.length);
  assert.equal(encodeGhost(back), str, 'a decoded ghost re-encodes to the same string');
});

test('ghost: malformed strings decode to null', () => {
  const str = encodeGhost(circleGhost(10)), o = JSON.parse(str);
  for (const bad of ['', 'nope', '{}', JSON.stringify({ ...o, v: 2 }), JSON.stringify({ ...o, n: o.n + 5 }),
    JSON.stringify({ ...o, d: o.d.slice(0, 20) }), JSON.stringify({ ...o, track: '../x' }), JSON.stringify({ ...o, timeS: -1 })]) {
    assert.equal(decodeGhost(bad), null, bad.slice(0, 40));
  }
});

test('ghost: a whole solo Threshold trial fits well under the cap', () => {
  const r = makeRace({ count: 1, seed: 3 }), P = r.racers.player, rec = createGhostRecorder({ track: 'threshold', laps: r.track.laps });
  let cum = 0, seen = 0;
  r.run(900, () => {
    if (r.race.phase === 'racing') rec.tick(r.race.timeS, P.st);
    while (seen < P.lapTimes.length) { cum += P.lapTimes[seen++]; rec.lap(cum); }
    return P.finished;
  });
  assert.ok(P.finished, 'the AI brought the lone kart home');
  const g = rec.finish(P.finishS), str = encodeGhost(g);
  assert.equal(g.splits.length, r.track.laps);
  assert.ok(Math.abs(g.splits[g.splits.length - 1] - P.finishS) < 0.05);
  assert.ok(str.length < GHOST_MAX_CHARS / 2, 'size ' + str.length);
  // the decoded ghost lands where the kart was (cm quantised, 10 Hz)
  const back = decodeGhost(str), p = sample(back, 0);
  assert.ok(Math.abs(p.x - back.rows[0] / 100) < 1e-9);
});

test('ghost: sample interpolates position, quat and clamps the ends', () => {
  const g = circleGhost(30), a = g.rows, CH = 9;
  const mid = sample(g, 0.55);                     // halfway between samples 5 and 6
  assert.ok(Math.abs(mid.x - (a[5 * CH] + a[6 * CH]) / 200) < 1e-9);
  assert.ok(Math.abs(Math.hypot(mid.qx, mid.qy, mid.qz, mid.qw) - 1) < 1e-9, 'unit quat');
  const start = sample(g, -3), end = sample(g, 999);
  assert.ok(Math.abs(start.x - a[0] / 100) < 1e-9);
  assert.ok(Math.abs(end.z - a[(g.n - 1) * CH + 2] / 100) < 1e-9);
  assert.equal(end.done, true); assert.equal(mid.done, false);
  const st = toState(mid, createState());
  assert.ok(Math.abs(st[S.posX] - mid.x) < 1e-4 && Math.abs(st[S.quatW] - mid.qw) < 1e-6);
});

test('ghost: the quat stays on one hemisphere so a lerp never flips', () => {
  const rec = createGhostRecorder({ track: 'keep', laps: 2 }), st = createState();
  for (let i = 0; i < 4; i++) { const sg = i % 2 ? -1 : 1; st[S.quatY] = 0.6 * sg; st[S.quatW] = 0.8 * sg; rec.tick(i / 10, st); }
  const g = rec.finish(0.4), p = sample(g, 0.15);
  assert.ok(Math.abs(Math.abs(p.qw) - 0.8) < 1e-3, 'no collapse through zero: ' + p.qw);
});

test('ghost: lap gaps and their copy', () => {
  const g = { splits: [100, 200, 300] };
  assert.ok(Math.abs(lapGap(g, 1, 100.42) - 0.42) < 1e-9);
  assert.equal(lapGap(g, 4, 400), null);
  assert.equal(lapGap(null, 1, 100), null);
  assert.equal(formatGap(0.42), '+0.42'); assert.equal(formatGap(-1.071), '-1.07'); assert.equal(formatGap(0), '+0.00');
});

test('ghost storage: keeps only the best finished trial per course', () => {
  const store = memStore(), slow = circleGhost(40), fast = circleGhost(30);
  assert.equal(loadGhost('threshold', store), null);
  assert.equal(saveBestGhost(slow, store), true);
  assert.equal(saveBestGhost(fast, store), true, 'faster replaces');
  assert.equal(saveBestGhost(slow, store), false, 'slower never replaces');
  assert.equal(loadGhost('threshold', store).timeS, 30);
  assert.ok(store.map.has(ghostKey('threshold')) && ghostKey('threshold') === 'glassroad.ghost.v1.threshold');
  const otherLaps = { ...circleGhost(50), laps: 5 };
  assert.equal(saveBestGhost(otherLaps, store), true, 'a different lap count is a different trial');
  assert.equal(saveBestGhost(fast, throwing), false);
  assert.equal(loadGhost('threshold', throwing), null);
  assert.equal(saveBestGhost(null, store), false);
  assert.equal(saveBestGhost(fast, null), false);
});

test('medals: every course has ordered times, and the table awards them', () => {
  for (const [id, m] of Object.entries(MEDALS)) {
    assert.ok(m.glass > 0 && m.glass < m.gold && m.gold < m.silver && m.silver < m.bronze, id);
  }
  assert.equal(medalFor('threshold', 570), 'glass');
  assert.equal(medalFor('threshold', 580), 'gold');
  assert.equal(medalFor('threshold', 600), 'silver');
  assert.equal(medalFor('threshold', 700), 'bronze');
  assert.equal(medalFor('threshold', 708), null);
  assert.equal(medalFor('goonworks', 402), 'gold');
  assert.equal(medalFor('keep', 0), null);
});

test('mode: url, saved pick, online is always a race', () => {
  assert.equal(parseMode('?mode=trial'), 'trial');
  assert.equal(parseMode('?track=keep&mode=race'), 'race');
  assert.equal(parseMode('?mode=drag'), null);
  assert.equal(parseMode(''), null);
  const store = memStore();
  assert.equal(loadMode(store), 'race');
  assert.equal(saveMode('trial', store), true);
  assert.equal(store.map.get(MODE_KEY), 'trial');
  assert.equal(loadMode(store), 'trial');
  assert.equal(saveMode('drag', store), false);
  assert.equal(saveMode('trial', throwing), false);
  assert.equal(loadMode(throwing), 'race');
  assert.equal(resolveMode({ search: '', storage: store }), 'trial');
  assert.equal(resolveMode({ search: '?mode=race', storage: store }), 'race');
  assert.equal(resolveMode({ search: '?mode=trial', storage: store, online: true }), 'race');
});
