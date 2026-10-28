import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { createMainframeScan, scanClock, scanZones } from '../sim/mainframeScan.js';
import { S } from '../sim/state.js';
import { makeRace } from './sim-racers-harness.mjs';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8')));

for (const seed of [1, 241023, 20261024]) test('Mainframe full grid finishes with live scan contact, seed ' + seed, () => {
  const run = makeRace({ track, seed }), hits = [];
  const scan = createMainframeScan(track, { emit: (name, data) => hits.push({ name, ...data }) });
  // Match boot's ordering: movement, scan contact, kart contact, standings and items.
  const step = run.racers.step;
  run.racers.step = (...args) => {
    step(...args);
    if (run.race.phase !== 'racing') return;
    const music = seed === 1 ? null : {
      song: { bpm: 135, downbeatS: 0.4 }, songS: run.race.timeS + 3.4,
    };
    const clock = scanClock(music, run.race.timeS);
    scan.update(run.racers.list, clock.timeS, clock.bpm);
  };
  run.run(60 + track.lengthM * track.laps / (run.feel.get('speed.topMps') * .6), () => run.racers.list.every(racer => racer.finished));
  assert.ok(hits.length > 0, 'real driving must encounter an active scan');
  assert.ok(hits.some(hit => hit.racer > 0), 'bots receive the same contact rule');
  assert.equal(scan.hits, hits.length);
  assert.deepEqual(new Set(hits.map(hit => hit.zone)), new Set(scanZones(track).map(zone => zone.id)));
  assert.ok(run.racers.list.every(racer => racer.finished && racer.st[S.lap] === 3),
    JSON.stringify(run.racers.list.map(r => ({ i: r.i, s: r.st[S.s], lap: r.st[S.lap], falls: r.falls }))));
  assert.ok(run.racers.list.every(racer => racer.falls === 0), 'scan stalls never strand a racer or require a rescue');
});
