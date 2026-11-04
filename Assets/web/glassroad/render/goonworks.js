/* GOONWORKS runtime construction. Every resource exists before the renderer warms the scene. */
import { Group, Mesh, InstancedMesh, BoxGeometry, SphereGeometry, CylinderGeometry, TorusGeometry,
  OctahedronGeometry, BufferGeometry, Float32BufferAttribute, BufferAttribute, MeshBasicNodeMaterial,
  Matrix4, Quaternion, Vector3, BackSide, FrontSide } from 'three/webgpu';
import { vec3, float, uv, attribute, positionWorld, normalWorld, normalize, abs, mix, smoothstep, min } from 'three/tsl';
import { goonworksWorldPlan } from './goonworksWorld.js';
import { createGoonworksMaterial, createGoonworksTubeMaterial, createGoonworksPlatformMaterial, GOONWORKS_PAPER_MATERIALS, GOONWORKS_PALETTE as C } from './goonworksMaterials.js';
import {goonworksTubeShell,goonworksTubeSolids,goonworksPlatformGeometry} from './goonworksGeometry.js';
import { Q } from './quality.js';
import { machineryBeatAt, gearBeatTravel } from '../track/machineryBeat.js';
import { createGearSpiralPaint } from './gearSpirals.js';
import { visualClearanceSamples, capVisualDepth } from '../track/visualDepth.js';
import { composeInto } from './instanceWrite.js';
export { applyGoonworksRoad, applyGoonworksRails } from './goonworksMaterials.js';
export { goonworksMediaLayout, goonworksSpiralLayout } from './goonworksMedia.js';

export function platformFeedback(age, gentle = false) {
  if (!(age >= 0) || age >= 1.8) return { dip: 0, roll: 0 };
  const spring = Math.sin(age * 12) * Math.exp(-3 * age);
  return { dip: spring * (gentle ? .045 : .48), roll: spring * (gentle ? .002 : .022) };
}

function islandGeometry(hex=false,cap=true){
  const positions=[],tri=(a,b,c)=>positions.push(...a,...b,...c),count=hex?6:24;
  const ring=(radius,y)=>Array.from({length:count},(_,i)=>{const a=i/count*Math.PI*2;
    const x=hex?Math.cos(a)*.5:Math.sign(Math.cos(a))*Math.pow(Math.abs(Math.cos(a)),.45)*.5;
    const z=hex?Math.sin(a)*.5:Math.sign(Math.sin(a))*Math.pow(Math.abs(Math.sin(a)),.45)*.5;
    return[x*radius,y,z*radius];});
  const rings=[ring(1,.5),ring(.98,.32),ring(.8,-.12),ring(.45,-.5)];
  for(let i=0;i<count;i++){const j=(i+1)%count;if(cap)tri([0,.5,0],rings[0][j],rings[0][i]);
    for(let k=1;k<rings.length;k++){tri(rings[k-1][i],rings[k-1][j],rings[k][j]);tri(rings[k-1][i],rings[k][j],rings[k][i]);}
    tri([0,-.5,0],rings.at(-1)[i],rings.at(-1)[j]);}
  const g=new BufferGeometry();g.setAttribute('position',new Float32BufferAttribute(positions,3));g.computeVertexNormals();g.computeBoundingSphere();return g;
}
function starGeometry(){
  const positions=[],tri=(a,b,c)=>positions.push(...a,...b,...c),points=Array.from({length:10},(_,i)=>{const a=i/10*Math.PI*2+Math.PI/2,r=i%2?.22:.5;return[Math.cos(a)*r,Math.sin(a)*r];});
  for(let i=0;i<10;i++){const j=(i+1)%10,a=points[i],b=points[j];tri([0,0,.5],[...a,.5],[...b,.5]);tri([0,0,-.5],[...b,-.5],[...a,-.5]);tri([...a,.5],[...a,-.5],[...b,-.5]);tri([...a,.5],[...b,-.5],[...b,.5]);}
  const g=new BufferGeometry();g.setAttribute('position',new Float32BufferAttribute(positions,3));g.computeVertexNormals();g.computeBoundingSphere();return g;
}
function spiralGeometry(){
  const P=[],N=[],U=[],I=[],steps=240;
  for(let i=0;i<=steps;i++){const t=i/steps,a=t*Math.PI*6.2,r=.04+t*.4;
    for(const side of[-1,1]){const width=.019,radius=r+side*width;P.push(Math.cos(a)*radius,Math.sin(a)*radius,0);N.push(0,0,1);U.push(t,(side+1)/2);}
    if(i){const j=(i-1)*2;I.push(j,j+1,j+3,j,j+3,j+2);}
  }
  const g=new BufferGeometry();g.setAttribute('position',new Float32BufferAttribute(P,3));g.setAttribute('normal',new Float32BufferAttribute(N,3));g.setAttribute('uv',new Float32BufferAttribute(U,2));g.setIndex(I);g.computeBoundingSphere();return g;
}

