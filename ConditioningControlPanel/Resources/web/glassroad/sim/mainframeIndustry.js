// Physical machine rooms. Render and contact share these race-clock poses.
import { S, BOOST, RESPAWN } from './state.js';
import { applyBoost, boostHoldLeftS } from './boost.js';
import { clamp, wrap } from '../core/math.js';
import { yawParams, calmOf } from './steer.js';
import { machineryBeatAt, beatCycle, machineryRearming } from '../track/machineryBeat.js';
import { makeQuery } from '../track/query.js';

export const PRESS_BODY = Object.freeze({ height: 2.2, radius: .7, kartHeight: 1.6, repeatS: .8 });

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
  if (zone.kind === 'piston') {
    // Silent races use the same brisk 120 BPM grid on the shared race clock.
    beat ||= { beat: Math.max(0, timeS) * 2, beatS: .5, safe: false, rearming: false };
    const offset = Math.round(zone.offsetS / beat.beatS);
    // An initial offset must enter at clear, never skip straight to a closed press.
    const safe = machineryRearming(beat, offset, 4) || beat.beat < beatCycle(-offset,4);
    const t = beatCycle(beat.beat + offset, 4);
    const phase = safe ? 'warn' : t < .5 ? 'clear' : t < 1.7 ? 'warn' : t < 2 ? 'lower' : t < 2.25 ? 'hold' : t < 2.6 ? 'rise' : 'clear';
    const lift = phase === 'lower' ? 7 * (1 - ease((t - 1.7) / .3)) : phase === 'hold' ? 0 : phase === 'rise' ? 7 * ease((t - 2.25) / .35) : 7;
    return { phase, lift, active: lift < PRESS_BODY.kartHeight, warning: phase === 'warn', travel: t / 4,
      impactIndex: safe ? null : Math.floor((beat.beat + offset - 2) / 4) };
  }
  const t = ((Math.max(0, timeS) + zone.offsetS) % 9 + 9) % 9;
  return { phase: 'running', lift: 0, active: true, warning: false, travel: t / 9 };
}

function local(st, z) {
  const d = z.p.map((v,j) => st[S.posX+j] - v);
  return { x: d.reduce((a,v,j)=>a+v*z.b[j],0), along: d.reduce((a,v,j)=>a+v*z.t[j],0), h: d.reduce((a,v,j)=>a+v*z.n[j],0) };
}
/** The same solid head the renderer draws, with space for the kart's actual body. */
export function pressOverlap(z, pose, p) {
  return Math.abs(p.x-z.x) < z.w/2+PRESS_BODY.radius && Math.abs(p.along) < z.len/2+PRESS_BODY.radius
    && p.h >= -.6 && p.h + PRESS_BODY.kartHeight > pose.lift && p.h < pose.lift + PRESS_BODY.height;
}

