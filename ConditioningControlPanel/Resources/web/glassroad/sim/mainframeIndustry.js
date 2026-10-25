// Physical machine rooms. Render and contact share these race-clock poses.
import { S, BOOST, RESPAWN } from './state.js';
import { applyBoost, boostHoldLeftS } from './boost.js';
import { clamp } from '../core/math.js';
import { yawParams, calmOf } from './steer.js';
import { machineryBeatAt, beatCycle, machineryRearming } from '../track/machineryBeat.js';

export function industryZones(track) {
  if (track.id !== 'mainframe') return [];
  const zones = [], section = id => track.sections.find(s => s.id === id);
  const add = (kind, id, s, side, len, offsetS = 0) => {
    const f = track.frameAt(s), w = Math.min(7, f.w / 2 - 7);
    if (w < 2) return;
    zones.push({ id, kind, s, len, w, x: side * (6 + w / 2), offsetS,
      p: [...f.p], b: [...f.b], n: [...f.n], t: [...f.t] });
  };
  for (const name of ['core-approach', 'upper-bus']) {
    const sec = section(name); if (!sec) continue;
    for (let i = 0; i < 4; i++) add('piston', name + '-press-' + i,
      sec.fromS + 48 + i * 29, i % 2 ? -1 : 1, 7, i * 1.6);
  }
  const bridge = section('skybridge');
  if (bridge) for (let i = 0; i < 3; i++) add('belt', 'accelerator-' + i,
    bridge.fromS + 35 + i * 33, i % 2 ? -1 : 1, 22, i);
  const lower = section('lower-bus');
  if (lower) for (let i = 0; i < 4; i++) add('tar', 'latex-spill-' + i,
    lower.fromS + 275 + i * 32, i % 2 ? -1 : 1, 22, i);
  return zones;
}
const ease = v => { v = clamp(v, 0, 1); return v * v * (3 - 2 * v); };
export function industryPose(zone, timeS, beat = null) {
  if (beat && zone.kind === 'piston') {
    const offset = Math.round(zone.offsetS / beat.beatS), safe = machineryRearming(beat, offset);
    const t = beatCycle(beat.beat + offset, 16);
    const phase = safe ? 'warn' : t < 4 ? 'clear' : t < 7 ? 'warn' : t < 8 ? 'lower' : t < 10 ? 'hold' : t < 13 ? 'rise' : 'clear';
    const lift = phase === 'lower' ? 7 * (1 - ease(t - 7)) : phase === 'hold' ? 0 : phase === 'rise' ? 7 * ease((t - 10) / 3) : 7;
    return { phase, lift, active: lift < 1.6, warning: phase === 'warn', travel: t / 16,
      impactIndex: safe ? null : Math.floor((beat.beat + offset - 8) / 16) };
  }
  const t = ((Math.max(0, timeS) + zone.offsetS) % 9 + 9) % 9;
  if (zone.kind !== 'piston') return { phase: 'running', lift: 0, active: true, warning: false, travel: t / 9 };
  const phase = t < 2.4 ? 'clear' : t < 4.2 ? 'warn' : t < 5.2 ? 'lower' : t < 6.4 ? 'hold' : t < 7.8 ? 'rise' : 'clear';
  const lift = phase === 'lower' ? 7 * (1 - ease((t - 4.2)))
    : phase === 'hold' ? 0 : phase === 'rise' ? 7 * ease((t - 6.4) / 1.4) : 7;
  return { phase, lift, active: lift < 1.6, warning: phase === 'warn', travel: t / 9 };
}

