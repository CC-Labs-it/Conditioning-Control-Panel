import {fitWardrobe} from './wardrobe-fit.js';
import {expandWardrobe as authorHairstyles} from './wardrobe-hairstyles.js';
/* Live silhouette derivatives. Canonical meshes and animation buffers stay intact.
 * A fitted affine motion transports small rest offsets. Original morph position
 * and normal buffers are shared, so ten styles do not duplicate the animation.
 */
export function expandWardrobe({THREE,avatar,nodes,materials,info}) {
  const sources=new Map(), derived=[], reports=[];
  const repairedHair=new Map();
  function hairSurface(source) {
    if(!/Foundation/.test(source.name))return source.geometry;
    if(repairedHair.has(source))return repairedHair.get(source);
    const original=source.geometry, geometry=original.clone(),count=original.attributes.position.count;
    const adjacent=Array.from({length:count},()=>new Set());
    for(let i=0;i<original.index.count;i+=3){const a=original.index.getX(i),b=original.index.getX(i+1),c=original.index.getX(i+2);adjacent[a].add(b).add(c);adjacent[b].add(a).add(c);adjacent[c].add(a).add(b);}
    // Apply one identical linear filter to the neutral surface and each motion
    // component. This repairs decimation chatter without freezing the curls.
    function filtered(attribute){
      let values=new Float32Array(attribute.array),next=new Float32Array(values.length);
      for(let pass=0;pass<6;pass++){
        const strength=pass%2?-.51:.5;
        for(let i=0;i<count;i++)for(let k=0;k<3;k++){let sum=0;for(const j of adjacent[i])sum+=values[j*3+k];next[i*3+k]=values[i*3+k]+strength*(sum/Math.max(1,adjacent[i].size)-values[i*3+k]);}
        [values,next]=[next,values];
      }
      return new THREE.BufferAttribute(values,3);
    }
    geometry.setAttribute('position',filtered(original.attributes.position));
    geometry.computeVertexNormals();
    geometry.morphAttributes.position=original.morphAttributes.position.map(filtered);
    // Normal deltas use the same surface filter as position deltas.
    geometry.morphAttributes.normal=(original.morphAttributes.normal||[]).map(filtered);
    repairedHair.set(source,geometry);return geometry;
  }
  const smooth=x=>{x=Math.max(0,Math.min(1,x));return x*x*(3-2*x);};
  const item=(slot,id)=>info.slots[slot].find(i=>i.id===id);
  const uniqueMeshes=entry=>{
    const found=new Set();
    for(const name of entry.nodes){const root=nodes.get(name);if(!root)throw Error(`Missing wardrobe source ${name}`);root.traverse(o=>{if(o.isMesh)found.add(o);});}
    return [...found];
  };
  function motionFor(source) {
    if(sources.has(source))return sources.get(source);
    const position=source.geometry.attributes.position, samples=[], count=Math.min(48,position.count);
    const mean=new THREE.Vector3(), cov=new THREE.Matrix3(), cross=new THREE.Matrix3();
    const values=new Float64Array(9), center=new THREE.Vector3(), point=new THREE.Vector3();
    for(let i=0;i<count;i++){const index=Math.min(position.count-1,Math.floor((i+.37)*position.count/count));const p=new THREE.Vector3().fromBufferAttribute(position,index);mean.add(p);samples.push({index,base:p,current:new THREE.Vector3()});}
    mean.multiplyScalar(1/count);
    for(const s of samples){s.base.sub(mean);const a=s.base.toArray();for(let r=0;r<3;r++)for(let c=0;c<3;c++)values[c*3+r]+=a[r]*a[c];}
    const regularizer=Math.max(1e-10,(values[0]+values[4]+values[8])*.00001);
    values[0]+=regularizer;values[4]+=regularizer;values[8]+=regularizer;
    cov.fromArray(values).invert();
    const state={matrix:new THREE.Matrix3(),normal:new THREE.Matrix3(),source,users:[],update(){
      if(!state.users.some(m=>m.parent?.visible&&m.visible))return;
      center.set(0,0,0);
      for(const s of samples){source.getVertexPosition(s.index,s.current);center.add(s.current);}
      center.multiplyScalar(1/count);values.fill(0);
      for(const s of samples){point.copy(s.current).sub(center);const a=point.toArray(),b=s.base.toArray();for(let r=0;r<3;r++)for(let c=0;c<3;c++)values[c*3+r]+=a[r]*b[c];}
      values[0]+=regularizer;values[4]+=regularizer;values[8]+=regularizer;
      cross.fromArray(values);state.matrix.multiplyMatrices(cross,cov);
      state.normal.copy(state.matrix).invert().transpose();
    }};
    sources.set(source,state);return state;
  }
  function register(root) {
    root.traverse(o=>{
      nodes.set(o.name,o);
      if(!o.isMesh)return;
      for(const mat of Array.isArray(o.material)?o.material:[o.material]){
        if(!materials.has(mat.name))materials.set(mat.name,[]);
        if(!materials.get(mat.name).includes(mat))materials.get(mat.name).push(mat);
      }
    });
  }
  function cloneStyle(slot,sourceEntry,definition) {
    const group=new THREE.Group();group.name=`Expanded.${slot}.${definition.id}`;group.visible=false;
    avatar.add(group);
    let maxOffset=0;
    for(const [partIndex,source] of uniqueMeshes(sourceEntry).entries()){
      if(definition.exclude?.test(source.name))continue;
      const old=slot==='hair'?hairSurface(source):source.geometry, geometry=new THREE.BufferGeometry();
      geometry.index=old.index;
      for(const [name,attribute] of Object.entries(old.attributes))geometry.setAttribute(name,attribute);
      geometry.groups=old.groups.map(g=>({...g}));geometry.morphAttributes=old.morphAttributes;geometry.morphTargetsRelative=old.morphTargetsRelative;
      const position=old.attributes.position, normal=old.attributes.normal;
      const offsets=new Float32Array(position.count*3), normalDelta=new Float32Array(position.count*3), positions=new Float32Array(position.count*3);
      const p=new THREE.Vector3(), q=new THREE.Vector3(), n=new THREE.Vector3(), axis=new THREE.Vector3(), before=new THREE.Vector3(), after=new THREE.Vector3();
      const worldWarp=definition.warp||((v)=>v), epsilon=.0002, jacobian=new THREE.Matrix3(), jvalues=new Float64Array(9);
      const center=new THREE.Vector3();for(let i=0;i<position.count;i++)center.add(new THREE.Vector3().fromBufferAttribute(position,i));center.multiplyScalar(1/position.count);
      source.updateWorldMatrix(true,false);
      const neutralWorld=source.matrixWorld.clone(),neutralInverse=neutralWorld.clone().invert(),worldCenter=center.clone().applyMatrix4(neutralWorld);
      const warp=v=>{v.applyMatrix4(neutralWorld);worldWarp(v,source.name,worldCenter);return v.applyMatrix4(neutralInverse);};
      for(let i=0;i<position.count;i++){
        p.fromBufferAttribute(position,i);q.copy(p);warp(q,source.name,center);
        positions.set(q.toArray(),i*3);const offset=q.clone().sub(p);offsets.set(offset.toArray(),i*3);maxOffset=Math.max(maxOffset,offset.length());
        if(normal){
          for(let c=0;c<3;c++){
            axis.set(0,0,0).setComponent(c,epsilon);before.copy(p).sub(axis);after.copy(p).add(axis);warp(before,source.name,center);warp(after,source.name,center);after.sub(before).multiplyScalar(.5/epsilon);
            jvalues[c*3]=after.x;jvalues[c*3+1]=after.y;jvalues[c*3+2]=after.z;
          }
          jacobian.fromArray(jvalues).invert().transpose();n.fromBufferAttribute(normal,i);q.copy(n).applyMatrix3(jacobian).normalize().sub(n);normalDelta.set(q.toArray(),i*3);
        }
      }
      geometry.setAttribute('position',new THREE.BufferAttribute(positions,3));
      geometry.setAttribute('wardrobeOffset',new THREE.BufferAttribute(offsets,3));
      geometry.setAttribute('wardrobeNormalDelta',new THREE.BufferAttribute(normalDelta,3));
      geometry.computeBoundingBox();geometry.computeBoundingSphere();
      const motion=motionFor(source);
      const sourceMats=Array.isArray(source.material)?source.material:[source.material];
      const mats=sourceMats.map((sourceMat,index)=>{
        const mat=sourceMat.clone();mat.name=`Expanded.Material.${definition.id}.${partIndex}.${index}`;
        if(definition.color)mat.color.set(definition.color);
        if(definition.roughness!==undefined)mat.roughness=definition.roughness;
        if(definition.metalness!==undefined)mat.metalness=definition.metalness;
        if(slot==='hair'){mat.polygonOffset=true;mat.polygonOffsetFactor=-1-partIndex*.1;mat.polygonOffsetUnits=-1-partIndex*.1;if(/Foundation/.test(source.name))mat.side=THREE.FrontSide;}
        mat.onBeforeCompile=shader=>{
          shader.uniforms.wardrobeMotion={value:motion.matrix};shader.uniforms.wardrobeNormalMotion={value:motion.normal};
          shader.vertexShader=`attribute vec3 wardrobeOffset;\nattribute vec3 wardrobeNormalDelta;\nuniform mat3 wardrobeMotion;\nuniform mat3 wardrobeNormalMotion;\n${shader.vertexShader}`;
          shader.vertexShader=shader.vertexShader.replace('#include <morphtarget_vertex>','#include <morphtarget_vertex>\ntransformed += wardrobeMotion * wardrobeOffset - wardrobeOffset;');
          shader.vertexShader=shader.vertexShader.replace('#include <morphnormal_vertex>','#include <morphnormal_vertex>\nobjectNormal += wardrobeNormalMotion * wardrobeNormalDelta;');
        };
        mat.customProgramCacheKey=()=> 'rain-expanded-affine-v1';return mat;
      });
      const mesh=new THREE.Mesh(geometry,Array.isArray(source.material)?mats:mats[0]);
      mesh.name=`${group.name}.Part${partIndex}`;mesh.frustumCulled=false;
      if(slot==='hair')mesh.renderOrder=5+partIndex;
      mesh.position.copy(source.position);mesh.quaternion.copy(source.quaternion);mesh.scale.copy(source.scale);
      // glTF action tracks continue driving canonical source arrays, including
      // while hidden. Shared buffers and weights preserve facial/hair timing.
      mesh.morphTargetInfluences=source.morphTargetInfluences;
      mesh.morphTargetDictionary=source.morphTargetDictionary;
      mesh.userData.wardrobeSource=source.name;mesh.userData.wardrobeMotion=motion;
      mesh.userData.wardrobeNeutralMatrix=neutralWorld.toArray();
      group.add(mesh);motion.users.push(mesh);derived.push({mesh,source});
    }
    register(group);
    const result={id:definition.id,name:definition.name,nodes:[group.name],swatch:definition.swatch||definition.color||sourceEntry.swatch,note:definition.note||'Live silhouette'};
    const index=info.slots[slot].findIndex(x=>x.id===result.id);
    if(index<0)info.slots[slot].push(result);else info.slots[slot][index]=result;
    reports.push({id:definition.id,maxRestOffsetM:Number(maxOffset.toFixed(4)),parts:group.children.length});
    return result;
  }

  const originalDress=item('dress','satin-original');
  const garment=(hem,flare,asymmetry=0,wave=0)=>(p)=>{
    const t=Math.max(0,Math.min(1,(1.025-p.y)/.27)),angle=Math.atan2(p.z-.027,p.x);
    const scale=1+flare*t*t;
    p.x*=scale;p.z=.027+(p.z-.027)*scale;
    p.y-=hem*t**1.25+asymmetry*Math.sin(angle)*t**3+wave*Math.cos(angle*8)*t**3;
  };
  // Five additions plus the original three and foundation's two hero garments.
  const dressDefs=[
    {id:'ivory-midi',name:'Ivory midi',color:'#fff0d9',swatch:'#e8dcc2',roughness:.62,warp:garment(.18,.20)},
    {id:'plum-asymmetric',name:'Plum asymmetric',color:'#6c335d',swatch:'#713855',roughness:.36,warp:garment(.065,.12,.075)},
    {id:'scarlet-bell',name:'Scarlet bell',color:'#e95148',swatch:'#c63a3c',roughness:.55,warp:garment(.045,.42,0,.006)},
    {id:'teal-tulip',name:'Teal tulip',color:'#4cb8b4',swatch:'#2e8d89',roughness:.40,warp:p=>{const t=Math.max(0,Math.min(1,(1.025-p.y)/.27));const s=1+.20*Math.sin(Math.PI*t);p.x*=s;p.z=.027+(p.z-.027)*s;p.y-=.08*t;}},
    {id:'black-highlow',name:'Black high-low',color:'#34303b',swatch:'#27232e',roughness:.43,warp:garment(.10,.24,-.085)}
  ];
  for(const def of dressDefs)cloneStyle('dress',originalDress,def);

  const originalHair=item('hair','original-blonde');
  info.slots.hair.push({...originalHair,id:'source-original',name:'Original render hair',note:'Unchanged source'});
  function hairWarp({length=1,width=1,curl=0,frontDrop=.025,side=0,clearance=.010}) {
    return (p,name)=>{
      const originalY=p.y, below=smooth((1.59-originalY)/.25);
      const isFront=/Swept|front/i.test(name);
      const lower=Math.min(0,originalY-1.59);p.y=originalY+lower*((length<.6&&/Swept/i.test(name)?1:length)-1);
      p.x*=1+(width-1)*below;
      if(isFront){
        const curlGate=smooth((1.60-originalY)/.10);
        p.y-=frontDrop*curlGate;
        // Push the curls sideways and forward across the cheek band. The
        // fitted crown is pinned; lower locks stay outside the shoulder/body.
        const cheek=smooth((1.67-originalY)/.09)*(1-smooth((1.39-originalY)/.10));
        p.x+=Math.sign(p.x||1)*clearance*cheek;p.z+=clearance*.55*cheek;
      }
      p.x+=side*below*(p.x>0?1:.35);
      if(curl){const phase=(originalY-1.12)*22;p.x+=Math.sign(p.x||1)*curl*Math.sin(phase)*below;p.z+=curl*.6*Math.cos(phase)*below;}
      if(length<1&&p.z>.095){
        // A shortened lock must also lose forward reach. Keeping the full
        // long-cut depth while lifting its tip makes an unsupported tusk.
        const tip=smooth((1.51-originalY)/.22),depth=Math.pow(length,1.7);
        p.z=.095+(p.z-.095)*(1-(1-depth)*tip);
        p.y-=.035*(1-length)*tip;
      }
      if(length<.6&&/Foundation/i.test(name)){
        const gate=smooth((1.60-originalY)/.16),angle=Math.atan2((p.z-.02)/.13,(p.x+.02)/.17);
        p.x=p.x*(1-gate)+(-.02+.17*Math.cos(angle))*gate;
        p.z=p.z*(1-gate)+(.02+.13*Math.sin(angle))*gate;
      }
      // Preserve space around cheeks and shoulders when shortening/narrowing.
      if(isFront){
        const cheek=smooth((p.y-1.32)/.10),minimum=.17-.024*cheek;
        const fit=smooth((Math.abs(p.x)-.08)/.025)*smooth((p.y-1.22)/.08)*(1-smooth((p.y-1.55)/.08));
        p.x+=Math.sign(p.x)*Math.max(0,minimum-Math.abs(p.x))*fit;
      }
      if(/Secondary|Swept/i.test(name)){
        const dx=p.x+.02,dz=p.z-.02,r=Math.hypot(dx,dz)||1;
        const clearance=.0035*smooth((1.73-originalY)/.07);
        p.x+=dx/r*clearance;p.z+=dz/r*clearance;
      }
    };
  }
  const hairDefs=[
    {id:'chestnut-bob',name:'Chestnut bob',color:'#74432e',swatch:'#74432e',exclude:/Secondary/i,warp:hairWarp({length:.34,width:1.02,frontDrop:.022,clearance:.019})},
    {id:'original-blonde',name:'Soft blonde curls',swatch:'#e7cc9a',warp:hairWarp({frontDrop:.035,clearance:.012})},
    {id:'copper-lob',name:'Copper shoulder cut',color:'#cf8754',swatch:'#ac6540',warp:hairWarp({length:.62,width:.95,frontDrop:.045})},
    {id:'ink-sleek',name:'Ink sleek',color:'#493f4b',swatch:'#332c38',warp:hairWarp({length:1.04,width:.83,frontDrop:.025})},
    {id:'rose-waves',name:'Rose long waves',color:'#d78eaa',swatch:'#b87594',warp:hairWarp({length:1.1,width:1.05,curl:.014,frontDrop:.040})},
    {id:'honey-roundbob',name:'Honey rounded bob',color:'#eec18a',swatch:'#bc8b51',exclude:/Secondary/i,warp:hairWarp({length:.43,width:1.14,frontDrop:.027,clearance:.022})},
    {id:'espresso-sidesweep',name:'Espresso side sweep',color:'#8a6a59',swatch:'#564034',warp:hairWarp({length:.74,width:.96,side:.030,frontDrop:.050})},
    {id:'platinum-long',name:'Platinum long',color:'#ffffff',swatch:'#e8e3da',warp:hairWarp({length:1.15,width:.91,frontDrop:.035})},
    {id:'auburn-volume',name:'Auburn full waves',color:'#b87956',swatch:'#865038',warp:hairWarp({length:.95,width:1.16,curl:.017,frontDrop:.060})}
  ];
  for(const def of hairDefs)cloneStyle('hair',originalHair,def);
  // Replaced bob source nodes are still in the file. Hide them permanently;
  // their original actions remain available and untouched.
  for(const [name,node] of nodes)if(name.startsWith('WardrobePrototype.Hair.Bob.'))node.visible=false;

  const accessories=[
    ['hoops-original',{id:'silver-drops',name:'Silver drops',color:'#dce7ee',swatch:'#c7d2da',metalness:.75,roughness:.23,warp:(p,n,c)=>{p.x=c.x+(p.x-c.x)*.70;p.y=c.y+(p.y-c.y)*1.22;}}],
    ['glasses',{id:'cat-eye-frames',name:'Cat-eye frames',color:'#7a384e',swatch:'#74364a',roughness:.24,warp:(p,n,c)=>{const side=Math.abs(p.x-c.x);p.y+=.009*smooth((side-.050)/.045);p.x=c.x+(p.x-c.x)*1.055;}}],
    ['velvet-choker',{id:'rose-gold-collar',name:'Rose-gold collar',color:'#e6b29a',swatch:'#d6a18b',metalness:.7,roughness:.30,warp:(p,n,c)=>{p.y=c.y+(p.y-c.y)*.52-.011;p.x=c.x+(p.x-c.x)*1.045;p.z=c.z+(p.z-c.z)*1.045;}}],
    ['hair-bow',{id:'midnight-ribbon',name:'Midnight ribbon',color:'#514d6a',swatch:'#4f4865',roughness:.61,warp:(p,n,c)=>{p.x=c.x+(p.x-c.x)*1.30;p.y=c.y+(p.y-c.y)*1.14;}}]
  ];
  for(const [source,definition] of accessories)cloneStyle('accessories',item('accessories',source),definition);
  const fitted=fitWardrobe({THREE,avatar,nodes,materials,info});
  const hairstyles=authorHairstyles({THREE,avatar,nodes,materials,info});
  info.expansion={version:1,method:'Shared source morphs with affine-transported rest-shape offsets',items:reports,limitations:['Runtime silhouette studies use the existing fitted garment construction.','Affine offset transport follows source animation; extreme combinations require fitting review.']};
  return {update(dt,look){
    avatar.updateMatrixWorld(true);
    for(const {mesh,source} of derived){
      if(mesh.morphTargetInfluences!==source.morphTargetInfluences)mesh.morphTargetInfluences=source.morphTargetInfluences;
      // Hair and accessories carry animated anchor TRS in addition to morphs.
      // Preserve the complete source world transform beneath the new group.
      mesh.matrix.copy(mesh.parent.matrixWorld).invert().multiply(source.matrixWorld);
      mesh.matrix.decompose(mesh.position,mesh.quaternion,mesh.scale);
    }
    for(const state of sources.values())state.update();
    fitted.update(dt,look);
    hairstyles.update(dt,look);
  }};
}
