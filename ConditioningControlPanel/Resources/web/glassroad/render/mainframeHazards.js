import { Group, Mesh, BoxGeometry, CylinderGeometry, TorusGeometry, Matrix4, Vector3 } from 'three/webgpu';
import { industryZones, industryPose } from '../sim/mainframeIndustry.js';
import { createMachineMaterial } from './mainframeMaterials.js';

export function buildMainframeHazards(track, art) {
  const group = new Group(); group.name = 'mainframe-industry';
  const box = new BoxGeometry(1,1,1), cylinder = new CylinderGeometry(1,1,1,12), ring = new TorusGeometry(1,.07,5,16);
  const mats = [], solid = (hex,glow=false) => { const m=createMachineMaterial(hex,art,{glow}); mats.push(m); return m; };
  const metal=solid('#647575'), dark=solid('#131d20'), amber=solid('#ffc34d',true), cyan=solid('#70fff0',true), black=solid('#090b10');
  const entries=[], basis=new Matrix4(), axes=[new Vector3(),new Vector3(),new Vector3()];
  const part=(root,geo,mat,p,size) => { const m=new Mesh(geo,mat); m.position.set(...p); m.scale.set(...size); root.add(m); return m; };
  for (const z of industryZones(track)) {
    const root=new Group(); root.name=z.id;
    basis.makeBasis(axes[0].fromArray(z.b),axes[1].fromArray(z.n),axes[2].fromArray(z.t).negate());
    root.quaternion.setFromRotationMatrix(basis); root.position.fromArray(z.p); group.add(root);
    // The painted footprint exactly matches the physical strip, with a clear dry centre lane.
    part(root,box,dark,[z.x,.035,0],[z.w+.5,.06,z.len+.5]);
    const moving=[], lamps=[];
    for(const side of [-1,1]) lamps.push(part(root,box,z.kind==='belt'?cyan:amber,[z.x+side*(z.w/2+.2),.085,0],[.16,.1,z.len]));
    if(z.kind==='piston') {
      const piston=new Group(); piston.name='press-head'; piston.position.x=z.x; root.add(piston);
      part(piston,box,metal,[0,1,0],[z.w,2,z.len]);
      for(let j=0;j<5;j++) part(piston,box,j%2?dark:amber,[0,.08,-z.len/2+.45+j*1.35],[z.w,.16,.5]);
      part(piston,cylinder,metal,[0,6.5,0],[.55,10,.55]);
      part(root,box,dark,[z.x,13.5,0],[z.w+1,2.2,z.len+1]);
      for(const side of [-1,1]) part(root,cylinder,metal,[z.x+side*(z.w/2+.45),7,0],[.2,13,.2]);
      entries.push({z,piston,lamps,moving});
    } else if(z.kind==='belt') {
      part(root,box,metal,[z.x,.09,0],[z.w,.12,z.len]);
      for(let j=0;j<8;j++) moving.push(part(root,box,cyan,[z.x,.18,-z.len/2+j*z.len/8],[z.w-.7,.05,.3]));
      for(const end of [-1,1]) { const roller=part(root,cylinder,dark,[z.x,.1,end*z.len/2],[.35,z.w,.35]); roller.rotation.z=Math.PI/2; }
      entries.push({z,lamps,moving});
    } else {
      part(root,box,black,[z.x,.12,0],[z.w,.2,z.len]);
      for(let j=0;j<6;j++) {
        const ripple=part(root,ring,metal,[z.x+Math.sin(j*2)*z.w*.23,.235,-z.len*.4+j*z.len*.16],[.7,.7,.7]);
        ripple.rotation.x=-Math.PI/2; moving.push(ripple);
      }
      entries.push({z,lamps,moving});
    }
  }
  function update(dt,{timeS=0,paused=false,gentle=false}={}) {
    if(paused) return;
    for(const e of entries) {
      const pose=industryPose(e.z,timeS);
      if(e.piston) e.piston.position.y=pose.lift;
      // Warning grows over 1.8 seconds, never flashes. Gentle retains identical collision timing.
      for(const lamp of e.lamps) {
        const target=pose.warning?.25:pose.active?.14:.06;
        lamp.scale.y+=(target-lamp.scale.y)*Math.min(1,dt/.3);
      }
      e.moving.forEach((m,j)=> {
        if(e.z.kind==='belt') m.position.z=-e.z.len/2+((j*e.z.len/8-timeS*(gentle?2:5))%e.z.len+e.z.len)%e.z.len;
        else { const wave=(Math.sin(timeS*(gentle?.3:.8)+j)*.5+.5); m.scale.setScalar(.5+wave*.35); }
      });
    }
  }
  update(0,{timeS:0});
  return {group,update,dispose(){ group.removeFromParent(); for(const g of [box,cylinder,ring])g.dispose(); for(const m of mats)m.dispose(); }};
}