export function createMainframeIndustry(track, events) {
  const zones = industryZones(track), memory = new Map(), yp = {};
  let lastT = null;
  const api = { zones, hits: 0, update, steer, reset };
  function reset() { memory.clear(); lastT = null; api.hits = 0; }
  function update(racers, timeS, dt) {
    if (!Number.isFinite(timeS) || !(dt > 0) || lastT === timeS) return;
    if (lastT !== null && (timeS < lastT || timeS - lastT > .5)) { memory.clear(); lastT = timeS; return; }
    lastT = timeS;
    for (const r of racers) {
      const st = r.st;
      if (r.finished || r.kind === 'remote' || st[S.respawnPhase] !== RESPAWN.NONE) continue;
      for (const z of zones) {
        // World-space height rejects a different road deck at an XZ crossing.
        const d = [st[S.posX] - z.p[0], st[S.posY] - z.p[1], st[S.posZ] - z.p[2]];
        const x = d.reduce((a,v,j) => a + v*z.b[j],0), along = d.reduce((a,v,j) => a + v*z.t[j],0);
        const h = d.reduce((a,v,j) => a + v*z.n[j],0), key = r.i + ':' + z.id;
        const near = Math.abs(st[S.s] - z.s) < z.len / 2 + 2 && Math.abs(along) < z.len / 2 + .6
          && Math.abs(x - z.x) < z.w / 2 + .6 && h >= -.4 && h < (z.kind === 'piston' ? 1.6 : .55);
        if (!near) { memory.delete(key); continue; }
        if (!industryPose(z,timeS,machineryBeatAt(track,timeS)).active) continue;
        if (z.kind === 'tar') {
          // Sticky drag is bounded and hop-clearable; it never locks steering or stops a kart.
          const v = st[S.engineV];
          if (v > 17) st[S.engineV] = Math.max(17, v - 15 * Math.min(dt,.05));
        }
        if (memory.has(key)) continue;
        memory.set(key, timeS);
        if (z.kind === 'piston') {
          st[S.stalled] = Math.max(st[S.stalled], .32);
          st[S.engineV] *= .82;
        } else if (z.kind === 'belt') {
          if (!(r.feel && st[S.boostAmount] >= .16 && boostHoldLeftS(st,r.feel) > 0))
            applyBoost(st, BOOST.PAD, 0, r.feel, r.events || events, { amount: .16, holdS: .65 });
        }
        api.hits++;
        events?.emit(z.kind === 'belt' ? 'industry-boost' : 'industry-hit', { racer:r.i,zone:z.id,kind:z.kind,s:z.s,x:z.x });
      }
    }
  }
  function steer(r, actions) {
    if (!zones.length || r.st[S.airborne] || r.st[S.respawnPhase] !== RESPAWN.NONE) return false;
    const st = r.st, s = st[S.s];
    const z = zones.find(z => z.kind !== 'belt' && s > z.s - 65 && s < z.s + z.len / 2 + 12);
    if (!z) return false;
    const speed = Math.hypot(st[S.velX],st[S.velY],st[S.velZ]), ahead = track.frameAt(s + clamp(speed*.65,12,20));
    const d = ahead.p.map((v,j) => v - st[S.posX+j]);
    const qx=st[S.quatX],qy=st[S.quatY],qz=st[S.quatZ],qw=st[S.quatW];
    const right=[1-2*(qy*qy+qz*qz),2*(qx*qy+qw*qz),2*(qx*qz-qw*qy)];
    const forward=[-2*(qx*qz+qw*qy),-2*(qy*qz-qw*qx),-(1-2*(qx*qx+qy*qy))];
    const x=d.reduce((a,v,j)=>a+v*right[j],0), f=d.reduce((a,v,j)=>a+v*forward[j],0);
    yawParams(r.feel,yp);
    actions.steer=clamp(Math.atan2(x,Math.max(f,.1))*r.feel.get('ai.steerGain')/Math.max(.2,calmOf(speed,yp)*yp.user),-1,1);
    actions.hop=false; actions.hopPressed=false;
    const cap = Math.abs(st[S.lateral]) > 3 ? 22 : 28;
    actions.throttle=speed<cap?1:0; actions.brake=speed>cap+3?.5:0;
    return true;
  }
  return api;
}
