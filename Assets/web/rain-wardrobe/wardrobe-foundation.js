// pelvis-data.js (26 MB) is not in the repo: the CCP host serves BIG_ASSETS from a local folder.
import {BIG_ASSETS} from './host-bridge.js';
import {moduleUrl,releaseModule} from './wardrobe-loading.js';
// Modular derivatives share the exported evaluated animation and geometry.
export async function expandWardrobe({THREE,avatar,nodes,materials,info}) {
  // viewer.js prefetches it with a byte count (the boot cover's bar); without that, the plain url.
  const pelvisUrl=BIG_ASSETS+'pelvis-data.js';
  let pelvisModule;try{pelvisModule=await import(await moduleUrl(pelvisUrl));}catch{pelvisModule=await import(pelvisUrl);}releaseModule(pelvisUrl);
  const {nativePelvis}=pelvisModule;
  const sync=[],anchors=[],generated=[],shafts=[],pantsCuts=[];
  const coveredRange={value:new THREE.Vector2(100,100)},coveredHem={value:new THREE.Vector3()},hasCoveredHem={value:0};
  avatar.updateMatrixWorld(true);
  function register(o){o.traverse(n=>{nodes.set(n.name,n);if(n.material)for(const m of Array.isArray(n.material)?n.material:[n.material]){const list=materials.get(m.name)||[];if(!list.includes(m))list.push(m);materials.set(m.name,list);}});}
  const mat=(name,color,roughness=.55,metalness=0)=>{const m=new THREE.MeshStandardMaterial({name,color,roughness,metalness,side:THREE.DoubleSide});m.userData.wardrobeColorChannel=/Mask|Katana/.test(name)?'accessories':/Shoes|boots|heels|Shaft|Cuff/i.test(name)?'shoes':'fabric';return m;};
  function group(name){const g=new THREE.Group();g.name=name;g.visible=false;avatar.add(g);nodes.set(name,g);generated.push(g);return g;}
  const originals=[...new Set(nodes.values())].filter(n=>n.isMesh);
  const body=nodes.get('GEO-rain-body'),dress=nodes.get('Companion.Wardrobe.Dress'),head=nodes.get('GEO-rain-head');
  if(!body?.isMesh||!dress?.isMesh||!head?.isMesh)throw new Error('Wardrobe foundation requires the animated Rain body, dress and head.');
  function unpack(value,Type=Float32Array){const decoded=atob(value),bytes=new Uint8Array(decoded.length);for(let i=0;i<bytes.length;i++)bytes[i]=decoded.charCodeAt(i);return new Type(bytes.buffer);}
  const semanticLower=unpack(nativePelvis.lowerBody);
  if(semanticLower.length!==body.geometry.attributes.position.count)throw new Error('Native body region mask does not match the avatar.');
  body.geometry.setAttribute('wardrobeLowerBody',new THREE.BufferAttribute(semanticLower,1));
  const pelvis=new THREE.BufferGeometry();
  pelvis.setAttribute('position',new THREE.BufferAttribute(unpack(nativePelvis.position),3));
  pelvis.setAttribute('normal',new THREE.BufferAttribute(unpack(nativePelvis.normal),3));
  pelvis.setAttribute('wardrobeReference',pelvis.attributes.position.clone());
  pelvis.setAttribute('wardrobeReferenceNormal',pelvis.attributes.normal.clone());
  pelvis.setAttribute('wardrobeLowerBody',new THREE.BufferAttribute(unpack(nativePelvis.patchLowerBody),1));
  pelvis.setIndex(new THREE.BufferAttribute(unpack(nativePelvis.indices,Uint32Array),1));
  pelvis.morphTargetsRelative=true;
  for(const [channel,key] of [['position','morphPosition'],['normal','morphNormal']]){const values=unpack(nativePelvis[key]);pelvis.morphAttributes[channel]=Array.from({length:nativePelvis.rank},(_,i)=>new THREE.BufferAttribute(values.subarray(i*nativePelvis.vertices*3,(i+1)*nativePelvis.vertices*3),3));}
  function clone(source,parent,material,name,geometry=source.geometry){
    const mesh=new THREE.Mesh(geometry,material);mesh.name=name;mesh.matrixAutoUpdate=false;mesh.frustumCulled=false;
    if(source.morphTargetInfluences)mesh.morphTargetInfluences=source.morphTargetInfluences;
    mesh.morphTargetDictionary=source.morphTargetDictionary;parent.add(mesh);sync.push([mesh,source]);register(mesh);return mesh;
  }
  const restGeometries=new Map();
  function restGeometry(source){
    if(restGeometries.has(source))return restGeometries.get(source);
    const g=source.geometry.clone(),p=g.attributes.position,a=new Float32Array(p.count*3),norm=new Float32Array(p.count*3),v=new THREE.Vector3(),nm=new THREE.Matrix3().getNormalMatrix(source.matrixWorld);
    for(let i=0;i<p.count;i++){v.fromBufferAttribute(p,i).applyMatrix4(source.matrixWorld);a.set(v.toArray(),i*3);v.fromBufferAttribute(g.attributes.normal,i).applyMatrix3(nm).normalize();norm.set(v.toArray(),i*3);}
    g.setAttribute('wardrobeReference',new THREE.BufferAttribute(a,3));g.setAttribute('wardrobeReferenceNormal',new THREE.BufferAttribute(norm,3));if(!g.attributes.wardrobeLowerBody)g.setAttribute('wardrobeLowerBody',new THREE.BufferAttribute(new Float32Array(p.count).fill(1),1));restGeometries.set(source,g);return g;
  }
  function garmentShader(material,{min=-10,max=10,pad=0,stripe=false,gloves=false,halfWidth=10,shoeClip=false,lowerOnly=false,pelvisCut=false,zipper=false,coverageFromDress=false}={}){
    const lower={value:min};if(shoeClip)pantsCuts.push(lower);
    material.onBeforeCompile=shader=>{
      shader.uniforms.foundationLower=lower;
      shader.uniforms.foundationCoveredRange=coveredRange;shader.uniforms.foundationCoveredHem=coveredHem;shader.uniforms.foundationHasCoveredHem=hasCoveredHem;
      shader.vertexShader='attribute vec3 wardrobeReference; attribute vec3 wardrobeReferenceNormal; attribute float wardrobeLowerBody; varying float foundationLowerBody; varying vec3 foundationRest; varying vec3 foundationNormal;\n'+shader.vertexShader;
      if(lowerOnly&&shoeClip)shader.vertexShader='uniform vec2 foundationCoveredRange; uniform vec3 foundationCoveredHem; uniform float foundationHasCoveredHem;\n'+shader.vertexShader;
      shader.vertexShader=shader.vertexShader.replace('#include <begin_vertex>',`#include <begin_vertex>\nfoundationRest=wardrobeReference;foundationNormal=wardrobeReferenceNormal;foundationLowerBody=wardrobeLowerBody;`);
      if(pad)shader.vertexShader=shader.vertexShader.replace('#include <displacementmap_vertex>',`#include <displacementmap_vertex>\n${lowerOnly&&shoeClip?'vec2 padAxis=vec2(foundationRest.x,foundationRest.z-.027);padAxis/=max(length(padAxis),.0001);float padHem=foundationHasCoveredHem>.5?foundationCoveredHem.x+dot(foundationCoveredHem.yz,padAxis):foundationCoveredRange.x;float padClearance=1.-smoothstep(padHem-.10,padHem-.05,foundationRest.y);':''}\ntransformed += normalize(objectNormal) * ${gloves?`mix(${pad.toFixed(5)},.003,step(.185,abs(foundationRest.x))*(1.-smoothstep(.80,.86,foundationRest.y)))`:pad.toFixed(5)}${lowerOnly&&shoeClip?'*padClearance':''};`);
      shader.fragmentShader='varying vec3 foundationRest; varying vec3 foundationNormal; varying float foundationLowerBody; uniform float foundationLower; uniform vec2 foundationCoveredRange; uniform vec3 foundationCoveredHem; uniform float foundationHasCoveredHem;\n'+shader.fragmentShader;
      shader.fragmentShader=shader.fragmentShader.replace('#include <color_fragment>',`#include <color_fragment>
        if(foundationRest.y<foundationLower || foundationRest.y>${max.toFixed(5)} || abs(foundationRest.x)>${halfWidth.toFixed(5)}) discard;
        ${lowerOnly?'if(foundationLowerBody<.5)discard;':''}
        ${lowerOnly||coverageFromDress?'vec2 hemAxis=vec2(foundationRest.x,foundationRest.z-.027);hemAxis/=max(length(hemAxis),.0001);float coveredBottom=foundationHasCoveredHem>.5?foundationCoveredHem.x+dot(foundationCoveredHem.yz,hemAxis):foundationCoveredRange.x;if(foundationRest.y>coveredBottom&&foundationRest.y<foundationCoveredRange.y)discard;':''}
        ${pelvisCut?'if(foundationLowerBody>.5 && foundationRest.y>.72 && foundationRest.y<1.40)discard;':''}
        ${stripe?'float outerLine=smoothstep(.76,.90,abs(normalize(foundationNormal).x)); diffuseColor.rgb=mix(diffuseColor.rgb,vec3(.012,.014,.018),outerLine);':''}
        ${zipper?'float zip=(1.-smoothstep(.0015,.0035,abs(foundationRest.x)))*step(.90,foundationRest.y)*step(.4,foundationNormal.z);diffuseColor.rgb=mix(diffuseColor.rgb,vec3(.12,.11,.07),zip*.8);':''}
        ${gloves?'float glove=step(.185,abs(foundationRest.x))*(1.-smoothstep(.80,.86,foundationRest.y)); diffuseColor.rgb=mix(diffuseColor.rgb,vec3(.015,.11,.26),glove);':''}`);
    };
    material.customProgramCacheKey=()=>JSON.stringify({min,max,pad,stripe,gloves,halfWidth,shoeClip,lowerOnly,pelvisCut,zipper,coverageFromDress});
  }
  const shoeSources=originals.filter(n=>(n.userData.name||n.name).includes('Companion.BootV3.'));
  const shoeNames=shoeSources.map(n=>n.userData.name||n.name);
  const shoes=[{id:'original-boots',name:'Plum ankle boots',swatch:'#7d294f',nodes:shoeNames}];
  const shoeDefs=[['black-boots','Ink ankle boots','#202128'],['ivory-boots','Ivory ankle boots','#ddd5c2'],['oxblood-boots','Oxblood boots','#702430'],['midcalf-boots','Mid-calf boots','#282b34',null,.39],['silver-boots','Silver boots','#a5b3bd'],['knee-high-boots','Knee-high boots','#28232e',null,.61],['rose-shoes','Rose cuff boots','#be6784',.19],['hazmat-boots','Safety boots','#25465b'],['yellow-trainers','Yellow cuff boots','#e6b62e',.19]];
  for(const [id,name,color,ceiling,shaftTop] of shoeDefs){
    const root=group(`Foundation.Shoes.${id}`);
    for(const src of shoeSources){
      const srcName=src.userData.name||src.name;const isMetal=/zip|clasp/i.test(srcName),isSole=/sole|heel tip|welt/i.test(srcName);
      const m=mat(`Foundation.${id}.${isMetal?'metal':isSole?'sole':'upper'}`,isMetal?'#a79b79':isSole?'#242229':color,.5,isMetal?.5:id==='silver-boots'?.45:0);
      if(ceiling)garmentShader(m,{max:ceiling});
      clone(src,root,m,`${root.name}.${srcName}`,ceiling?restGeometry(src):src.geometry);
    }
    if(ceiling){
      const collar=mat(`Foundation.${id}.FittedUpper`,color,.5);
      garmentShader(collar,{min:.135,max:ceiling+.005,pad:.007,lowerOnly:true});
      clone(body,root,collar,`${root.name}.FittedOpening`,restGeometry(body));
    }
    if(shaftTop){
      const leather=mat(`Foundation.${id}.Shaft`,color,.55);garmentShader(leather,{min:.15,max:shaftTop,pad:.014,halfWidth:.18,coverageFromDress:true});
      clone(body,root,leather,`${root.name}.FittedShaft`,restGeometry(body));
      const cuff=mat(`Foundation.${id}.Cuff`,color,.4);garmentShader(cuff,{min:shaftTop-.016,max:shaftTop+.001,pad:.017,halfWidth:.18,coverageFromDress:true});
      clone(body,root,cuff,`${root.name}.RaisedCuff`,restGeometry(body));
      shafts.push({root,top:shaftTop});
    }
    shoes.push({id,name,swatch:color,nodes:[root.name],...(shaftTop?{coverage:{hideBodyBelow:shaftTop,bodyHalfWidth:.18}}:ceiling?{coverage:{hideBodyBelow:ceiling-.005,bodyHalfWidth:.18}}:{})});
  }
  info.footwearFit={revision:'opening-2',method:'Animated native ankle collars overlap clipped low heel uppers'};
  info.slots.shoes=shoes;info.defaultLook.shoes='original-boots';
  const legwear=[];
  const hosiery=[['fishnets-original','Original fishnets','#432032','fishnet',.78,5],['bare-legs','Bare legs','#dfb59e','bare',0,5],['sheer-black','Sheer black','#29232c','tights',.42,5],['opaque-black','Opaque black','#171922','tights',.94,5],['ivory-tights','Ivory tights','#ded5c7','tights',.9,5],['burgundy-tights','Wine tights','#6e1e3a','tights',.82,5],['fine-fishnet','Fine fishnets','#37222d','fishnet',.7,8]];
  for(const [id,name,color,mode,opacity,density] of hosiery)legwear.push({id,name,swatch:color,nodes:[],legwear:{mode,color,opacity,density,lower:.129,upper:.713}});
  for(const [id,name,color,stripe] of [['ink-trousers','Ink fitted trousers','#252732',false],['hazmat-trousers','Yellow coverall legs','#deb630',false],['action-leggings','Yellow stripe trousers','#e9b82d',true]]){
    const root=group(`Foundation.Legs.${id}`),m=mat(`Foundation.${id}`,color,.8);garmentShader(m,{min:.13,max:.92,pad:id==='hazmat-trousers'?.013:.006,stripe,halfWidth:.18,shoeClip:true,lowerOnly:true});
    clone(body,root,m,`${root.name}.Fitted`,pelvis);
    legwear.push({id,name,swatch:color,nodes:[root.name],legwear:{mode:'pants',color,opacity:1,density:5,lower:.129,upper:.92},coverage:{hideBodyBelow:.92,bodyHalfWidth:.18}});
  }
  info.slots.legwear=legwear;info.defaultLook.legwear='fishnets-original';
  const yellow=mat('Foundation.Hazmat.Coated yellow','#dfbb35',.8),suit=group('Foundation.Hazmat.Coverall');
  // Restore the complete unmasked native surface as one continuous garment.
  // It shares the existing sampled body coefficients, including hands and seat.
  garmentShader(yellow,{min:.15,pad:.013,gloves:true,zipper:true});clone(body,suit,yellow,'Foundation.Hazmat.Complete',pelvis);
  const actionRoot=group('Foundation.Action.Dress'),actionMat=mat('Foundation.Action.Gold','#edbd30',.64);
  garmentShader(actionMat,{min:.145,pad:.005,zipper:true});
  const actionCompile=actionMat.onBeforeCompile;
  actionMat.onBeforeCompile=shader=>{actionCompile(shader);shader.fragmentShader=shader.fragmentShader.replace('#include <color_fragment>','#include <color_fragment>\nif(foundationLowerBody<.5 && foundationRest.y<.97 && abs(foundationRest.x)>.202)discard;');};
  clone(body,actionRoot,actionMat,'Foundation.Action.ContinuousSuit',pelvis);
  const actionTrim=mat('Foundation.Action.SideStripes','#1d2025',.71);actionTrim.userData.wardrobeColorChannel='trim';
  garmentShader(actionTrim,{min:.145,pad:.0057});const stripeCompile=actionTrim.onBeforeCompile;
  actionTrim.onBeforeCompile=shader=>{stripeCompile(shader);shader.fragmentShader=shader.fragmentShader.replace('#include <color_fragment>','#include <color_fragment>\nif(abs(normalize(foundationNormal).x)<.91)discard;\nif(foundationLowerBody<.5 && foundationRest.y<.97 && abs(foundationRest.x)>.202)discard;');};
  clone(body,actionRoot,actionTrim,'Foundation.Action.OuterSeamStripes',pelvis);
  function primitive(parent,geometry,material,name,position,scale,rotation){
    const mesh=new THREE.Mesh(geometry,material);mesh.name=name;if(position)mesh.position.fromArray(position);if(scale)mesh.scale.fromArray(scale);if(rotation)mesh.rotation.set(...rotation);parent.add(mesh);register(mesh);return mesh;
  }
  function headFollow(g){const inv=head.matrixWorld.clone().invert();anchors.push(()=>{g.matrix.copy(head.matrixWorld).multiply(inv);g.matrixAutoUpdate=false;});}
  // Fit both perimeter curves against the evaluated head in its neutral pose.
  // Morph accessor bounds include basis extremes, not the current visible head.
  const headBounds=new THREE.Box3(),headPoint=new THREE.Vector3();
  for(let i=0;i<head.geometry.attributes.position.count;i++){head.getVertexPosition(i,headPoint);headPoint.applyMatrix4(head.matrixWorld);headBounds.expandByPoint(headPoint);}
  const cx=headBounds.getCenter(new THREE.Vector3()).x,cy=headBounds.max.y-.184;
  const ray=new THREE.Raycaster(),rayOrigin=new THREE.Vector3(),rayDirection=new THREE.Vector3(0,0,-1);
  const fitHead=new THREE.Mesh(head.geometry,new THREE.MeshBasicMaterial({side:THREE.DoubleSide}));fitHead.matrixAutoUpdate=false;fitHead.matrixWorld.copy(head.matrixWorld);fitHead.morphTargetInfluences=head.morphTargetInfluences;
  function faceDepth(x,y){rayOrigin.set(x,y,.6);ray.set(rayOrigin,rayDirection);return ray.intersectObject(fitHead,false)[0]?.point.z??.055;}
  function tube(parent,points,radius,material,name,closed=false){const curve=new THREE.CatmullRomCurve3(points,closed,'centripetal');return primitive(parent,new THREE.TubeGeometry(curve,Math.max(48,points.length*2),radius,8,closed),material,name);}
  const hood=group('Foundation.Hazmat.Hood');headFollow(hood);
  const hoodMat=mat('Foundation.Hazmat.HoodYellow','#dfbb35',.82),hoodEdge=mat('Foundation.Hazmat.Edge','#c7a62d',.83);hoodEdge.userData.wardrobeColorChannel='trim';
  const segments=80,rings=12,hoodPositions=[],hoodIndices=[],opening=[];
  for(let j=0;j<=rings;j++){
    const u=j/rings;
    for(let i=0;i<segments;i++){
      const a=i/segments*Math.PI*2,cos=Math.cos(a),sin=Math.sin(a),x0=cx+.105*cos,y0=cy+.132*sin,z0=faceDepth(x0,y0)+.012;
      if(j===0)opening.push(new THREE.Vector3(x0,y0,z0));
      const polar=Math.PI*(.25+.75*u),radius=Math.sin(polar);
      const rx=.1485*radius,ry=.1867*radius;
      const x=cx+rx*cos,y=cy+.023*u+ry*sin,z=-.010+.125*Math.cos(polar)+(z0-.078388)*(1-u)*(1-u);
      hoodPositions.push(x,y,z);
      if(j<rings){const p=j*segments+i,q=j*segments+(i+1)%segments;hoodIndices.push(p,q,p+segments,q,q+segments,p+segments);}
    }
  }
  const hoodGeometry=new THREE.BufferGeometry();hoodGeometry.setAttribute('position',new THREE.Float32BufferAttribute(hoodPositions,3));hoodGeometry.setIndex(hoodIndices);hoodGeometry.computeVertexNormals();
  primitive(hood,hoodGeometry,hoodMat,'Foundation.Hazmat.FittedHood');tube(hood,opening,.0034,hoodEdge,'Foundation.Hazmat.FaceBinding',true);
  const black=mat('Foundation.Mask.Rubber','#24272a',.72),filter=mat('Foundation.Mask.Filters','#b9bcb4',.6),blue=mat('Foundation.Mask.Glass','#b9d3d6',.18);
  blue.transparent=true;blue.opacity=.085;blue.depthWrite=false;
  const mask=group('Foundation.Hazmat.Mask');headFollow(mask);
  const seal=[];
  for(let i=0;i<80;i++){const a=i/80*Math.PI*2,x=cx+.094*Math.cos(a),y=cy+.112*Math.sin(a);seal.push(new THREE.Vector3(x,y,faceDepth(x,y)+.023));}
  tube(mask,seal,.0048,black,'Foundation.Mask.ContinuousSeal',true);
  const glassPositions=[cx,cy,headBounds.max.z+.032],glassIndices=[];
  for(let r=1;r<=8;r++)for(let i=0;i<80;i++){const t=r/8,p=seal[i];glassPositions.push(THREE.MathUtils.lerp(cx,p.x,t),THREE.MathUtils.lerp(cy,p.y,t),THREE.MathUtils.lerp(headBounds.max.z+.032,p.z,t*t));}
  for(let i=0;i<80;i++)glassIndices.push(0,1+i,1+(i+1)%80);
  for(let r=0;r<7;r++)for(let i=0;i<80;i++){const a=1+r*80+i,b=1+r*80+(i+1)%80;glassIndices.push(a,b,a+80,b,b+80,a+80);}
  const glass=new THREE.BufferGeometry();glass.setAttribute('position',new THREE.Float32BufferAttribute(glassPositions,3));glass.setIndex(glassIndices);glass.computeVertexNormals();primitive(mask,glass,blue,'Foundation.Mask.ClearVisor');
  const mouthZ=faceDepth(cx,cy-.065)+.020;
  const cupPositions=[],cupIndices=[],cupSegments=64;
  for(let r=0;r<4;r++)for(let i=0;i<cupSegments;i++){
    const a=i/cupSegments*Math.PI*2,rx=[.078,.078,.050,0][r],ry=[.050,.050,.033,0][r],x=cx+rx*Math.cos(a),y=cy-.062+ry*Math.sin(a);
    const z=r<2?faceDepth(x,y)+(r===0?.003:.023):mouthZ+(r===2?.022:.043);
    cupPositions.push(x,y,z);
    if(r<3){const p=r*cupSegments+i,q=r*cupSegments+(i+1)%cupSegments;cupIndices.push(p,q,p+cupSegments,q,q+cupSegments,p+cupSegments);}
  }
  const cup=new THREE.BufferGeometry();cup.setAttribute('position',new THREE.Float32BufferAttribute(cupPositions,3));cup.setIndex(cupIndices);cup.computeVertexNormals();primitive(mask,cup,black,'Foundation.Mask.FittedLowerCup');
  for(const side of [-1,1]){
    const x=cx+side*.054,y=cy-.072,z=mouthZ+.014;
    primitive(mask,new THREE.CylinderGeometry(.023,.025,.026,32),filter,`Foundation.Mask.Filter${side}`,[x,y,z],null,[Math.PI/2,0,0]);
    primitive(mask,new THREE.CylinderGeometry(.020,.020,.002,32),black,`Foundation.Mask.FilterGrid${side}`,[x,y,z+.014],null,[Math.PI/2,0,0]);
    for(let k=-3;k<=3;k++)primitive(mask,new THREE.BoxGeometry(.030,.0016,.002),filter,`Foundation.Mask.Vent${side}.${k}`,[x,y+k*.004,z+.016]);
    // Follow the actual head cross-section, including the narrower nape.
    // Hair can naturally cover this strap instead of holding it away from skin.
    const strap=[new THREE.Vector3(cx+side*.094,cy,faceDepth(cx+side*.094,cy)+.023)];
    for(let j=1;j<=28;j++){
      const t=j/28,a=THREE.MathUtils.lerp(.85,Math.PI,t),y=cy+.020*Math.sin(t*Math.PI/2);
      const radial=new THREE.Vector3(side*Math.sin(a),0,Math.cos(a)),center=new THREE.Vector3(cx,y,.005);
      ray.set(center.clone().addScaledVector(radial,.5),radial.clone().negate());
      const hit=ray.intersectObject(fitHead,false)[0];
      strap.push((hit?.point||center.addScaledVector(radial,.095)).clone().addScaledVector(radial,.007));
    }
    tube(mask,strap,.0038,black,`Foundation.Mask.SideStrap${side}`);
  }
  const sword=group('Foundation.Action.Katana'),gold=mat('Foundation.Katana.Brass','#bd9139',.35,.65),swordBlack=mat('Foundation.Katana.Black','#171a22',.58),wrap=mat('Foundation.Katana.Wrap','#c9aa63',.8);
  const swordLocal=new THREE.Group();swordLocal.name='Foundation.Action.KatanaAssembly';swordLocal.position.set(.10,1.17,-.18);swordLocal.rotation.z=-.48;sword.add(swordLocal);
  const torsoFit=[body,dress].map(source=>{const mesh=new THREE.Mesh(source.geometry,new THREE.MeshBasicMaterial({side:THREE.DoubleSide}));mesh.matrixAutoUpdate=false;mesh.matrixWorld.copy(source.matrixWorld);mesh.morphTargetInfluences=source.morphTargetInfluences;return mesh;});
  function backPoint(x,y){ray.set(new THREE.Vector3(x,y,-.6),new THREE.Vector3(0,0,1));const hits=torsoFit.flatMap(mesh=>ray.intersectObject(mesh,false));const z=hits.length?Math.min(...hits.map(hit=>hit.point.z)):-.060;return new THREE.Vector3(x,y,z-.012);}
  function leatherStrap(points,width,name){
    const curve=new THREE.CatmullRomCurve3(points,false,'centripetal'),positions=[],indices=[];
    for(let i=0;i<=64;i++){const t=i/64,p=curve.getPoint(t),tangent=curve.getTangent(t),across=new THREE.Vector3(tangent.y,-tangent.x,0).normalize().multiplyScalar(width/2);positions.push(...p.clone().sub(across).toArray(),...p.clone().add(across).toArray());if(i<64){const a=i*2;indices.push(a,a+1,a+2,a+1,a+3,a+2);}}
    const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));geometry.setIndex(indices);geometry.computeVertexNormals();primitive(sword,geometry,swordBlack,name);
    for(const direction of [-1,1]){const edge=[];for(let i=0;i<=24;i++){const t=i/24,p=curve.getPoint(t),tangent=curve.getTangent(t);p.add(new THREE.Vector3(tangent.y,-tangent.x,0).normalize().multiplyScalar(width*.46*direction));edge.push(p);}tube(sword,edge,.0015,wrap,name+'.Stitch'+direction);}
  }
  swordLocal.updateMatrix();
  const upperCuff=new THREE.Vector3(0,.10,0).applyMatrix4(swordLocal.matrix),lowerCuff=new THREE.Vector3(0,-.15,0).applyMatrix4(swordLocal.matrix);
  leatherStrap([backPoint(-.100,1.325),backPoint(-.065,1.285),backPoint(-.020,1.225),backPoint(.025,1.160),backPoint(.065,1.090),backPoint(.080,1.025)],.020,'Foundation.Katana.DiagonalHarness');
  leatherStrap([backPoint(-.065,1.285),backPoint(.015,1.300),new THREE.Vector3(.100,1.290,-.145),upperCuff],.016,'Foundation.Katana.UpperHolster');
  leatherStrap([backPoint(.065,1.090),new THREE.Vector3(.070,1.070,-.140),lowerCuff],.016,'Foundation.Katana.LowerHolster');
  for(const y of [.10,-.15])primitive(swordLocal,new THREE.TorusGeometry(.019,.003,8,32),swordBlack,'Foundation.Katana.ScabbardCuff'+y,[0,y,0],[1,.82,1],[Math.PI/2,0,0]);
  info.foundationAccessoryFit={revision:'surface-straps-1',mask:'ray-fitted neutral head',holster:'fitted diagonal leather harness'};
  primitive(swordLocal,new THREE.BoxGeometry(.026,.70,.025),swordBlack,'Foundation.Katana.Scabbard',[0,-.09,0]);
  primitive(swordLocal,new THREE.BoxGeometry(.026,.19,.023),swordBlack,'Foundation.Katana.Handle',[0,.355,0]);
  primitive(swordLocal,new THREE.BoxGeometry(.075,.012,.054),gold,'Foundation.Katana.Guard',[0,.25,0]);
  for(let i=0;i<8;i++)primitive(swordLocal,new THREE.TorusGeometry(.017,.0026,5,12),wrap,`Foundation.Katana.Wrap${i}`,[0,.28+i*.020,0],[1,.65,1],[Math.PI/2,0,.5]);
  function nearest(point){const p=dress.geometry.attributes.position,v=new THREE.Vector3();let index=0,best=Infinity;for(let i=0;i<p.count;i++){dress.getVertexPosition(i,v);v.applyMatrix4(dress.matrixWorld);const d=v.distanceToSquared(point);if(d<best){index=i;best=d;}}return index;}
  const a=nearest(new THREE.Vector3(-.10,1.23,-.04)),b=nearest(new THREE.Vector3(.10,1.23,-.04)),c=nearest(new THREE.Vector3(0,1.03,-.04));
  function torsoFrame(){const p=[a,b,c].map(i=>dress.getVertexPosition(i,new THREE.Vector3()).applyMatrix4(dress.matrixWorld)),x=p[1].clone().sub(p[0]).normalize(),mid=p[0].clone().add(p[1]).multiplyScalar(.5),y=mid.clone().sub(p[2]).normalize(),z=new THREE.Vector3().crossVectors(x,y).normalize();y.crossVectors(z,x).normalize();return new THREE.Matrix4().makeBasis(x,y,z).setPosition(mid);}
  const torsoInverse=torsoFrame().invert();anchors.push(()=>{sword.matrix.copy(torsoFrame()).multiply(torsoInverse);sword.matrixAutoUpdate=false;});
  info.slots.dress.push({id:'hazmat-yellow',name:'Yellow coverall',swatch:'#dfbb35',nodes:[suit.name,hood.name],coverage:{hiddenNodes:[body.userData.name||body.name],hideSlots:['hair','legwear']},note:'Full coverall. Pair with its mask.'},{id:'action-yellow',name:'Yellow action suit',swatch:'#edbd30',nodes:[actionRoot.name],coverage:{hideBodyBelow:.92,hideSlots:['legwear']},note:'Continuous tailored suit with black outer seam stripes.'});
  info.slots.accessories.push({id:'hazmat-mask',name:'Filter visor',swatch:'#647d83',nodes:[mask.name]},{id:'back-katana',name:'Back katana',swatch:'#282735',nodes:[sword.name]});
  for(const g of generated)register(g);
  function update(dt,look){avatar.updateMatrixWorld(true);for(const [mesh,source] of sync){mesh.matrix.copy(source.matrix);if(source.morphTargetInfluences)mesh.morphTargetInfluences=source.morphTargetInfluences;}for(const follow of anchors)follow();const lower=Math.max(.13,...shafts.filter(s=>s.root.visible).map(s=>s.top));for(const u of pantsCuts)u.value=lower;const coverage=info.slots.dress.find(d=>d.id===look?.dress)?.coverage;coveredRange.value.fromArray(coverage?.underlayerMaskRange||coverage?.bodyMaskRange||[100,100]);const hem=coverage?.bodyMaskHem;hasCoveredHem.value=hem?1:0;if(hem)coveredHem.value.set(hem.base,hem.x||0,hem.z||0);}
  update();
  return {update};
}
