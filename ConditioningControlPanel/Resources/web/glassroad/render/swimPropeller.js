/* Rear marine drive. All parts exist before warmup; one reversible travel coordinate
 * extends the piston, opens three hinges, then accelerates the rotor. Forward is -Z. */
import { Group, Mesh, InstancedMesh, MeshBasicNodeMaterial, CylinderGeometry, TorusGeometry, Shape,
  ExtrudeGeometry, Box3, Color, Matrix4, Vector3 } from 'three/webgpu';
import { normalWorld, positionViewDirection, normalView, vec3, float, max, dot, normalize, pow, abs } from 'three/tsl';

const TAU = Math.PI * 2;
const clamp01 = x => Math.max(0, Math.min(1, x));
const smooth = (a,b,x) => { const k=clamp01((x-a)/(b-a)); return k*k*(3-2*k); };

export function createPropellerMotion() { return { progress:0, extension:0, unfold:0, rpm:0, angle:0 }; }
export function stepPropellerMotion(m, underwater, dt, {gentle=false,paused=false}={}) {
  if(paused || !(dt>0)) return m;
  const target=clamp01(Number.isFinite(underwater)?underwater:0), slow=gentle?.5:1;
  const travel=dt*slow*(target>m.progress?.72:.6);
  m.progress += Math.max(-travel,Math.min(travel,target-m.progress));
  m.extension=smooth(0,.42,m.progress);
  m.unfold=smooth(.42,.76,m.progress);
  m.rpm=320*smooth(.76,1,m.progress)*slow;
  m.angle=(m.angle+dt*m.rpm*TAU/60)%TAU;
  return m;
}

function metal(hex) {
  const material=new MeshBasicNodeMaterial(), c=new Color(hex);
  const light=max(dot(normalWorld,normalize(vec3(-.5,.8,.5))),0);
  const rim=pow(float(1).sub(abs(dot(normalView,positionViewDirection))),3);
  material.colorNode=vec3(c.r,c.g,c.b).mul(light.mul(.64).add(.42)).add(rim.mul(.18));
  return material;
}

function bladeGeometry() {
  const s=new Shape();s.moveTo(.06,-.045);s.lineTo(.16,-.07);
  s.bezierCurveTo(.3,-.15,.45,-.11,.47,-.025);
  s.bezierCurveTo(.44,.04,.3,.09,.16,.06);s.lineTo(.06,.04);s.closePath();
  const g=new ExtrudeGeometry(s,{depth:.018,bevelEnabled:true,bevelThickness:.007,bevelSize:.008,bevelSegments:1,steps:1,curveSegments:6});
  const p=g.attributes.position;
  for(let i=0;i<p.count;i++) {
    const x=p.getX(i),y=p.getY(i),z=p.getZ(i)-.009, pitch=.22+x*.55;
    p.setXYZ(i,x,y*Math.cos(pitch)-z*Math.sin(pitch),y*Math.sin(pitch)+z*Math.cos(pitch));
  }
  g.computeVertexNormals();return g;
}

/** The three metals and three shapes every drive is made of. */
function createPropellerKit() {
  const materials=[metal('#273a48'),metal('#9caeb9'),metal('#38c9ce')];
  const cylinder=new CylinderGeometry(1,1,1,12), ring=new TorusGeometry(.128,.016,5,16), blade=bladeGeometry();
  return {materials,cylinder,ring,blade,geometries:[cylinder,ring,blade],
    dispose() {for(const g of this.geometries)g.dispose();for(const m of this.materials)m.dispose();}};
}

/**
 * Perf (Firefox frame cost): every kart's drive is the same twelve parts in three metals. Drawn as plain meshes
 * that was 12 draws per kart per pass (96 in the view plus 96 in the shadow map with eight karts). A fleet draws
 * each part ONCE for every kart: one InstancedMesh per part (shared kit), its instances copied from the karts'
 * part placeholders right before each draw. The placeholders stay in each kart's tree with the same names,
 * transforms, geometry and material (so fit, motion, castShadow and the tests see the same drive), on no layer,
 * so no camera ever draws them. A kart whose tree is hidden drops out of the instances. Picture unchanged.
 * capacity: the most drives at once (the kart count). Add `group` to the scene beside the karts.
 */
export function createPropellerFleet(capacity=8) {
  const kit=createPropellerKit(), group=new Group(), parts=new Map();
  group.name='swim-propeller-fleet';
  const shown=o=>{for(let p=o;p;p=p.parent)if(!p.visible)return false;return true;};
  function sync(row) {
    const mesh=row.mesh, out=mesh.instanceMatrix.array;
    let n=0, changed=false;
    for(const m of row.members) {
      if(n>=capacity || !shown(m)) continue;
      const e=m.matrixWorld.elements, o=n*16;
      for(let i=0;i<16;i++) if(out[o+i]!==e[i]) {out[o+i]=e[i];changed=true;}
      n++;
    }
    if(mesh.count!==n) {mesh.count=n;changed=true;}
    if(changed && n) {mesh.instanceMatrix.clearUpdateRanges();mesh.instanceMatrix.addUpdateRange(0,n*16);mesh.instanceMatrix.needsUpdate=true;}
  }
  function rowFor(name,geometry,material) {
    let row=parts.get(name);
    if(row) return row;
    const mesh=new InstancedMesh(geometry,material,capacity);
    mesh.name=name;mesh.count=0;mesh.frustumCulled=false;
    row={mesh,members:[]};
    // The map's casters are chosen on the karts' trees (shadows.js castFrom): the fleet casts when they do.
    Object.defineProperty(mesh,'castShadow',{configurable:true,get:()=>row.members.some(m=>m.castShadow),set(){}});
    Object.defineProperty(mesh,'receiveShadow',{configurable:true,get:()=>row.members.some(m=>m.receiveShadow),set(){}});
    mesh.onBeforeRender=()=>sync(row);
    parts.set(name,row);group.add(mesh);return row;
  }
  return {group,kit,parts,capacity,
    /** Draw `mesh` (a drive part) through the fleet: it keeps its transform but leaves every layer. */
    adopt(mesh) {const row=rowFor(mesh.name,mesh.geometry,mesh.material);row.members.push(mesh);mesh.layers.disableAll();return row;},
    release(mesh) {for(const row of parts.values()){const i=row.members.indexOf(mesh);if(i>=0)row.members.splice(i,1);}},
    /** Instance count per part right now (diagnostics). */
    count() {let n=0;for(const row of parts.values())n+=row.members.length;return n;},
    dispose() {group.removeFromParent();for(const row of parts.values()){row.members.length=0;row.mesh.dispose();}parts.clear();kit.dispose();},
  };
}

