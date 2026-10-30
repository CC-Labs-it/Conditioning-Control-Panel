import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { S, cloneState } from '../sim/state.js';
import { createAssist } from '../sim/assist.js';
import { createContact } from '../sim/contact.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { createBoudoirPerfume } from '../sim/boudoirPerfume.js';
import { createBoudoirHazards } from '../sim/boudoirHazards.js';
import { createMinimap } from '../hud/minimap.js';
import { makeFeel, place, recorder, DT, acts } from './sim-fake.mjs';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url))));
function branchKart(fork, share = .5, lateral = 0, speed = 20) {
  const local = fork.lengthM * share;
  const st = place({ frameAt: fork.frameAt }, { s: local, lateral, speed });
  st[S.s] = fork.mainS(local);
  return st;
}

test('fork steering help follows the narrow branch instead of the remote main lane', () => {
  const feel = makeFeel(), assist = createAssist(track, feel, { mode: 'strong' });
  for (const fork of track.forks) {
    const st = branchKart(fork, .5, 0, 16), input = acts({ throttle: 1, steer: .7 });
    const out = assist.apply(input, st, DT, 'racing');
    assert.ok(Number.isFinite(out.steer));
    assert.ok(out.steer >= .7, 'the player can override branch assistance');
    assert.equal(input.steer, .7);
    const f = fork.frameAt(fork.lengthM * .5), side = branchKart(fork, .5, f.w / 2 - .7, 12);
    const helped = assist.apply(acts({ throttle: 1 }), side, DT, 'racing');
    assert.ok(helped.steer < 0, fork.id + ' pulls away from its own right rail');
  }
});

test('bubble contact uses the bank and rails of the current fork', () => {
  const feel = makeFeel(), events = recorder(), contact = createContact(track, feel, events);
  for (const fork of track.forks) {
    contact.reset();
    const local = fork.lengthM * .45, f = fork.frameAt(local);
    const make = (i, x) => ({ i, st: branchKart(fork, .45, x), feel, fxv: {}, events });
    const a = make(0, f.w / 2 - 2.6), b = make(1, f.w / 2 - .8);
    a.fxv.shield = 'glass';
    contact.update(DT, [a, b], { phase: 'racing', timeS: 10 });
    assert.equal(b.st[S.airborne], 1, fork.id);
    const normalSpeed = f.n.reduce((v, n, i) => v + n * b.st[S.velX + i], 0);
    assert.ok(normalSpeed >= 6.4);
    const support = track.query(b.st[S.posX], b.st[S.posY], b.st[S.posZ], b.st[S.seg]);
    assert.equal(support.forkId, fork.id);
    assert.ok(support.edgeDist > .4, fork.id + ' rail retains the contact position');
  }
});

test('hands-off steering carries a player along each fork and through its rejoin', () => {
  const feel = makeFeel({ 'assist.autoSteerAfterS': 0, 'assist.cruiseAfterS': 0 });
  for (const fork of track.forks) {
    const assist = createAssist(track, feel, { mode: 'strong' }), events = recorder(), st = branchKart(fork, .2, 0, 18);
    let merged = false;
    for (let i = 0; i < 90 / DT; i++) {
      const action = assist.apply(acts(), st, DT, 'racing');
      step(st, action, track, feel, DT, events); updateRespawn(st, track, feel, DT, events);
      if (!track.forkAtSeg(st[S.seg]) && st[S.s] > fork.toS + 5) { merged = true; break; }
    }
    assert.ok(merged, fork.id + ' rejoin');
    assert.equal(events.all('rescue-pull').length, 0, fork.id);
  }
});

test('slipstream requires the same physical path and measures branch distance in metres', () => {
  const fork = track.forks.find(f => f.kind === 'slalom'), feel = makeFeel(), events = recorder(), contact = createContact(track, feel, events);
  const make = (i, st) => { st[S.speedPct] = .8; return { i, st, feel, events, fxv: {} }; };
  const tail = make(0, branchKart(fork, .5)), remote = make(1, place(track, { s: tail.st[S.s] + 8, speed: 28 }));
  for (let i = 0; i < 180; i++) contact.update(DT, [tail, remote], { phase: 'racing', timeS: 10 });
  assert.equal(tail.fxv.slip, 0);
  const gap = (feel.get('contact.slipMinM') + feel.get('contact.slipMaxM')) / 2;
  const lead = make(1, branchKart(fork, .5 + gap / fork.lengthM));
  for (let i = 0; i <= feel.get('contact.slipChargeS') / DT + 1; i++) contact.update(DT, [tail, lead], { phase: 'racing', timeS: 10 });
  assert.ok(events.all('slipstream').some(e => e.payload.racer === 0 && e.payload.state === 'fire'));
});

test('mapped fork progress cannot trigger a remote main-road perfume cloud or ribbon', () => {
  const fork = track.forks[0], st = branchKart(fork), s = st[S.s], events = recorder();
  const racer = { i: 0, st, kind: 'local', feel: makeFeel(), events };
  const perfume = createBoudoirPerfume(track, events, { zones: [{ id: 'remote', s0: s - 5, s1: s + 5, x: 0, w: 8 }] });
  const hazards = createBoudoirHazards(track, { presses: [], sweepers: [], mirrorGates: [], conveyors: [{ id: 'remote', s0: s - 5, s1: s + 5, x: 0, w: 8, offsetS: 0 }] });
  const before = cloneState(st);
  perfume.update([racer], 6, DT);
  hazards.apply(racer, before, 6, events);
  assert.deepEqual(st, before);
  assert.equal(perfume.hits, 0);
  assert.equal(hazards.inspect().ribbonPasses, 0);
});

test('minimap draws forks as open paths and fits their outermost point', () => {
  const sheets = [];
  const canvas = () => {
    const lines = [], context = { scale() {}, beginPath() {}, moveTo(x, y) { lines.push([x, y]); }, lineTo(x, y) { lines.push([x, y]); },
      closePath() { this.closed++; }, closed: 0, stroke() {}, setTransform() {}, clearRect() {}, drawImage() {}, arc() {}, fill() {} };
    const c = { clientWidth: 120, clientHeight: 96, getContext: () => context, context, lines };
    sheets.push(c); return c;
  };
  const map = createMinimap(canvas(), { doc: { createElement: canvas } });
  map.setTrack({ xz: [0, 0, 100, 0, 100, 100, 0, 100], forks: [{ xz: [0, 0, 200, 40, 100, 100] }] });
  const branch = sheets[1];
  assert.equal(branch.context.closed, 0);
  assert.equal(branch.lines.length, 3);
  for (const [x, y] of branch.lines) assert.ok(x >= 7 && x <= 113 && y >= 7 && y <= 89);
});