/** A tiled front, tiled back and perimeter walls, with no overlapping colored caps. */
export function goonworksGearGeometry(){
  const P=[],A=[],count=96,hub=.355;
  const tri=(a,b,c,tone)=>{P.push(...a,...b,...c);A.push(tone,tone,tone);};
  const point=(i,r,z)=>{const a=i/count*Math.PI*2;return[Math.cos(a)*r,Math.sin(a)*r,z];};
  const radius=i=>[.405,.405,.5,.5,.5,.5,.405,.405][i%8];
  for(let i=0;i<count;i++){const j=(i+1)%count;
    for(const side of[-1,1]){const z=side*.5,a=point(i,hub,z),b=point(j,hub,z),c=point(i,radius(i),z),d=point(j,radius(j),z);
      if(side>0){tri([0,0,z],a,b,0);tri(a,c,d,1);tri(a,d,b,1);}
      else{tri([0,0,z],b,a,0);tri(a,d,c,1);tri(a,b,d,1);}
    }
    const a=point(i,radius(i),.5),b=point(j,radius(j),.5),c=point(i,radius(i),-.5),d=point(j,radius(j),-.5);
    tri(a,c,d,1);tri(a,d,b,1);
  }
  const g=new BufferGeometry();g.setAttribute('position',new Float32BufferAttribute(P,3));g.setAttribute('goonworksAccent',new Float32BufferAttribute(A,1));g.computeVertexNormals();g.computeBoundingSphere();return g;
}

/** The shared depth-capped road owns the volume. This skin only paints its sides and underside. */
export function goonworksRibbonShell(track){
  const source=track.meshes.road,P=new Float32Array(source.positions),N=source.normals,U=source.uvs,I=[],types=new Int8Array(P.length/3);
  for(let i=0;i<types.length;i++){
    const k=i*3,f=track.frameAt(U[i*2+1]),up=N[k]*f.n[0]+N[k+1]*f.n[1]+N[k+2]*f.n[2];
    if(up>.15){types[i]=0;continue;}types[i]=up<-.15?2:1;
  }
  for(let i=0;i<source.indices.length;i+=3){const a=source.indices[i],b=source.indices[i+1],c=source.indices[i+2];
    if(types[a]&&types[b]&&types[c])I.push(a,b,c);}
  return{positions:P,normals:new Float32Array(N),uvs:new Float32Array(U),indices:new Uint32Array(I)};
}

