// World-space exhaust bubbles and suspended sediment. Every pool exists before shader warmup.
import { Group, InstancedMesh, SphereGeometry, MeshBasicNodeMaterial, Object3D, Vector3, Quaternion, DynamicDrawUsage } from 'three/webgpu';
import { normalView, positionViewDirection, positionView, smoothstep, dot, abs, pow, float, vec3, normalWorld, normalize, max } from 'three/tsl';
import { createImmersion } from '../track/immersion.js';

export function createSubmergedBubbleMaterial() {
  const bubbleMat=new MeshBasicNodeMaterial({transparent:true,depthWrite:false});
  const rim=pow(float(1).sub(abs(dot(normalView,positionViewDirection))),2.6);
  const glint=pow(max(dot(normalWorld,normalize(vec3(-.4,.8,.5))),0),28);
  bubbleMat.colorNode=vec3(.28,.65,.78).add(vec3(.5,.65,.7).mul(rim)).add(glint.mul(.65));
  bubbleMat.opacityNode=float(.035).add(rim.mul(.58)).add(glint.mul(.3)).mul(smoothstep(.5,2,positionView.z.negate()));
  return bubbleMat;
}
export function createUnderwaterFx(track,{quality='mid'}={}) {
  const object=new Group();object.name='underwater-current-and-exhaust';
  const wet=createImmersion(track), waterY=track.reservoir?.waterY, phone=quality==='phone';
  const bubbleMat=createSubmergedBubbleMaterial();
  const moteMat=new MeshBasicNodeMaterial({transparent:true,opacity:.32,depthWrite:false,color:0x9ad5db});
  const makePool=(capacity,material,name)=>{
    const bubble=name==='propeller-bubbles';
    const geometry=new SphereGeometry(1,bubble?(phone?10:16):5,bubble?(phone?7:10):5),mesh=new InstancedMesh(geometry,material,capacity);
    mesh.name=name;mesh.count=0;mesh.frustumCulled=false;mesh.userData.noCast=true;mesh.renderOrder=2;
    mesh.instanceMatrix.setUsage(DynamicDrawUsage);object.add(mesh);
    return {mesh,geometry,capacity,data:new Float32Array(capacity*12),cursor:0,emitted:0,active:0};
  };
  const bubbles=makePool(phone?160:512,bubbleMat,'propeller-bubbles'),motes=makePool(phone?120:400,moteMat,'underwater-suspended-particles');
  const dummy=new Object3D(),origin=new Vector3(),direction=new Vector3(),rotation=new Quaternion();
  const memory=new Map(),stats={bubbles:0,motes:0,emitted:0,emitters:0},surface=track.sections?.find(s=>s.id==='submerged');
  let random=191,clock=0,moteClock=0;
  const rnd=()=>((random=(Math.imul(random,1664525)+1013904223)>>>0)/4294967296);
  function spawn(pool,p,v,radius,life) {
    const o=(pool.cursor++%pool.capacity)*12,d=pool.data;
    d.set([...p,...v,0,life,radius,rnd()*6.283,1,0],o);pool.emitted++;
  }
  function step(pool,dt,bubble) {
    let count=0;const d=pool.data;
    for(let i=0;i<pool.capacity;i++) {
      const o=i*12;if(!d[o+10])continue;
      const age=d[o+6]+=dt,life=d[o+7];
      if(age>=life||d[o+1]>=waterY-.06){d[o+10]=0;continue;}
      const drag=Math.exp(-dt*(bubble?1.1:.45));d[o+3]*=drag;d[o+5]*=drag;
      d[o+4]=Math.min(bubble?1.65:.3,d[o+4]+dt*(bubble?.75:.025));
      d[o]+=dt*(d[o+3]+Math.sin(clock*1.3+d[o+9])*(bubble?.16:.22));
      d[o+1]+=d[o+4]*dt;d[o+2]+=dt*(d[o+5]+Math.cos(clock*.9+d[o+9])*.16);
      const fade=Math.min(1,age/.16,(life-age)/.7,Math.max(0,(waterY-d[o+1])/.5));
      const r=d[o+8]*Math.max(0,fade)*(bubble?1+age*.12:1);
      dummy.position.set(d[o],d[o+1],d[o+2]);dummy.scale.set(r,r*(bubble?1.06:.55),r);
      dummy.updateMatrix();pool.mesh.setMatrixAt(count++,dummy.matrix);
    }
    pool.mesh.count=count;pool.mesh.instanceMatrix.needsUpdate=true;pool.active=count;
  }
  function update(dt,{karts=[],playerS=0,playerPos=null,paused=false,gentle=false}={}) {
    if(paused||!surface||!Number.isFinite(waterY))return;
    dt=Math.min(.1,Math.max(0,dt))*(gentle?.5:1);clock+=dt;stats.emitters=0;
    for(const k of karts) {
      const prop=k.propeller,root=k.root;
      let row=memory.get(k);if(!row){row={time:0,previous:root.position.clone()};memory.set(k,row);}
      const speed=dt>0?Math.min(45,root.position.distanceTo(row.previous)/dt):0;row.previous.copy(root.position);
      if(!root.visible||!prop||k.underwater<=.01||prop.motion.rpm<5||(playerPos&&root.position.distanceTo(playerPos)>90)){row.time=0;continue;}
      prop.rotor.getWorldPosition(origin);prop.object.getWorldQuaternion(rotation);direction.set(0,0,1).applyQuaternion(rotation);
      if(origin.y>=waterY-.1){row.time=0;continue;}
      stats.emitters++;row.time+=dt*(phone?16:32)*(prop.motion.rpm/320)*(1+speed/35);
      const n=Math.min(10,Math.floor(row.time));row.time-=n;
      for(let j=0;j<n;j++)spawn(bubbles,[origin.x+(rnd()-.5)*.18,origin.y+(rnd()-.5)*.18,origin.z+(rnd()-.5)*.18],
        [direction.x*(2.5+speed*.08)+(rnd()-.5)*.5,direction.y*2.5+.35,direction.z*(2.5+speed*.08)+(rnd()-.5)*.5],.045+rnd()*.075,2.8+rnd()*1.8);
    }
    if(playerPos&&wet(playerS,playerPos.y)>.01) {
      moteClock+=dt*(phone?24:70);
      for(let n=Math.min(12,Math.floor(moteClock));n>0;n--) {
        moteClock--;const s=playerS+(rnd()-.35)*100,f=track.frameAt(s),x=(rnd()-.5)*Math.min(f.w+8,50);
        const y=f.p[1]+.7+rnd()*Math.max(0,waterY-f.p[1]-1.4);
        if(wet(s,y)<.05)continue;
        spawn(motes,[f.p[0]+f.b[0]*x,y,f.p[2]+f.b[2]*x],[(rnd()-.5)*.15,.04+rnd()*.12,(rnd()-.5)*.15],.018+rnd()*.038,5+rnd()*4);
      }
    } else moteClock=0;
    step(bubbles,dt,true);step(motes,dt,false);
    stats.bubbles=bubbles.active;stats.motes=motes.active;stats.emitted=bubbles.emitted;
  }
  function reset(){for(const pool of [bubbles,motes]){pool.data.fill(0);pool.mesh.count=0;pool.cursor=pool.emitted=pool.active=0;}memory.clear();clock=moteClock=0;Object.assign(stats,{bubbles:0,motes:0,emitted:0,emitters:0});}
  return {object,stats,bubbles,motes,update,reset,
    dispose(){reset();for(const pool of [bubbles,motes]){pool.mesh.dispose();pool.geometry.dispose();pool.mesh.material.dispose();}object.removeFromParent();},
  };
}
