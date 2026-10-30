import { test } from 'node:test';
import assert from 'node:assert/strict';
import { buildMainframe } from '../dev/gen-mainframe.mjs';
import { loadTrack } from '../track/load.js';
import { createActions } from '../core/actions.js';
import { qfromBasis } from '../core/math.js';
import { S, createState, setPos, setQuat, setVel } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { createProgress } from '../sim/progress.js';
import { registeredSkip } from '../sim/routes.js';
import { makeFeel, DT } from './sim-fake.mjs';

const track = loadTrack(buildMainframe().track);
function cut(route, v, yaw, { followS = 0, brakeOnLand = false } = {}) {
  const st = createState(), s = route.openS0 + 3, f = track.frameAt(s);
  const side = route.side === 'L' ? -1 : 1, lat = side * (f.w / 2 - 4);
  const c = Math.cos(yaw * Math.PI / 180), sn = Math.sin(yaw * Math.PI / 180) * side;
  const t = f.t.map((n, k) => n * c + f.b[k] * sn), b = f.b.map((n, k) => n * c - f.t[k] * sn);
  setPos(st, f.p.map((n, k) => n + f.b[k] * lat));
  setQuat(st, qfromBasis([0, 0, 0, 1], b, f.n, t.map(n => -n))); setVel(st, t.map(n => n * v));
  st[S.engineV] = v; st[S.grounded] = 1; st[S.seg] = st[S.latchSeg] = f.seg; st[S.s] = s; st[S.lateral] = lat; st[S.hopT] = 1e3;
  const events = [], rollout = [], sink = { emit(name, data) { events.push({ name, ...data }); } };
  let landedAt = -1;
  const feel = makeFeel(), actions = createActions(), progress = createProgress(track, sink), race = { phase: 'racing', timeS: 0 };
  progress.reset(st); st[S.checkpoint] = track.checklines.filter(c => c.s <= s).length - 1;
  for (let i = 0; i < 6 / DT; i++) {
    const braking = brakeOnLand && landedAt >= 0;
    actions.throttle = braking ? 0 : 1; actions.brake = braking ? 1 : 0;
    step(st, actions, track, feel, DT, sink); updateRespawn(st, track, feel, DT, sink);
    race.timeS += DT; progress.update(st, race);
    if (landedAt < 0 && events.some(e => e.name === 'route-land')) landedAt = i;
    if (landedAt >= 0) {
      const f = track.frameAt(st[S.s]);
      rollout.push({ engine: st[S.engineV], grounded: st[S.grounded],
        forward: [st[S.velX], st[S.velY], st[S.velZ]].reduce((sum, v, k) => sum + v * f.t[k], 0) });
    }
    if (events.some(e => e.name === 'drop') || (followS ? landedAt >= 0 && (i - landedAt) * DT >= followS : events.some(e => e.name === 'route-skip'))) break;
  }
  return { events, st, rollout };
}

test('Mainframe descents preserve momentum through landing and the next second', () => {
  for (const route of track.routes) for (const [v, yaw] of [[24, 25], [30, 30], [34, 25]]) {
    const { events, rollout } = cut(route, v, yaw, { followS: 1 });
    assert.ok(events.some(e => e.name === 'route-skip'));
    assert.ok(!events.some(e => e.name === 'drop' || e.name === 'wall'));
    assert.ok(rollout.length >= 60);
    for (const r of rollout) {
      assert.equal(r.grounded, 1, 'the lower road immediately restores grip');
      assert.ok(r.engine >= v * .8, `${route.id}: engine ${r.engine} after ${v} m/s approach`);
      assert.ok(r.forward >= v * .8, `${route.id}: forward ${r.forward} after ${v} m/s approach`);
    }
  }
});

test('Mainframe descent rollout still obeys the player brake', () => {
  for (const route of track.routes) {
    const { events, rollout } = cut(route, 30, 30, { followS: 1, brakeOnLand: true });
    assert.ok(events.some(e => e.name === 'route-skip'));
    assert.ok(!events.some(e => e.name === 'drop'));
    assert.ok(rollout.at(-1).engine < rollout[0].engine * .5);
    assert.ok(rollout.at(-1).forward < rollout[0].forward * .5);
  }
});

test('Mainframe optional descent cuts physically land and credit the skipped turn', () => {
  assert.equal(track.routes.length, 2);
  for (const route of track.routes) for (const [v, yaw] of [[24, 25], [30, 30], [34, 25]]) {
    const { events, st } = cut(route, v, yaw);
    const landed = events.find(e => e.name === 'route-land');
    assert.ok(landed, JSON.stringify({ id: route.id, v, yaw, events: events.filter(e => /route|drop|fall|wall/.test(e.name)), s: st[S.s] }));
    assert.ok(events.some(e => e.name === 'route-skip'));
    assert.ok(!events.some(e => e.name === 'drop'));
    assert.ok(landed.skipM >= route.land.fromS - route.openS1 - 1
      && landed.skipM <= route.land.toS - route.openS0 + 1, 'skip remains inside the physically landed route range');
    assert.equal(registeredSkip(track, route.openS0, landed.s)?.id, route.id);
  }
});

test('Mainframe crossings have real vertical clearance for the full road width', () => {
  const frames = Array.from({ length: Math.floor(track.lengthM / 8) }, (_, i) => ({ s: i * 8, f: track.frameAt(i * 8) }));
  const footprint = f => [[-1,-1],[-1,1],[1,1],[1,-1]].map(([side,along])=>[f.p[0]+f.b[0]*side*f.w/2+f.t[0]*along*4,f.p[2]+f.b[2]*side*f.w/2+f.t[2]*along*4]);
  const intersects = (a,b) => {
    for (const poly of [a,b]) for(let i=0;i<4;i++) {
      const p=poly[i],q=poly[(i+1)%4],axis=[p[1]-q[1],q[0]-p[0]];
      const A=a.map(v=>v[0]*axis[0]+v[1]*axis[1]),B=b.map(v=>v[0]*axis[0]+v[1]*axis[1]);
      if(Math.max(...A)<Math.min(...B)||Math.max(...B)<Math.min(...A))return false;
    }
    return true;
  };
  const clashes = [], crossings = [];
  for (let i = 0; i < frames.length; i++) for (let j = i + 20; j < frames.length; j++) {
    const a = frames[i], b = frames[j];
    if (track.lengthM - b.s + a.s < 160) continue;
    if (Math.hypot(a.f.p[0] - b.f.p[0], a.f.p[2] - b.f.p[2]) > (a.f.w + b.f.w) / 2) continue;
    // Broad straight ribbons have overlapping bounding circles but distinct along-road footprints.
    if (!intersects(footprint(a.f),footprint(b.f))) continue;
    const height = Math.abs(a.f.p[1] - b.f.p[1]);
    const bank = Math.abs(a.f.b[1]) * a.f.w / 2 + Math.abs(b.f.b[1]) * b.f.w / 2;
    if (height - bank < 9) clashes.push([a.s, b.s, +(height - bank).toFixed(1)]);
    crossings.push(height);
  }
  assert.deepEqual(clashes.slice(0, 15), []);
  assert.ok(crossings.length > 40 && Math.max(...crossings) > 60);
});
