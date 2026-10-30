/* tests/assist-trance-stones.test.mjs - desk run 2026-10-30 (owner): Trance steer assist with the throttle HELD and no
 * steer looped fall / rescue at the Threshold stepping stones (race 0:40-2:20); letting go of W crossed, because only
 * the hands-off cruise took the challenge jumps. Trance now jumps the islands for a player who holds the throttle and
 * leaves the wheel to the autopilot, and holds the hat over the long aerial gaps the same way. Help (Light) still never jumps for anyone, and any steer or brake hands every
 * jump back to the player. Headless sim on the real Threshold.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { S, RESPAWN } from '../sim/state.js';
import { createRace } from '../sim/race.js';
import { createRacers } from '../sim/racers.js';
import { createAssist } from '../sim/assist.js';
import { createActions } from '../core/actions.js';
import { makeFeel, DT } from './sim-fake.mjs';
import { threshold } from './sim-racers-harness.mjs';

function solo({ mode, hands }) {
  const track = threshold(), feel = makeFeel();
  const log = [];
  const queue = { emit(name, payload = {}) { log.push({ name, payload, t: race.timeS }); } };
  const race = createRace(track, feel, queue);
  const racers = createRacers(track, feel, queue, { count: 1, seed: 3 });
  const assist = createAssist(track, feel, { mode });
  const a = Object.assign(createActions(), { item: false, back: false });
  const P = racers.player;
  const trace = [];
  let t = 0, rescued = 0, wasNone = true;
  race.start();
  function run(maxS, done) {
    for (let i = 0, n = Math.round(maxS / DT); i < n; i++) {
      Object.assign(a, { steer: 0, throttle: 0, brake: 0, hop: false, hopPressed: false, item: false, back: false }, hands(t, P.st));
      const pa = assist.apply(a, P.st, DT, race.phase);
      trace.push({ t, s: P.st[S.s], hop: pa.hop || pa.hopPressed, auto: assist.view.auto, cruise: assist.view.cruise });
      race.update(DT, pa, P.st);
      racers.step(DT, pa, race, {});
      const none = P.st[S.respawnPhase] === RESPAWN.NONE;
      if (wasNone && !none) rescued++;
      wasNone = none;
      t += DT;
      if (done()) break;
    }
  }
  return { track, feel, P, log, trace, run, get rescued() { return rescued; }, get t() { return t; },
    events: (n) => log.filter((e) => e.name === n) };
}

const fieldOf = (track) => track.fields.find((f) => f.platforming);

test('Trance, throttle held, no steer: the autopilot jumps the Threshold stones and the lap has no fall or rescue', () => {
  const r = solo({ mode: 'strong', hands: () => ({ throttle: 1 }) });
  const F = fieldOf(r.track);
  assert.ok(F, 'Threshold has a platforming field');
  const capS = 80 * Math.max(1, r.track.lengthM / 1350);
  r.run(capS, () => r.P.st[S.lap] >= 1);
  const label = JSON.stringify({ lap: r.P.st[S.lap], s: +r.P.st[S.s].toFixed(1), field: [F.fromS, F.toS],
    void: r.events('void').map((e) => +e.payload.s.toFixed(1)), fall: r.events('fall').length, drop: r.events('drop').length, rescued: r.rescued });
  assert.equal(r.events('void').length, 0, 'never off a stone into the void: ' + label);
  assert.equal(r.events('fall').length, 0, 'no fall: ' + label);
  assert.equal(r.events('drop').length, 0, 'never off the road: ' + label);
  assert.equal(r.rescued, 0, 'no rescue: ' + label);
  assert.ok(r.P.st[S.lap] >= 1, 'a whole lap with the throttle held: ' + label);
  const hops = r.trace.filter((x) => x.hop);
  assert.ok(hops.length > 0, 'the assist took the island jumps');
  assert.ok(hops.some((x) => x.s >= F.fromS && x.s < F.toS), 'island jumps taken in the stepping field');
  assert.ok(hops.every((x) => x.s >= F.fromS && x.s < F.toS || r.track.aerials.some((j) => x.s >= j.rampS0 && x.s < j.landingS1)),
    'assisted jumps and hat holds only in their authored challenges');
  assert.ok(r.trace.every((x) => x.cruise === 0), 'the throttle is the player\'s: no cruise');
});

test('Trance hands the jumps back the moment the player steers or brakes inside the field', () => {
  for (const extra of [{ steer: 0.3 }, { brake: 0.3 }]) {
    let r;
    r = solo({ mode: 'strong', hands: (t, st) => {
      const F = fieldOf(r.track);
      return st[S.s] >= F.fromS && st[S.s] < F.toS ? { throttle: 1, ...extra } : { throttle: 1 };
    } });
    const F = fieldOf(r.track);
    r.run(60, () => (r.P.st[S.s] >= F.toS + 10 && r.P.st[S.s] < F.toS + 500) || r.events('void').length > 0);   // the grid sits behind the line, s near the lap's end
    const inField = r.trace.filter((x) => x.s >= F.fromS && x.s < F.toS);
    assert.ok(inField.length > 0, 'reached the field');
    assert.ok(inField.every((x) => !x.hop), JSON.stringify(extra) + ': no assisted jump while the player steers or brakes');
  }
});

test('Help (Light), throttle held: still never jumps for the player', () => {
  const r = solo({ mode: 'light', hands: () => ({ throttle: 1 }) });
  r.run(90, () => r.events('void').length > 0);
  assert.ok(r.trace.every((x) => !x.hop && x.auto === 0), 'light never jumps and never drives');
});
