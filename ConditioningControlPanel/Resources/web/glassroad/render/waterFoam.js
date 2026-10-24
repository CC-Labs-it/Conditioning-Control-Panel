// Small three-dimensional foam clusters, carried by the simulated crests and gravity-fed spills.
import { InstancedMesh, IcosahedronGeometry, MeshBasicNodeMaterial, Object3D, Vector3 } from 'three/webgpu';
import { vec3, dot, normalWorld, normalize, max } from 'three/tsl';

const hash=n=>{const q=Math.sin(n*127.1+311.7)*43758.5453;return q-Math.floor(q);};
export function createWaterFoam(track,field,spills,{quality='mid'}={}) {
  const capacity=quality==='phone'?72:240,parts=3,geometry=new IcosahedronGeometry(1,1);
  const material=new MeshBasicNodeMaterial({depthWrite:true});
  const light=max(dot(normalize(normalWorld),normalize(vec3(.35,1,.4))),0);
  material.colorNode=vec3(.28,.42,.45).add(vec3(.62,.55,.49).mul(light));
  const object=new InstancedMesh(geometry,material,capacity*parts);
  object.name='water-dimensional-foam';object.frustumCulled=false;object.count=0;object.userData.noCast=true;
  const particles=[],lastCell=new Map(),dummy=new Object3D(),up=new Vector3(0,1,0),normal=new Vector3();
  const sample={},stats={active:0,surface:0,spill:0,emitted:0,capacity};
  let clock=0,emitClock=0,cursor=0,sequence=0;
  function contact(s,x) {
    const f=track.frameAt(s);
    return track.surfaceAt?track.surfaceAt(s,x):{...f,p:f.p.map((v,k)=>v+f.b[k]*x)};
  }
  function spawn(p) {if(particles.length>=capacity)return;particles.push(p);stats.emitted++;}
  function emit(playerS) {
    let quota=quality==='phone'?5:14;
    for(const r of field.runs) {
      if(playerS<r.fromS-55||playerS>r.toS+65)continue;
      const first=Math.max(0,Math.floor((playerS-42-r.fromS)/r.ds)),last=Math.min(r.ny-1,Math.ceil((playerS+34-r.fromS)/r.ds));
      if(last<first)continue;
      const cells=(last-first+1)*r.nx;
      for(let pick=0;pick<(quality==='phone'?110:240)&&quota>0;pick++) {
        const choice=(cursor++*37)%cells,row=first+Math.floor(choice/r.nx),col=choice%r.nx;
        const cell=(r.rowOffset+row)*field.width+col,offset=cell*4;
        if(field.pixels[offset+3]<20||field.pixels[offset]<112||clock-(lastCell.get(cell)??-10)<.55)continue;
        const seed=++sequence,s=r.fromS+row*r.ds,x=(col/(r.nx-1)-.5)*r.widths[row];
        lastCell.set(cell,clock);quota--;
        spawn({s,x,seed,age:0,life:1.4+hash(seed)*1.7,fall:false,radius:.04+hash(seed+4)*.06});
      }
    }
    for(const spill of spills) {
      if(playerS<spill.fromS-75||playerS>spill.toS+90)continue;
      for(let j=0;j<(quality==='phone'?1:3);j++) {
        const seed=++sequence,s=spill.fromS+hash(seed)*(spill.toS-spill.fromS),f=track.frameAt(s);
        const x=spill.side*(f.w/2-.08),c=contact(s,x),h=field.sample(s,x,sample).height;
        spawn({s,x,seed,age:0,life:Math.min(3.2,Math.sqrt(spill.dropM/4.9)),fall:true,radius:.07+hash(seed+2)*.07,
          p:c.p.map((v,k)=>v+c.n[k]*(.14+h)),v:[c.b[0]*spill.side*(.8+hash(seed)),.2,c.b[2]*spill.side*(.8+hash(seed))]});
      }
    }
    for(const [key,time] of lastCell)if(clock-time>4)lastCell.delete(key);
  }
  function draw(dt) {
    let instance=0;stats.surface=stats.spill=0;
    for(let i=particles.length-1;i>=0;i--) {
      const p=particles[i];p.age+=dt;
      if(p.age>=p.life){particles.splice(i,1);continue;}
      let position,n,b,t;
      if(p.fall) {
        p.v[1]-=9.8*dt;for(let k=0;k<3;k++)p.p[k]+=p.v[k]*dt;
        position=p.p;n=[0,1,0];b=[1,0,0];t=[0,0,1];stats.spill++;
      } else {
        const frame=track.frameAt(p.s),flow=frame.flow||track.flowAt?.(frame.seg,frame.segT||0,[0,0])||[.4,0];
        p.s+=flow[0]*dt*.55;p.x+=flow[1]*dt*.55;
        const state=field.sample(p.s,p.x,sample);
        if(!state.wet){particles.splice(i,1);continue;}
        const c=contact(p.s,p.x);n=c.n;b=c.b;t=c.t;
        position=c.p.map((v,k)=>v+n[k]*(.1+state.height));stats.surface++;
      }
      const fade=Math.min(1,p.age/.12,(p.life-p.age)/.5),r=p.radius*Math.max(0,fade);
      normal.set(...n);dummy.quaternion.setFromUnitVectors(up,normal.normalize());
      for(let part=0;part<parts;part++) {
        const a=part*2.4+p.seed,offset=part===0?0:r*.85;
        const bx=Math.cos(a)*offset,tz=Math.sin(a)*offset,ny=p.fall?r*.38+part*r*.13:r*.1;
        dummy.position.set(...position.map((v,k)=>v+b[k]*bx+t[k]*tz+n[k]*ny));
        const size=r*(1-part*.19);dummy.scale.set(size,size*(p.fall?.9:.35),size*(1.1+hash(p.seed+part)*.5));
        dummy.updateMatrix();object.setMatrixAt(instance++,dummy.matrix);
      }
    }
    object.count=instance;object.instanceMatrix.needsUpdate=true;stats.active=particles.length;
  }
  return {object,stats,
    update(dt,{paused=false,gentle=false,playerS=0}={}) {
      if(paused)return;
      dt=Math.min(.1,Math.max(0,dt))*(gentle?.5:1);clock+=dt;emitClock+=dt;
      if(emitClock>=.09){emitClock=0;emit(playerS);}draw(dt);
    },
    reset(){particles.length=0;lastCell.clear();object.count=0;stats.active=stats.surface=stats.spill=0;clock=emitClock=0;},
    dispose(){object.dispose();geometry.dispose();material.dispose();particles.length=0;lastCell.clear();},
  };
}
