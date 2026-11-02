// Tailored silhouettes retain the source dress's evaluated animation buffers.
export function expandWardrobe({THREE,avatar,nodes,materials,info}) {
  const source=nodes.get('Companion.Wardrobe.Dress'),body=nodes.get('GEO-rain-body'),native=nodes.get('Foundation.Hazmat.Complete'),derived=[],upperMeshes=[];
  if(!source?.isMesh)throw Error('Tailoring requires the original fitted dress.');
  const motion=[...nodes.values()].find(n=>n.isMesh&&n.userData.wardrobeMotion?.source===source)?.userData.wardrobeMotion;
  if(!motion)throw Error('Tailoring requires the shared dress deformation transport.');
  const clamp=x=>Math.max(0,Math.min(1,x)),smooth=x=>{x=clamp(x);return x*x*(3-2*x);};
  const base=source.geometry,positions=base.attributes.position;
  // The morph-aware bounding box includes coefficient extremes. Tailor from
  // the actual neutral vertex positions, not that conservative animation box.
  let hem=Infinity;for(let i=0;i<positions.count;i++)hem=Math.min(hem,positions.getY(i));
  const pinkHemClearance=new Float32Array(positions.count);
  for(let i=0;i<positions.count;i++)pinkHemClearance[i]=.005*smooth((hem+.055-positions.getY(i))/.040);
  base.setAttribute('pinkHemClearance',new THREE.BufferAttribute(pinkHemClearance,1));
  const originalMaterials=(Array.isArray(source.material)?source.material:[source.material]).map(original=>{
    const m=original.clone(),compile=original.onBeforeCompile,cacheKey=original.customProgramCacheKey();
    m.onBeforeCompile=shader=>{compile.call(m,shader);shader.vertexShader='attribute float pinkHemClearance;\n'+shader.vertexShader;shader.vertexShader=shader.vertexShader.replace('#include <displacementmap_vertex>','#include <displacementmap_vertex>\ntransformed += normalize(objectNormal)*pinkHemClearance;');};
    m.customProgramCacheKey=()=>cacheKey+'-pink-hem-clearance-1';return m;
  });
  source.material=Array.isArray(source.material)?originalMaterials:originalMaterials[0];
  register(source);
  const originalEntry=info.slots.dress.find(d=>d.id==='satin-original');
  originalEntry.coverage={...(originalEntry.coverage||{}),underlayerMaskRange:[.76,1.04]};
  const hemFollow={value:new THREE.Vector3()},hemPoint=new THREE.Vector3(),hemInverse=new THREE.Matrix3();
  function driver(mesh,predicate){const ids=[],p=mesh.geometry.attributes.position;for(let i=0;i<p.count;i++)if(predicate(p,i))ids.push(i);if(!ids.length)throw Error('Hem fitting driver has no surface samples.');const average=()=>{const v=new THREE.Vector3();for(const i of ids){mesh.getVertexPosition(i,hemPoint);v.add(hemPoint.applyMatrix4(mesh.matrixWorld));}return v.multiplyScalar(1/ids.length);};return {average,neutral:average()};}
  const thighDriver=driver(body,(p,i)=>p.getY(i)>.61&&p.getY(i)<.66&&p.getZ(i)>.12&&Math.abs(p.getX(i))<.16);
  const skirtDriver=driver(source,(p,i)=>p.getY(i)<hem+.020&&p.getZ(i)>.065);
  const definitions=[
    {id:'midnight-cocktail',name:'Midnight bolero',color:'#273249',trim:'#a9afba',roughness:.47,length:.025,flare:.02,pleat:0,design:'column'},
    {id:'rose-flare',name:'Rose wrap',color:'#ae4769',trim:'#e3b2a0',roughness:.67,length:.045,flare:.20,asym:.040,design:'wrap'},
    {id:'ivory-midi',name:'Ivory blouse midi',color:'#e7dfca',trim:'#8d7860',roughness:.79,length:.30,flare:.33,pleat:.018,design:'pleated'},
    {id:'plum-asymmetric',name:'Plum one-side drape',color:'#663650',trim:'#d7a898',roughness:.56,length:.085,flare:.18,asym:.105,design:'drape'},
    {id:'scarlet-bell',name:'Scarlet petal dress',color:'#bf443c',trim:'#e4ad82',roughness:.66,length:.045,flare:.43,petals:.015,design:'petal'},
    {id:'teal-tulip',name:'Teal wrap pencil',color:'#358b88',trim:'#a9d2c4',roughness:.69,length:.095,flare:0,design:'pencil'},
    {id:'black-highlow',name:'Black evening train',color:'#28262d',trim:'#bbb8b3',roughness:.48,length:.30,flare:.36,highlow:.20,design:'evening'}
  ];
  function register(o){o.traverse(n=>{nodes.set(n.name,n);if(n.material)for(const m of Array.isArray(n.material)?n.material:[n.material]){const list=materials.get(m.name)||[];if(!list.includes(m))list.push(m);materials.set(m.name,list);}});}
  function warp(p,d){
    const t=clamp((1.035-p.y)/(1.035-hem)),a=Math.atan2(p.z-.027,p.x),rad=Math.hypot(p.x,p.z-.027);
    p.y-=(d.length||0)*t**1.35+(d.asym||0)*Math.cos(a)*t**3+(d.highlow||0)*(-Math.sin(a))*t**3+(d.petals||0)*(1+Math.cos(a*8))*t**3;
    const clearance=d.length>.15?smooth((.79-p.y)/.27):t;
    let f=1+(d.flare||0)*clearance*clearance;
    if(d.design==='pencil')f+=.10*Math.sin(Math.PI*t);
    const fold=(d.pleat||0)*Math.cos(a*12)*smooth((.86-p.y)/.30);
    p.x*=f+(rad>.001?fold/rad:0);p.z=.027+(p.z-.027)*(f+(rad>.001?fold/rad:0));
    if(d.design==='pencil'||d.design==='drape')p.z+=Math.max(0,Math.sin(a))*(d.design==='pencil'?.028:.020)*t*t;
    if(d.design==='evening')p.z+=Math.max(0,Math.sin(a))*.012*t*t;
    if(d.design==='drape'){const front=smooth((Math.sin(a)+.25)/.9);p.z+=.009*front*Math.sin(t*Math.PI*3)*t;p.x+=.010*Math.sin(t*Math.PI)*front;}
    return p;
  }
  function geometryFor(d){
    const g=new THREE.BufferGeometry();g.index=base.index;g.groups=base.groups.map(x=>({...x}));
    for(const [name,a] of Object.entries(base.attributes))g.setAttribute(name,name==='position'||name==='normal'?a.clone():a);
    g.morphAttributes=base.morphAttributes;g.morphTargetsRelative=true;
    const p=new THREE.Vector3(),q=new THREE.Vector3(),offset=new Float32Array(positions.count*3),normalDelta=new Float32Array(positions.count*3);
    for(let i=0;i<positions.count;i++){p.fromBufferAttribute(positions,i);q.copy(p);warp(q,d);g.attributes.position.setXYZ(i,q.x,q.y,q.z);q.sub(p);offset.set(q.toArray(),i*3);}
    g.computeVertexNormals();
    for(let i=0;i<positions.count;i++){p.fromBufferAttribute(g.attributes.normal,i);q.fromBufferAttribute(base.attributes.normal,i);p.sub(q);normalDelta.set(p.toArray(),i*3);}
    g.setAttribute('tailorReference',positions);g.setAttribute('tailorOffset',new THREE.BufferAttribute(offset,3));g.setAttribute('tailorNormalDelta',new THREE.BufferAttribute(normalDelta,3));
    g.computeBoundingBox();g.computeBoundingSphere();return g;
  }
  function materialFor(d,trim=false,panel=false){
    const m=new THREE.MeshStandardMaterial({name:`Tailored.${d.id}.${trim?'Trim':panel?'WrapPanel':'Fabric'}`,color:trim?d.trim:d.color,roughness:trim?.43:d.roughness,metalness:trim?.12:0,side:THREE.DoubleSide});
    m.userData.wardrobeColorChannel=trim?'trim':'fabric';
    m.onBeforeCompile=shader=>{
      shader.uniforms.tailorMotion={value:motion.matrix};shader.uniforms.tailorNormalMotion={value:motion.normal};
      shader.uniforms.tailorHemFollow=hemFollow;
      shader.vertexShader='attribute vec3 tailorReference; attribute vec3 tailorOffset; attribute vec3 tailorNormalDelta; uniform mat3 tailorMotion; uniform mat3 tailorNormalMotion; varying vec3 tailorRest;\n'+shader.vertexShader;
      shader.vertexShader=shader.vertexShader.replace('#include <begin_vertex>','#include <begin_vertex>\ntailorRest=tailorReference;');
      shader.vertexShader=shader.vertexShader.replace('#include <morphtarget_vertex>','#include <morphtarget_vertex>\ntransformed += tailorMotion*tailorOffset-tailorOffset;');
      if(d.design==='pencil'||d.design==='drape'||d.design==='evening'){
        shader.vertexShader='uniform vec3 tailorHemFollow;\n'+shader.vertexShader;
        shader.vertexShader=shader.vertexShader.replace('#include <displacementmap_vertex>',`#include <displacementmap_vertex>\nfloat hemT=clamp((1.035-tailorReference.y)/(1.035-${hem.toFixed(6)}),0.,1.);float hemFront=max(0.,(tailorReference.z-.027)/max(length(vec2(tailorReference.x,tailorReference.z-.027)),.001));transformed += tailorHemFollow*hemFront*hemT*hemT*${d.design==='evening'?'1.40':'1.0'};`);
      }
      shader.vertexShader=shader.vertexShader.replace('#include <morphnormal_vertex>','#include <morphnormal_vertex>\nobjectNormal += tailorNormalMotion*tailorNormalDelta-tailorNormalDelta;');
      if(trim||panel)shader.vertexShader=shader.vertexShader.replace('#include <displacementmap_vertex>',`#include <displacementmap_vertex>\ntransformed += normalize(objectNormal)*${trim?'.0027':'.0021'};`);
      shader.fragmentShader='varying vec3 tailorRest;\n'+shader.fragmentShader;
      const restHem=hem.toFixed(6),design=d.design;
      const belt=design==='evening'?'.010':design==='pleated'?'.009':'.006';
      const diagonal=`abs(tailorRest.x-(.075-(1.035-tailorRest.y)*.63))<.003 && tailorRest.y<1.035 && tailorRest.z>.045`;
      const yoke='false';
      let mask=trim?`bool belt=abs(tailorRest.y-1.035)<${belt};bool edge=tailorRest.y<${restHem}+.009;bool detail=${['wrap','pencil','drape'].includes(design)?diagonal:yoke};if(!(belt||edge||detail))discard;`:'';
      if(design==='pleated')mask+='if(tailorRest.y>1.05)discard;';
      if(panel)mask+=`if(tailorRest.z<.045||tailorRest.y>1.035||tailorRest.x<(.075-(1.035-tailorRest.y)*.63))discard;`;
      shader.fragmentShader=shader.fragmentShader.replace('#include <color_fragment>',`#include <color_fragment>\n${mask}`);
    };
    m.customProgramCacheKey=()=>`tailoring-v2-${d.id}-${trim}-${panel}`;return m;
  }
  function addMesh(group,g,m,name){const mesh=new THREE.Mesh(g,m);mesh.name=name;mesh.matrixAutoUpdate=false;mesh.frustumCulled=false;mesh.morphTargetInfluences=source.morphTargetInfluences;mesh.morphTargetDictionary=source.morphTargetDictionary;mesh.userData.wardrobeMotion=motion;mesh.userData.wardrobeSource=source.name;group.add(mesh);motion.users.push(mesh);derived.push(mesh);return mesh;}
  function upperPart(group,d,kind,trim=false){
    if(!native)return;
    const m=new THREE.MeshStandardMaterial({name:`Tailored.${d.id}.${kind}${trim?'Trim':''}`,color:trim?d.trim:d.color,roughness:.75,side:THREE.DoubleSide});m.userData.wardrobeColorChannel=trim?'trim':'fabric';
    m.onBeforeCompile=shader=>{
      shader.vertexShader='attribute vec3 wardrobeReference; attribute float wardrobeLowerBody; varying vec3 upperRest; varying float upperRegion;\n'+shader.vertexShader;
      shader.vertexShader=shader.vertexShader.replace('#include <begin_vertex>','#include <begin_vertex>\nupperRest=wardrobeReference;upperRegion=wardrobeLowerBody;');
      shader.vertexShader=shader.vertexShader.replace('#include <displacementmap_vertex>',`#include <displacementmap_vertex>\ntransformed += normalize(objectNormal)*${trim?'.008':kind==='Cape'?'.010':'.005'};`);
      shader.fragmentShader='varying vec3 upperRest; varying float upperRegion;\n'+shader.fragmentShader;
      let clip='';
      if(kind==='Blouse')clip='if(upperRest.y<1.035||upperRest.y>1.34||(abs(upperRest.x)>.135&&upperRest.y<1.16))discard;';
      if(kind==='Bolero')clip='if(upperRest.y>1.34 || (upperRegion>.5 && upperRest.y<1.17) || (upperRegion<.5 && upperRest.y<1.02))discard;if(upperRest.z>.035&&abs(upperRest.x)<(.040+(1.32-upperRest.y)*.26))discard;';
      if(kind==='Lapels')clip='if(upperRest.z<.035||upperRest.y<1.17||upperRest.y>1.32||abs(abs(upperRest.x)-(.052+(1.32-upperRest.y)*.26))>.010)discard;';
      if(kind==='Cape')clip='if(upperRest.x>-.015||upperRest.y<1.14||upperRest.y>1.34)discard;';
      if(kind==='CapeBinding')clip='if(upperRest.x>-.015||upperRest.y<1.14||upperRest.y>1.34)discard;if(!(upperRest.y<1.151||upperRest.x>-.024))discard;';
      shader.fragmentShader=shader.fragmentShader.replace('#include <color_fragment>','#include <color_fragment>\n'+clip);
    };m.customProgramCacheKey=()=>`tailored-native-${kind}-${trim}`;
    const mesh=new THREE.Mesh(native.geometry,m);mesh.name=`${group.name}.${kind}`;mesh.matrixAutoUpdate=false;mesh.frustumCulled=false;mesh.morphTargetInfluences=body.morphTargetInfluences;group.add(mesh);upperMeshes.push(mesh);
  }
  function foldedCollar(group,d){
    native.updateWorldMatrix(true,false);const inverse=native.matrixWorld.clone().invert(),ray=new THREE.Raycaster(),p=native.geometry.attributes.position,n=native.geometry.attributes.normal;
    const coords=[],indices=[],bindings=[],outline=[[.006,1.327],[.036,1.327],[.048,1.308],[.025,1.292],[.008,1.310]];
    for(const side of [-1,1]){
      const start=bindings.length,points=[[.022,1.313],...outline];
      for(const [k,xy] of points.entries()){
        ray.set(new THREE.Vector3(side*xy[0],xy[1],.5),new THREE.Vector3(0,0,-1));const hit=ray.intersectObject(native,false)[0];
        if(!hit)throw Error('Collar fitting could not find the native neckline.');
        const ids=[hit.face.a,hit.face.b,hit.face.c],at=hit.point.clone().applyMatrix4(inverse),triangle=new THREE.Triangle(...ids.map(i=>new THREE.Vector3().fromBufferAttribute(p,i))),bary=triangle.getBarycoord(at,new THREE.Vector3());
        const fold=k===0?.014:k===4?.012:.006;const target=hit.point.clone().add(new THREE.Vector3(0,0,fold)).applyMatrix4(inverse);coords.push(...target.toArray());bindings.push({ids,w:bary.toArray(),fold});
      }
      for(let i=1;i<=5;i++)indices.push(start,start+i,start+(i===5?1:i+1));
    }
    const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(coords,3));geometry.setIndex(indices);geometry.computeVertexNormals();geometry.morphTargetsRelative=true;
    for(const channel of ['position','normal'])geometry.morphAttributes[channel]=(native.geometry.morphAttributes[channel]||[]).map(attribute=>{
      const values=new Float32Array(bindings.length*3),v=new THREE.Vector3();
      for(let i=0;i<bindings.length;i++){v.set(0,0,0);const binding=bindings[i];for(let j=0;j<3;j++)v.addScaledVector(new THREE.Vector3().fromBufferAttribute(attribute,binding.ids[j]),binding.w[j]);values.set(v.toArray(),i*3);}return new THREE.BufferAttribute(values,3);
    });
    const material=new THREE.MeshStandardMaterial({name:`Tailored.${d.id}.FoldedCollar`,color:'#f4eddd',roughness:.77,side:THREE.DoubleSide});material.userData.wardrobeColorChannel='trim';
    const mesh=new THREE.Mesh(geometry,material);mesh.name=`${group.name}.FoldedCollar`;mesh.matrixAutoUpdate=false;mesh.frustumCulled=false;mesh.morphTargetInfluences=body.morphTargetInfluences;group.add(mesh);upperMeshes.push(mesh);
  }
  for(const d of definitions){
    const entry=info.slots.dress.find(i=>i.id===d.id);if(!entry)continue;
    for(const name of entry.nodes){const old=nodes.get(name);if(old)old.visible=false;}
    const group=new THREE.Group();group.name=`Tailored.Dress.${d.id}`;group.visible=false;avatar.add(group);
    const geometry=geometryFor(d);addMesh(group,geometry,materialFor(d),`${group.name}.Fabric`);
    addMesh(group,geometry,materialFor(d,true),`${group.name}.BoundEdges`);
    if(['wrap','pencil','drape'].includes(d.design))addMesh(group,geometry,materialFor(d,false,true),`${group.name}.OverlapPanel`);
    if(d.design==='pleated'){upperPart(group,d,'Blouse');foldedCollar(group,d);entry.coverage={...(entry.coverage||{}),bodyMaskRange:[.44,1.04]};}
    if(d.design==='column'){upperPart(group,d,'Bolero');entry.coverage={...(entry.coverage||{}),bodyMaskRange:[.70,1.04]};}
    if(d.design==='drape'){upperPart(group,d,'Cape');upperPart(group,d,'CapeBinding',true);entry.coverage={...(entry.coverage||{}),bodyMaskRange:[.558,1.04],bodyMaskHem:{base:.663,x:-.105,z:0}};}
    if(d.design==='wrap')entry.coverage={...(entry.coverage||{}),bodyMaskRange:[.638,1.04],bodyMaskHem:{base:.678,x:-.040,z:0}};
    if(d.design==='pencil')entry.coverage={...(entry.coverage||{}),bodyMaskRange:[.65,1.04]};
    if(d.design==='evening')entry.coverage={...(entry.coverage||{}),bodyMaskRange:[.615,1.04]};
    if(d.design==='petal')entry.coverage={...(entry.coverage||{}),underlayerMaskRange:[.72,1.04]};
    register(group);entry.nodes=[group.name];entry.name=d.name;entry.swatch=d.color;entry.note='Tailored cut, bound hem and fitted waist.';
  }
  info.tailoring={version:2,revision:'fit-17-evening-final-clearance',method:'Source dress deformation with transported tailored cuts and separate trim materials',styles:definitions.map(d=>d.id)};
  return {update(){
    const thighDelta=thighDriver.average().sub(thighDriver.neutral),skirtDelta=skirtDriver.average().sub(skirtDriver.neutral);
    hemInverse.setFromMatrix4(source.matrixWorld).invert();hemFollow.value.set(0,0,Math.min(.060,Math.max(0,thighDelta.z-skirtDelta.z)*1.8)).applyMatrix3(hemInverse);
    for(const mesh of derived){mesh.matrix.copy(source.matrix);mesh.morphTargetInfluences=source.morphTargetInfluences;}
    for(const mesh of upperMeshes){mesh.matrix.copy(body.matrix);mesh.morphTargetInfluences=body.morphTargetInfluences;}
  }};
}
