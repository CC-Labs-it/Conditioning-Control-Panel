/* Shared physical finishes for the accepted racer assets. No new textures or vertex buffers. */
import {MeshPhysicalNodeMaterial} from 'three/webgpu';
import {positionGeometry, sin, float} from 'three/tsl';

const cache=new WeakMap();
export function racerFinish(source) {
  if(!source||!/^emi_(vehicle|outfit)_/.test(source.name))return source;
  if(cache.has(source))return cache.get(source);
  const m=new MeshPhysicalNodeMaterial();
  for(const key in source)if(!['uuid','type'].includes(key)&&key in m)m[key]=source[key];
  m.color=source.color.clone();m.emissive=source.emissive.clone();m.name=source.name;
  const fabric=/fabric/.test(source.name),metal=/metal/.test(source.name),rubber=/rubber/.test(source.name);
  if(fabric){
    m.roughness=.84;m.sheen=.35;m.sheenRoughness=.8;m.sheenColor.setRGB(.55,.35,.42);
    // Restrained cross weave changes the highlight, keeping painted colours intact.
    const p=positionGeometry;
    m.roughnessNode=float(.83).add(sin(p.x.mul(760)).mul(sin(p.y.add(p.z).mul(760))).mul(.025));
  }else if(metal){m.metalness=.82;m.roughness=.25;m.clearcoat=.24;m.clearcoatRoughness=.22;}
  else if(rubber){m.roughness=.68;}
  else{m.roughness=.25;m.clearcoat=.55;m.clearcoatRoughness=.19;}
  m.userData={...source.userData,racerFinish:true};cache.set(source,m);return m;
}
