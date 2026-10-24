// Bounded shallow surface waves in track coordinates. No renderer or wall clock dependencies.
import { isWaterSurface } from '../track/waterSurface.js';
const clamp = (x,a,b) => Math.max(a,Math.min(b,x));
const finite = (x, fallback=0) => Number.isFinite(x)?x:fallback;
const FIXED = 1/60, MAX_HEIGHT = .8, MAX_VELOCITY = 3.5;

function wetRuns(track) {
  const points=track.nodeS?[...track.nodeS,track.lengthM]:Array.from({length:Math.ceil(track.lengthM/2)+1},(_,i)=>Math.min(track.lengthM,i*2));
  const runs=[];
  for(let i=0;i<points.length-1;i++) {
    const a=points[i],b=points[i+1]; if(!(b>a))continue;
    const s=(a+b)/2,f=track.frameAt(s),section=track.sections?.find(r=>s>=r.fromS&&s<r.toS)?.id||'';
    const wet=isWaterSurface(f.surface,section);
    if(!wet||/submerged|emerge/.test(section)||f.n?.[1]<.45||f.flags?.includes('jump'))continue;
    const previous=runs.at(-1);
    if(previous&&Math.abs(previous.toS-a)<.001)previous.toS=b;
    else runs.push({fromS:a,toS:b});
  }
  return runs;
}

