import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { makeRace } from './sim-racers-harness.mjs';
import { createMovingHazards } from '../sim/movingHazards.js';
import { createKeepHazards } from '../sim/keepHazards.js';
import { createGoonworksHazards } from '../sim/goonworksHazards.js';
import { createBoudoirHazards } from '../sim/boudoirHazards.js';
import { createMainframeIndustry } from '../sim/mainframeIndustry.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { setMachineryBeat } from '../track/machineryBeat.js';
import { S } from '../sim/state.js';

for (const [id, bpm] of [['threshold', 120], ['keep', 142], ['mainframe', 135], ['boudoir', 99.02], ['goonworks', 96.75]]) {
  for (const seed of id === 'keep' ? [1, 241023, 20261025] : [20261025])
  test(id + ' eight racers finish with beat-fast hazards, moving traps and timed bridge support, seed ' + seed, () => {
    const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8')), { moving: true });
    track.laps = 1;
    const run = makeRace({ track, seed }), moving = createMovingHazards(track);
    const hazards = id === 'keep' ? createKeepHazards(track, getKeepLayout(track))
      : id === 'goonworks' ? createGoonworksHazards(track) : id === 'boudoir' ? createBoudoirHazards(track) : null;
    const industry = createMainframeIndustry(track, { emit() {} });
    const drive = run.ai.drive;
    run.ai.drive = (r, ctx, dt, out) => {
      drive(r, ctx, dt, out);
      if (ctx.race.phase !== 'racing' && ctx.race.phase !== 'finished') return;
      if (!track.forkAtSeg?.(r.st[S.seg])) hazards?.steer(r, out);
      moving.steer(r, out);
      if (id === 'mainframe') industry.steer(r, out);
    };
    const step = run.racers.step, fallsAtFinish = new Map();
    run.racers.step = (dt, actions, race, ctx) => {
      setMachineryBeat(track, { enabled: race.phase === 'racing', songS: race.timeS, raceS: race.timeS, bpm });
      step(dt, actions, race, { ...ctx, moving, hazards, industry });
      // A racer past the line keeps driving a cooldown lap under the same machinery: a fall there costs no one anything.
      for (const r of run.racers.list) if (r.finished && !fallsAtFinish.has(r.i)) fallsAtFinish.set(r.i, r.falls);
    };
    run.run(700, () => run.racers.list.every(r => r.finished));
    assert.ok(run.racers.list.every(r => r.finished), JSON.stringify(run.racers.list.map(r => ({ i: r.i, s: r.progressM, finished: r.finished, falls: r.falls }))));
    assert.ok(run.racers.list.every(r => fallsAtFinish.get(r.i) === 0), JSON.stringify(run.racers.list.map(r => ({ i: r.i, falls: fallsAtFinish.get(r.i) }))));
  });
}
