import {doorways} from './doorways.js';

const office=['twelve','hall','atrium','service'];
export const railRoutes=[
  {key:'print-',rooms:['chronology'],back:'Return the print to the desk'},
  {key:'tea',rooms:['service','atrium','hall'],back:'Return the tea to the canteen'},
  {key:'stapler',rooms:office,back:'Return Greg’s stapler'},
  {key:'signed-form',rooms:['twelve','hall','shop'],back:'Return the signed form to Lady Lashes'},
  {key:'souvenir:',rooms:['shop'],back:'Return carried item to its bay'},
  {key:'staff-sign-',rooms:office,back:'Return the notice to its stand'}
];
const teaBranches=new Set(['staff-upper','staff-store','server-cupboard','maintenance-stairs','archive-passage']);
const keeper={id:'keeper-passage',rooms:['service','habitat'],x:16,y:0,z:32.92,width:1.3,axis:'x'};
export function routeFor(key){
  return railRoutes.find(r=>r.key.endsWith('-')||r.key.endsWith(':')?key?.startsWith(r.key):r.key===key);
}
export function routeGates(route){
  return [...doorways,keeper].filter(d=>d.rooms.length===2&&d.rooms.filter(r=>route.rooms.includes(r)).length===1||
    route.key==='tea'&&teaBranches.has(d.id));
}
export const archiveGalleries=new Set(['atrium','chronology','forum','commons','aquarium','service','hall','hoof','habitat','shop','twelve']);
export const exemptRecord=id=>id==='ex_press'||id==='log_lashes';
export function railSegment(ax,az,bx,bz,y){return {ax,az,bx,bz,y};}
export function threshold(d){
  const half=(d.roughWidth||d.width)/2+.08;
  return d.axis==='x'?railSegment(d.x-half,d.z,d.x+half,d.z,d.y):railSegment(d.x,d.z-half,d.x,d.z+half,d.y);
}
export function penSegments(feet,station){
  if(station&&Math.hypot(feet.x-station.x,feet.z-station.z)<.7)station=null;
  const result=[],segments=station?8:16;
  const cx=station?.x??feet.x,cz=station?.z??feet.z,dir=Math.atan2(feet.z-cz,feet.x-cx);
  const radius=station?Math.max(1.35,Math.hypot(feet.x-cx,feet.z-cz)+.81):1.05;
  const spread=station?Math.PI*.48:Math.PI;
  const arc=Array.from({length:segments+1},(_,i)=>{
    const angle=dir-spread+2*spread*i/segments;
    return [cx+radius*Math.cos(angle),cz+radius*Math.sin(angle)];
  });
  if(station){
    const back=[cx-.65*Math.cos(dir),cz-.65*Math.sin(dir)];
    arc.unshift(back);arc.push(back);
  }
  for(let i=1;i<arc.length;i++)result.push(railSegment(...arc[i-1],...arc[i],feet.y));
  return result;
}
export function distanceToBar(s,p){
  const dx=s.bx-s.ax,dz=s.bz-s.az;
  const t=Math.max(0,Math.min(1,((p.x-s.ax)*dx+(p.z-s.az)*dz)/(dx*dx+dz*dz)));
  return Math.hypot(p.x-s.ax-t*dx,p.z-s.az-t*dz);
}
