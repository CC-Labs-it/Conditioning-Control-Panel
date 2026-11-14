/* tests/sim-stones-far-deck.test.mjs - owner bug 2026-10-30 (Threshold): "jumping after the first two stepping
 * platforms makes you fall if you hit the first few meters after the gap". A hop off the second stone whose arc
 * crossed deck level over the 2 m void before the deck resumes (still inside the snap band, so no void, no hole latch)
 * met the deck's front edge a few centimetres under its plane. The air landing asked for a previous height of at least
 * -3 cm above the new deck, so the kart passed UNDER the road and dropped at fallY. sim/stones.js fromLipVoid now
 * catches that crossing. A second way under the same deck: an outer-lane flight at 60+ m/s came down along the rail
 * where the road funnels from 50 m to 32 m, ended every tick just past the narrowing edge and sank under the road; an
 * airborne kart already over the road now touches down rail-held like a grounded one. Headless sim on the real Threshold: every lane, lateral offsets, 20-66.7 m/s and hop points
 * across the whole second stone. A kart either lands (stone or the deck past the field) keeping its speed, or came
 * down in the gap itself (a `void` event first, the designed miss); it never falls through supported road.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { fromLipVoid } from '../sim/stones.js';
import { S, RESPAWN, createState, setPos, setQuat, setVel } from '../sim/state.js';
import { qfromBasis } from '../core/math.js';
import { createActions } from '../core/actions.js';
import { makeFeel, DT } from './sim-fake.mjs';

const TRACK = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
const FIELD = TRACK.fields.find((f) => f.id === 'bridge-stones');
const FEEL = makeFeel();

function place(st, s, x, v) {
  st.fill(0);
  const f = TRACK.frameAt(s);
  setPos(st, f.p.map((p, i) => p + f.b[i] * x));
  setQuat(st, qfromBasis([0, 0, 0, 1], f.b, f.n, f.t.map((c) => -c)));
  setVel(st, f.t.map((c) => c * v));
  st[S.engineV] = v; st[S.speedPct] = v / 30; st[S.grounded] = 1;
  st[S.seg] = f.seg; st[S.latchSeg] = f.seg; st[S.s] = s; st[S.lateral] = x; st[S.hopT] = 1e3;
}

/** Drive the second stone of a lane at speed v with the throttle held, hop once at hopS (null: drive off the end). */
function jump(x, startS, v, hopS) {
  const st = createState(), events = [];
  const ev = { emit: (n, p) => events.push({ n, p, s: st[S.s] }) };
  place(st, startS, x, v);
  const a = createActions();
  let hopped = false, deck = null, hopSpeed = v, under = null;
  for (let i = 0; i < 8 / DT; i++) {
    Object.assign(a, { steer: 0, throttle: 1, brake: 0, hop: false, hopPressed: false, item: false, back: false });
    if (!hopped && hopS !== null && st[S.s] >= hopS && st[S.grounded]) {
      a.hop = true; a.hopPressed = true; hopped = true; hopSpeed = Math.hypot(st[S.velX], st[S.velZ]);
    }
    const was = st[S.grounded];
    step(st, a, TRACK, FEEL, DT, ev);
    updateRespawn(st, TRACK, FEEL, DT, ev);
    if (!was && st[S.grounded] && deck === null && st[S.s] >= FIELD.toS) {
      deck = { s: st[S.s] - FIELD.toS, speed: Math.hypot(st[S.velX], st[S.velZ]), hopSpeed };
    }
    // the bug's signature: flying more than a kart radius UNDER supported road, i.e. through its deck
    const q = TRACK.query(st[S.posX], st[S.posY], st[S.posZ], st[S.seg] | 0);
    if (under === null && !st[S.grounded] && st[S.respawnPhase] === RESPAWN.NONE && q.onRoad && q.height < -0.5) under = +(q.s - FIELD.toS).toFixed(1);
    if (st[S.respawnPhase] !== RESPAWN.NONE || st[S.s] > FIELD.toS + 90) break;
  }
  const bad = events.filter((e) => e.n === 'drop' || e.n === 'fall');
  return { events, bad, voided: events.some((e) => e.n === 'void'), deck, under, end: st[S.s] - FIELD.toS };
}

