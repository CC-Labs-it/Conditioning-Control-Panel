/* Shared real spline fixture for branch support, driving and integration tests. */
import { loadTrack } from '../track/load.js';
import { S } from '../sim/state.js';
import { place } from './sim-fake.mjs';

export function forkFixture({ rail = true, surface = 'grip', raised = 0 } = {}) {
  const nodes = Array.from({ length: 80 }, (_, i) => {
    const a = i * Math.PI / 40;
    return { p: [140 * Math.sin(a), 0, 140 - 140 * Math.cos(a)], w: 24, roll: 0, speed: 1,
      surface: 'glass', flags: i ? ['rail'] : ['start', 'rail'] };
  });
  const json = { format: 'glassroad-track', id: 'fork-fixture', version: 1, name: { en: 'Fork fixture' }, loop: true, laps: 2, units: 'm', nodes,
    sections: [{ id: 'a', from: 0, to: 80, act: 'a', palette: 'a', sky: 'a', bandSpeed: 1 }],
    checkpoints: [0, 10, 20, 40, 60], spawn: { node: 0, grid: Array.from({ length: 8 }, (_, i) => [(i % 2 ? 1 : -1) * 3, -4 - Math.floor(i / 2) * 4]) },
    items: [], boosts: [], jumps: [], hazards: [], media: [], ai: { lines: [], pace: [] }, beat: { bpm: null, offsetMs: 0, map: [] },
    limits: { fallY: -30, edgeGraceM: 1.2, edgeGraceS: .8 } };
  const main = loadTrack(json), from = 5, to = 25, a = main.frameAt(main.nodeS[from]), b = main.frameAt(main.nodeS[to]);
  const points = [a.p, a.p.map((v, i) => v + a.t[i] * 20), a.p.map((v, i) => (v + b.p[i]) / 2 + (i === 1 ? raised : 0)),
    b.p.map((v, i) => v - b.t[i] * 20), b.p].map(p => ({ p, w: 14, surface, flags: [rail ? 'rail' : 'open'] }));
  json.forks = [{ id: 'inside-chord', label: 'Inside chord', from, to, points }];
  const track = loadTrack(json);
  return { json, track, fork: track.forks[0] };
}

export function placeOnFork(fork, { localS = fork.lengthM / 2, ...options } = {}) {
  const st = place({ frameAt: fork.frameAt }, { ...options, s: localS });
  st[S.s] = fork.mainS(localS);
  return st;
}
