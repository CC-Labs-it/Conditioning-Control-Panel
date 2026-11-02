/* Optional open branch roads. Geometry owns support; lap distance is a continuous map. */
import { createSpline } from './spline.js';
import { makeFrame } from './frames.js';
import { makeQuery, copyQuery } from './query.js';
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const length = a => Math.hypot(...a);
const unit = a => { const l = length(a) || 1; return a.map(v => v / l); };
const cross = (a, b) => [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]];
const dist = (a, b) => Math.hypot(...a.map((v, i) => v - b[i]));
const railFlags = (flags, left, right) => {
  if (!left && !right) return flags;
  const l = !left && (flags.includes('rail') || flags.includes('railL'));
  const r = !right && (flags.includes('rail') || flags.includes('railR'));
  return [...flags.filter(f => !['rail', 'railL', 'railR', 'open'].includes(f)), ...(l ? ['railL'] : []), ...(r ? ['railR'] : []), ...(!l && !r ? ['open'] : [])];
};

function buildFork(row, index, baseSeg, main) {
  const fromS = main.nodeS[row.from], toS = main.nodeS[row.to], first = main.frameAt(fromS), last = main.frameAt(toS);
  const endpointRoll = f => { const right = unit(cross(f.t, [0, 1, 0])), up = cross(right, f.t); return Math.atan2(dot(f.n, right), dot(f.n, up)); };
  const points = row.points.map((p, i) => ({ ...p, p: [...p.p], roll: i === 0 ? endpointRoll(first) : i === row.points.length - 1 ? endpointRoll(last) : (p.roll || 0) * Math.PI / 180,
    surface: p.surface || 'glass', flags: p.flags || ['rail'], flow: p.flow || [0, 0] }));
  // Reflect each neighboring control across its endpoint's normal plane. Equal
  // chord lengths make the centripetal tangent exactly match the main road.
  const n = points.length, da = points[1].p.map((v,i)=>v-points[0].p[i]), db = points[n-2].p.map((v,i)=>v-points[n-1].p[i]);
  const before = points[1].p.map((v,i)=>v-2*dot(da,first.t)*first.t[i]);
  const after = points[n-2].p.map((v,i)=>v-2*dot(db,last.t)*last.t[i]);
  // Only segments 1..n-1 are real. The closing and phantom spans are never queried or drawn.
  const sp = createSpline([before, ...points.map(p => p.p), after], Array(n + 2).fill(false));
  const startS = sp.nodeS[1], nodeS = Float64Array.from({ length: n }, (_, i) => sp.nodeS[i + 1] - startS), lengthM = nodeS[n - 1];
  if ((toS - fromS) / lengthM > 8) throw Error('fork ' + row.id + ' progress map exceeds 8 main metres per physical metre');
  const mouthM = Math.min(100, lengthM * .28), bounds = { min: [Infinity, Infinity, Infinity], max: [-Infinity, -Infinity, -Infinity] };
  const segmentBounds = [];
  for (let i = 0; i < n - 1; i++) {
    const min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
    for (let k = 0; k <= sp.SUB; k++) for (let j = 0; j < 3; j++) {
      const v = sp.samplePos[((i + 1) * sp.SUB + k) * 3 + j]; min[j] = Math.min(min[j], v); max[j] = Math.max(max[j], v);
    }
    const w = Math.max(points[i].w, points[i+1].w) / 2 + 3;
    for (let j = 0; j < 3; j++) { bounds.min[j] = Math.min(bounds.min[j], min[j] - w); bounds.max[j] = Math.max(bounds.max[j], max[j] + w); }
    segmentBounds.push({ min, max });
  }
  const qMain = makeQuery(), rawFrame = makeFrame();
  function segAt(s) { let lo = 0, hi = n - 2; while (lo < hi) { const m = (lo + hi + 1) >> 1; if (nodeS[m] <= s) lo = m; else hi = m - 1; } return lo; }
  const mainS = s => fromS + clamp(s / lengthM, 0, 1) * (toS - fromS);
  const localS = s => clamp((s - fromS) / (toS - fromS), 0, 1) * lengthM;
  function frameAt(s, out = makeFrame(), raw = false) {
    s = clamp(s, 0, lengthM); const seg = segAt(s), u = sp.uFromS(seg + 1, s + startS), a = points[seg], b = points[seg+1];
    sp.pos(seg + 1, u, out.p); sp.deriv(seg + 1, u, out.t); const tangent = unit(out.t), right = unit(cross(tangent, [0, 1, 0])), up = cross(right, tangent);
    const roll = a.roll + (b.roll - a.roll) * u, c = Math.cos(roll), sn = Math.sin(roll);
    for (let j = 0; j < 3; j++) { out.t[j] = tangent[j]; out.n[j] = up[j] * c + right[j] * sn; out.b[j] = right[j] * c - up[j] * sn; }
    out.w = a.w + (b.w - a.w) * u; out.roll = roll; out.seg = baseSeg + seg; out.segT = u;
    out.surface = s >= lengthM ? b.surface : a.surface; out.flags = a.flags;
    if (!out.flow) out.flow = [0, 0];
    for (let j = 0; j < 2; j++) out.flow[j] = out.surface === 'stream' ? a.flow[j] + (b.flow[j] - a.flow[j]) * u : 0;
    out.localS = s; out.s = mainS(s); out.forkId = row.id;
    if (!raw && (s < mouthM || s > lengthM - mouthM)) {
      const left = overlapsMain(out, -1), rightOpen = overlapsMain(out, 1); out.flags = railFlags(out.flags, left, rightOpen);
    }
    return out;
  }
  function overlapsMain(f, side) {
    const p = f.p.map((v, i) => v + f.b[i] * side * f.w / 2);
    const q = main.query(...p, side < 0 ? row.from : row.to, qMain);
    return q.onRoad && Math.abs(q.height) < .65;
  }
  function surfaceAt(s, x = 0, out = makeFrame()) { frameAt(s, out); for (let j = 0; j < 3; j++) out.p[j] += out.b[j] * x; return out; }
  function query(px, py, pz, hint = -1, out = makeQuery()) {
    const p = [px, py, pz]; let best = Infinity, bestSeg = 0, bestU = 0;
    for (let i = 0; i < n - 1; i++) {
      const bb = segmentBounds[i], lower = p.reduce((sum, v, j) => sum + Math.max(bb.min[j] - v, 0, v - bb.max[j]) ** 2, 0);
      if (lower > best) continue;
      for (let k = 0; k < sp.SUB; k++) {
        const o = ((i+1)*sp.SUB+k)*3, a = [sp.samplePos[o],sp.samplePos[o+1],sp.samplePos[o+2]];
        const d = [sp.samplePos[o+3]-a[0],sp.samplePos[o+4]-a[1],sp.samplePos[o+5]-a[2]], v = p.map((x,j)=>x-a[j]);
        const u = clamp(dot(v,d)/(dot(d,d)||1),0,1), dd = v.reduce((sum,x,j)=>sum+(x-d[j]*u)**2,0);
        if (dd < best) { best = dd; bestSeg = i; bestU = (k+u)/sp.SUB; }
      }
    }
    const c = [0,0,0], d = [0,0,0], e = [0,0,0]; let u = bestU;
    for (let k = 0; k < 5; k++) {
      sp.pos(bestSeg+1,u,c); sp.deriv(bestSeg+1,u,d); sp.deriv2(bestSeg+1,u,e); const v = p.map((x,j)=>x-c[j]);
      const den = dot(v,e)-dot(d,d); if (den >= -1e-9) break; const next = clamp(u-dot(v,d)/den,0,1); if (Math.abs(next-u)<1e-8) break; u=next;
    }
    const s = sp.sFromU(bestSeg+1,u)-startS, f = frameAt(s,rawFrame), v = p.map((x,j)=>x-f.p[j]);
    out.seg=f.seg; out.segT=f.segT; out.s=f.s; out.forkId=row.id; out.forkS=s; out.pathScale=(toS-fromS)/lengthM;
    out.lateral=dot(v,f.b); out.height=dot(v,f.n); out.edgeDist=f.w/2-Math.abs(out.lateral);
    const along=dot(v,f.t); out.beyond=(s<.01 && along<-.2)||(s>lengthM-.01 && along>.2);
    out.onRoad=!out.beyond && out.edgeDist>=0; out.surface=f.surface; out.flags=f.flags; out.hole=null;out.holeDist=Infinity;out.stone=null;
    out.railed=f.flags.includes('rail')||f.flags.includes(out.lateral>=0?'railR':'railL');
    if(!out.flow) out.flow=[0,0];
    for(let j=0;j<3;j++){out.normal[j]=f.n[j];out.p[j]=f.p[j]+f.b[j]*out.lateral;} out.flow[0]=f.flow[0];out.flow[1]=f.flow[1];
    return out;
  }
  return { id:row.id,label:row.label||row.id,kind:row.kind||'split',index,from:row.from,to:row.to,fromS,toS,lengthM,nodeS,points,baseSeg,segmentCount:n-1,mouthM,bounds,
    frameAt,surfaceAt,query,mainS,localS, samples(stepM=4){const count=Math.ceil(lengthM/stepM);return Array.from({length:count+1},(_,i)=>frameAt(lengthM*i/count));} };
}