test('Threshold: the bridge field ends in a short void and the deck resumes past it', () => {
  assert.ok(FIELD && FIELD.platforming, 'Threshold keeps its platforming bridge field');
  for (const x of FIELD.lanes) {
    const last = FIELD.stones.filter((st) => st.x === x).sort((a, b) => a.s - b.s).pop();
    const gapM = FIELD.toS - (last.s + last.len);
    assert.ok(gapM > 0.5 && gapM < 4, 'lane ' + x + ' ends in a short void before the deck: ' + gapM);
  }
});

test('fromLipVoid: only a void inside the snap band hands over to supported ground', () => {
  const lip = { hole: { lip: true }, stone: null, onRoad: false, forkId: null };
  const deck = { hole: null, onRoad: true, forkId: null };
  assert.equal(fromLipVoid({ ...lip, height: -0.2 }, deck, 0.35), true);
  assert.equal(fromLipVoid({ ...lip, height: -0.5 }, deck, 0.35), false, 'below the band it has fallen in the gap');
  assert.equal(fromLipVoid({ ...lip, height: -0.2, hole: { lip: false } }, deck, 0.35), false, 'a court hole never catches');
  assert.equal(fromLipVoid({ ...lip, height: -0.2 }, { ...deck, onRoad: false }, 0.35), false);
  assert.equal(fromLipVoid({ ...lip, height: -0.2 }, { ...deck, forkId: 'dare' }, 0.35), false);
  assert.equal(fromLipVoid({ onRoad: true, hole: null, height: -0.2 }, deck, 0.35), false, 'road to road is the ordinary landing');
});

test('Threshold stones: a hop off the second stone never falls through the deck past the gap', () => {
  const failures = [];
  let landedNear = 0, runs = 0, worstKeep = Infinity;
  for (const lane of FIELD.lanes) {
    const second = FIELD.stones.filter((st) => st.x === lane).sort((a, b) => a.s - b.s)[1];
    const end = second.s + second.len;
    for (const dx of [-3, 0, 3]) for (const v of [20, 25, 30, 36, 42, 50, 60, 66.7]) {
      for (let back = 0; back <= 16; back += 1) {
        const r = jump(lane + dx, second.s + 1.5, v, end - back);
        runs++;
        const label = JSON.stringify({ lane, dx, v, back, end: +r.end.toFixed(1), deck: r.deck, under: r.under,
          events: r.events.filter((e) => /void|drop|fall|stone|land/.test(e.n)).map((e) => e.n + '@' + e.s.toFixed(1)) });
        if (r.voided) continue;                  // came down in the gap: the designed miss
        if (r.under !== null) { failures.push('passed under the road: ' + label); continue; }
        if (!r.deck) { failures.push('never landed on the deck past the field: ' + label); continue; }
        if (r.deck.s < 6) landedNear++;
        worstKeep = Math.min(worstKeep, r.deck.speed / r.deck.hopSpeed);
      }
    }
  }
  assert.deepEqual(failures.slice(0, 5), [], failures.length + ' of ' + runs + ' runs failed');
  assert.ok(landedNear >= 40, 'the sweep really lands in the first metres past the gap: ' + landedNear);
  assert.ok(worstKeep >= 0.85, 'a landing past the gap keeps its momentum: worst ' + worstKeep.toFixed(3));
});

test('Threshold stones: the owner case, a slow hop late on the right stone lands just past the gap', () => {
  const lane = 18, second = FIELD.stones.filter((st) => st.x === lane).sort((a, b) => a.s - b.s)[1];
  const r = jump(lane, second.s + 1.5, 20, second.s + second.len - 9);
  assert.deepEqual(r.bad, [], 'no drop: ' + JSON.stringify(r.bad));
  assert.ok(r.deck && r.deck.s >= 0 && r.deck.s < 3, 'lands in the first metres past the gap: ' + JSON.stringify(r.deck));
});