export function createSwimPropeller(body, source=null, fleet=null) {
  const object=new Group(), carriage=new Group(), rotor=new Group(), hinges=[];
  object.name='swim-propeller';carriage.name='swim-drive-carriage';rotor.name='swim-drive-rotor';
  body.add(object);object.add(carriage);carriage.add(rotor);
  const kit=fleet?fleet.kit:createPropellerKit(), materials=kit.materials, {cylinder,ring,blade}=kit, adopted=[];
  function part(parent,name,geometry,mat,x,y,z,sx=1,sy=1,sz=1) {
    const mesh=new Mesh(geometry,materials[mat]);mesh.name=name;mesh.position.set(x,y,z);mesh.scale.set(sx,sy,sz);
    mesh.frustumCulled=false;parent.add(mesh);
    if(fleet) {fleet.adopt(mesh);adopted.push(mesh);}
    return mesh;
  }
  function barrel(parent,name,mat,radius,length,z) {
    const mesh=part(parent,name,cylinder,mat,0,0,z,radius,length,radius);mesh.rotation.x=Math.PI/2;return mesh;
  }
  barrel(object,'drive-housing',0,.145,.25,-.07);
  part(object,'housing-cyan-ring',ring,2,0,0,.065);
  const shaft=barrel(object,'telescoping-shaft',1,.046,.04,.06);
  barrel(carriage,'rotor-bearing',0,.095,.13,0);
  barrel(rotor,'rotor-hub',1,.115,.12,.09);
  barrel(rotor,'hub-cyan-cap',2,.07,.018,.16);
  for(let i=0;i<3;i++) {
    const radial=new Group(),hinge=new Group();radial.rotation.z=i*TAU/3;
    radial.name='blade-arm-'+i;hinge.name='blade-hinge-'+i;
    rotor.add(radial);radial.add(hinge);hinge.position.set(.055,0,.045);hinges.push(hinge);
    part(hinge,'hydrofoil-'+i,blade,1,0,0,0);
    // A small inset follows the actual foil instead of a glowing billboard.
    part(hinge,'foil-inset-'+i,blade,2,.052,0,.017,.7,.22,.28);
  }
  const motion=createPropellerMotion(), bounds=new Box3(), local=new Matrix4(), parentInverse=new Matrix4(), vertex=new Vector3();
  function fit(model) {
    let rear=.65, scale=1;
    if(model) {
      // Fit in the source parent's frame. A kart's world position, heading and root
      // scale must never become a local rear offset when its wardrobe changes.
      model.updateWorldMatrix(true,true);bounds.makeEmpty();parentInverse.identity();
      if(model.parent) parentInverse.copy(model.parent.matrixWorld).invert();
      model.traverse(mesh=>{
        if(!mesh.isMesh || !mesh.geometry?.attributes.position) return;
        local.multiplyMatrices(parentInverse,mesh.matrixWorld);
        for(let i=0;i<mesh.geometry.attributes.position.count;i++) {
          mesh.getVertexPosition(i,vertex);bounds.expandByPoint(vertex.applyMatrix4(local));
        }
      });
      if(!bounds.isEmpty() && Number.isFinite(bounds.max.z)) {
        rear=Math.max(.42,bounds.max.z-.04);
        scale=Math.max(.9,Math.min(1.25,(bounds.max.x-bounds.min.x)/1.44));
      }
    }
    object.position.set(0,.52*scale,rear);object.scale.setScalar(scale);
  }
  function apply() {
    carriage.position.z=-.16+.79*motion.extension;
    shaft.scale.y=.04+.55*motion.extension;shaft.position.z=.06+.275*motion.extension;
    rotor.rotation.z=motion.angle;
    for(const h of hinges) h.rotation.y=(1-motion.unfold)*Math.PI/2;
  }
  function reset() { Object.assign(motion,createPropellerMotion());apply(); }
  fit(source);apply();
  return {object,carriage,rotor,hinges,motion,fit,
    update(underwater,dt,options) {stepPropellerMotion(motion,underwater,dt,options);apply();}, reset,
    // A fleet owns the shared kit (its own dispose frees it); a lone drive owns its kit.
    dispose() {object.removeFromParent();if(fleet){for(const m of adopted)fleet.release(m);}else kit.dispose();},
  };
}