export function installForks(track, rows) {
  if (!rows?.length) return;
  const main = { frameAt:track.frameAt,query:track.query,nodeS:track.nodeS,nodeCount:track.nodeCount };
  let offset=track.nodeCount;
  const forks=rows.map((row,i)=>{const fork=buildFork(row,i,offset,main);offset+=fork.segmentCount;return fork;});
  track.forks=forks;
  track.forkAtSeg=seg=>forks.find(f=>seg>=f.baseSeg && seg<f.baseSeg+f.segmentCount)||null;
  track.pathFrameAt=(s,seg,out)=>{const f=track.forkAtSeg(seg);return f?f.frameAt(f.localS(s),out):main.frameAt(s,out);};
  const mainNodeFlags=track.nodeFlags, mainNodeSurface=track.nodeSurface;
  track.nodeFlags=seg=>{const f=track.forkAtSeg(seg);return f?f.frameAt(f.nodeS[seg-f.baseSeg]).flags:mainNodeFlags(seg);};
  track.nodeSurface=seg=>{const f=track.forkAtSeg(seg);return f?f.points[seg-f.baseSeg].surface:mainNodeSurface(seg);};
  const portalQ=makeQuery(), portals=[];
  for(const f of forks)for(const end of [f.fromS,f.toS])for(const side of [-1,1]) {
    let begin=null;
    const lo=Math.max(0,end-f.mouthM-20),hi=Math.min(track.lengthM,end+f.mouthM+20);
    for(let s=lo;s<=hi+2;s+=2){let open=false;
      if(s<=hi){const frame=main.frameAt(s),p=frame.p.map((v,j)=>v+frame.b[j]*side*frame.w/2);const q=f.query(...p,-1,portalQ);
        open=q.onRoad && Math.abs(q.height)<.65 && (q.forkS<f.mouthM||q.forkS>f.lengthM-f.mouthM);}
      if(open&&begin===null)begin=s-1;
      if(!open&&begin!==null){portals.push({id:f.id,side,s0:begin,s1:s-1});begin=null;}
    }
  }
  track.forkPortals=portals;
  track.mainRailOpen=(s,side)=>portals.some(p=>p.side===side&&s>=p.s0&&s<=p.s1);
  const mainResult=makeQuery(), candidate=makeQuery(), under=makeQuery();
  const inBounds=(f,p,extra=0)=>p.every((v,j)=>v>=f.bounds.min[j]-extra&&v<=f.bounds.max[j]+extra);
  function markMain(q){q.forkId=null;q.forkS=0;q.pathScale=1;q.beyond=false;if(track.mainRailOpen(q.s,q.lateral>=0?1:-1))q.railed=false;return q;}
  function query(px,py,pz,hint=-1,out=makeQuery()) {
    const held=track.forkAtSeg(hint),p=[px,py,pz];
    if(held){held.query(px,py,pz,hint,out);
      const atMouth=out.forkS<held.mouthM||out.forkS>held.lengthM-held.mouthM;
      if(atMouth){
        markMain(main.query(px,py,pz,out.forkS<held.mouthM?held.from:held.to,mainResult));
        if(mainResult.onRoad&&Math.abs(mainResult.height)<2 && (out.beyond||!out.onRoad||Math.abs(mainResult.height)+.15<Math.abs(out.height)))copyQuery(out,mainResult);
      }
      return out;
    }
    markMain(main.query(px,py,pz,hint,mainResult));copyQuery(out,mainResult);
    let best=out.onRoad?Math.abs(out.height)+Math.abs(out.lateral)*.015:Infinity;
    for(const f of forks){if(!inBounds(f,p,3))continue;f.query(px,py,pz,-1,candidate);
      if(!candidate.onRoad||Math.abs(candidate.height)>3)continue;
      if(hint>=0 && !(candidate.forkS<f.mouthM && Math.abs(mainResult.s-f.fromS)<f.mouthM+25))continue;
      const score=Math.abs(candidate.height)+Math.abs(candidate.lateral)*.015;
      if(score+.015<best){copyQuery(out,candidate);best=score;}
    }
    return out;
  }
  query.below=(px,py,pz,out=makeQuery())=>{
    const m=main.query.below(px,py,pz,out);if(m)markMain(out);let found=!!m,best=found?out.height:Infinity;
    for(const f of forks){if(px<f.bounds.min[0]||px>f.bounds.max[0]||pz<f.bounds.min[2]||pz>f.bounds.max[2])continue;
      f.query(px,py,pz,-1,under);if(!under.onRoad||under.normal[1]<.5||under.height< -1.5||under.height>=best)continue;
      best=under.height;copyQuery(out,under);found=true;
    }return found?out:null;
  };
  track.query=query;
}
