/* Deterministic room construction. Media mounts and collision clearance share this plan. */
import { getGoonworksLayout } from '../track/goonworks-layout.js';
import { axesFrom, roadSamples, V3 } from './mediaMath.js';

const {add,sub,mul,dot,norm,cross}=V3, TAU=Math.PI*2;
const WORLD=[[1,0,0],[0,1,0],[0,0,1]], CACHE=new WeakMap();
const point=(f,x,y,z=0)=>add(add(add(f.p,mul(f.b,x)),mul(f.n,y)),mul(f.t,z));
const axes=f=>[[...f.b],[...f.n],mul(f.t,-1)];
const finite=v=>v.every(Number.isFinite);
export function goonworksBoxesIntersect(a,b,margin=0){
  const A=a.axes||WORLD,B=b.axes||WORLD,ah=a.size.map(v=>v/2),bh=b.size.map(v=>v/2),d=sub(b.p,a.p);
  if(Math.hypot(...d)>Math.hypot(...ah)+Math.hypot(...bh)+margin)return false;
  for(const axis of [...A,...B,...A.flatMap(x=>B.map(y=>cross(x,y)))]){
    const length=Math.hypot(...axis);if(length<1e-7)continue;
    const reach=A.reduce((n,x,i)=>n+Math.abs(dot(x,axis))*ah[i],0)+B.reduce((n,x,i)=>n+Math.abs(dot(x,axis))*bh[i],0);
    if(Math.abs(dot(d,axis))>reach+margin*length)return false;
  }return true;
}
export function goonworksSegmentBlocked(from,to,row){
  const delta=sub(to,from),origin=sub(from,row.p);let near=0,far=1;
  for(let i=0;i<3;i++){const axis=(row.axes||WORLD)[i],p=dot(origin,axis),v=dot(delta,axis),h=row.size[i]/2;
    if(Math.abs(v)<1e-8){if(Math.abs(p)>h)return false;continue;}
    const a=(-h-p)/v,b=(h-p)/v;near=Math.max(near,Math.min(a,b));far=Math.min(far,Math.max(a,b));if(near>far)return false;
  }return far>.002&&near<.998;
}
export function goonworksWorldPlan(track){
  if(track?.id!=='goonworks')return null;if(CACHE.has(track))return CACHE.get(track);
  const layout=getGoonworksLayout(track),samples=roadSamples(track,7),rows=[],mounts=[],particles=[],features=[],rooms=[];
  const corridor=samples.map(f=>({p:point(f,0,6),size:[f.w+6,16,9],axes:axes(f)}));
  const grid=new Map(),cell=64;
  for(const c of corridor){const radius=Math.hypot(...c.size)/2;for(let x=Math.floor((c.p[0]-radius)/cell);x<=Math.floor((c.p[0]+radius)/cell);x++)for(let z=Math.floor((c.p[2]-radius)/cell);z<=Math.floor((c.p[2]+radius)/cell);z++){const key=x+':'+z;if(!grid.has(key))grid.set(key,[]);grid.get(key).push(c);}}
  const nearby=row=>{const r=Math.hypot(...row.size)/2,found=new Set();for(let x=Math.floor((row.p[0]-r)/cell);x<=Math.floor((row.p[0]+r)/cell);x++)for(let z=Math.floor((row.p[2]-r)/cell);z<=Math.floor((row.p[2]+r)/cell);z++)for(const c of grid.get(x+':'+z)||[])found.add(c);return found;};
  const clear=row=>![...nearby(row)].some(c=>goonworksBoxesIntersect(row,c));
  let rejected=0;
  const put=(id,material,p,size,{shape='box',basis=WORLD,check=true,outline=true,motion=null}={})=>{
    if(!finite(p)||!finite(size)||size.some(v=>v<=0))throw Error('Invalid GOONWORKS part '+id);
    const row={id,material,p,size,shape,axes:basis,outline,motion};if(check&&!clear(row)){rejected++;return null;}rows.push(row);return row;
  };
  const local=(id,material,f,x,y,z,size,options={})=>put(id,material,point(f,x,y,z),size,{basis:axes(f),...options});
  const span=id=>layout.sections?.find(s=>s.id===id)||layout.landmarks?.find(s=>s.id===id)||track.sections.find(s=>s.id===id);
  const range=id=>{const r=span(id);return r?{...r,s0:r.s0??r.fromS,s1:r.s1??r.toS}:null;};
  function beam(id,material,a,b,width=.6,outline=true){const Y=norm(sub(b,a)),ref=Math.abs(Y[1])>.95?[1,0,0]:[0,1,0],X=norm(cross(ref,Y)),Z=norm(cross(X,Y));return put(id,material,mul(add(a,b),.5),[width,Math.hypot(...sub(b,a)),width],{basis:[X,Y,Z],outline});}
  function screen(id,s,side,height=13,w=15,h=11,extra=0){
    const f=track.frameAt(s),normal=norm(add(mul(f.b,-side),mul(f.t,-.4))),basis=axesFrom(normal,f.n);
    const C=point(f,side*(f.w/2+8+extra),height),p=add(C,mul(normal,-1.05));
    // Reserve the authored mount, but let media own the complete fitted enclosure.
    // Enlarged pictures can move or be dropped; scenery must not leave an empty shell.
    if(!clear({p,size:[w+2.4,h+2.4,2.5],axes:[basis.X,basis.Y,basis.Z]})){rejected++;return;}
    mounts.push({id,s,C:add(C,mul(normal,.7)),...basis,w,h,frame:'billboard',phone:true});
  }
  function drip(id,f,x,y,z,scale=1){
    local(id+'-stem','white',f,x,y,z,[1.7*scale,6*scale,1.9*scale],{shape:'sphere'});
    local(id+'-drop','white',f,x,y-2.7*scale,z,[2.2*scale,2.8*scale,2.2*scale],{shape:'sphere'});
  }
  function arch(id,s,width,high=32,depth=5){const f=track.frameAt(s);
    for(const side of[-1,1]){local(id+'-pier','fuchsia',f,side*(width/2+3),high/2,0,[6,high,depth]);local(id+'-white-pier','white',f,side*(width/2+3),high/2,-depth*.55,[1,high-1,.8]);}
    local(id+'-crown','fuchsia',f,0,high+3,0,[width+13,7,depth+1]);
    local(id+'-enamel','white',f,0,high+7,0,[width+15,1.6,depth+3]);
    for(let i=-3;i<=3;i++)drip(id+'-drip',f,i*(width+9)/7,high+2,-depth*.7,.6+(i%3+3)%3*.18);
  }
  function room(id,{height=43,spacing=22,open=true}={}){
    const hall={'aperture-mill':'mill-hall','pop-quiz':'quiz-ballroom','pinball-launch':'pinball-table'}[id];
    const r=layout.byId?.[hall]||range(id);if(!r)return;rooms.push({id,s0:r.s0,s1:r.s1,height});
    const count=Math.max(2,Math.ceil((r.s1-r.s0)/spacing)),step=(r.s1-r.s0)/count;
    const mid=track.frameAt((r.s0+r.s1)/2),length=r.s1-r.s0,roomHalf=Math.max(mid.w,track.frameAt(r.s1-1).w)/2;
    // Whole ledges and one suspended hull give each room a single readable silhouette.
    for(const side of[-1,1]){
      const x=side*(roomHalf+10);
      local(id+'-wall-base','fuchsia',mid,x,3,0,[10,10,length+5]);
      local(id+'-enamel-sill','white',mid,x,8.5,0,[11.2,1.2,length+6]);
      local(id+'-roof-beam','fuchsia',mid,x,height,0,[12,8,length+8]);
      local(id+'-scalloped-eave','white',mid,x,height+4.8,0,[14,1.5,length+10]);
      local(id+'-balcony-band','fuchsia',mid,x,25,0,[13,3.4,length-5]);
      local(id+'-balcony-white-lip','white',mid,x-side*.5,27,0,[14,1.2,length-4]);
    }
    local(id+'-island-keel','wine',mid,0,-17,0,[roomHalf*2+30,24,length+7],{shape:'island'});
    for(let i=0;i<count;i++){const s=r.s0+(i+.5)*step,f=track.frameAt(s),half=f.w/2;
      for(const side of[-1,1]){
        const x=side*(roomHalf+10);
        local(id+'-upright','fuchsia',f,x,height/2,step*.44,[5,height,4]);
        local(id+'-upright-inset','white',f,x-side*3,height/2,step*.44,[1,height-7,2]);
        for(const z of[-.3,.15])drip(id+'-eave',f,x-side*5.2,height-1,z*step,.75+((i+side+5)%3)*.2);
        if(!open||i%3===1)local(id+'-inset-wall','wine',f,x+side*2,24,0,[4,25,step*.83]);
        if(id==='pop-quiz'){
          local('quiz-domino-pier','white',f,x-side*1.8,16,0,[2,10,step*.55]);
          for(const z of[-.16,.16])local('quiz-domino-pip','ink',f,x-side*3,16,z*step,[.55,2.5,2.5],{shape:'sphere',outline:false});
        }
        if(id==='pinball-launch'){
          local('pinball-balcony-star','white',f,x-side*6.4,34,0,[1.5,8,8],{shape:'star',basis:[f.t,f.n,f.b]});
          for(const z of[-.2,0,.2])local('pinball-lamp','hot',f,x-side*6,12,z*step,[1.8,2,2],{shape:'sphere'});
        }
      }
      if(i%2===0){screen(id+'-screen-'+i,s,-1,22,Math.min(20,step*.76),10,-7);screen(id+'-screen-'+i+'b',s,1,22,Math.min(20,step*.76),10,-7);}
      if(id==='aperture-mill'&&i%2===0)local(id+'-vault-rib','white',f,0,height-4,0,[roomHalf*2+20,20,3],{shape:'arch'});
      if(id==='pop-quiz'&&i%3===1){
        for(const x of[-roomHalf*.65,0,roomHalf*.65])local('quiz-icing-canopy','fuchsia',f,x,height+8,0,[roomHalf,14,step+6],{shape:'sphere'});
        local('quiz-ceiling-white-band','white',f,0,height+2,0,[roomHalf*2+28,2.4,3]);
      }
      if(id==='pinball-launch'&&i%3===0)local('pinball-striped-ceiling-beam','white',f,0,height+2,0,[roomHalf*2+29,3.4,4]);
      particles.push({p:point(f,0,-9),spread:half+12,phase:i*.93});
    }
    if(id==='aperture-mill'){
      const f=track.frameAt(r.s0+4);local('mill-grand-clock','fuchsia',f,0,height+20,0,[36,5,36],{shape:'cylinder',basis:[f.b,f.t,f.n]});
      local('mill-grand-clock-ring','white',f,0,height+20,-3,[31,31,2],{shape:'torus'});
      local('mill-grand-clock-hand','ink',f,0,height+25,-4,[1.8,13,1],{motion:{spin:.12,axis:2,phase:0}});
    }
    arch(id+'-entrance',r.s0+6,track.frameAt(r.s0+6).w+9,height-3);
    arch(id+'-exit',r.s1-7,track.frameAt(r.s1-7).w+9,height-3);
  }
  room('aperture-mill',{height:48});room('pop-quiz',{height:40,spacing:25});room('pinball-launch',{height:48,spacing:22});
  const grotto=range('screen-grotto');
  if(grotto){features.push('watching-grotto');const count=Math.max(4,Math.ceil((grotto.s1-grotto.s0)/20));
    for(let i=0;i<count;i++){const s=grotto.s0+(i+.5)*(grotto.s1-grotto.s0)/count,f=track.frameAt(s),half=f.w/2;
      for(const side of[-1,1]){
        local('grotto-scalloped-rock','wine',f,side*(half+19),20,0,[28,58,26],{shape:'sphere'});
        local('grotto-inner-rock','fuchsia',f,side*(half+11),3,5,[14,17,16],{shape:'sphere'});
        if(i%3!==1)local('grotto-arched-roof','wine',f,side*(half*.6),48,0,[half+28,20,27],{shape:'sphere'});
        if(i%2===0)drip('grotto-stalactite',f,side*(half-1),40,0,1.7);
        // The inside of the entry curve needs wider spacing so consecutive cabinets do not mask each other.
        if(side<0||i%2===0||s>=layout.byId['grotto-gallery'].s0)screen('grotto-'+i+'-'+side,s,side,24,17,12,-7);
        if(i%2===0)screen('grotto-high-'+i+'-'+side,s-8,side,34,12,8,-10);
      }
      local('grotto-floating-rock-keel','wine',f,0,-18,0,[f.w+45,28,29],{shape:'island'});
      particles.push({p:point(f,0,7),spread:half,phase:i});
    }arch('grotto-mouth',grotto.s0+3,track.frameAt(grotto.s0+3).w+5,43,8);
  }
  const tube=range('rabbit-hole');
  if(tube){features.push('windowed-rabbit-tube');const step=38,count=Math.ceil((tube.s1-tube.s0)/step);
    for(let i=0;i<count;i++){const s=tube.s0+(i+.5)*(tube.s1-tube.s0)/count,f=track.frameAt(s),radius=Math.max(23,f.w/2+4),center=point(f,0,radius-1),depth=(tube.s1-tube.s0)/count;
      // The shell is a single swept mesh. These small fixtures sit entirely above the driving line.
      for(const side of[-1,1])local('rabbit-ceiling-conduit','white',f,side*5,44,0,[1.8,depth+1,1.8],{shape:'cylinder',basis:[f.b,f.t,f.n]});
      if(i%2===0)screen('rabbit-screen-'+i,s,1,28,10,8,-6);
      // The enclosed tube stays clear of floating white sparkles and bubble rings.
    }
  }
  // Dense shoulder scenery changes silhouette at each district, with no common foundation.
  for(const id of['start','aperture-mill','pop-quiz','pinball-launch','finish']){const r=range(id);if(!r)continue;
    for(let s=r.s0+18,i=0;s<r.s1-10;s+=37,i++){
      const f=track.frameAt(s),side=i%2?1:-1,x=side*(f.w/2+20);
      if(id==='aperture-mill'){
        // One closed mesh partitions the ivory center and gold crown. Separate coplanar box
        // teeth used to overlap the cylinder caps and flicker from both approach directions.
        local('toy-gear-solid','gear',f,x,15,0,[24,24,4],{shape:'gear',motion:{spin:.16,axis:2,phase:i}});
      }else if(id==='pinball-launch'){
        local('pinball-bumper-stem','white',f,x,6,0,[9,12,9],{shape:'cylinder'});
        local('pinball-bumper-cap','hot',f,x,13,0,[25,13,25],{shape:'sphere'});
        local('pinball-bumper-highlight','white',f,x-4,17,-4,[7,2.2,7],{shape:'sphere',outline:false});
      }else{
        local('funhouse-pipe','fuchsia',f,x,16,0,[5,38,5],{shape:'cylinder'});
        local('funhouse-pipe-rim','white',f,x,35,0,[11,3,11],{shape:'cylinder'});
        local('funhouse-funnel','white',f,x,40,0,[16,10,16],{shape:'cone'});
        local('funhouse-star','white',f,x-side*9,26,-4,[9,9,3],{shape:'star',motion:{spin:.08,axis:2,phase:i}});
      }
    }
  }
  // Actual platform footprints own the tops. White caps do not bridge the real void gaps.
  const platforms=[];
  for(const field of track.fields||[])for(const st of field.stones||[]){
    const f=track.frameAt(st.s+st.len/2),p=point(f,st.x,0),row={id:st.id,field:field.id,s:st.s,source:st,p,b:[...f.b],n:[...f.n],t:[...f.t],w:st.w,len:st.len};platforms.push(row);
    particles.push({p:point(f,st.x,-9),spread:st.w*.4,phase:st.s*.01});
  }
  for(const id of['ribbon-helix','grand-fold','leapfrog']){const r=range(id);if(!r)continue;features.push(id);
    for(let s=r.s0+30,i=0;s<r.s1;s+=62,i++){const f=track.frameAt(s);particles.push({p:point(f,0,-5),spread:f.w*.7,phase:i*1.2});
      if(id==='leapfrog'){const side=i%2?1:-1,x=side*(f.w/2+20);local('orchard-floating-cloud','fuchsia',f,x,-9,0,[28,17,28],{shape:'island'});local('orchard-tube-trunk','white',f,x,11,0,[3.5,28,3.5],{shape:'cylinder'});local('orchard-star-crown','white',f,x,28,0,[15,15,4],{shape:'star',motion:{spin:.1,axis:2,phase:i}});}}
  }
  // Three authored silhouettes are recognizable from several elevations around the open atrium.
  const landmarks=[];
  function landmarkSite(id,s,side,start,size,centerY){
    const f=track.frameAt(s);
    for(let distance=start;distance<=start+160;distance+=16){const x=side*distance,volume={p:point(f,x,centerY),size,axes:axes(f)};
      if(clear(volume)){landmarks.push({id,s,...volume});features.push(id);return{f,x};}}
    return null;
  }
  const braid=layout.byId['ribbon-braid'];
  const badge=landmarkSite('spiral-cameo',braid.s0+(braid.s1-braid.s0)*.51,1,54,[62,94,52],22);
  if(badge){const{f,x}=badge;
    local('spiral-cameo-island','fuchsia',f,x,-14,0,[52,20,44],{shape:'island'});
    local('spiral-cameo-icing','white',f,x,-2,0,[54,3,46],{shape:'island'});
    for(const dx of[-13,13]){local('spiral-cameo-pillar','fuchsia',f,x+dx,11,0,[5,26,5],{shape:'cylinder'});drip('spiral-cameo-icing-drip',f,x+dx,-2,-21,.75);}
    local('spiral-cameo-disc','white',f,x,36,0,[56,5,56],{shape:'cylinder',basis:[f.b,f.t,f.n]});
    local('spiral-cameo-rim','ink',f,x,36,-2.2,[60,60,3],{shape:'torus',outline:false});
    local('spiral-cameo-painted-face','fuchsia',f,x,36,-2.8,[54,54,1],{shape:'spiral',outline:false,motion:{spin:.025,axis:2,phase:0}});
    local('spiral-cameo-painted-back','fuchsia',f,x,36,2.8,[54,54,1],{shape:'spiral',basis:[mul(f.b,-1),f.n,f.t],outline:false,motion:{spin:.025,axis:2,phase:0}});
    particles.push({p:point(f,x,-6),spread:24,phase:2.1});
  }
  const descent=layout.byId['fold-descent'];
  const pipes=landmarkSite('rabbit-pipe-organ',descent.s0+205,-1,100,[94,114,66],28);
  if(pipes){const{f,x}=pipes;
    local('rabbit-pipe-organ-island','wine',f,x,-17,0,[90,22,60],{shape:'island'});
    local('rabbit-pipe-organ-icing','white',f,x,-4,0,[92,3.5,62],{shape:'island'});
    for(const [dx,y,w,h,z]of[[-14,25,60,58,0],[18,12,44,44,-14]]){
      local('rabbit-pipe-organ-arch','fuchsia',f,x+dx,y,z,[w,h,7],{shape:'arch'});
      for(const side of[-1,1]){
        const legX=x+dx+side*w*.48;
        local('rabbit-pipe-organ-leg','fuchsia',f,legX,(y-3)/2,z,[3.8,y+3,3.8],{shape:'cylinder'});
        local('rabbit-pipe-organ-white-cuff','white',f,legX,4,z,[6,4,6],{shape:'cylinder'});
      }
    }
    for(const dx of[-27,9,29])drip('rabbit-pipe-organ-icing-drip',f,x+dx,-4,-30,1+(dx%3)*.12);
    particles.push({p:point(f,x,4),spread:38,phase:4.2});
  }
  const overlook=layout.byId['fold-overlook'];
  const factory=landmarkSite('icing-observatory',overlook.s0+(overlook.s1-overlook.s0)*.45,-1,88,[100,128,78],38);
  if(factory){const{f,x}=factory;
    local('icing-observatory-island','fuchsia',f,x,-14,0,[94,26,72],{shape:'island'});
    local('icing-observatory-icing','white',f,x,1,0,[96,3,74],{shape:'island'});
    for(const[dx,z,height,width]of[[-23,0,46,24],[9,10,73,30],[33,-17,31,18]]){
      local('icing-observatory-tower','fuchsia',f,x+dx,height/2+3,z,[width,height,width],{shape:'cylinder'});
      local('icing-observatory-tower-roof','white',f,x+dx,height+4,z,[width+5,12,width+5],{shape:'sphere'});
      local('icing-observatory-window','ink',f,x+dx,height*.7,z-width*.51,[width*.4,width*.5,1.2],{shape:'sphere'});
      local('icing-observatory-window-glint','white',f,x+dx-2,height*.7+2,z-width*.54,[width*.1,width*.19,1.4],{shape:'sphere',outline:false});
      for(const side of[-1,1])drip('icing-observatory-roof-drip',f,x+dx+side*width*.35,height+1,z-width*.4,.9+side*.18);
    }
    for(const dx of[-34,-9,18,36])drip('icing-observatory-platform-drip',f,x+dx,0,-35,1.1+(dx%3)*.12);
    local('icing-observatory-loop-pipe','white',f,x-24,53,4,[29,25,6],{shape:'arch'});
    particles.push({p:point(f,x,15),spread:34,phase:5.7});
  }
  const minP=[Infinity,Infinity,Infinity],maxP=[-Infinity,-Infinity,-Infinity];for(const f of samples)for(let k=0;k<3;k++){minP[k]=Math.min(minP[k],f.p[k]);maxP[k]=Math.max(maxP[k],f.p[k]);}
  const center=minP.map((v,k)=>(v+maxP[k])/2),radius=Math.hypot(maxP[0]-minP[0],maxP[2]-minP[2])/2;
  // Chunky suspended silhouettes replace the old spiral sky, with visible pale air below every island.
  for(let i=0;i<20;i++){const a=i/20*TAU+Math.sin(i*2.3)*.035,r=radius+360+(i%3)*140,p=[center[0]+Math.cos(a)*r,minP[1]-35+(i%4)*45,center[2]+Math.sin(a)*r];
    const horizon=(id,offset,size,shape='box')=>put('horizon-'+id,'distant',add(p,offset),size,{shape,check:false,outline:false});
    horizon('island',[0,0,0],[82+(i%3)*28,40+(i%4)*9,65+(i%4)*15],i%3===0?'hexIsland':'island');
    if(i%4===0){
      horizon('twin-tower',[-20,58,0],[31,78,32],'cylinder');horizon('twin-tower',[20,45,8],[40,52,36]);
      horizon('icing-dome',[-20,102,0],[40,14,40],'sphere');horizon('chimney',[22,94,8],[9,50,9],'cylinder');
    }else if(i%4===1){
      horizon('loop-pipe',[0,58,0],[82,88,12],'arch');for(const dx of[-39,39])horizon('loop-leg',[dx,40,0],[7,42,7],'cylinder');
      horizon('small-tower',[15,35,-18],[22,32,22],'cylinder');
    }else if(i%4===2){
      horizon('dome-house',[0,50,0],[60,52,48],'cylinder');horizon('dome-roof',[0,80,0],[72,22,58],'sphere');
      horizon('thin-mast',[-20,108,0],[5,52,5],'cylinder');horizon('mast-pearl',[-20,136,0],[17,17,17],'sphere');
    }else{
      horizon('stepped-factory',[-19,48,2],[38,60,44]);horizon('stepped-factory',[20,68,-8],[33,100,34]);
      horizon('wide-funnel',[20,126,-8],[38,19,38],'cone');horizon('side-chimney',[-35,85,3],[7,30,7],'cylinder');
    }
    for(let j=0;j<2;j++)put('white-cartoon-cloud','cream',[p[0]+j*22,p[1]+180+(i%3)*35,p[2]],[55,16+j*4,22],{shape:'sphere',check:false,outline:false});
  }
  // Colour is assigned after placement so district art changes cannot move a support or a screen.
  const whiteFamily=id=>id.startsWith('grotto')||id.startsWith('rabbit')?'ice':id.startsWith('pop-quiz')||id.startsWith('quiz-')?'lilac':id.startsWith('aperture-mill')||id.startsWith('mill-')||id.startsWith('pinball')?'ivory':id.startsWith('icing-observatory')?'blush':'white';
  const hallColours={
    'aperture-mill':{structure:'gold',secondary:'gold'},
    'pop-quiz':{structure:'violet',secondary:'periwinkle'},
    'pinball-launch':{structure:'tangerine',secondary:'gold'}
  };
  const counts=new Map();
  for(const row of rows){const id=row.id,repeat=counts.get(id)||0;counts.set(id,repeat+1);
    if(row.material==='white')row.material=whiteFamily(id);
    if(id.startsWith('horizon-'))row.material=repeat%3===1?'distantCool':repeat%3===2?'distantWarm':'distant';
    if(row.material==='fuchsia'&&id.includes('balcony-band'))row.material='rose';
    if(row.material==='wine'&&id.includes('island-keel'))row.material='plum';

    // Full upper beams and columns carry each hall's identity. Low walls, island bodies,
    // pale eaves and balcony lips retain the common fuchsia and porcelain construction.
    for(const [hall,colours]of Object.entries(hallColours))if(id.startsWith(hall+'-')){
      if(id===hall+'-roof-beam'||id===hall+'-upright'||id===hall+'-entrance-crown'||id===hall+'-exit-crown')row.material=colours.structure;
      if(id===hall+'-inset-wall')row.material=colours.secondary;
    }
    if(id==='mill-grand-clock-ring')row.material='gold';
    if(id==='quiz-icing-canopy')row.material=repeat%3===1?'violet':'periwinkle';
    if(id==='quiz-domino-pier')row.material='lilac';
    if(id==='pinball-launch-balcony-band'||id==='pinball-striped-ceiling-beam'||id==='pinball-balcony-star')row.material='gold';
    if(id==='pinball-bumper-stem')row.material='gold';
    if(id==='pinball-bumper-cap')row.material=repeat%3===1?'hot':'tangerine';
    if(id==='pinball-lamp')row.material=repeat%2?'apricot':'gold';

    // Broad cobalt scallops alternate with pink walls, so blue reads from the racing line
    // while the fuchsia base rocks and suspended roof still tie the cavern to the factory.
    if(id==='grotto-scalloped-rock')row.material=repeat%4<2?'cobalt':repeat%4===2?'fuchsia':'magenta';
    if(id==='grotto-inner-rock'||id==='grotto-arched-roof')row.material='fuchsia';
    if(id.startsWith('grotto-stalactite'))row.material=repeat%3===0?'periwinkle':'ice';
    if(id==='grotto-mouth-crown'||id==='grotto-mouth-pier')row.material='cobalt';

    // The rabbit tube skin is shaded separately; its long conduits use the mint family.
    if(id==='rabbit-ceiling-conduit')row.material='mint';

    // Outdoor districts use their existing large landmarks and broad orchard bodies.
    if(id==='spiral-cameo-disc'||id==='spiral-cameo-pillar')row.material='coral';
    if(id==='orchard-floating-cloud')row.material=repeat%3===1?'fuchsia':'mint';
    if(id==='orchard-tube-trunk')row.material='mint';
    if(id==='orchard-star-crown')row.material=repeat%3===1?'ivory':'lime';
    if(id==='rabbit-pipe-organ-arch')row.material='indigo';
    if(id==='rabbit-pipe-organ-leg')row.material='periwinkle';
    if(id==='rabbit-pipe-organ-white-cuff')row.material='lilac';
    if(id==='icing-observatory-tower')row.material=repeat===1?'magenta':'indigo';
    if(id==='icing-observatory-tower-roof')row.material=repeat===1?'periwinkle':'lilac';
    if(id==='icing-observatory-loop-pipe')row.material='periwinkle';
    if(id==='white-cartoon-cloud')row.material=repeat%3===0?'ice':repeat%3===1?'cream':'lilac';
  }
  const plan={layout,rows,mounts,particles,platforms,rooms,features,landmarks,rejected,corridor,center,radius,skyRadius:Math.max(2400,radius+1100)};
  CACHE.set(track,plan);return plan;
}
