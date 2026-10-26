import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { densifyBoosts, safeBoostStation, boostExclusions } from '../track/boostPlan.js';
import { filterKeepHoleBoosts } from '../track/keepHoles.js';
import { holeAt } from '../track/courts.js';
import { regenerateBoosts } from '../dev/densify-boosts.mjs';
import { place, makeFeel, acts, recorder, run } from './sim-fake.mjs';
import { S } from '../sim/state.js';
import { makeRace } from './sim-racers-harness.mjs';
import { createItems } from '../sim/items.js';
import { createMainframeIndustry } from '../sim/mainframeIndustry.js';
import { createMainframeScan } from '../sim/mainframeScan.js';

for (const id of ['threshold', 'keep', 'mainframe']) {
  const json = JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8'));
  const originalCount = id === 'mainframe' ? 8 : 4;
  const source = { ...json, boosts: json.boosts.slice(0, originalCount) };
  // Keep preserves its established runs and removes only unsafe pads after authoring worn slabs.
  const plan = densifyBoosts(id === 'keep' ? { ...source, courts: [] } : source), track = loadTrack(json);
  if (id === 'keep') {
    plan.boosts = filterKeepHoleBoosts(track, plan.boosts);
    const kept = new Set(plan.boosts.map(p => p.node));
    plan.runs = plan.runs.map(run => run.filter(node => kept.has(node))).filter(run => run.length);
  }

  test(id + ': committed plan preserves original pads, uses 2-4 pad trains and regenerates exactly', () => {
    assert.deepEqual(json.boosts, regenerateBoosts(id));
    assert.deepEqual(json.boosts, plan.boosts);
    assert.deepEqual(json.boosts.slice(0, originalCount), source.boosts);
    assert.ok(json.boosts.length >= (id === 'threshold' ? 16 : 100));
    assert.ok(plan.runs.length >= 2);
    assert.ok(plan.runs.every((r) => r.length >= (id === 'keep' ? 1 : 2) && r.length <= 4));
    const authored = loadTrack(id === 'keep' ? { ...json, courts: [] } : json, { aerials: false }), exclusions = boostExclusions(authored);
    for (const p of json.boosts.slice(originalCount)) assert.ok(safeBoostStation(authored, authored.nodeS[p.node], { ...p, exclusions }), 'safe authored pad at ' + p.node);
    for (const r of plan.runs) for (let i = 1; i < r.length; i++) {
      const gap = track.nodeS[r[i]] - track.nodeS[r[i - 1]];
      assert.ok(gap > 9 && gap < 38, 'separate short pad entries');
    }
  });

  test(id + ': an actual kart contacts each pad in a straight acceleration train once', () => {
    const nodes = plan.runs.find((r) => {
      if (!r.every(node => track.boosts.some(p => p.node === node && p.x === 0))) return false;
      if (track.nodeS[r[0]] < 2) return false;
      const first = track.frameAt(track.nodeS[r[0]] - 2), last = track.frameAt(track.nodeS[r.at(-1)] + 7);
      return first.t.reduce((sum, v, i) => sum + v * last.t[i], 0) > .9999;
    });
    assert.ok(nodes, 'a straight manual acceleration run exists');
    const s0 = track.nodeS[nodes[0]], endS = track.nodeS[nodes.at(-1)] + 7;
    const lane = 0;
    const st = place(track, { s: s0 - 2, lateral: lane, speed: 30 }), events = recorder();
    run(st, track, makeFeel(), acts({ throttle: 1 }), 4, events, () => st[S.s] > endS);
    const wanted = nodes.map((node) => track.boosts.findIndex((p) => p.node === node && p.x === lane));
    const contacts = events.all('pad').map((p) => p.payload.index);
    for (const index of wanted) assert.equal(contacts.filter((p) => p === index).length, 1, 'one real contact per pad');
    assert.ok(st[S.engineV] > 31, 'sequential entries deliver actual boost');
    assert.equal(events.all('drop').length, 0);
  });

  test(id + ': independent half-metre sweep preserves the shoulder margin through the full rollout endpoint', () => {
    for (const pad of track.boosts.slice(originalCount)) {
      // This audit deliberately does not call the planner's validation function.
      for (let d = -8; d <= pad.len + 60; d += .5) {
        const frame = track.frameAt(pad.s + d);
        const shoulder = frame.w / 2 - Math.abs(pad.x) - pad.w / 2;
        assert.ok(shoulder >= 2, '2m shoulder at node ' + pad.node + ', x=' + pad.x + ', offset=' + d + ': ' + shoulder);
        assert.ok(frame.flags.includes('rail') && !frame.flags.includes('jump') && !frame.flags.includes('magnet'));
        if (id === 'keep') for (let x = pad.x - pad.w / 2 - .65; x <= pad.x + pad.w / 2 + .65; x += .25)
          assert.equal(holeAt(track.courts, pad.s + d, x), null, 'Retained pad rollout never reaches a new opening');
      }
    }
  });
}

