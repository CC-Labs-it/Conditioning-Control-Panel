/* Physical light fittings and rotating Loom cores. One existing frame clock, shared finishes, no lights. */
import {Mesh,Group,BufferGeometry,Float32BufferAttribute,MeshBasicMaterial,MeshBasicNodeMaterial,MeshStandardMaterial,
  CircleGeometry,CylinderGeometry,Vector3,Color} from 'three/webgpu';
import {texture,uv,uniform} from 'three/tsl';

const finishes = new Map(), loomFinishes = new WeakMap();
const THEMES = {
  pendulum:{color:'#e8b56a',x:.28,y:.25,radius:.055,petals:12,rate:.48},
  feedback:{color:'#79dfd8',y:.335,petals:9,rate:.65},
  reverie:{color:'#b7dbff',y:.28,petals:5,rate:.32},
  undertow:{color:'#67d6c6',y:.27,petals:3,rate:-.55},
  checkmate:{color:'#e8b56a',y:.31,petals:4,rate:.40},
  'house-edge':{color:'#efc274',y:.31,petals:10,rate:.52},
  lovebomb:{color:'#ff9db7',y:.27,petals:8,rate:.36},
  'velvet-lock':{color:'#e8a4cb',x:.43,y:.32,petals:6,rate:.30},
};
/** Exact rear surface at an XY station; moving ornaments and the saucer cannot support a fitting. */
export function rearShellSurface(meshes,x,y) {
  let rear=-Infinity;
  for(const mesh of meshes) {
    const g=mesh.geometry,p=g.getAttribute('position'),life=g.getAttribute('_life')||g.getAttribute('_LIFE');
    const joints=g.getAttribute('skinIndex'),cup=mesh.skeleton?.bones.findIndex(b=>b.name==='cup_body');
    if(!g.index||!p||!joints||cup<0)continue;
    const indices=g.index.array;
    for(let k=0;k<indices.length;k+=3) {
      const a=indices[k],b=indices[k+1],c=indices[k+2];
      if((life&&(life.getX(a)||life.getX(b)||life.getX(c)))||joints.getX(a)!==cup||joints.getX(b)!==cup||joints.getX(c)!==cup)continue;
      const ax=p.getX(a),ay=p.getY(a),bx=p.getX(b)-ax,by=p.getY(b)-ay,cx=p.getX(c)-ax,cy=p.getY(c)-ay;
      const determinant=bx*cy-by*cx;if(Math.abs(determinant)<1e-12)continue;
      const dx=x-ax,dy=y-ay,u=(dx*cy-dy*cx)/determinant,v=(bx*dy-by*dx)/determinant;
      if(u>=-1e-6&&v>=-1e-6&&u+v<=1.000001)rear=Math.max(rear,p.getZ(a)+u*(p.getZ(b)-p.getZ(a))+v*(p.getZ(c)-p.getZ(a)));
    }
  }
  return rear;
}
function material(key) {
  if(!finishes.has(key)) finishes.set(key,key==='glow'?new MeshBasicMaterial({vertexColors:true}):
    new MeshStandardMaterial({color:key==='mount'?'#302d35':'#cbb89c',metalness:.72,roughness:.30}));
  return finishes.get(key);
}
function loomMaterial(mask,color) {
  let pool=loomFinishes.get(mask);if(!pool){pool=new Map();loomFinishes.set(mask,pool);}
  if(!pool.has(color)) {
    const m=new MeshBasicNodeMaterial(),c=uniform(new Color(color));
    m.colorNode=c.mul(texture(mask,uv()).r.mul(.75).add(.13));pool.set(color,m);
  }
  return pool.get(color);
}
function rotorGeometry(petals,radius) {
  const p=[],n=petals*4;
  for(let i=0;i<n;i++) {
    const a=i*Math.PI*2/n,b=(i+1)*Math.PI*2/n;
    const r=radius*(i%4===0||i%4===3?1:.80),s=radius*((i+1)%4===0||(i+1)%4===3?1:.80);
    p.push(0,0,0,Math.cos(a)*r,Math.sin(a)*r,0,Math.cos(b)*s,Math.sin(b)*s,0);
  }
  const g=new BufferGeometry();g.setAttribute('position',new Float32BufferAttribute(p,3));g.computeVertexNormals();return g;
}
function lightRing(radius,color) {
  const p=[],colors=[],c=new Color(color),n=40;
  for(let i=0;i<n;i++){
    const a=i*Math.PI*2/n,b=(i+1)*Math.PI*2/n,r=radius-.006;
    p.push(Math.cos(a)*r,Math.sin(a)*r,0,Math.cos(a)*radius,Math.sin(a)*radius,0,Math.cos(b)*radius,Math.sin(b)*radius,0,
      Math.cos(a)*r,Math.sin(a)*r,0,Math.cos(b)*radius,Math.sin(b)*radius,0,Math.cos(b)*r,Math.sin(b)*r,0);
    for(let j=0;j<6;j++)colors.push(c.r,c.g,c.b);
  }
  const g=new BufferGeometry();g.setAttribute('position',new Float32BufferAttribute(p,3));g.setAttribute('color',new Float32BufferAttribute(colors,3));return g;
}
/** Returns null when the shared Loom mask is absent, rather than inventing a replacement spiral. */
export function createVehicleAccents(meshes,vehicleId,{art}={}) {
  const theme=THEMES[vehicleId],mask=art?.tex?.('sky-spiral-open-mask');
  if(!theme || !mask || !meshes.length)return null;
  const mesh=meshes.find(m=>m.skeleton?.bones.some(b=>b.name==='cup_body'));if(!mesh)return null;
  const boneIndex=mesh.skeleton.bones.findIndex(b=>b.name==='cup_body'),bone=mesh.skeleton.bones[boneIndex];
  const inverse=mesh.skeleton.boneInverses[boneIndex].clone().multiply(mesh.bindMatrix);
  const x=theme.x||0,rear=rearShellSurface(meshes,x,theme.y);
  if(!Number.isFinite(rear))return null;
  const feedback=vehicleId==='feedback',radius=theme.radius||(feedback?.042:.071);
  // Feedback's core sits in the existing central tape window, keeping both reels visible.
  const centre=new Vector3(x,theme.y,rear+.006).applyMatrix4(inverse);
  const root=new Group();root.name='vehicle-accent-'+vehicleId;root.position.copy(centre);
  root.quaternion.setFromUnitVectors(new Vector3(0,0,1),new Vector3(0,0,1).transformDirection(inverse));bone.add(root);
  const geometries=[],objects=[];
  const add=(g,m,z)=>{geometries.push(g);const o=new Mesh(g,m);o.position.z=z;o.castShadow=false;o.receiveShadow=false;o.frustumCulled=false;root.add(o);objects.push(o);return o;};
  const mount=new CylinderGeometry(radius+.014,radius+.014,.016,32,1);mount.rotateX(Math.PI/2);
  add(mount,material('mount'),0);
  const rotor=add(rotorGeometry(theme.petals,radius+.006),material('metal'),.011);
  const spiral=add(new CircleGeometry(radius*.72,32),loomMaterial(mask,theme.color),.014);
  const lights=add(lightRing(radius+.010,theme.color),material('glow'),.010);
  const colors=lights.geometry.getAttribute('color'),base=colors.array.slice();
  let angle=0,disposed=false;
  return {metrics:{draws:4,triangles:128+theme.petals*4+32+80,mount:{x,y:theme.y,shellZ:rear,radius:radius+.014}},update(dt,s) {
    if(disposed||(s.near===false&&!s.motionOff))return;
    if(s.motionOff)angle=0;
    else angle+=(Number.isFinite(dt)?Math.min(.1,Math.max(0,dt)):0)*theme.rate*(s.gentle?.4:1)*(.45+Math.min(1,Math.max(0,s.speed||0))*.8+(s.boost||0)*.4);
    rotor.rotation.z=angle;spiral.rotation.z=-angle*.72;
    const time=Number.isFinite(s.time)?s.time:0;
    const gain=s.motionOff?1:.80+.12*Math.sin(time*(s.gentle?.55:1.1))+.08*Math.min(1,Math.max(0,s.boost||0));
    for(let i=0;i<base.length;i++)colors.array[i]=base[i]*gain;colors.needsUpdate=true;
  },dispose(){if(disposed)return;disposed=true;root.removeFromParent();for(const g of geometries)g.dispose();}};
}
