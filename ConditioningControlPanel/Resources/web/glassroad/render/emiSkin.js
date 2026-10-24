/* Per-instance uniforms keep the accepted face pixels and skin patterns attached to the source mesh. */
import {InterleavedBuffer,InterleavedBufferAttribute,Color} from 'three/webgpu';
import {uniform,texture,attribute,positionGeometry,mix,float,step,fract} from 'three/tsl';

export function createEmiSkin(model,{cloneMaterials=true}={}) {
  const u=Object.fromEntries(['primary','secondary','antenna','hands','shoes','face'].map(k=>[k,uniform(new Color('#ffffff'))]));
  u.amount=uniform(0);u.pattern=uniform(0);
  const p=positionGeometry;
  const stripes=step(.5,fract(p.x.add(p.y.mul(1.2)).mul(14)));
  const dots=float(1).sub(step(.23,fract(p.xy.mul(17)).sub(.5).length()));
  const checks=fract(p.x.mul(14).floor().add(p.y.mul(14).floor()).mul(.5)).mul(2);
  const pattern=stripes.mul(step(.5,u.pattern)).mul(float(1).sub(step(1.5,u.pattern)))
    .add(dots.mul(step(1.5,u.pattern)).mul(float(1).sub(step(2.5,u.pattern)))).add(checks.mul(step(2.5,u.pattern)));
  const owned=[];
  model.traverse(m=>{
    if(!m.isSkinnedMesh)return;
    if(!['emi_screen','kart_surface'].includes(m.material.name))return;
    const src=m.material,material=cloneMaterials?src.clone():src;m.material=material;if(cloneMaterials)owned.push(material);
    const base=src.colorNode || (src.map?texture(src.map).rgb:float(1));
    if(src.name==='emi_screen'){
      const glyph=base.r.sub(.06).div(.45).clamp(0,1);
      material.colorNode=mix(base,u.face,glyph.mul(u.amount));
      return;
    }
    if(src.name!=='kart_surface')return;
    const g=m.geometry.clone(),j=g.getAttribute('skinIndex'),w=g.getAttribute('skinWeight');
    if(!j){g.dispose();return;}
    const masks=new Float32Array(j.count*4),names=m.skeleton.bones.map(b=>b.name);
    for(let n=0;n<j.count;n++)for(let k=0;k<4;k++){
      if(w.getComponent(n,k)<.5)continue;
      const name=names[j.getComponent(n,k)];if(name==='EMI_root')masks[n*4]=1;
      if(['ant0','ant1','ant2','ballpiv'].includes(name))masks[n*4+1]=1;
      if(['shoulderL','shoulderR'].includes(name))masks[n*4+2]=1;
      if(['footL','footR'].includes(name))masks[n*4+3]=1;
    }
    // Both masks share one GPU buffer, keeping the accepted skinned kart within WebGPU's eight-buffer floor.
    const packed=new InterleavedBuffer(masks,4);
    g.setAttribute('garageShell',new InterleavedBufferAttribute(packed,1,0));g.setAttribute('garageDetail',new InterleavedBufferAttribute(packed,3,1));m.geometry=g;owned.push(g);
    const d=attribute('garageDetail','vec3'),shade=base.r.mul(.299).add(base.g.mul(.587)).add(base.b.mul(.114)).mul(1.5).add(.42).min(1.2);
    let painted=mix(base,mix(u.primary,u.secondary,pattern).mul(shade),attribute('garageShell','float'));
    painted=mix(painted,u.antenna.mul(shade),d.x);painted=mix(painted,u.hands.mul(shade),d.y);painted=mix(painted,u.shoes.mul(shade),d.z);
    material.colorNode=mix(base,painted,u.amount);
  });
  return {
    set(s){for(const k of ['primary','secondary','antenna','hands','shoes','face'])u[k].value.set(s[k]);u.amount.value=s.original?0:1;u.pattern.value=s.pattern;},
    dispose(){for(const x of owned)x.dispose();},
  };
}