test('Threshold exact 66m rollout endpoint rejects the former shoulder overlap', () => {
  const json = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
  const track = loadTrack(json), s = track.nodeS[2], endWidth = track.frameAt(s + 66).w;
  const safeX = endWidth / 2 - 4.25, unsafeX = safeX + .5;
  assert.ok(endWidth < track.frameAt(s + 61).w, 'the real taper continues through the former missed final 5m');
  for (let d = -8; d < 66; d += 5)
    assert.ok(track.frameAt(s + d).w / 2 - unsafeX - 2 >= 2, 'the old sample points have a full 2m shoulder');
  assert.equal(endWidth / 2 - safeX - 2, 2.25, 'accepted endpoint retains the exact shoulder margin');
  assert.equal(endWidth / 2 - unsafeX - 2, 1.75, 'half a metre outward fails only the final endpoint');
  assert.equal(safeBoostStation(track, s, { x: unsafeX, w: 4, len: 6 }), false);
  assert.equal(safeBoostStation(track, s, { x: safeX, w: 4, len: 6 }), true);
});

for (const seed of [1, 241023, 20261024]) test('dense Threshold: eight racers finish with no falls, seed ' + seed, () => {
  const r = makeRace({ seed });
  r.run(900, () => r.racers.list.every((kart) => kart.finished));
  assert.ok(r.racers.list.every((kart) => kart.finished));
  assert.deepEqual(r.racers.list.map((kart) => kart.falls), Array(8).fill(0));
});

for (const id of ['threshold', 'mainframe']) for (const seed of [1, 241023, 20261024]) test('dense ' + id + ' combat: every racer finishes, no drop on a new pad rollout, seed ' + seed, () => {
  const json = JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8'));
  const r = makeRace({ seed, track: loadTrack(json), items: createItems });
  if (id === 'mainframe') {
    const industry = createMainframeIndustry(r.track, { emit() {} }), scan = createMainframeScan(r.track, { emit() {} });
    const drive = r.ai.drive, step = r.racers.step;
    r.ai.drive = (kart, ctx, dt, a) => { drive(kart, ctx, dt, a); industry.steer(kart, a); };
    r.racers.step = (...args) => {
      step(...args);
      if (['racing', 'finished'].includes(r.race.phase)) {
        industry.update(r.racers.list, r.racers.clockS, 1 / 60);
        scan.update(r.racers.list, r.racers.clockS, 120);
      }
    };
  }
  const fallCounts = new Int32Array(8), before = new Float64Array(8), L = r.track.lengthM;
  const added = r.track.boosts.slice(id === 'mainframe' ? 8 : 4);
  const combatFlights = new Set();
  for (let tick = 0; tick < (id === 'mainframe' ? 1200 : 900) * 60 && !r.racers.list.every((kart) => kart.finished); tick++) {
    for (const kart of r.racers.list) before[kart.i] = kart.st[S.s];
    const logStart = r.log.length, combatDrops = new Set();
    r.tick();
    // A recorded bubble attack can deliberately throw a kart over a rail. Attribute only that same flight.
    for (let i = logStart; i < r.log.length; i++) {
      const event = r.log[i], wrapped = event.name === 'racer-event';
      const name = wrapped ? event.payload.name : event.name, payload = wrapped ? event.payload.p : event.payload;
      const who = wrapped ? event.payload.racer : 0;
      if (name === 'bubble-ram') { combatFlights.add(payload.victim); if (payload.both) combatFlights.add(payload.attacker); }
      if (name === 'land') combatFlights.delete(who);
      if (name === 'drop') { if (combatFlights.has(who)) combatDrops.add(who); combatFlights.delete(who); }
    }
    for (const kart of r.racers.list) {
      if (kart.falls > fallCounts[kart.i]) {
        assert.ok(combatDrops.has(kart.i) || !added.some((pad) => ((before[kart.i] - pad.s + L) % L) <= pad.len + 60),
          'new pad rollout caused a drop: ' + JSON.stringify({ id, seed, racer: kart.i, s: before[kart.i], lateral: kart.st[S.lateral], height: kart.st[S.height],
            pads: added.filter(pad => ((before[kart.i] - pad.s + L) % L) <= pad.len + 60).map(p => p.node) }));
        fallCounts[kart.i] = kart.falls;
      }
    }
  }
  assert.ok(r.racers.list.every((kart) => kart.finished));
});
