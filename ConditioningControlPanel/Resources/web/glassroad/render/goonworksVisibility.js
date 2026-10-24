/* Offline sightline audit using driver-height rays and the same solid meshes as the scene. */
import {goonworksSegmentBlocked} from './goonworksWorld.js';
import {goonworksTubeShell} from './goonworksGeometry.js';
import {V3} from './mediaMath.js';
const {add,sub,mul,dot,cross}=V3,CACHE=new WeakMap(),CELL=32;
const xyz=(P,i)=>Array.from(P.subarray(i*3,i*3+3));

function meshOccluders(track){
  if(CACHE.has(track))return CACHE.get(track);
  const grid=new Map(),triangles=[];
  const sources=[{name:'road',mesh:track.meshes.road},...[false,true].map(outer=>({name:'rabbit-shell',mesh:goonworksTubeShell(track,outer)}))];
  for(const {name,mesh}of sources){if(!mesh)continue;const P=mesh.positions,I=mesh.indices;
    for(let i=0;i<I.length;i+=3){
      const ids=[I[i],I[i+1],I[i+2]],a=xyz(P,ids[0]),b=xyz(P,ids[1]),c=xyz(P,ids[2]);
      const lo=a.map((v,k)=>Math.floor(Math.min(v,b[k],c[k])/CELL)),hi=a.map((v,k)=>Math.floor(Math.max(v,b[k],c[k])/CELL));
      const id=triangles.length;triangles.push({name,mesh,ids,a,e1:sub(b,a),e2:sub(c,a)});
      for(let x=lo[0];x<=hi[0];x++)for(let y=lo[1];y<=hi[1];y++)for(let z=lo[2];z<=hi[2];z++){
        const key=x+':'+y+':'+z;if(!grid.has(key))grid.set(key,[]);grid.get(key).push(id);
      }
    }
  }
  const result={grid,triangles};CACHE.set(track,result);return result;
}

function segmentMeshBlocker(from,to,index){
  const D=sub(to,from),cell=from.map(v=>Math.floor(v/CELL)),step=D.map(v=>Math.sign(v));
  const next=D.map((v,k)=>v?((cell[k]+(v>0?1:0))*CELL-from[k])/v:Infinity),delta=D.map(v=>v?CELL/Math.abs(v):Infinity),seen=new Set();
  let distance=0;
  while(distance<=1){
    for(const id of index.grid.get(cell.join(':'))||[]){if(seen.has(id))continue;seen.add(id);
      const tri=index.triangles[id],p=cross(D,tri.e2),det=dot(tri.e1,p);if(Math.abs(det)<1e-10)continue;
      const offset=sub(from,tri.a),u=dot(offset,p)/det;if(u<0||u>1)continue;
      const q=cross(offset,tri.e1),v=dot(D,q)/det;if(v<0||u+v>1)continue;
      const t=dot(tri.e2,q)/det;if(t<=.002||t>=.998)continue;
      if(tri.name==='rabbit-shell'){
        const U=tri.mesh.uvs,weights=[1-u-v,u,v],uv=[0,1].map(k=>tri.ids.reduce((n,j,i)=>n+U[j*2+k]*weights[i],0));
        const along=Math.abs(((uv[1]/76)%1+1)%1-.5)*76,angular=Math.min(Math.abs(Math.sin(uv[0]-.04)),Math.abs(Math.sin(uv[0]+.04)));
        if((along/24)**2+(angular/.6)**2<.98)continue;
      }
      return tri.name;
    }
    const axis=next[0]<=next[1]&&next[0]<=next[2]?0:next[1]<=next[2]?1:2;
    distance=next[axis];cell[axis]+=step[axis];next[axis]+=delta[axis];
  }
  return null;
}

/** Conservative boxes cover solid room dressing. Hollow ring centers are intentionally not filled. */
export function goonworksScreenVisibility(track,plan){
  const solids=plan.rows.filter(r=>!['torus','arch','star'].includes(r.shape)&&!r.id.startsWith('horizon-')&&!r.id.startsWith('white-cartoon'));
  const index=meshOccluders(track),report=[];
  for(const mount of plan.mounts){let total=0,visible=0;const blockers=new Map(),approaches=[];
    for(const distance of[16,32,56,84]){const f=track.frameAt(mount.s-distance);let clear=0;
      for(const lane of[-.22,0,.22]){
        const surface=track.surfaceAt?.(mount.s-distance,f.w*lane);
        const from=surface?add(surface.p,mul(surface.n,3.2)):add(add(f.p,mul(f.n,3.2)),mul(f.b,f.w*lane));
        for(const[x,y]of[[0,0],[-.3,0],[.3,0],[0,-.3],[0,.3]]){
          const to=add(add(mount.C,mul(mount.X,mount.w*x)),mul(mount.Y,mount.h*y)),view=sub(from,to);total++;
          let blocker=dot(mount.Z,view)/Math.hypot(...view)<.2?'back-facing':null;
          // Own backing is included: the picture must really sit in front of its cabinet.
          if(!blocker)blocker=solids.find(r=>goonworksSegmentBlocked(from,to,r))?.id;
          if(!blocker)blocker=segmentMeshBlocker(from,to,index);
          if(!blocker){visible++;clear++;}else blockers.set(blocker,(blockers.get(blocker)||0)+1);
        }
      }
      approaches.push({distance,visible:clear/15});
    }
    report.push({id:mount.id,visible:visible/total,approaches,blockers:[...blockers].sort((a,b)=>b[1]-a[1])});
  }
  return report;
}
