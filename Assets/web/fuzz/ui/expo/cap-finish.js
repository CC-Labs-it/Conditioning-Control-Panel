// A cap whose lamp is out. Its coral face goes grey while a press would do nothing, so a button that cannot be used
// looks like one. The original finish comes back when the cap is live again.
export function capFinish(cap) {
  const faces=[],clones=new Map();
  function neutral(material) {
    if(material.name!=='action_coral')return material;
    if(!clones.has(material)){
      const grey=material.clone();
      grey.name='Cap lamp out';
      grey.map=null;grey.color.setHex(0x939e96);
      if(grey.emissive)grey.emissiveIntensity=0;
      clones.set(material,grey);
    }
    return clones.get(material);
  }
  cap?.traverse(face=>{
    if(!face.isMesh)return;
    const original=face.material;
    if(![original].flat().some(m=>m.name==='action_coral'))return;
    const muted=Array.isArray(original)?original.map(neutral):neutral(original);
    faces.push({face,original,muted});
  });
  let lit=true;
  function set(value) {
    value=!!value;
    if(value===lit)return;
    lit=value;for(const {face,original,muted} of faces)face.material=value?original:muted;
  }
  return {set,get lit(){return lit;},dispose(){set(true);for(const copy of clones.values())copy.dispose();}};
}
