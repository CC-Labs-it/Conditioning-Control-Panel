// One bounded sprite batch below the platforms. No emissions, allocations or shader changes per frame.
import { Sprite, SpriteNodeMaterial, InstancedBufferAttribute, DynamicDrawUsage, AdditiveBlending } from 'three/webgpu';
import { instancedDynamicBufferAttribute, uv, smoothstep, vec3 } from 'three/tsl';
import { lensDepth, lensScale, lensAlpha } from './lens.js';
import { Q } from './quality.js';

export function createStoneHover(rows) {
  const per = Q.name === 'phone' ? 5 : 14, count = rows.length*per;
  const pos = new InstancedBufferAttribute(new Float32Array(count*3),3).setUsage(DynamicDrawUsage);
  const size = new InstancedBufferAttribute(new Float32Array(count),1).setUsage(DynamicDrawUsage);
  const alpha = new InstancedBufferAttribute(new Float32Array(count),1).setUsage(DynamicDrawUsage);
  const mat = new SpriteNodeMaterial({ transparent:true, depthWrite:false, blending:AdditiveBlending });
  const pn = instancedDynamicBufferAttribute(pos,'vec3'), d = lensDepth(pn);
  mat.positionNode = pn; mat.scaleNode = lensScale(instancedDynamicBufferAttribute(size,'float'),d);
  mat.colorNode = vec3(0.18,1,0.78);
  mat.opacityNode = smoothstep(0.05,0.5,uv().sub(0.5).length()).oneMinus()
    .mul(instancedDynamicBufferAttribute(alpha,'float')).mul(lensAlpha(d));
  const object = new Sprite(mat); object.name='stone-hover-motes'; object.count=count; object.frustumCulled=false;
  object.renderOrder=2;
  let time=0, age=1;
  function update(dt, gentle=false) {
    time += dt*(gentle?0.5:1); age += dt;
    if(age<1/30) return; age=0;
    rows.forEach((r,ri)=>{
      for(let j=0;j<per;j++) {
        const i=ri*per+j, phase=(time/4.8+j/per+ri*0.173)%1;
        const a=j*2.39996+time*0.28+ri, rad=0.38+0.12*Math.sin(j*3.7);
        const x=Math.cos(a)*r.w*rad, z=Math.sin(a)*r.len*rad, y=-3.5-phase*8;
        for(let k=0;k<3;k++) pos.array[i*3+k]=r.p[k]+r.b[k]*x+r.t[k]*z+r.n[k]*y;
        size.array[i]=(0.45+0.7*(1-phase))*(r.source.spin?1.3:1);
        alpha.array[i]=Math.sin(phase*Math.PI)*(gentle?0.35:0.65);
      }
    });
    pos.needsUpdate=size.needsUpdate=alpha.needsUpdate=true;
  }
  update(0);
  return {object,update,dispose(){mat.dispose();}};
}
