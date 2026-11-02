import test from 'node:test';
import assert from 'node:assert/strict';
import { createWaterContact, createWaterTravel } from '../audio/water-travel.js';
import { S, createState } from '../sim/state.js';
import { loadTrack } from '../track/load.js';
import { readFileSync } from 'node:fs';

function context() {
  const nodes = [], sources = [], buffers = [];
  const param = value => ({ value, setTargetAtTime(v) { this.value = v; }, cancelScheduledValues() {} });
  const node = extra => {
    const n = { links: [], connect(to) { this.links.push(to); }, disconnect() { this.links = []; }, ...extra };
    nodes.push(n); return n;
  };
  return { currentTime: 0, sampleRate: 8000, nodes, sources, buffers,
    createGain: () => node({ gain: param(1) }),
    createBiquadFilter: () => node({ frequency: param(300), Q: param(1) }),
    createBufferSource() {
      const n = node({ playbackRate: param(1), started: 0, stopped: 0, start() { this.started++; }, stop() { this.stopped++; } });
      sources.push(n); return n;
    },
    createBuffer(channels, length) {
      const data = Array.from({ length: channels }, () => new Float32Array(length));
      buffers.push(data); return { getChannelData: c => data[c] };
    },
  };
}

test('water contact follows supporting roads and actual kart depth, including dry bridges and hops', () => {
  let surface = 'stream', onRoad = true, probes = 0;
  const track = { lengthM: 100, reservoir: { waterY: 10 },
    sections: [{ id: 'submerged', fromS: 20, toS: 60 }, { id: 'emerge', fromS: 60, toS: 80 }],
    query(x, y, z, hint, out) { probes++; out.surface = surface; out.onRoad = onRoad; return out; } };
  const contact = createWaterContact(track), st = createState();
  st[S.grounded] = 1; st[S.s] = 40; st[S.posY] = 14;
  assert.equal(contact(st).waterSurface, true); assert.equal(contact(st).waterDepth, 0);
  surface = 'pool'; assert.equal(contact(st).waterSurface, true);
  surface = 'glass'; assert.equal(contact(st).waterSurface, false, 'a dry fork bridge never makes a wake');
  st[S.posY] = 7; assert.equal(contact(st).waterDepth, 1);
  st[S.s] = 95; assert.equal(contact(st).waterDepth, 0, 'low crossing elsewhere in lap is dry');
  surface = 'stream'; onRoad = false; assert.equal(contact(st).waterSurface, false);
  onRoad = true; st[S.airborne] = 1; const count = probes;
  assert.equal(contact(st).waterSurface, false); assert.equal(probes, count, 'no ground probe while airborne');
  st[S.airborne] = 0; st[S.s] = 40; st[S.respawnPhase] = 1;
  assert.equal(contact(st).waterDepth, 0); assert.equal(contact(st).waterSurface, false);
  st[S.respawnPhase] = 0; assert.equal(contact(st, false).waterDepth, 0);
  st[S.velX] = -6; assert.equal(contact(st).waterSpeedPct, .2, 'reverse motion still stirs water');
});

test('real Mainframe coolant canal is wet across its curved grip banks; dry forks stay dry', () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8')));
  const contact = createWaterContact(track), canal = track.sections.find(s => s.id === 'coolant-canal');
  const s = (canal.fromS + canal.toS) / 2, st = createState(); st[S.grounded] = 1; st[S.s] = s;
  for (const x of [-14, 0, 14]) {
    const f = track.surfaceAt(s, x); st[S.seg] = f.seg;
    st[S.posX] = f.p[0]; st[S.posY] = f.p[1]; st[S.posZ] = f.p[2];
    assert.equal(f.surface, 'grip'); assert.equal(contact(st).waterSurface, true);
  }
  const dryFork = createWaterContact({ ...track, query: (...args) => Object.assign(args[4], { onRoad: true, surface: 'grip', s, forkId: 'dry-bridge' }) });
  assert.equal(dryFork(st).waterSurface, false);
});

test('wake and spray follow movement, steering and boost; dry road, hops and reset silence the layer', () => {
  const ctx = context(), out = {}, water = createWaterTravel(ctx, out);
  const flags = { waterSurface: true, onRoad: true };
  assert.equal(water.inspect.spray, 0);
  water.update(.2, flags); const slow = water.inspect;
  water.update(1, flags); const fast = water.inspect;
  assert.ok(fast.spray > slow.spray && fast.wake > slow.wake);
  water.update(1, { ...flags, waterSteer: 1, boosting: true }); assert.ok(water.inspect.spray > fast.spray);
  for (const change of [{ waterSurface: false }, { airborne: true }, { onRoad: false }, { paused: true }]) {
    water.update(1, { ...flags, ...change });
    assert.equal(water.inspect.spray + water.inspect.wake + water.inspect.bubbles, 0);
  }
  water.update(0, flags); assert.equal(water.inspect.wake, 0, 'no travelling wake at rest');
  water.update(0, { ...flags, waterSpeedPct: .2 }); assert.ok(water.inspect.wake > 0, 'physical reverse motion makes a wake');
  water.update(1, { ...flags, waterDepth: 1 });
  assert.ok(water.inspect.spray < fast.spray); assert.ok(water.inspect.bubbles > fast.bubbles);
  water.reset(); assert.equal(water.inspect.depth, 0); assert.equal(water.inspect.bubbles, 0);
  for (const invalid of [NaN, Infinity, -2]) {
    water.update(invalid, { ...flags, waterDepth: invalid, waterSteer: invalid });
    for (const k of ['spray', 'wake', 'bubbles', 'depth']) assert.ok(Number.isFinite(water.inspect[k]));
  }
  assert.equal(ctx.sources.length, 2); assert.ok(ctx.sources.every(n => n.started === 1));
  for (let i = 0; i < 100; i++) water.update(i / 100, flags);
  assert.equal(ctx.sources.length, 2, 'no per-frame voices');
  assert.equal(ctx.nodes.filter(n => n.links.includes(out)).length, 3, 'all layers use the supplied SFX bed');
  for (const channels of ctx.buffers) for (const data of channels) {
    assert.ok(data.some(v => Math.abs(v) > .01)); assert.ok(data.every(v => Number.isFinite(v) && Math.abs(v) <= 1));
    assert.ok(data[0] === 0 && data.at(-1) === 0);
  }
  water.dispose(); water.dispose(); water.update(1, flags);
  assert.equal(water.inspect.available, false); assert.equal(water.inspect.wake, 0);
  assert.ok(ctx.sources.every(n => n.stopped === 1)); assert.ok(ctx.nodes.every(n => !n.links.length));
});
