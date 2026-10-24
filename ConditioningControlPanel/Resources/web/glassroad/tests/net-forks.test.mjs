import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { createInterp } from '../net/interp.js';
import { createUplink } from '../net/snapshot.js';
import { BIN, createKart, encodeFrame, decodeFrame } from '../net/protocol.js';
import { S, createState } from '../sim/state.js';
import { pathFrame, racerFork } from '../net/trackPath.js';
import { updateStoneMotion } from '../track/stoneMotion.js';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8')));

test('all Boudoir branch roads survive upload, relay, interpolation and keeper hand-off', () => {
  assert.equal(track.forks.length, 3);
  for (const [i, fork] of track.forks.entries()) {
    const s = (fork.fromS + fork.toS) / 2, f = fork.frameAt(fork.localS(s));
    const state = createState(); state[S.seg] = f.seg; state[S.s] = s;
    assert.equal(racerFork(track, state), i + 1);
    const row = createUplink({ track }).build([{ seat: 2, r: { st: state } }], 60, 1000)[0];
    const relayed = decodeFrame(encodeFrame(BIN.BATCH, 1000, [row])).karts[0];
    assert.equal(relayed.fork, i + 1);
    const interp = createInterp(track), out = createState();
    interp.push(relayed, 1000); interp.sample(2, 1100, out);
    assert.ok(Math.hypot(out[S.posX] - f.p[0], out[S.posY] - f.p[1], out[S.posZ] - f.p[2]) < .02);
    interp.seed(2, out);
    assert.equal(racerFork(track, out), i + 1);
    const main = pathFrame(track, 0, s);
    assert.ok(Math.hypot(main.p[0] - f.p[0], main.p[1] - f.p[1], main.p[2] - f.p[2]) > 1);
  }
});

test('main-road records retain the original binary contract', () => {
  const k = createKart(), original = encodeFrame(BIN.BATCH, 0, [k]);
  assert.deepEqual(encodeFrame(BIN.BATCH, 0, [{ ...k, fork: 0 }]), original);
  assert.equal(Object.hasOwn(decodeFrame(original).karts[0], 'fork'), false);
});

test('online rotating collision fields use shared race time across motion settings', () => {
  const fields = [{ stones: [{ spin: 1.2, angle: 0 }] }], calm = [{ stones: [{ spin: 1.2, angle: 0 }] }];
  updateStoneMotion(fields, 1/60, false, false, 17);
  updateStoneMotion(calm, 1/30, true, false, 17);
  assert.equal(fields[0].stones[0].angle, calm[0].stones[0].angle);
});