export function buildGoonworks(track,art=null,pal=null,samples=null,{shareHulls=true}={}){
  const plan=goonworksWorldPlan(track),group=new Group();group.name='goonworks-funhouse';
  if(!plan)return{group,update(){},hit(){},reset(){},dispose(){},stats:{},casters:[]};
  const geometries={box:new BoxGeometry(1,1,1),sphere:new SphereGeometry(.5,20,12),
    cylinder:new CylinderGeometry(.5,.5,1,20),cone:new CylinderGeometry(.48,.2,1,20),
    torus:new TorusGeometry(.48,.02,6,40),arch:new TorusGeometry(.48,.025,8,40,Math.PI),star:starGeometry(),gear:goonworksGearGeometry(),spiral:spiralGeometry(),island:islandGeometry(),openIsland:islandGeometry(false,false),hexIsland:islandGeometry(true),spark:new OctahedronGeometry(.5)};
  const materials={};for(const[name,hex]of Object.entries(C))materials[name]=createGoonworksMaterial(hex,{ink:name==='ink',paper:GOONWORKS_PAPER_MATERIALS.includes(name),unlit:name.startsWith('distant')});
  materials.gear=createGoonworksMaterial(C.ivory,{paper:true});materials.gear.side=FrontSide;
  materials.gear.colorNode=mix(materials.gear.colorNode,materials.gold.colorNode,attribute('goonworksAccent','float'));
  const gearPaint=createGearSpiralPaint(materials.gear,.5,4);
  const ink=createGoonworksMaterial(C.ink,{outline:true}),owned=[ink],extraGeometry=[];
  function upload(data){const g=new BufferGeometry();g.setAttribute('position',new BufferAttribute(data.positions,3));g.setAttribute('normal',new BufferAttribute(data.normals,3));g.setAttribute('uv',new BufferAttribute(data.uvs,2));g.setIndex(new BufferAttribute(data.indices,1));g.computeBoundingSphere();extraGeometry.push(g);return g;}
  const M=new Matrix4(),B=new Matrix4(),P=new Vector3(),S=new Vector3(),rotation=new Quaternion(),X=new Vector3(),Y=new Vector3(),Z=new Vector3();
  const batches=new Map(),moving=[];
  // Static ink hulls share one flat outline material, so one instanced draw per SHAPE carries every
  // colour's hull (Firefox pays per draw). Moving hulls stay with their batch for the spin update.
  const sharedHulls=new Map();
  for(const row of plan.rows){const key=row.shape+':'+row.material+(row.motion?':moving':'');if(!batches.has(key))batches.set(key,[]);batches.get(key).push(row);}
  for(const[key,rows]of batches){const[shape,material]=key.split(':'),geo=geometries[shape],mesh=new InstancedMesh(geo,materials[material],rows.length);
    mesh.name='goonworks-'+key.replaceAll(':','-');group.add(mesh);
    const outlined=rows.filter(r=>r.outline&&r.material!=='ink'&&r.material!=='distant');
    const share=shareHulls&&!key.endsWith(':moving');
    const hull=outlined.length&&!share?new InstancedMesh(geo,ink,outlined.length):null;if(hull){hull.name=mesh.name+'-ink';group.add(hull);}
    if(share&&outlined.length&&!sharedHulls.has(shape))sharedHulls.set(shape,[]);
    let h=0;
    rows.forEach((row,index)=>{B.makeBasis(X.fromArray(row.axes[0]),Y.fromArray(row.axes[1]),Z.fromArray(row.axes[2]));rotation.setFromRotationMatrix(B);P.fromArray(row.p);S.fromArray(row.size);mesh.setMatrixAt(index,M.compose(P,rotation,S));
      let hullIndex=-1;if(row.outline&&row.material!=='ink'&&row.material!=='distant'){S.set(row.size[0]+.45,row.size[1]+.45,row.size[2]+.45);
        if(share)sharedHulls.get(shape).push(M.compose(P,rotation,S).clone());else{hullIndex=h++;hull.setMatrixAt(hullIndex,M.compose(P,rotation,S));}}
      if(row.motion)moving.push({row,mesh,index,hull,hullIndex,rest:rotation.clone()});});
    mesh.instanceMatrix.needsUpdate=true;mesh.computeBoundingSphere();if(hull){hull.instanceMatrix.needsUpdate=true;hull.computeBoundingSphere();}
    if(key.endsWith(':moving')){mesh.boundingSphere.radius+=10;if(hull)hull.boundingSphere.radius+=10;}
  }
  for(const[shape,list]of sharedHulls){const hull=new InstancedMesh(geometries[shape],ink,list.length);hull.name='goonworks-'+shape+'-ink-hulls';
    list.forEach((m,i)=>hull.setMatrixAt(i,m));hull.instanceMatrix.needsUpdate=true;hull.computeBoundingSphere();group.add(hull);}
  // An opaque skirt gives the white ribbon a visible deep fuchsia thickness at every crossing.
  const data=goonworksRibbonShell(track),shell=new BufferGeometry();
  shell.setAttribute('position',new BufferAttribute(data.positions,3));shell.setAttribute('normal',new BufferAttribute(data.normals,3));shell.setAttribute('uv',new BufferAttribute(data.uvs,2));shell.setIndex(new BufferAttribute(data.indices,1));shell.computeBoundingSphere();extraGeometry.push(shell);
  const shellMat=createGoonworksMaterial(C.fuchsia),up=normalize(normalWorld).y;
  // This mesh contains outward side and underside triangles only. Its back faces must never
  // compete with the white driving surface when a distant camera quantizes their depth together.
  shellMat.side=FrontSide;
  shellMat.polygonOffset=true;shellMat.polygonOffsetFactor=-1;shellMat.polygonOffsetUnits=-1;
  const sideInk=float(1).sub(smoothstep(.015,.042,abs(uv().x))).mul(float(1).sub(smoothstep(.3,.7,abs(up))));
  shellMat.colorNode=mix(shellMat.colorNode,vec3(.009,.002,.012),sideInk);owned.push(shellMat);
  const ribbon=new Mesh(shell,shellMat);ribbon.name='goonworks-thick-ribbon';group.add(ribbon);
  // Its whole-course box fills empty air. The fitter reserves the sampled road corridor.
  ribbon.userData.screenPlacementIgnore=true;

  for(const outer of[false,true]){const data=goonworksTubeShell(track,outer);if(!data)continue;
    const material=createGoonworksTubeMaterial(outer),tube=new Mesh(upload(data),material);owned.push(material);
    tube.name=outer?'goonworks-rabbit-outer-shell':'goonworks-rabbit-inner-shell';
    // One set of wall proxies covers both skins without filling the hollow interior.
    if(outer)tube.userData.screenPlacementSolids=goonworksTubeSolids(data);
    else tube.userData.screenPlacementIgnore=true;group.add(tube);}

  // Every broad field platform is one whole deck, never a repetition of the small stone kit.
  const platformGroups=[];
  const platformClearance=visualClearanceSamples(track);
  for(const row of plan.platforms){const holder=new Group();holder.name='goonworks-platform-'+row.id;
    B.makeBasis(X.fromArray(row.b),Y.fromArray(row.n),Z.fromArray(row.t).negate());holder.quaternion.setFromRotationMatrix(B);holder.position.fromArray(row.p);
    const capMaterial=createGoonworksPlatformMaterial(row.w,row.len,platformGroups.length);owned.push(capMaterial);
    const top=new Mesh(upload(goonworksPlatformGeometry(row.w,row.len)),capMaterial);top.name='single-surface-platform-cap';holder.add(top);
    // Retain the rounded glazed island, extending its keel down from the cap's existing bottom.
    const depthFrame={...row,s:row.s+row.len/2,p:row.p.map((v,i)=>v-row.n[i]*.6)};
    row.depthM=capVisualDepth(depthFrame,Math.min(10,Math.max(7.8,Math.min(row.w,row.len)*.4)),platformClearance,
      {halfWidth:row.w/2,halfLength:row.len/2,motionM:.65,ignoreId:row.id});
    const skirt=new Mesh(geometries.openIsland,materials[platformGroups.length%3===0?'magenta':platformGroups.length%3===1?'fuchsia':'rose']);
    skirt.name='goonworks-platform-keel';skirt.scale.set(row.w-.3,row.depthM,row.len-.3);
    skirt.position.y=-.6-row.depthM/2;skirt.visible=row.depthM>.05;holder.add(skirt);
    group.add(holder);platformGroups.push({row,holder,rest:holder.position.clone(),restRotation:holder.quaternion.clone(),age:Infinity});
  }
  // The sky carries static pale ink silhouettes, not a rainbow spiral or a distant ground plane.
  const skyG=new SphereGeometry(plan.skyRadius,32,18),skyM=new MeshBasicNodeMaterial({side:BackSide,depthWrite:false,fog:false,toneMapped:false});
  skyM.colorNode=mix(vec3(.62,.51,.69),vec3(.86,.8,.78),smoothstep(plan.center[1]-250,plan.center[1]+350,positionWorld.y));
  const sky=new Mesh(skyG,skyM);sky.position.fromArray(plan.center);sky.name='goonworks-blush-atmosphere';sky.renderOrder=-2;sky.frustumCulled=false;group.add(sky);extraGeometry.push(skyG);owned.push(skyM);
  sky.userData.screenPlacementIgnore=true;
  const sparkleCount=Math.min(Q.name==='phone'?450:1900,plan.particles.length*11),sparkle=new InstancedMesh(geometries.spark,materials.white,sparkleCount);
  sparkle.name='goonworks-ink-sparkles';group.add(sparkle);
  const bubblesCount=Math.min(Q.name==='phone'?80:260,plan.particles.length*2),bubbles=new InstancedMesh(geometries.torus,materials.cream,bubblesCount);bubbles.name='goonworks-floating-bubbles';group.add(bubbles);
  let time=0,drive=0,lastBeat=null,disposed=false;const spin=new Quaternion(),axis=new Vector3();
  function update(dt=0,view={},gentle=!!view.gentle){
    if(disposed||view.paused)return;const step=Math.max(0,Math.min(dt,1)),speed=gentle?.45:1;time+=step*speed;
    const beat=machineryBeatAt(track,view.raceTimeS??view.timeS),nextBeat=beat&&!beat.safe?beat.beat:null;
    // Integrate beat travel so pickup, seeks and Gentle changes never reset decorative gear angles.
    const advance=nextBeat!==null&&lastBeat!==null&&nextBeat>=lastBeat&&nextBeat-lastBeat<2
      ?(gearBeatTravel(nextBeat)-gearBeatTravel(lastBeat))*beat.beatS:step;
    drive+=advance*speed;lastBeat=nextBeat;
    for(const item of moving){const{row,mesh,index,hull,hullIndex}=item;axis.fromArray(row.axes[row.motion.axis??1]);spin.setFromAxisAngle(axis,drive*row.motion.spin+row.motion.phase);rotation.copy(spin).multiply(item.rest);P.fromArray(row.p);S.fromArray(row.size);composeInto(mesh.instanceMatrix.array,index,P,rotation,S);mesh.instanceMatrix.needsUpdate=true;
      if(hullIndex>=0){S.set(row.size[0]+.45,row.size[1]+.45,row.size[2]+.45);composeInto(hull.instanceMatrix.array,hullIndex,P,rotation,S);hull.instanceMatrix.needsUpdate=true;}}
    const sparkleArray=sparkle.instanceMatrix.array,bubbleArray=bubbles.instanceMatrix.array;
    for(let i=0;i<sparkleCount;i++){const seed=plan.particles[Math.floor(i/11)%plan.particles.length],phase=i*2.399963+seed.phase,rise=((time*1.4+phase*2)%25+25)%25;
      P.fromArray(seed.p);P.x+=Math.sin(phase+time*.09)*seed.spread;P.z+=Math.cos(phase+time*.07)*seed.spread;P.y+=rise;
      const fade=Math.min(1,rise/3,(25-rise)/3),size=(.22+i%4*.12)*fade;S.set(size,size*(i%4===0?3:1),size);rotation.setFromAxisAngle(Y.set(0,1,0),phase+time*.2);composeInto(sparkleArray,i,P,rotation,S);}
    sparkle.instanceMatrix.needsUpdate=true;
    for(let i=0;i<bubblesCount;i++){const seed=plan.particles[Math.floor(i/2)%plan.particles.length],phase=i*1.74,rise=((time*2+phase*4)%36+36)%36;
      P.fromArray(seed.p);P.x+=Math.sin(phase)*seed.spread;P.z+=Math.cos(phase)*seed.spread;P.y+=rise;const size=(1.4+i%3*.75)*Math.min(1,rise/3,(36-rise)/3);S.set(size,size,size);
      rotation.setFromAxisAngle(Y.set(0,1,0),phase+time*.15);composeInto(bubbleArray,i,P,rotation,S);}bubbles.instanceMatrix.needsUpdate=true;
    for(const platform of platformGroups){if(!Number.isFinite(platform.age))continue;platform.age+=step;const a=platform.age;
      const feedback=platformFeedback(a,gentle);platform.holder.position.copy(platform.rest).addScaledVector(axis.fromArray(platform.row.n),-feedback.dip);
      spin.setFromAxisAngle(axis.set(0,0,1),feedback.roll);platform.holder.quaternion.copy(platform.restRotation).multiply(spin);
      if(a>=1.8)platform.age=Infinity;}
  }
  update();sparkle.computeBoundingSphere();bubbles.computeBoundingSphere();if(sparkle.boundingSphere)sparkle.boundingSphere.radius+=50;if(bubbles.boundingSphere)bubbles.boundingSphere.radius+=60;
  const hit=event=>{const id=typeof event==='string'?event:event?.id,platform=platformGroups.find(p=>p.row.id===id);if(platform){platform.age=0;return platform.row;}return null;};
  const reset=()=>{time=0;drive=0;lastBeat=null;for(const p of platformGroups){p.age=Infinity;p.holder.position.copy(p.rest);p.holder.quaternion.copy(p.restRotation);}update();};
  const stats={draws:0,parts:plan.rows.length,rooms:plan.rooms.length,media:plan.mounts.length,platforms:platformGroups.length,sparkles:sparkleCount,bubbles:bubblesCount,rejected:plan.rejected};group.traverse(o=>{if(o.isMesh)stats.draws++;});
  return{group,plan,stats,casters:[],update,hit,reset,platforms:platformGroups,
    dispose(){if(disposed)return;disposed=true;group.removeFromParent();gearPaint.dispose();group.traverse(o=>{if(o.isInstancedMesh)o.dispose();});Object.values(geometries).forEach(g=>g.dispose());extraGeometry.forEach(g=>g.dispose());Object.values(materials).forEach(m=>m.dispose());owned.forEach(m=>m.dispose());}};
}