export function createMainframeIndustry(track, events) {
  const zones = industryZones(track), memory = new Map(), yp = {}, query = makeQuery();
  let times = new WeakMap();
  const api = { zones, hits: 0, update, apply, steer, reset };
  function reset() { memory.clear(); times = new WeakMap(); api.hits = 0; }
  function pressHit(r,z,previous,timeS,t0,sink) {
    const st=r.st, distance=Math.hypot(...z.p.map((_,j)=>st[S.posX+j]-previous[S.posX+j]));
    if(distance>25) return false;
    if(Math.abs(wrap(previous[S.s]-z.s+track.lengthM/2,track.lengthM)-track.lengthM/2)>distance+z.len/2+3)return false;
    const a=local(previous,z), b=local(st,z), at=f=>({x:a.x+(b.x-a.x)*f,along:a.along+(b.along-a.along)*f,h:a.h+(b.h-a.h)*f});
    if(Math.min(Math.abs(a.along),Math.abs(b.along))>z.len/2+PRESS_BODY.radius+distance || Math.min(a.h,b.h)>10) return false;
    const solid=f=>pressOverlap(z,industryPose(z,t0+(timeS-t0)*f,machineryBeatAt(track,t0+(timeS-t0)*f)),at(f));
    const inside=solid(0);
    if(inside&&!solid(1))return false; // A caught kart may always escape the head.
    const steps=Math.min(128,Math.max(2,Math.ceil((distance+(timeS-t0)*45)/.2)));
    let fraction=inside?1:null;
    if(!inside)for(let i=1;i<=steps;i++)if(solid(i/steps)){
      let lo=(i-1)/steps,hi=i/steps;
      for(let n=0;n<9;n++){const mid=(lo+hi)/2;if(solid(mid))hi=mid;else lo=mid;}
      fraction=lo;break;
    }
    if(fraction===null)return false;
    const point=at(fraction), xDepth=z.w/2+PRESS_BODY.radius-Math.abs(point.x-z.x), alongDepth=z.len/2+PRESS_BODY.radius-Math.abs(point.along);
    const sideHit=xDepth<alongDepth, axis=sideHit?z.b:z.t, value=sideHit?point.x-z.x:point.along, sign=Math.sign(value)||-1;
    if(inside){
      const push=(sideHit?xDepth:alongDepth)+.06;
      for(let j=0;j<3;j++)st[S.posX+j]+=axis[j]*sign*push;
    }else{
      const f=Math.max(0,fraction-.06/Math.max(distance,.06));
      for(let j=0;j<3;j++)st[S.posX+j]=previous[S.posX+j]+(st[S.posX+j]-previous[S.posX+j])*f;
    }
    const speed=axis.reduce((v,n,j)=>v+st[S.velX+j]*n,0), inward=Math.min(0,speed*sign);
    for(let j=0;j<3;j++)st[S.velX+j]+=axis[j]*sign*(1.2-inward);
    st[S.engineV]=Math.min(Math.abs(st[S.engineV])*.15,5);
    st[S.stalled]=Math.max(st[S.stalled],.32);
    st[S.boostKind]=BOOST.NONE;st[S.boostAmount]=st[S.boostHoldS]=st[S.boostT]=0;
    st[S.driftDir]=st[S.driftCharge]=st[S.driftStage]=0;
    const q=track.query(st[S.posX],st[S.posY],st[S.posZ],st[S.seg]|0,query);
    for(const k of ['seg','segT','s','lateral','height'])st[S[k]]=q[k];
    const key=r.i+':'+z.id, fresh=timeS>=(memory.get(key)??-Infinity);
    if(fresh){memory.set(key,timeS+PRESS_BODY.repeatS);api.hits++;
      events?.emit('industry-hit',{racer:r.i,zone:z.id,kind:z.kind,s:z.s,x:z.x});
      sink?.emit('wall',{dv:Math.max(2,-inward),kind:'press'});
    }
    return true;
  }
  function update(racers, timeS, dt) {
    if(!(dt>0))return;
    for(const r of racers)apply(r,r.prev||r.st,timeS,r.events,dt);
  }
  function apply(r, previous, timeS, sink=r.events, dt=1/60) {
      if(!Number.isFinite(timeS))return;
      const old=times.get(r);
      if(old===timeS)return;
      times.set(r,timeS);
      if(old!==undefined&&(timeS<old||timeS-old>.5)){for(const z of zones)memory.delete(r.i+':'+z.id);return;}
      const st = r.st;
      if (r.finished || r.kind === 'remote' || st[S.respawnPhase] !== RESPAWN.NONE) return;
      const t0=old??Math.max(0,timeS-dt);
      for (const z of zones) {
        if(z.kind==='piston'){if(pressHit(r,z,previous,timeS,t0,sink))return z.id;continue;}
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
        if (z.kind === 'belt') {
          if (!(r.feel && st[S.boostAmount] >= .16 && boostHoldLeftS(st,r.feel) > 0))
            applyBoost(st, BOOST.PAD, 0, r.feel, r.events || events, { amount: .16, holdS: .65 });
        }
        api.hits++;
        events?.emit(z.kind === 'belt' ? 'industry-boost' : 'industry-hit', { racer:r.i,zone:z.id,kind:z.kind,s:z.s,x:z.x });
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
