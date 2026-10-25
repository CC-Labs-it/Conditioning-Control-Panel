import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { createKeepGates, keepGatePhase } from '../sim/keepGates.js';
import { createKeepChains, keepChainPhase } from '../sim/keepChains.js';
import { setMachineryBeat, machineryBeatAt } from '../track/machineryBeat.js';
import { S } from '../sim/state.js';
import { place, makeFeel, recorder } from './sim-fake.mjs';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/keep.json', import.meta.url), 'utf8')));
const layout = getKeepLayout(track), beatS = 60 / 142;
const gates = layout.gates.filter(row => /^(ascent|descent)-/.test(row.id));
const chains = layout.chains.filter(row => row.id.startsWith('descent-'));
const clock = beat => {
  setMachineryBeat(track, null);
  setMachineryBeat(track, { enabled: true, songS: beat * beatS, raceS: .1, bpm: 142 });
};
const crossing = (row, lateral = row.x || 0) => ({
  racer: { i: 0, kind: 'local', st: place(track, { s: row.s + 5, lateral, speed: 45 }), feel: makeFeel(), events: recorder() },
  before: place(track, { s: row.s - 5, lateral, speed: 45 }),
});

test('Keep spiral stations break up both drives and leave shortcut approaches and landings clear', () => {
  const ascent = layout.landmarks.find(row => row.id === 'tower-spiral');
  const descent = layout.landmarks.find(row => row.id === 'north-tower');
  const up = gates.filter(row => row.id.startsWith('ascent-'));
  const down = [...gates.filter(row => row.id.startsWith('descent-')), ...chains];
  assert.equal(up.length, 8); assert.equal(down.length, 4);
  for (const [rows, section] of [[up, ascent], [down, descent]]) {
    for (let i = 0; i < rows.length; i++) {
      const row = rows[i];
      assert.ok(row.s > section.s0 + 80 && row.s < section.s1 - 80);
      if (i) assert.ok(row.s - rows[i - 1].s >= 160, 'a full approach between obstacles');
      for (const route of track.routes) {
        assert.ok(row.s < route.fromS - 70 || row.s > route.toS + 70, 'open takeoff ' + row.id);
        assert.ok(row.s < route.land.fromS - 70 || row.s > route.land.toS + 70, 'clear landing ' + row.id);
      }
    }
  }
  for (const row of gates) assert.ok(track.frameAt(row.s).w / 2 - row.w / 2 - Math.abs(row.x) >= 6 - 1e-5);
  assert.ok(chains.every(row => track.frameAt(row.s).w >= 33));
});

test('every spiral grille has real beat-timed collision and two continuously usable bypasses', () => {
  for (const row of gates) {
    const offset = Math.round(row.offsetS / beatS);
    clock(64 + 1.1 - offset);
    const expected = keepGatePhase(row, .1, {}, machineryBeatAt(track, .1));
    assert.ok(expected.blocking);
    const trap = createKeepGates(track, { gates: [row] });
    assert.equal(trap.inspect(.1).gates[0].liftM, expected.liftM);
    const centre = crossing(row);
    assert.equal(trap.apply(centre.racer, centre.before, .1), row.id);
    for (const side of [-1, 1]) {
      const x = side * (track.frameAt(row.s).w / 2 - 2), c = crossing(row, x);
      assert.equal(createKeepGates(track, { gates: [row] }).apply(c.racer, c.before, .1), null, row.id + ' bypass ' + side);
      assert.equal(c.racer.st[S.stalled], 0);
    }
    clock(64 + 1.75 - offset);
    const open = crossing(row);
    assert.equal(createKeepGates(track, { gates: [row] }).apply(open.racer, open.before, .1), null, 'open beat restores the centre');
  }
  setMachineryBeat(track, null);
});

test('descent weights swing on four beats, hit their visible low sweep and leave wide escape lanes', () => {
  for (const row of chains) {
    const offset = Math.round(row.offsetS / beatS), beat = 64 + 1 - offset;
    clock(beat);
    const pose = keepChainPhase(row, .1, machineryBeatAt(track, .1));
    const repeated = keepChainPhase(row, .1, { beat: beat + 4, beatS, safe: false });
    assert.ok(Math.abs(pose.x) < 1e-9 && Math.abs(pose.y - 3) < 1e-9);
    assert.ok(Math.abs(pose.x - repeated.x) < 1e-9 && Math.abs(pose.y - repeated.y) < 1e-9);
    const trap = createKeepChains(track, { chains: [row] }), centre = crossing(row);
    assert.equal(trap.inspect(.1).chains[0].x, pose.x);
    assert.equal(trap.apply(centre.racer, centre.before, .1), row.id);
    for (const side of [-1, 1]) {
      const c = crossing(row, side * (track.frameAt(row.s).w / 2 - 2));
      assert.equal(createKeepChains(track, { chains: [row] }).apply(c.racer, c.before, .1), null);
    }
  }
  setMachineryBeat(track, null);
});

test('loop traps never collide with the same footprint on the neighbouring revolution', () => {
  for (const row of [...gates, ...chains]) {
    const up = row.id.startsWith('ascent-');
    const section = layout.landmarks.find(p => p.id === (up ? 'tower-spiral' : 'north-tower'));
    const turnM = (section.s1 - section.s0) / (up ? 3.5 : 2.25);
    const otherS = row.s + turnM < section.s1 - 10 ? row.s + turnM : row.s - turnM;
    const offset = Math.round(row.offsetS / beatS), isChain = row.id.includes('chain');
    clock(64 + (isChain ? 1 : 1.1) - offset);
    const other = crossing({ ...row, s: otherS });
    const trap = isChain ? createKeepChains(track, { chains: [row] }) : createKeepGates(track, { gates: [row] });
    assert.equal(trap.apply(other.racer, other.before, .1), null, row.id + ' leaves the other deck free');
    assert.equal(other.racer.st[S.stalled], 0);
    assert.ok(Math.abs(track.frameAt(otherS).p[1] - track.frameAt(row.s).p[1]) > 30, 'raised machinery fits below the next revolution');
  }
  setMachineryBeat(track, null);
});
