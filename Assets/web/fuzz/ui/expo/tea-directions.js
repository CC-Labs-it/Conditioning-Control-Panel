import {teaPath} from './trail-fittings.js';
import {doorways} from './doorways.js';

const routeOrder=['service','atrium','hall'];
const route=routeOrder.flatMap(id=>teaPath[id].map(at=>({id,at})));
const lengths=route.map((_,i)=>i+1<route.length?distance(route[i].at,route[i+1].at):0);
const remaining=new Array(route.length).fill(0);
for(let i=route.length-2;i>=0;i--)remaining[i]=remaining[i+1]+lengths[i];
function distance(a,b){return Math.hypot(a[0]-b[0],a[1]-b[1],a[2]-b[2]);}

// Point along the actual wool, including the turn at the foot of the stairs.
export function teaDirections(room,p){
  const position=[p.x,p.y,p.z],targetRoom=routeOrder.includes(room)?room:null;
  if(!targetRoom){
    const next={secret:'service',catwalk:'atrium',chronology:'atrium',forum:'chronology',
      commons:'aquarium',aquarium:'atrium',hoof:'hall',shop:'hall',dome:'hall',habitat:'hall',twelve:'hall'}[room];
    const door=doorways.find(d=>d.rooms.includes(room)&&d.rooms.includes(next));
    if(door)return {target:[door.x,door.z],metres:Math.round(distance(position,[door.x,door.y,door.z])+teaDirections(next,{x:door.x,y:door.y,z:door.z}).metres)};
    return {target:[11,11.95],metres:Math.round(distance(position,[11,3.4,11.95]))};
  }
  let best=Infinity,closest=0,index=0,fraction=0;
  for(let i=0;i<route.length-1;i++){
    const a=route[i],b=route[i+1],len=lengths[i];
    if(a.id!==targetRoom||!len)continue;
    const dx=b.at[0]-a.at[0],dy=b.at[1]-a.at[1],dz=b.at[2]-a.at[2];
    const t=Math.max(0,Math.min(1,((p.x-a.at[0])*dx+(p.y-a.at[1])*dy+(p.z-a.at[2])*dz)/(len*len)));
    const x=p.x-a.at[0]-dx*t,y=p.y-a.at[1]-dy*t,z=p.z-a.at[2]-dz*t;
    const score=x*x+z*z+9*y*y;
    if(score<best){best=score;closest=Math.hypot(x,y,z);index=i;fraction=t;}
  }
  let ahead=index+1;
  while(ahead+1<route.length&&distance(position,route[ahead].at)<1.4)ahead++;
  const at=route[ahead].at;
  return {target:[at[0],at[2]],metres:Math.round(closest+remaining[index]-fraction*lengths[index])};
}
