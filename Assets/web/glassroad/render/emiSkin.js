/* Per-instance uniforms keep the accepted face pixels and skin patterns attached to the source mesh.
 * Patterns 0-3 are the free native ones; the Prize Counter paints (lane B3-G1: hearts, spiral, host gold) are
 * Astra's 4x4 masks (art/paints/*-mask.png, art/kart/paints registry) projected on the shell with her EMI_root
 * mapping, loaded on first wear and shared by every kart. Shell only: vehicle, face, hands and antenna keep theirs. */
import {InterleavedBuffer,InterleavedBufferAttribute,Color,DataTexture,TextureLoader,RepeatWrapping,NoColorSpace} from 'three/webgpu';
import {uniform,texture,attribute,positionGeometry,mix,float,step,fract,vec2} from 'three/tsl';

export const PAINT_PATTERN = Object.freeze({hearts:4,spiral:5,'host-gold':6});
const MASK_FILES = {hearts:'hearts-mask.png',spiral:'spiral-mask.png','host-gold':'host-gold-mask.png'};
const masks = new Map();
let blank = null;
const blankMask = () => blank || (blank = new DataTexture(new Uint8Array(4), 1, 1));
function maskFor(paint) {
  if (!masks.has(paint)) {
    const t = new TextureLoader().load(new URL('../art/paints/' + MASK_FILES[paint], import.meta.url).href);
    t.wrapS = t.wrapT = RepeatWrapping; t.colorSpace = NoColorSpace; masks.set(paint, t);
  }
  return masks.get(paint);
}

export function createEmiSkin(model,{cloneMaterials=true}={}) {
  const u=Object.fromEntries(['primary','secondary','antenna','hands','shoes','face'].map(k=>[k,uniform(new Color('#ffffff'))]));
  u.amount=uniform(0);u.pattern=uniform(0);
  const p=positionGeometry;
  const stripes=step(.5,fract(p.x.add(p.y.mul(1.2)).mul(14)));
  const dots=float(1).sub(step(.23,fract(p.xy.mul(17)).sub(.5).length()));
  const checks=fract(p.x.mul(14).floor().add(p.y.mul(14).floor()).mul(.5)).mul(2);
  const band=(lo,hi)=>step(lo,u.pattern).mul(float(1).sub(step(hi,u.pattern)));
  const mask=texture(blankMask(),vec2(p.x.add(.255).div(.511),p.y.sub(.427).div(.52)));
  const pattern=stripes.mul(band(.5,1.5)).add(dots.mul(band(1.5,2.5))).add(checks.mul(band(2.5,3.5))).add(mask.r.mul(step(3.5,u.pattern)));
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
    // The existing shell mask adds a coated enamel highlight without another vertex buffer.
    const shell=attribute('garageShell','float');
    material.roughnessNode=mix(material.roughnessNode||float(material.roughness),float(.34),shell.mul(.45));
    material.clearcoatNode=mix(material.clearcoatNode||float(material.clearcoat||0),float(.42),shell);
    material.clearcoatRoughnessNode=mix(material.clearcoatRoughnessNode||float(material.clearcoatRoughness||.1),float(.24),shell);
  });
  return {
    set(s){for(const k of ['primary','secondary','antenna','hands','shoes','face'])u[k].value.set(s[k]);u.amount.value=s.original?0:1;
      const paint=Object.hasOwn(PAINT_PATTERN,String(s.paint))?s.paint:null;
      if(paint)mask.value=maskFor(paint);
      u.pattern.value=paint?PAINT_PATTERN[paint]:s.pattern;},
    dispose(){for(const x of owned)x.dispose();},
  };
}
