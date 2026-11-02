/* tests/route-commit.test.mjs - the shortcut COMMIT (owner 2026-10-28: "most shortcuts arent really intuitive (often they
 * are at a sharp angle and its hard to catch them when going fast)"). Real courses, the real sim and the light Steer
 * assist: a player who steers for an opening from the centre line at speed lands on its lower road; a player who only
 * follows the road never does. Openings on the outside of a bend take a gentle stick at every speed; openings on the
 * inside of a spiral need a stick past the road's own turn (sim/assist.js COMMIT).
 */
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { placeKart, steerTo } from '../dev/hook.js';
import { S, createState } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { createAssist } from '../sim/assist.js';
import { createActions } from '../core/actions.js';
import { makeFeel, DT } from './sim-fake.mjs';

function run(track, r, { v, top = Math.min(v, 39), lead, stick }) {
  const feel = makeFeel({ 'speed.topMps': top }), st = createState(), side = r.side === 'L' ? -1 : 1;
  placeKart(st, track, feel, r.openS0 - lead, 0, v, 0);
  const assist = createAssist(track, feel, { mode: 'light' }), a = createActions();
  a.throttle = 1;
  let land = false;
  const ev = { emit(n) { if (n === 'route-land') land = true; } };
  for (let i = 0; i < 8 / DT && !land && !st[S.respawnPhase]; i++) {
    a.steer = stick === 'follow' ? steerTo(st, track, st[S.s] + 15, 0) : side * stick;
    step(st, assist.apply(a, st, DT, 'racing'), track, feel, DT, ev);
    if (st[S.grounded] > 0 && st[S.s] > r.openS1 + 20 && st[S.s] < r.openS1 + 400) break;
  }
  return land;
}

/** Bend share of the mouth toward the open side: + = the opening is on the inside of the turn. */
function insideTurn(track, r) {
  const a = track.frameAt(r.openS0), b = track.frameAt(r.openS1);
  return (b.t[0] * a.b[0] + b.t[1] * a.b[1] + b.t[2] * a.b[2]) * (r.side === 'L' ? -1 : 1) > 0.05;
}

for (const id of ['threshold', 'mainframe', 'keep', 'boudoir', 'goonworks']) {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8')), { moving: true });
  for (const r of track.routes) {
    const inside = insideTurn(track, r);
    test(`${id} ${r.id}: a player who only follows the road never drops in`, () => {
      for (const v of [30, 39, 46.8]) for (const lead of [20, 60, 100]) assert.equal(run(track, r, { v, lead, stick: 'follow' }), false, `${v} m/s from ${lead} m`);
    });
    test(`${id} ${r.id}: a committed steer from the centre line catches it (${inside ? 'inside' : 'outside'} of the bend)`, () => {
      // at the ordinary top speed, a firm steer from 2 s out and a late one from 1 s out
      for (const lead of [30, 60]) assert.ok(run(track, r, { v: 30, lead, stick: inside ? 1 : 0.25 }), `30 m/s from ${lead} m`);
      // at clean-pace speed, from about 2.5 s out
      assert.ok(run(track, r, { v: 39, lead: 100, stick: inside ? 1 : 0.25 }), '39 m/s from 100 m');
    });
    if (!inside) test(`${id} ${r.id}: a gentle steer gets an entry window of at least a second at pad speed`, () => {
      let ok = 0;
      for (let lead = 0; lead <= 96; lead += 8) if (run(track, r, { v: 46.8, top: 39, lead, stick: 0.25 })) ok++;
      assert.ok(ok * 8 / 46.8 >= 1, `${ok} of 13 reaction points`);
    });
  }
}
