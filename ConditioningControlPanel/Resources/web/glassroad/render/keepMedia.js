/* Close fortress wall panels and overhead faces. Their alternation is shared by host and renderer. */
import { getKeepLayout } from '../track/keep-layout.js';
import { axesFrom } from './mediaMath.js';

export function keepMediaLayout(track) {
  const course=getKeepLayout(track);if(!course)return null;
  const slots=[];
  const at=(id,t)=>{const p=course.landmarks.find(x=>x.id===id);return p.s0+(p.s1-p.s0)*t;};
  const wall=(id,s,side,frame='billboard',phone=true,raised=false)=>{
    const f=track.frameAt(s),normal=f.b.map(v=>-side*v);
    const hall=course.halls.some(h=>s>=h.s0&&s<=h.s1),h=raised?6:hall?9:4.5,y=raised?9:hall?10.5:3.25;
    slots.push({id,frame,s,kind:'landscape',w:raised?14:hall?18:12,h,phone,program:slots.length%2?3:0,
      C:f.p.map((v,i)=>v+f.b[i]*side*(f.w/2+.2)+f.n[i]*y),...axesFrom(normal,f.n)});
  };
  const walls=[['forecourt',.40],['outer-gate',.22],['outer-gate',.78],['outer-exit',.50],
    ['east-ascent',.35],['east-ascent',.72],['high-gate',.35],['high-gate',.76],
    ['north-tower',.55],['crown-walk',.50],['west-descent',.35],['west-descent',.75],
    ['west-tower',.45],['moat-return',.35],['moat-return',.72],['home',.45]];
  walls.forEach(([id,t],i)=>wall('keep-wall-'+i,at(id,t),i%2?1:-1,'billboard',i%4!==3));
  [.12,.26,.40,.54,.68,.82].forEach((t,i)=>wall('keep-spiral-'+i,at('tower-spiral',t),i%2?1:-1,'billboard',true,true));
  // Pairs line the descending gallery without narrowing its driving surface.
  for(let i=0;i<6;i++)for(const side of [-1,1])wall('keep-gallery-'+i+'-'+side,
    at('west-descent',.1+i*.16),side,'tunnel');
  for(const [i,[id,t]] of [['forecourt',.68],['outer-exit',.45],['high-gate',.15],['crown-walk',.45],
    ['west-approach',.4],['moat-return',.2],['moat-return',.8],['home',.65]].entries()){
    const s=at(id,t),f=track.frameAt(s);
    slots.push({id:'keep-overhead-'+i,frame:'halo',s,kind:'landscape',w:23,h:7,phone:true,program:i%2?3:0,
      C:f.p.map((v,k)=>v+f.n[k]*15),...axesFrom(f.t.map(v=>-v),f.n)});
  }
  return {track:'keep',slots};
}