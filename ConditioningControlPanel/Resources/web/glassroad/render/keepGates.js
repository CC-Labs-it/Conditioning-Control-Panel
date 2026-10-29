/* Visible Keep gates use the same pure phase function as collision. */
import { Group, Mesh, BoxGeometry, PlaneGeometry, MeshBasicNodeMaterial, Matrix4, Vector3, Color, DoubleSide } from 'three/webgpu';
import { texture, uv, uniform, vec3, normalWorld, normalize, dot, clamp } from 'three/tsl';
import { keepGatePhase, createKeepGates } from '../sim/keepGates.js';
import { LOOK } from './look.js';
import { machineryBeatAt } from '../track/machineryBeat.js';

export function createKeepGateView(scene, track, art, { gates = track.keepGates || [] } = {}) {
  const group = new Group(); group.name = 'keep-portcullises'; scene.add(group);
  const rows = createKeepGates(track, { gates }).rows, owned = [], geometries = [], entries = [];
  const source = art?.model('hazard-keep-portcullis')?.scene;
  const mask = art?.tex('hazard-keep-footprint-phone') || art?.tex('hazard-keep-footprint');
  const materials = new Map(), basis = new Matrix4(), axis = [new Vector3(),new Vector3(),new Vector3()];
  const shade = clamp(dot(normalize(normalWorld),normalize(vec3(...LOOK.lightDir))).mul(.38).add(.62),0,1), phase = {};
  function solid(name, color) {
    const m = new MeshBasicNodeMaterial(); m.name = name;
    const c = new Color(color); m.colorNode = vec3(c.r,c.g,c.b).mul(shade); owned.push(m); return m;
  }
  const fallbackMat = source ? null : solid('keep-gate-fallback', '#655a6d');
  const bar = source ? null : new BoxGeometry(1,1,1);
  if (bar) geometries.push(bar);
  const footprint = new PlaneGeometry(14,3); geometries.push(footprint);
  for (const g of rows) {
    const holder = new Group(); holder.name = 'keep-gate:' + g.id;
    holder.position.fromArray(g.p);
    basis.makeBasis(axis[0].fromArray(g.b),axis[1].fromArray(g.n),axis[2].fromArray(g.t).negate());
    holder.quaternion.setFromRotationMatrix(basis); holder.scale.x = g.w / 14;
    let body, move;
    if (source) {
      body = source.clone(true); move = body.getObjectByName('move');
      if (!move) { body = new Group(); move = body; }
      body.traverse(o => {
        if (!o.isMesh) return;
        const replace = m => {
          if (!materials.has(m)) {
            const color = /gate_(edges|seal)/.test(m.name) ? '#ed3e9d' : m.name === 'gate_iron' ? '#626372' : m.color || '#655a6d';
            materials.set(m,solid('keep:' + m.name,color));
          }
          return materials.get(m);
        };
        o.material = Array.isArray(o.material) ? o.material.map(replace) : replace(o.material);
        o.castShadow = true;
      });
    } else {
      body = new Group(); move = body; move.name = 'move';
      for (let x=-6.5;x<=6.5;x+=1) {
        const m=new Mesh(bar,fallbackMat);m.position.set(x,4,0);m.scale.set(.18,8,.24);move.add(m);
      }
      for (const y of [1.2,4,7.2]) {
        const m=new Mesh(bar,fallbackMat);m.position.set(0,y,0);m.scale.set(14,.22,.3);move.add(m);
      }
    }
    holder.add(body);
    const alpha = uniform(.035), warning = new MeshBasicNodeMaterial();
    warning.name = 'keep-footprint:' + g.id; warning.color.set('#e39ac1');
    warning.transparent = true; warning.depthWrite = false; warning.side = DoubleSide;
    warning.opacityNode = mask ? texture(mask,uv()).r.mul(alpha) : alpha;
    owned.push(warning);
    const floor = new Mesh(footprint,warning); floor.rotation.x = -Math.PI/2; floor.position.y=.055;
    floor.renderOrder = 4; holder.add(floor);group.add(holder);
    entries.push({g,move,restY:move.position.y,alpha});
  }
  function update(timeS) {
    for (const e of entries) {
      keepGatePhase(e.g,timeS,phase,machineryBeatAt(track,timeS));e.move.position.y=e.restY+phase.liftM;e.alpha.value=phase.opacity;
    }
  }
  update(0);
  let disposed=false;
  return {group,update,inspect:()=>({count:entries.length,model:!!source,mask:!!mask}),
    dispose(){if(disposed)return;disposed=true;group.removeFromParent();owned.forEach(m=>m.dispose());geometries.forEach(g=>g.dispose());} };
}
