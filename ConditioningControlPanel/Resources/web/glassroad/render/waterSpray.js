// Real small water geometry, pooled once before shader warmup. No particle-atlas icons.
import { Group, InstancedMesh, SphereGeometry, BufferGeometry, Float32BufferAttribute, MeshBasicNodeMaterial,
  DynamicDrawUsage, DoubleSide, Matrix4, Quaternion, Vector3 } from 'three/webgpu';
import { vec3, normalWorld, dot, normalize, max, pow, uv, smoothstep, positionView, float } from 'three/tsl';
import { Q } from './quality.js';

export function createWaterSpray({ capacity = Q.name === 'phone' ? 256 : 768, fanCapacity = 32 } = {}) {
  const object = new Group(); object.name = 'water-ballistic-spray';
  const material = new MeshBasicNodeMaterial({ transparent: true, opacity: .38, depthWrite: false });
  const shine=pow(max(dot(normalWorld,normalize(vec3(-.4,.8,.3))),0),10);
  material.colorNode = vec3(.08,.32,.36).add(vec3(.5,.72,.78).mul(shine)); material.toneMapped = false;
  material.opacityNode=float(.38).mul(smoothstep(.8,3,positionView.z.negate()));
  const fanMaterial = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide });
  fanMaterial.colorNode = vec3(.24,.62,.76); fanMaterial.toneMapped = false;
  const edge=smoothstep(0,.18,uv().x).mul(smoothstep(.82,1,uv().x).oneMinus());
  const end=smoothstep(0,.15,uv().y).mul(smoothstep(.6,1,uv().y).oneMinus());
  fanMaterial.opacityNode=float(.12).mul(edge).mul(end).mul(smoothstep(1.5,4,positionView.z.negate()));
  const dropletGeo = new SphereGeometry(1,10,7), fanGeo = new BufferGeometry();
  // A bowed fan, narrow at its source and divided into strips as the liquid breaks apart.
  const pos=[],uvs=[];
  for(let k=0;k<5;k++) {
    const a=-1+k*.4, b=a+.29;
    const vertex=(x,v,u)=>{pos.push(x*(.08+Math.sin(v*2.2)*.55),Math.sin(v*1.7)*.65,v*.8);uvs.push(u,v);};
    for(let j=0;j<6;j++) {const v=j/6,w=(j+1)/6;
      vertex(a,v,0);vertex(b,v,1);vertex(a,w,0);vertex(b,v,1);vertex(b,w,1);vertex(a,w,0);
    }
  }
  fanGeo.setAttribute('position',new Float32BufferAttribute(pos,3));fanGeo.setAttribute('uv',new Float32BufferAttribute(uvs,2));fanGeo.computeVertexNormals();
  const drops=new InstancedMesh(dropletGeo,material,capacity), fans=new InstancedMesh(fanGeo,fanMaterial,fanCapacity);
  drops.name='water-droplets'; fans.name='water-fan-sheets';
  for(const mesh of [drops,fans]) { mesh.frustumCulled=false; mesh.renderOrder=2; mesh.instanceMatrix.setUsage(DynamicDrawUsage); object.add(mesh); }
  const stride=12, data=new Float32Array(capacity*stride), fanData=new Float32Array(fanCapacity*stride);
  const fanRot=Array.from({length:fanCapacity},()=>new Quaternion());
  const matrix=new Matrix4(), zero=new Matrix4().makeScale(0,0,0), p=new Vector3(), v=new Vector3(), scale=new Vector3();
  const rotation=new Quaternion(), up=new Vector3(0,1,0), axis=new Vector3(), normal=new Vector3(), across=new Vector3();
  let cursor=0, fanCursor=0, emitted=0, random=731;
  const rnd=()=>{ random=(Math.imul(random,1664525)+1013904223)>>>0; return random/4294967296; };
  function reset() {
    data.fill(0); fanData.fill(0); cursor=fanCursor=emitted=0;
    for(let i=0;i<capacity;i++)drops.setMatrixAt(i,zero);
    for(let i=0;i<fanCapacity;i++)fans.setMatrixAt(i,zero);
    drops.instanceMatrix.needsUpdate=fans.instanceMatrix.needsUpdate=true;
  }
  reset();
  function emit({ p: origin, n=[0,1,0], b=[1,0,0], t=[0,0,1], dirX=0, dirS=0,
    count=12, lift=3, spread=2, size=.055, life=.85, gentle=false, fan=true }={}) {
    if(!origin)return;
    const amount=Math.min(capacity,Math.max(0,Math.round(count*(gentle?.65:1)))), gain=gentle?.8:1;
    for(let j=0;j<amount;j++) {
      const index=cursor++%capacity, o=index*stride, vx=dirX+(rnd()-.5)*spread, vs=dirS+(rnd()-.5)*spread;
      const rise=lift*(.65+rnd()*.65)*gain;
      for(let k=0;k<3;k++) { data[o+k]=origin[k]+b[k]*(rnd()-.5)*.22; data[o+3+k]=b[k]*vx+t[k]*vs+n[k]*rise; }
      data[o+6]=0;data[o+7]=life*(.7+rnd()*.5);data[o+8]=size*.5*(.65+rnd()*.7);data[o+9]=1;
      emitted++;
    }
    if(fan&&amount) {
      const index=fanCursor++%fanCapacity,o=index*stride;
      for(let k=0;k<3;k++){fanData[o+k]=origin[k];fanData[o+3+k]=b[k]*dirX*.38+t[k]*dirS*.38+n[k]*lift*.65*gain;}
      fanData[o+6]=0;fanData[o+7]=Math.min(.42,life*.6);fanData[o+8]=Math.min(.85,.3+Math.hypot(dirX,dirS)*.07);fanData[o+9]=1;
      normal.fromArray(n).normalize(); axis.fromArray(b).multiplyScalar(dirX).addScaledVector(v.fromArray(t),dirS);
      if(axis.lengthSq()<.01)axis.fromArray(t); axis.addScaledVector(normal,-axis.dot(normal)).normalize(); across.crossVectors(normal,axis).normalize();
      matrix.makeBasis(across,normal,axis);fanRot[index].setFromRotationMatrix(matrix);
    }
    update(0);
  }
  function update(dt,{paused=false,gentle=false}={}) {
    if(paused)return;
    const step=Math.max(0,dt)*(gentle?.5:1);
    for(let i=0;i<capacity;i++) {
      const o=i*stride;if(!data[o+9])continue;
      const age=data[o+6]+=step,life=data[o+7];
      if(age>=life){data[o+9]=0;drops.setMatrixAt(i,zero);continue;}
      p.set(data[o]+data[o+3]*age,data[o+1]+data[o+4]*age-4.9*age*age,data[o+2]+data[o+5]*age);
      v.set(data[o+3],data[o+4]-9.8*age,data[o+5]);
      const speed=v.length(),fade=Math.min(1,(life-age)/.18),r=data[o+8]*fade;
      if(speed>.001)rotation.setFromUnitVectors(up,v.multiplyScalar(1/speed));else rotation.identity();
      scale.set(r,r*Math.min(1.8,1+speed*.08),r);matrix.compose(p,rotation,scale);drops.setMatrixAt(i,matrix);
    }
    for(let i=0;i<fanCapacity;i++) {
      const o=i*stride;if(!fanData[o+9])continue;
      const age=fanData[o+6]+=step,life=fanData[o+7],k=age/life;
      if(k>=1){fanData[o+9]=0;fans.setMatrixAt(i,zero);continue;}
      p.set(fanData[o]+fanData[o+3]*age,fanData[o+1]+fanData[o+4]*age-4.9*age*age,fanData[o+2]+fanData[o+5]*age);
      const fade=Math.min(1,(1-k)*3),grow=.3+k*.85;
      scale.set(fanData[o+8]*grow*fade, (.2+Math.sin(k*Math.PI)*.8)*fade,grow*fade);
      matrix.compose(p,fanRot[i],scale);fans.setMatrixAt(i,matrix);
    }
    drops.instanceMatrix.needsUpdate=fans.instanceMatrix.needsUpdate=true;
  }
  return {object,drops,fans,data,fanData,capacity,fanCapacity,emit,update,reset,
    stats:()=>({emitted,active:data.reduce((n,v,i)=>n+(i%stride===9&&v?1:0),0),fanActive:fanData.reduce((n,v,i)=>n+(i%stride===9&&v?1:0),0)}),
    dispose(){reset();drops.dispose();fans.dispose();dropletGeo.dispose();fanGeo.dispose();material.dispose();fanMaterial.dispose();object.removeFromParent();} };
}
