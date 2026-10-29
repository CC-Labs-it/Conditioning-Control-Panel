/* tests/assist-edges.test.mjs - playtest 2026-10-28 #6: "steering felt inverted near the left/right edges".
 * With the Steer assist on Help (light), a player steering hard near either edge of Threshold, at any heading and
 * speed, gets exactly that side and never less turn than asked from the assist, and the kart's own yaw follows the
 * stick while it rolls forward. No U-turn ever fires on Help, and on Trance (strong) only after a fully hands-off spell.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createAssist } from '../sim/assist.js';
import { step as kartStep } from '../sim/kart.js';
import { S, RESPAWN } from '../sim/state.js';
import { place, makeFeel, acts, DT } from './sim-fake.mjs';
import { threshold } from './sim-racers-harness.mjs';

const sink = { emit() {} };

test('Help near the edges: the player\'s side always wins, never less turn, and the kart yaws the way asked', () => {
  const track = threshold(), feel = makeFeel();
  let checked = 0, yawChecked = 0;
  const bad = [];
  for (let s0 = 200; s0 < track.lengthM - 200; s0 += Math.floor(track.lengthM / 23)) {
    const f = track.frameAt(s0);
    if (f.surface !== 'glass' && f.surface !== undefined) continue;
    const h = f.w / 2;
    for (const side of [-1, 1]) for (const headingDeg of [-35, -12, 0, 12, 35]) for (const speed of [12, 30]) {
      for (const steer of [-1, -0.5, 0.5, 1]) {
        const assist = createAssist(track, feel, { mode: 'light' });
        const st = place(track, { s: s0, lateral: side * h * 0.85, headingDeg, speed });
        for (let i = 0; i < 36; i++) {
          if (st[S.respawnPhase] !== RESPAWN.NONE || st[S.grounded] <= 0) break;
          const a = acts({ steer, throttle: 1 });
          const out = assist.apply(a, st, DT, 'racing');
          checked++;
          if (Math.sign(out.steer) !== Math.sign(steer) || Math.abs(out.steer) < Math.abs(steer) - 1e-9) bad.push({ s0, side, headingDeg, speed, steer, out: out.steer, i });
          assert.equal(assist.view.auto, 0); assert.equal(assist.view.cruise, 0);
          kartStep(st, out, track, feel, DT, sink);
          // the kart's yaw follows the stick while it rolls forward (the yaw law flips only in reverse)
          if (i > 8 && st[S.grounded] > 0 && st[S.driftDir] === 0 && Math.abs(st[S.yawRate]) > 1e-4 && st[S.engineV] > 2) {
            yawChecked++;
            if (Math.sign(st[S.yawRate]) !== Math.sign(steer)) bad.push({ s0, side, headingDeg, speed, steer, yaw: st[S.yawRate], i, kind: 'yaw' });
          }
        }
      }
    }
  }
  assert.ok(checked > 3000 && yawChecked > 1000, 'the sweep ran (' + checked + ' / ' + yawChecked + ')');
  assert.deepEqual(bad.slice(0, 5), [], bad.length + ' steps turned less or the other way');
});

test('no U-turn near an edge mid-race: Help never, Trance only after a fully hands-off spell', () => {
  const track = threshold(), feel = makeFeel();
  const s0 = 600, f = track.frameAt(s0), h = f.w / 2;
  for (const side of [-1, 1]) {
    // a kart facing backwards against the edge (after a spin), the player steering or holding the throttle
    for (const hands of [{ steer: side }, { throttle: 1 }, { steer: -side * 0.4, throttle: 1 }]) {
      for (const mode of ['light', 'strong']) {
        const assist = createAssist(track, feel, { mode });
        const st = place(track, { s: s0, lateral: side * h * 0.85, headingDeg: 180, speed: 4 });
        for (let i = 0; i < 400; i++) {
          const a = acts(hands), out = assist.apply(a, st, DT, 'racing');
          assert.equal(out.steer, a.steer, mode + ': the stick as it is, facing back ' + JSON.stringify(hands));
          assert.equal(out.throttle, a.throttle, mode + ': no throttle added facing back');
        }
      }
    }
    // hands fully off: Help leaves the kart alone; only Trance's cruise comes round
    const light = createAssist(track, feel, { mode: 'light' }), strong = createAssist(track, feel, { mode: 'strong' });
    const st = place(track, { s: s0, lateral: side * h * 0.85, headingDeg: 180, speed: 4 });
    let lo, so;
    for (let i = 0; i < 400; i++) { lo = light.apply(acts(), st, DT, 'racing'); so = strong.apply(acts(), st, DT, 'racing'); }
    assert.equal(lo.steer, 0); assert.equal(lo.throttle, 0);
    assert.ok(Math.abs(so.steer) === 1 && so.throttle > 0, 'Trance, hands off a while: the gentle U-turn');
  }
});