/** Atlas channels: signed height metres, signed lateral/along slopes, foam. UVs address texel centres. */
export function createWaterField(track,{quality='mid'}={}) {
  const width=quality==='phone'?32:64, runs=wetRuns(track);
  const totalLength=runs.reduce((sum,r)=>sum+r.toS-r.fromS,0);
  const spacing=Math.max(quality==='phone'?2.5:1.5,totalLength/Math.max(16,2000-runs.length*4));
  let height=0;
  for(const r of runs) {
    r.nx=width;r.ny=Math.max(2,Math.ceil((r.toS-r.fromS)/spacing)+1);r.rowOffset=height+1;
    r.ds=(r.toS-r.fromS)/(r.ny-1);r.widths=new Float32Array(r.ny);
    for(let row=0;row<r.ny;row++)r.widths[row]=Math.max(.2,finite(track.frameAt(r.fromS+row*r.ds).w,1)-.08);
    r.speed=Math.min(4,Math.min(r.ds,...r.widths.map(w=>w/(width-1)))*20);
    height+=r.ny+2;
  }
  height=Math.max(1,height);
  const count=width*height,H=new Float32Array(count),V=new Float32Array(count),F=new Float32Array(count);
  const SX=new Float32Array(count),SS=new Float32Array(count),pixels=new Uint8Array(count*4);
  const stats={cells:runs.reduce((n,r)=>n+r.nx*r.ny,0),runCount:runs.length,steps:0,disturbances:0,version:0,
    activeCells:0,maxHeight:0,energy:0,droppedTimeS:0};
  let accumulator=0,dirty=true,awake=false;
  const index=(r,row,col)=>(r.rowOffset+row)*width+col;
  function locate(s,x) {
    if(!Number.isFinite(s)||!Number.isFinite(x))return null;
    const r=runs.find(r=>s>=r.fromS&&s<=r.toS);if(!r)return null;
    const y=clamp((s-r.fromS)/r.ds,0,r.ny-1),row=Math.min(r.ny-2,Math.floor(y)),t=y-row;
    const w=r.widths[row]*(1-t)+r.widths[row+1]*t;
    if(Math.abs(x)>w/2)return null;
    return {r,y,col:clamp((x/w+.5)*(width-1),0,width-1)};
  }
  function rowValue(array,r,row,col) {
    col=clamp(col,0,width-1);const left=Math.min(width-2,Math.floor(col)),t=col-left,i=index(r,row,left);
    return array[i]*(1-t)+array[i+1]*t;
  }
  function value(array,q) {
    const row=Math.min(q.r.ny-2,Math.floor(q.y)),t=q.y-row;
    return rowValue(array,q.r,row,q.col)*(1-t)+rowValue(array,q.r,row+1,q.col)*t;
  }
  function aligned(array,r,row,x) {return rowValue(array,r,row,(x/r.widths[row]+.5)*(width-1));}
  function pack() {
    stats.activeCells=stats.maxHeight=stats.energy=0;
    let maxVelocity=0,maxFoam=0;
    for(const r of runs)for(let row=0;row<r.ny;row++)for(let col=0;col<width;col++) {
      const i=index(r,row,col),dx=r.widths[row]/(width-1),x=(col/(width-1)-.5)*r.widths[row];
      const left=index(r,row,Math.max(0,col-1)),right=index(r,row,Math.min(width-1,col+1));
      const previous=Math.max(0,row-1),next=Math.min(r.ny-1,row+1);
      SX[i]=clamp((H[right]-H[left])/(dx*(col===0||col===width-1?1:2)),-1,1);
      SS[i]=clamp((aligned(H,r,next,x)-aligned(H,r,previous,x))/(r.ds*(next-previous)),-1,1);
      const p=i*4;pixels[p]=Math.round((H[i]+1)*127.5);pixels[p+1]=Math.round((SX[i]+1)*127.5);
      pixels[p+2]=Math.round((SS[i]+1)*127.5);pixels[p+3]=Math.round(F[i]*255);
      const a=Math.abs(H[i]);stats.maxHeight=Math.max(stats.maxHeight,a);stats.energy+=H[i]*H[i]+V[i]*V[i]*.05;
      maxVelocity=Math.max(maxVelocity,Math.abs(V[i]));maxFoam=Math.max(maxFoam,F[i]);
      if(a>.002||Math.abs(V[i])>.002||F[i]>.01)stats.activeCells++;
    }
    // Repeated guard rows isolate linear filtering from the next run in the atlas.
    for(const r of runs)for(const [src,dst] of [[r.rowOffset,r.rowOffset-1],[r.rowOffset+r.ny-1,r.rowOffset+r.ny]])
      pixels.copyWithin(dst*width*4,src*width*4,(src+1)*width*4);
    if(!runs.length)for(let i=0;i<count;i++)pixels.set([128,128,128,0],i*4);
    if(stats.maxHeight<.00001&&maxVelocity<.00001&&maxFoam<.002) {
      H.fill(0);V.fill(0);F.fill(0);SX.fill(0);SS.fill(0);
      for(let i=0;i<count;i++){const p=i*4;pixels[p]=pixels[p+1]=pixels[p+2]=128;pixels[p+3]=0;}
      stats.activeCells=stats.maxHeight=stats.energy=0;awake=false;
    }
    dirty=false;stats.version++;
  }
  function disturb({s,x,radius=2,strength=.25,dirX=0,dirS=0,foam=.4}={}) {
    const q=locate(s,x);if(!q||!Number.isFinite(strength)||strength===0)return false;
    const r=q.r,row=Math.min(r.ny-1,Math.round(q.y)),dx=r.widths[row]/(width-1);
    radius=clamp(finite(radius,2),.5,8);radius=Math.max(radius,Math.max(dx,r.ds)*1.35);
    strength=clamp(strength,-.8,.8);foam=clamp(finite(foam,.4),0,1);
    const mag=Math.hypot(finite(dirX),finite(dirS)),ux=mag?finite(dirX)/mag:0,us=mag?finite(dirS)/mag:0;
    const cells=[];let coreMass=0,rimMass=0;
    for(let row=Math.max(0,Math.floor((s-radius-r.fromS)/r.ds));row<=Math.min(r.ny-1,Math.ceil((s+radius-r.fromS)/r.ds));row++) {
      const widthM=r.widths[row],cellArea=widthM/(width-1)*r.ds,ds=r.fromS+row*r.ds-s;
      for(let col=0;col<width;col++) {
        const dx=(col/(width-1)-.5)*widthM-x,d=Math.hypot(dx,ds),u=d/radius;if(u>=1)continue;
        const core=u<.55?(1-(u/.55)**2)**2:0;
        const rim=u>=.55?(1-((u-.775)/.225)**2)**2*(1+.65*(d?(dx*ux+ds*us)/d:0)):0;
        cells.push([index(r,row,col),core,rim]);coreMass+=core*cellArea;rimMass+=rim*cellArea;
      }
    }
    if(!(coreMass>0))return false;
    const balance=rimMass>0?coreMass/rimMass:0;
    for(const [i,core,rim] of cells) {
      const delta=strength*(-core+rim*balance);
      H[i]=clamp(H[i]+delta,-MAX_HEIGHT,MAX_HEIGHT);V[i]=clamp(V[i]+delta*2.2,-MAX_VELOCITY,MAX_VELOCITY);
      F[i]=clamp(F[i]+foam*Math.max(core*.2,rim*.75),0,1);
    }
    stats.disturbances++;dirty=true;awake=true;return true;
  }
  function integrate() {
    const damping=Math.exp(-1.25*FIXED),foamDecay=Math.exp(-.8*FIXED);
    for(const r of runs)for(let row=0;row<r.ny;row++)for(let col=0;col<width;col++) {
      const i=index(r,row,col),h=H[i],dx=r.widths[row]/(width-1),x=(col/(width-1)-.5)*r.widths[row];
      const left=H[index(r,row,Math.max(0,col-1))],right=H[index(r,row,Math.min(width-1,col+1))];
      const previous=aligned(H,r,Math.max(0,row-1),x),next=aligned(H,r,Math.min(r.ny-1,row+1),x);
      const lap=(left+right-2*h)/(dx*dx)+(previous+next-2*h)/(r.ds*r.ds);
      V[i]=clamp((V[i]+(r.speed*r.speed*lap-.45*h)*FIXED)*damping,-MAX_VELOCITY,MAX_VELOCITY);
    }
    for(const r of runs)for(let row=0;row<r.ny;row++)for(let col=0;col<width;col++) {
      const i=index(r,row,col);H[i]=clamp(H[i]+V[i]*FIXED,-MAX_HEIGHT,MAX_HEIGHT);
      F[i]=Math.max(F[i]*foamDecay,Math.min(.35,Math.abs(V[i])*.045));
      if(Math.abs(H[i])<1e-7&&Math.abs(V[i])<1e-7)H[i]=V[i]=0;
    }
    stats.steps++;dirty=true;
  }
  function step(dt,{gentle=false,paused=false}={}) {
    if(paused)return false;
    if(!awake&&!dirty){accumulator=0;return false;}
    dt=Math.max(0,finite(dt));stats.droppedTimeS+=Math.max(0,dt-.1);
    accumulator+=Math.min(dt,.1)*(gentle?.5:1);
    let n=0;while(accumulator+1e-10>=FIXED&&n<6){integrate();accumulator-=FIXED;n++;}
    if(accumulator>FIXED*6){stats.droppedTimeS+=accumulator;accumulator=0;}
    if(!dirty)return false;pack();return true;
  }
  function sample(s,x,out={}) {
    const q=locate(s,x);
    if(!q)return Object.assign(out,{height:0,slopeX:0,slopeS:0,foam:0,wet:false});
    // Read current heights, including an injection not yet packed for the renderer.
    const r=q.r,row=Math.min(r.ny-2,Math.floor(q.y)),t=q.y-row,w=r.widths[row]*(1-t)+r.widths[row+1]*t;
    const dx=w/(width-1),left=Math.max(-w/2,x-dx),right=Math.min(w/2,x+dx);
    const previous=Math.max(0,q.y-1),next=Math.min(r.ny-1,q.y+1);
    function at(y,x) {
      const row=Math.min(r.ny-2,Math.floor(y)),t=y-row;
      return aligned(H,r,row,x)*(1-t)+aligned(H,r,row+1,x)*t;
    }
    return Object.assign(out,{height:value(H,q),slopeX:clamp((at(q.y,right)-at(q.y,left))/(right-left),-1,1),
      slopeS:clamp((at(next,x)-at(previous,x))/((next-previous)*r.ds),-1,1),foam:value(F,q),wet:true});
  }
  function atlasUV(s,x) {const q=locate(s,x);return q?{u:(q.col+.5)/width,v:(q.r.rowOffset+q.y+.5)/height,wet:true}:{u:.5/width,v:.5/height,wet:false};}
  function reset() {H.fill(0);V.fill(0);F.fill(0);SX.fill(0);SS.fill(0);accumulator=0;awake=false;stats.steps=stats.disturbances=stats.droppedTimeS=0;dirty=true;pack();}
  pack();
  return {runs,width,height,pixels,disturb,step,sample,atlasUV,reset,stats};
}
