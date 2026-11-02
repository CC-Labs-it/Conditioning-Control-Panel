import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { shapeThresholdNodes } from '../track/shapeThreshold.js';
import { loadTrack } from '../track/load.js';
import { createFeel } from '../core/feel.js';
import { bendSpeed } from '../sim/steer.js';
import { edgeAt } from '../sim/routes.js';
const source = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const shaped = structuredClone(source); shaped.routes = shapeThresholdNodes(shaped.nodes, shaped).routes;
const track = loadTrack(shaped, { moving: true });
const degrees = 180 / Math.PI;
const angle = (a,b) => Math.atan2(a[0]*b[2]-a[2]*b[0], a[0]*b[0]+a[2]*b[2]);

test('Threshold broadens its plan while retaining every authored flight, field and shortcut anchor', () => {
  assert.equal(shaped.nodes.length, source.nodes.length);
  source.nodes.forEach((n,i) => {
    assert.equal(shaped.nodes[i].p[1], n.p[1], 'height node ' + i);
    assert.deepEqual(shaped.nodes[i].flags, n.flags, 'flags node ' + i);
    assert.equal(shaped.nodes[i].w, n.w, 'width node ' + i);
  });
  for (const key of ['fields','jumps','checkpoints','boosts','items']) assert.deepEqual(shaped[key], source[key]);
  assert.deepEqual(shaped.routes.map(({open,...r}) => r), source.routes);
  assert.equal(track.routes[0].openS1-track.routes[0].openS0, 24);
  assert.ok(!source.routes[0].open, 'source anchors remain unchanged');
  assert.ok(track.lengthM > 8000 && track.lengthM < 8600, 'reasonable broad-course length ' + track.lengthM);
  const again = structuredClone(shaped.nodes); shapeThresholdNodes(again); assert.deepEqual(again, shaped.nodes);
});

test('Threshold has no short horizontal hairpins and retains normal-speed steering reach', () => {
  const feel = createFeel(JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8')));
  let maxTurn = 0, minSpeed = Infinity;
  for (let s = 0; s < track.lengthM; s += 2) {
    const a = track.frameAt(s - 2), b = track.frameAt(s + 2);
    if ([a,b].some(f => f.flags.includes('magnet') || f.flags.includes('jump') || Math.abs(f.t[1]) > .6)) continue;
    minSpeed = Math.min(minSpeed, bendSpeed(Math.abs(angle(a.t,b.t)) * degrees / 4, feel));
    let turn = 0, valid = true;
    for (let d = 0; d < 60; d += 2) {
      const p = track.frameAt(s+d), q = track.frameAt(s+d+2);
      if ([p,q].some(f => f.flags.includes('magnet') || f.flags.includes('jump') || Math.abs(f.t[1]) > .6)) { valid = false; break; }
      turn += angle(p.t,q.t);
    }
    if (valid) maxTurn = Math.max(maxTurn, Math.abs(turn) * degrees);
  }
  assert.ok(maxTurn < 45, 'worst 60m heading change ' + maxTurn);
  assert.ok(minSpeed > 30, 'full-lock bend speed ' + minSpeed);
});

test('Threshold keeps its vertical loops and nested dare landing physically separated', () => {
  for (const [from,to] of [[395,405],[410,419]]) {
    const a = track.frameAt(track.nodeS[from]), b = track.frameAt(track.nodeS[to]);
    assert.ok(Math.abs(b.p[1]-a.p[1]) < .1);
    assert.ok(Math.hypot(b.p[0]-a.p[0],b.p[2]-a.p[2]) > 18);
  }
  const route = track.routes.find(r => r.id === 'dare'); assert.ok(route);
  for (const s of [route.openS0+2, (route.openS0+route.openS1)/2, route.openS1-2]) {
    const f = track.frameAt(s), outer = [f.p[0]+f.b[0]*(f.w/2+8)*(route.side==='L'?-1:1),f.p[1]-4,f.p[2]+f.b[2]*(f.w/2+8)*(route.side==='L'?-1:1)];
    const q = track.query.below(...outer);
    assert.ok(q && q.s > route.land.fromS-20 && q.s < route.land.toS+20, 'dare has its lower outside deck');
  }
});

test('Threshold rail queries match the visible 24m kicker and its restored guarded tail', () => {
  const r = track.routes[0], side = r.side === 'L' ? -1 : 1;
  for (const [s,railed] of [[r.openS0-3,true],[(r.openS0+r.openS1)/2,false],[r.openS1+2,true]]) {
    const f = track.frameAt(s), x = side*6, p = f.p.map((v,k) => v+f.b[k]*x), q = track.query(...p,f.seg);
    assert.equal(q.railed, railed, 'physical rail at ' + (s-r.openS0));
    if (s >= r.fromS && s < r.toS) assert.equal(edgeAt(track,q,x), railed ? 'rail' : 'kicker');
  }
});

test('Threshold sidecars retain their original physical node districts after the road broadens', () => {
  const original=loadTrack(source,{courseShape:false,boostSafety:false});
  const runtime=loadTrack(source,{moving:true});
  assert.ok(Math.abs(runtime.courseShape.sourceLengthM-original.lengthM)<1e-7);
  for (const node of [0,26,83,164,227,263,375,422,456,485,513]) {
    assert.ok(Math.abs(runtime.courseShape.mapS(original.nodeS[node],runtime)-runtime.nodeS[node])<1e-6,
      'media, colossi and gates retain district node '+node);
  }
  assert.deepEqual(source,JSON.parse(readFileSync(new URL('../tracks/threshold.json',import.meta.url),'utf8')));
});
