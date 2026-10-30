/* Material channels preserve the authored palette until explicitly overridden. */
export function createWardrobeColors({THREE,avatar,nodes,materials,info}){
  const records=[],cloneCache=new Map(),counts={};
  let updateNails=()=>{};
  const nailMesh=makeNailPlates();
  const channels=[['fabric','Outfit fabric'],['trim','Outfit details'],['hair','Hair'],['skin','Skin'],['eyes','Eyes'],['lips','Lips'],['nails','Nails'],['shoes','Shoes'],['accessories','Accessories']];
  const uniforms={skin:{value:new THREE.Color()},skinOn:{value:0},lips:{value:new THREE.Color()},lipsOn:{value:0},eyes:{value:new THREE.Color()},eyesOn:{value:0}};
  const membership=new Map();
  for(const slot of ['dress','legwear','hair','shoes','accessories','arms','ankles'])for(const item of info.slots[slot]||[])for(const name of item.nodes||[])nodes.get(name)?.traverse(mesh=>{if(mesh.isMesh)membership.set(mesh,slot);});
  function channelFor(mesh,mat){
    const tag=mat.userData.wardrobeColorChannel;if(tag)return tag;
    const name=mat.name,slot=membership.get(mesh),meshName=mesh.userData.name||mesh.name;
    if(/Ivory and turquoise/.test(name))return 'eyes';
    if(/MakeupV10.Skin|Complexion.Stockings/.test(name)&&/GEO-rain-(body|head)/.test(meshName))return 'skin';
    if(slot==='hair')return 'hair';
    if(slot==='dress'||slot==='legwear')return /trim|seam|clasp|zip|welt|gold|stripe|button|belt/i.test(name)?'trim':'fabric';
    if(slot==='shoes')return /sole|heel tip|zip|clasp|metal/i.test(name)?null:'shoes';
    if(slot==='accessories')return mat.transparent||/lens|glass|visor/i.test(name)&&!/frame/i.test(name)?null:'accessories';
    return null;
  }
  function chainShader(material,kind,head){
    const previous=material.onBeforeCompile,oldKey=material.customProgramCacheKey;
    material.onBeforeCompile=shader=>{
      previous.call(material,shader);
      if(kind==='eyes'){
        shader.uniforms.wardrobeEyeColor=uniforms.eyes;shader.uniforms.wardrobeEyeOn=uniforms.eyesOn;
        shader.fragmentShader='uniform vec3 wardrobeEyeColor;\nuniform float wardrobeEyeOn;\n'+shader.fragmentShader;
        shader.fragmentShader=shader.fragmentShader.replace('#include <map_fragment>',`#include <map_fragment>
          float irisHigh=max(diffuseColor.r,max(diffuseColor.g,diffuseColor.b));
          float irisLow=min(diffuseColor.r,min(diffuseColor.g,diffuseColor.b));
          float irisMask=smoothstep(.025,.12,irisHigh-irisLow)*step(diffuseColor.r*1.1,diffuseColor.b)*wardrobeEyeOn;
          diffuseColor.rgb=mix(diffuseColor.rgb,wardrobeEyeColor*min(1.15,irisHigh*1.8),irisMask);
        `);
      }else if(kind==='skin'){
        shader.uniforms.wardrobeSkinColor=uniforms.skin;shader.uniforms.wardrobeSkinOn=uniforms.skinOn;
        shader.uniforms.wardrobeLipColor=uniforms.lips;shader.uniforms.wardrobeLipOn=uniforms.lipsOn;
        shader.fragmentShader='uniform vec3 wardrobeSkinColor;\nuniform float wardrobeSkinOn;\nuniform vec3 wardrobeLipColor;\nuniform float wardrobeLipOn;\n'+shader.fragmentShader;
        if(head){shader.vertexShader='attribute float wardrobeLipMask;\nvarying float vWardrobeLipMask;\n'+shader.vertexShader;shader.vertexShader=shader.vertexShader.replace('#include <begin_vertex>','#include <begin_vertex>\nvWardrobeLipMask=wardrobeLipMask;');shader.fragmentShader='varying float vWardrobeLipMask;\n'+shader.fragmentShader;}
        shader.fragmentShader=shader.fragmentShader.replace('#include <color_fragment>',`#include <color_fragment>
          float lipRegion=${head?'vWardrobeLipMask':'0.0'};
          diffuseColor.rgb*=mix(vec3(1.0),wardrobeSkinColor/vec3(.665,.266,.184),wardrobeSkinOn*(1.0-lipRegion));
          diffuseColor.rgb=mix(diffuseColor.rgb,wardrobeLipColor,wardrobeLipOn*lipRegion);
        `);
      }
    };
    material.customProgramCacheKey=()=>`${oldKey.call(material)}|colors-${kind}-${head}`;
  }
  avatar.traverse(mesh=>{
    if(!mesh.isMesh)return;
    const head=(mesh.userData.name||mesh.name)==='GEO-rain-head';
    if(head){
      const color=mesh.geometry.attributes.color,mask=new Float32Array(mesh.geometry.attributes.position.count);
      if(color)for(let i=0;i<mask.length;i++){const r=color.getX(i),g=color.getY(i),b=color.getZ(i);mask[i]=THREE.MathUtils.smoothstep((r/(g+.001)-2.8),.1,2)*THREE.MathUtils.smoothstep(b/(g+.001),1.15,2.1);}
      mesh.geometry.setAttribute('wardrobeLipMask',new THREE.BufferAttribute(mask,1));counts.lipVertices=mask.filter(v=>v>.5).length;
    }
    const replace=mat=>{
      const kind=channelFor(mesh,mat);if(!kind)return mat;
      let byKind=cloneCache.get(mat);if(!byKind){byKind=new Map();cloneCache.set(mat,byKind);}const key=`${kind}-${head}`;
      if(byKind.has(key))return byKind.get(key);
      const clone=mat.clone();clone.name=mat.name;clone.onBeforeCompile=mat.onBeforeCompile;clone.customProgramCacheKey=mat.customProgramCacheKey;clone.userData={...mat.userData,colorChannel:kind};
      if(kind==='eyes'||kind==='skin')chainShader(clone,kind,head);
      if(!materials.has(clone.name))materials.set(clone.name,[]);materials.get(clone.name).push(clone);
      records.push({material:clone,kind,base:clone.color?.clone()});counts[kind]=(counts[kind]||0)+1;byKind.set(key,clone);return clone;
    };
    mesh.material=Array.isArray(mesh.material)?mesh.material.map(replace):replace(mesh.material);
  });
  function apply(overrides={}){
    if(nailMesh)nailMesh.visible=Boolean(overrides.nails);
    for(const key of ['skin','lips','eyes']){uniforms[key+'On'].value=overrides[key]?1:0;if(overrides[key])uniforms[key].value.set(overrides[key]);}
    for(const {material,kind} of records){if(kind==='skin'){if(overrides.skin)material.color.set('#ffffff');continue;}if(kind==='eyes')continue;if(overrides[kind])material.color?.set(overrides[kind]);}
  }
  function makeNailPlates(){
    const body=nodes.get('GEO-rain-body');if(!body?.geometry?.index)return null;
    const source=body.geometry,p=source.attributes.position,n=source.attributes.normal,index=source.index;
    // Smooth oval outlines are fitted to the actual dorsal surface. Barycentric
    // anchors follow the evaluated finger vertices without tinting the skin.
    const fingersRight=[{z:.0418,y:.7473},{z:.0665,y:.7271},{z:.0925,y:.7221},{z:.1135,y:.7371},{z:.133,y:.8067,thumb:true}];
    const fingersLeft=[{z:.0375,y:.7341},{z:.0628,y:.7137},{z:.0891,y:.7084},{z:.1103,y:.7237},{z:.1288,y:.7949,thumb:true}];
    const faces=[];
    for(let i=0;i<index.count;i+=3){const ids=[index.getX(i),index.getX(i+1),index.getX(i+2)],x=ids.reduce((s,j)=>s+p.getX(j),0)/3,y=ids.reduce((s,j)=>s+p.getY(j),0)/3,z=ids.reduce((s,j)=>s+p.getZ(j),0)/3,side=Math.sign(x);if(Math.abs(x)<.19||y>.84||y<.69)continue;if(ids.reduce((s,j)=>s+n.getX(j)*side,0)/3<.35)continue;faces.push({ids,x,y,z,side,triangle:new THREE.Triangle(...ids.map(j=>new THREE.Vector3(0,p.getY(j),p.getZ(j))))});}
    const anchors=[],indices=[],sourcePoints=new Map(),closest=new THREE.Vector3(),bary=new THREE.Vector3(),query=new THREE.Vector3();let patches=0;
    const segments=32,rings=3;
    function anchor(y,z,side,candidates,radius){
      query.set(0,y,z);let best=null,bestDistance=Infinity;
      for(const face of candidates){face.triangle.closestPointToPoint(query,closest);const distance=closest.distanceToSquared(query);if(distance<bestDistance-1e-12||(Math.abs(distance-bestDistance)<1e-12&&Math.abs(face.x)>Math.abs(best?.face.x||0))){face.triangle.getBarycoord(closest,bary);best={face,weights:bary.clone()};bestDistance=distance;}}
      if(!best)return;
      for(const id of best.face.ids)if(!sourcePoints.has(id))sourcePoints.set(id,new THREE.Vector3());
      anchors.push({points:best.face.ids.map(id=>sourcePoints.get(id)),weights:best.weights,side,offset:.00045+.00065*(1-radius*radius)});
    }
    for(const side of [1,-1])for(const finger of side>0?fingersRight:fingersLeft){
      const cy=finger.y+.009,cz=finger.z-(finger.thumb?.003:0),ry=finger.thumb?.0068:.0075,rz=finger.thumb?.0048:.0042;
      const candidates=faces.filter(face=>face.side===side&&Math.abs(face.y-cy)<.025&&Math.abs(face.z-cz)<.017);if(!candidates.length)continue;
      const start=anchors.length;anchor(cy,cz,side,candidates,0);
      for(let ring=1;ring<=rings;ring++)for(let segment=0;segment<segments;segment++){const t=segment/segments*Math.PI*2,r=ring/rings;anchor(cy+Math.cos(t)*ry*r,cz+Math.sin(t)*rz*r,side,candidates,r);}
      const tri=(a,b,c)=>indices.push(...(side>0?[a,b,c]:[a,c,b]));
      for(let s=0;s<segments;s++)tri(start,start+1+s,start+1+(s+1)%segments);
      for(let r=1;r<rings;r++)for(let s=0;s<segments;s++){const a=start+1+(r-1)*segments+s,b=start+1+(r-1)*segments+(s+1)%segments,c=start+1+r*segments+s,d=start+1+r*segments+(s+1)%segments;tri(a,c,d);tri(a,d,b);}
      patches++;
    }
    if(!anchors.length)return null;
    const geo=new THREE.BufferGeometry(),positions=new Float32Array(anchors.length*3),normal=new THREE.Vector3(),edge=new THREE.Vector3(),point=new THREE.Vector3();geo.setAttribute('position',new THREE.BufferAttribute(positions,3));geo.setIndex(indices);
    const mat=new THREE.MeshStandardMaterial({color:'#c45c85',roughness:.27,metalness:.02});mat.name='Colors.FittedNailLacquer';mat.userData.wardrobeColorChannel='nails';
    const mesh=new THREE.Mesh(geo,mat);mesh.name='Colors.FittedNailPlates';mesh.frustumCulled=false;mesh.visible=false;
    updateNails=(force=false)=>{if(!mesh.visible&&!force)return;for(const [id,position]of sourcePoints)body.getVertexPosition(id,position);anchors.forEach((anchor,i)=>{const [a,b,c]=anchor.points;normal.subVectors(b,a).cross(edge.subVectors(c,a)).normalize();if(normal.x*anchor.side<0)normal.negate();point.copy(a).multiplyScalar(anchor.weights.x).addScaledVector(b,anchor.weights.y).addScaledVector(c,anchor.weights.z).addScaledVector(normal,anchor.offset);point.toArray(positions,i*3);});geo.attributes.position.needsUpdate=true;geo.computeVertexNormals();};updateNails(true);
    body.add(mesh);nodes.set(mesh.name,mesh);materials.set(mat.name,[mat]);counts.nailPatches=patches;counts.nailTriangles=indices.length/3;counts.nailAnchors=sourcePoints.size;return mesh;
  }
  return {apply,update(){updateNails();},channels:channels.map(([id,name])=>({id,name,available:id==='lips'?Boolean(counts.lipVertices):Boolean(counts[id])})),diagnostics:()=>({...counts}),defaultColor(id){if(id==='eyes')return '#327b9c';if(id==='skin')return '#d5a087';const r=records.find(r=>r.kind===id);return r?.base?'#'+r.base.getHexString():({lips:'#b5286a',eyes:'#327b9c',skin:'#d5a087',nails:'#c45c85'}[id]||'#b47c8c');}};
}
