/* Render-only depth limits. Top surfaces and simulation data remain unchanged. */
const dot = (a,b) => a[0]*b[0]+a[1]*b[1]+a[2]*b[2];
const sub = (a,b) => a.map((v,i)=>v-b[i]);
const cross = (a,b) => [a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
export function visualBoxesOverlap(a,b) {
  const delta=sub(b.p,a.p), axes=[...a.axes,...b.axes];
  for(const x of a.axes)for(const y of b.axes)axes.push(cross(x,y));
  for(const axis of axes){
    if(dot(axis,axis)<1e-10)continue;
    const radius=box=>box.half.reduce((r,h,i)=>r+h*Math.abs(dot(box.axes[i],axis)),0);
    if(Math.abs(dot(delta,axis))>radius(a)+radius(b)+1e-6)return false;
  }
  return true;
}
function bounds(box) {
  const radius=axis=>box.half.reduce((r,h,i)=>r+h*Math.abs(box.axes[i][axis]),0);
  return [box.p[0]-radius(0),box.p[2]-radius(2),box.p[0]+radius(0),box.p[2]+radius(2)];
}
/** Spatially indexed solid road strips include fork decks and the actual curved-bank height. */
export function visualClearanceSamples(track,{stepM=4,headroomM=4.5}={}) {
  const samples=[], grid=new Map(), cellM=64;
  const insert=(f,s,forkId,halfWidth,halfLength,kind='road',id=null,extraUp=0)=>{
    const above=headroomM+extraUp,below=.4;
    const row={s,forkId,kind,id,surfaceP:[...f.p],p:f.p.map((v,i)=>v+f.n[i]*(above-below)/2),
      axes:[[...f.b],[...f.n],[...f.t]],half:[halfWidth,(above+below)/2,halfLength]};
    const index=samples.length;samples.push(row);const b=bounds(row);
    for(let x=Math.floor(b[0]/cellM);x<=Math.floor(b[2]/cellM);x++)for(let z=Math.floor(b[1]/cellM);z<=Math.floor(b[3]/cellM);z++){
      const key=x+':'+z;if(!grid.has(key))grid.set(key,[]);grid.get(key).push(index);
    }
  };
  if(track?.frameAt&&Number.isFinite(track.lengthM)){
    const paths=[{id:'',main:true,lengthM:track.lengthM,frameAt:track.frameAt,surfaceAt:track.surfaceAt},...(track.forks||[])];
    for(const path of paths){
      const count=Math.max(1,Math.ceil(path.lengthM/stepM)),step=path.lengthM/count;
      for(let i=0;i<=count;i++){
        const s=Math.min(path.lengthM,i*step);
        if(path.main&&(track.fields||[]).some(f=>s>f.fromS&&s<f.toS))continue;
        const f=path.frameAt(s);let extraUp=0;
        if(path.surfaceAt)for(const x of [-f.w/2,f.w/2]){
          const edge=path.surfaceAt(s,x);extraUp=Math.max(extraUp,dot(sub(edge.p,f.p),f.n));
        }
        insert(f,s,path.id||'',f.w/2+.35,step/2+.35,'road',null,extraUp);
      }
    }
    for(const field of track.fields||[])for(const stone of field.stones||[]){
      const s=stone.s+stone.len/2,f=track.frameAt(s);f.p=f.p.map((v,i)=>v+f.b[i]*stone.x);
      const radius=stone.spin?Math.hypot(stone.w,stone.len)/2:null;
      insert(f,s,'',radius||stone.w/2,radius||stone.len/2,'platform',stone.id);
    }
  }
  samples.query=box=>{
    const b=bounds(box),seen=new Set(),out=[];
    for(let x=Math.floor(b[0]/cellM);x<=Math.floor(b[2]/cellM);x++)for(let z=Math.floor(b[1]/cellM);z<=Math.floor(b[3]/cellM);z++)
      for(const i of grid.get(x+':'+z)||[])if(!seen.has(i)){seen.add(i);out.push(samples[i]);}
    return out;
  };
  samples.stepM=stepM;return samples;
}
/** Maximum safe downward extension, including landing dip and rocking allowance. */
export function capVisualDepth(frame,requested,samples,{halfWidth=frame.w/2,halfLength=2,motionM=.4,skipSpanM=halfLength+8,ignoreId=null}={}) {
  if(!(requested>0))return 0;
  const box=depth=>({p:frame.p.map((v,i)=>v-frame.n[i]*(depth+motionM)/2),
    axes:[frame.b,frame.n,frame.t],half:[halfWidth,(depth+motionM)/2,halfLength]});
  const full=box(requested),candidates=(samples.query?samples.query(full):samples).filter(row=>{
    if(ignoreId&&row.id===ignoreId)return false;
    if((row.forkId||'')===(frame.forkId||'')&&Math.abs(row.s-frame.s)<skipSpanM)return false;
    // Same-level neighboring landing decks are not lower routes. Their top footprints own the gap.
    return dot(sub(row.surfaceP,frame.p),frame.n)<-.65&&visualBoxesOverlap(full,row);
  });
  if(!candidates.length)return requested;
  let lo=0,hi=requested;
  for(let i=0;i<12;i++){const mid=(lo+hi)/2;if(candidates.some(row=>visualBoxesOverlap(box(mid),row)))hi=mid;else lo=mid;}
  return Math.max(0,lo-.03);
}
