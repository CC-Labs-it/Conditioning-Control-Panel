import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { densifyBoosts, safeBoostStation, boostExclusions } from '../track/boostPlan.js';
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
  const plan = densifyBoosts(source), track = loadTrack(json);

  test(id + ': committed plan preserves original pads, uses 2-4 pad trains and regenerates exactly', () => {
    assert.deepEqual(json.boosts, regenerateBoosts(id));
    assert.deepEqual(json.boosts, plan.boosts);
    assert.deepEqual(json.boosts.slice(0, originalCount), source.boosts);
    assert.ok(json.boosts.length >= (id === 'threshold' ? 16 : 100));
    assert.ok(plan.runs.length >= 2);
    assert.ok(plan.runs.every((r) => r.length >= 2 && r.length <= 4));
    const exclusions = boostExclusions(track);
    for (const p of json.boosts.slice(originalCount)) assert.ok(safeBoostStation(track, track.nodeS[p.node], { ...p, exclusions }), 'safe pad at ' + p.node);
    for (const r of plan.runs) for (let i = 1; i < r.length; i++) {
      const gap = track.nodeS[r[i]] - track.nodeS[r[i - 1]];
      assert.ok(gap > 9 && gap < 38, 'separate short pad entries');
    }
  });

  test(id + ': an actual kart contacts each pad in a straight acceleration train once', () => {
    const nodes = plan.runs.find((r) => {
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
      }
    }
  });
}

test('Threshold exact 66m rollout endpoint rejects the former shoulder overlap', () => {
  const json = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
  const track = loadTrack(json), s = track.nodeS[2], endWidth = track.frameAt(s + 66).w;
  assert.ok(endWidth < 18 && endWidth >= 17, 'the real taper reproduces the missed final 4m');
  assert.equal(safeBoostStation(track, s, { x: 5, w: 4, len: 6 }), false);
  assert.equal(safeBoostStation(track, s, { x: 4.5, w: 4, len: 6 }), true);
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
  for (let tick = 0; tick < (id === 'mainframe' ? 1200 : 900) * 60 && !r.racers.list.every((kart) => kart.finished); tick++) {
    for (const kart of r.racers.list) before[kart.i] = kart.st[S.s];
    r.tick();
    for (const kart of r.racers.list) {
      if (kart.falls > fallCounts[kart.i]) {
        assert.ok(!added.some((pad) => ((before[kart.i] - pad.s + L) % L) <= pad.len + 60), 'new pad rollout caused a drop');
        fallCounts[kart.i] = kart.falls;
      }
    }
  }
  assert.ok(r.racers.list.every((kart) => kart.finished));
});
