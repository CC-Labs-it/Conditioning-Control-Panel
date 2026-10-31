/* Fitted accessories use measured neutral face landmarks and the live head anchor.
 * Canonical source meshes remain untouched and hidden behind replacement items.
 */
import {createOpticsEnvironment} from './wardrobe-optics.js';
export function fitWardrobe({THREE,avatar,nodes,materials,info}){
  avatar.updateMatrixWorld(true);
  const head=nodes.get('GEO-rain-head');
  if(!head?.isMesh)throw Error('Accessory fitting requires the canonical head mesh.');
  const inverse=head.matrixWorld.clone().invert(),v=new THREE.Vector3();
  const fittings=[];
  const bodyFittings=[],bodySurfaces=[];
  const reflection=createOpticsEnvironment(THREE);
  const ribbonOffsets=new Map();
  const allMeshes=root=>{const meshes=[];root?.traverse(o=>{if(o.isMesh)meshes.push(o);});return meshes;};
  function points(root){const p=[];for(const mesh of allMeshes(root)){const a=mesh.geometry.attributes.position;for(let i=0;i<a.count;i++){mesh.getVertexPosition(i,v);p.push(v.clone().applyMatrix4(mesh.matrixWorld));}}return p;}
  const face=points(head),eyes=points(nodes.get('GEO-rain-eyes'));
  if(!eyes.length)throw Error('Glasses fitting requires measured eye geometry.');
  const eyeMidX=eyes.reduce((a,p)=>a+p.x,0)/eyes.length;
  function bounds(ps){const b=new THREE.Box3();for(const p of ps)b.expandByPoint(p);return b;}
  const eyeBounds=[bounds(eyes.filter(p=>p.x<eyeMidX)),bounds(eyes.filter(p=>p.x>=eyeMidX))];
  const centers=eyeBounds.map(b=>b.getCenter(new THREE.Vector3()));
  const eyeY=(centers[0].y+centers[1].y)/2;
  const centerX=(centers[0].x+centers[1].x)/2,spacing=centers[1].x-centers[0].x;
  const nose=face.filter(p=>Math.abs(p.x-centerX)<.014&&p.y>eyeY+.002&&p.y<eyeY+.026);
  const noseZ=Math.max(...nose.map(p=>p.z));
  const lensZ=Math.max(noseZ+.003,...eyeBounds.map(b=>b.max.z+.006));
  function mat(name,color,roughness=.35,metalness=0){const m=new THREE.MeshStandardMaterial({name:'Fit.'+name,color,roughness,metalness,side:THREE.DoubleSide});m.userData.wardrobeColorChannel='accessories';return m;}
  function attachGeometry(group,geometry,material,name){
    const attr=geometry.attributes.position;
    for(let i=0;i<attr.count;i++){v.fromBufferAttribute(attr,i).applyMatrix4(inverse);attr.setXYZ(i,v.x,v.y,v.z);}
    geometry.computeVertexNormals();geometry.computeBoundingSphere();
    const mesh=new THREE.Mesh(geometry,material);mesh.name=group.name+'.'+name;mesh.frustumCulled=false;group.add(mesh);return mesh;
  }
  function tube(group,ps,radius,material,name,closed=false){const curve=new THREE.CatmullRomCurve3(ps.map(p=>new THREE.Vector3(...p)),closed,'centripetal');return attachGeometry(group,new THREE.TubeGeometry(curve,Math.max(24,ps.length*3),radius,8,closed),material,name);}
  function sphere(group,p,scale,material,name){const geometry=new THREE.SphereGeometry(1,20,12);geometry.scale(...scale);geometry.translate(...p);return attachGeometry(group,geometry,material,name);}
  function newItem(id){const group=new THREE.Group();group.name='Fit.Item.'+id;group.visible=false;avatar.add(group);fittings.push(group);return group;}
  function finish(group,id,extra={}){
    let item=info.slots.accessories.find(i=>i.id===id);if(!item){item={id,name:extra.name||id,swatch:extra.swatch||'#29252d',nodes:[]};info.slots.accessories.push(item);}
    for(const name of item.nodes||[]){const old=nodes.get(name);if(old)old.visible=false;}
    item.nodes=[group.name];Object.assign(item,extra);
    group.traverse(o=>{nodes.set(o.name,o);for(const m of o.isMesh?(Array.isArray(o.material)?o.material:[o.material]):[]){const list=materials.get(m.name)||[];if(!list.includes(m))list.push(m);materials.set(m.name,list);}});
  }
  for(const [id,color,shape,shade,label] of [['glasses','#372d35','oval',false,'Fine frames'],['cat-eye-frames','#773850','cat',false,'Cat-eye frames'],['sunglasses','#28252d','cat',true,'Sunglasses'],['heart-frames','#ba5274','heart',false,'Heart frames'],['heart-shades','#9d2949','heart',true,'Heart shades']]){
    const group=newItem(id),frame=mat(id+'.Frame',color,.29,.30),metal=mat(id+'.Hinges','#c5a17a',.25,.65);
    const glass=new THREE.MeshPhysicalMaterial({name:'Fit.'+id+'.Lens',color:shade?'#302631':'#eef8ff',transparent:true,opacity:shade?.88:.22,roughness:.055,metalness:shade?.12:0,depthWrite:false,side:THREE.DoubleSide,clearcoat:1,clearcoatRoughness:.035,envMap:reflection,envMapIntensity:shade?2.8:3.5,ior:1.52});
    const rx=spacing*(shade?.455:.42),ry=rx*(shape==='heart'?.91:.72);
    for(let side=0;side<2;side++){
      const cx=centers[side].x,cy=centers[side].y+.001,sign=side?1:-1,ring=[];
      for(let i=0;i<64;i++){const a=Math.PI*2*i/64;let dx=rx*Math.cos(a),dy=ry*Math.sin(a);if(shape==='heart'){dx=rx*Math.sin(a)**3;dy=ry*(13*Math.cos(a)-5*Math.cos(2*a)-2*Math.cos(3*a)-Math.cos(4*a))/17;}if(shape==='cat')dy+=.006*Math.max(0,sign*Math.cos(a));const x=cx+dx;ring.push([x,cy+dy,lensZ-.006*(Math.abs(x-centerX)/(spacing*.9))**2]);}
      tube(group,ring,shade?.0021:.00165,frame,'Rim'+side,true);
      const lp=[cx,cy,lensZ+.0025],verts=[...lp],indices=[],lensRings=5;
      for(let r=1;r<=lensRings;r++){const t=r/lensRings;for(const p of ring)verts.push(cx+(p[0]-cx)*t,cy+(p[1]-cy)*t,p[2]-.0004+.003*(1-t*t));}
      for(let i=0;i<64;i++)indices.push(0,i+1,(i+1)%64+1);
      for(let r=1;r<lensRings;r++)for(let i=0;i<64;i++){const a=1+(r-1)*64+i,b=1+(r-1)*64+(i+1)%64,c=1+r*64+i,d=1+r*64+(i+1)%64;indices.push(a,c,d,a,d,b);}
      const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(verts,3));geometry.setIndex(indices);attachGeometry(group,geometry,glass,'Lens'+side);
      const x=cx+sign*rx,y=cy+.004,z=lensZ-.006*(Math.abs(x-centerX)/(spacing*.9))**2;
      // Hinges and arms wrap outside temples, then curl behind the ear.
      const templePoints=face.filter(p=>Math.abs(p.y-eyeY)<.035);
      const earX=(side?Math.max(...templePoints.map(p=>p.x)):Math.min(...templePoints.map(p=>p.x)))-sign*.006;
      tube(group,[[x,y,z],[x+sign*.004,y-.001,z-.030],[earX-sign*.010,y-.006,.032],[earX-sign*.018,y-.018,.018]],.0015,frame,'Temple'+side);
      sphere(group,[x,y,z],[.0028,.0024,.0025],metal,'Hinge'+side);
      sphere(group,[cx-sign*rx*.88,cy-.003,lensZ-.006],[.0022,.0035,.003],metal,'Pad'+side);
    }
    const innerLeft=centers[0].x+rx,innerRight=centers[1].x-rx;
    tube(group,[[innerLeft,eyeY+.006,lensZ],[centerX,eyeY+.014,lensZ+.001],[innerRight,eyeY+.006,lensZ]],.0015,metal,'Bridge');
    finish(group,id,{name:label,swatch:color,exclusiveGroup:'eyewear',note:shade?'Glossy tinted lenses':'Reflective clear lenses'});
  }
  const hoopItems=info.slots.accessories.find(i=>i.id==='hoops-original');
  const earAnchors=(hoopItems?.nodes||[]).map(n=>bounds(points(nodes.get(n)))).map(b=>new THREE.Vector3((b.min.x+b.max.x)/2,b.max.y,(b.min.z+b.max.z)/2));
  for(const [id,color,drop] of [['hoops-original','#d0a553',false],['silver-drops','#ced8e0',true]]){
    const group=newItem(id),metal=mat(id+'.Metal',color,.24,.78);
    for(const [i,ear]of earAnchors.entries()){
      const sign=ear.x>centerX?1:-1;
      // Small forward-facing hoops hang from a visible stud, outside the
      // cheek, with a shorter diameter that does not collide with long curls.
      const e=ear.clone();e.x+=sign*.005;e.z+=.010;
      sphere(group,e.toArray(),[.0033,.0033,.0032],metal,'Stud'+i);
      if(drop){tube(group,[e.toArray(),[e.x,e.y-.014,e.z+.001]],.0011,metal,'Chain'+i);sphere(group,[e.x,e.y-.023,e.z+.001],[.005,.009,.004],metal,'Drop'+i);}
      else{const ps=[];for(let k=0;k<64;k++){const a=k*Math.PI*2/64;ps.push([e.x+.013*Math.cos(a),e.y-.014+.014*Math.sin(a),e.z+.001]);}tube(group,ps,.0013,metal,'Hoop'+i,true);}
    }
    finish(group,id,{exclusiveGroup:'earrings',note:drop?'Small articulated drops':'Fine fitted hoops'});
  }
  const foundation=(info.slots.hair.find(i=>i.id==='source-original')?.nodes||['Companion.HairV9.Foundation']).flatMap(n=>points(nodes.get(n)));
  const hairTarget=new THREE.Vector3(centerX+.116,eyeY+.145,lensZ-.025);
  const nearest=foundation.filter(p=>p.x>centerX+.065&&p.y>eyeY+.09).sort((a,b)=>a.distanceToSquared(hairTarget)-b.distanceToSquared(hairTarget))[0];
  if(!nearest)throw Error('Ribbon fitting requires the hair crown.');
  const anchor=nearest.clone();
  const topLayer=foundation.filter(p=>Math.abs(p.x-anchor.x)<.006&&Math.abs(p.y-anchor.y)<.006);
  anchor.z=Math.max(anchor.z,...topLayer.map(p=>p.z))+.003;
  for(const [id,color]of [['hair-bow','#c05780'],['midnight-ribbon','#49425d']]){
    const group=newItem(id),fabric=mat(id+'.Fabric',color,.6);
    const tilt=-.22,cs=Math.cos(tilt),sn=Math.sin(tilt);
    const place=(x,y,z)=>[anchor.x+x*cs-y*sn,anchor.y+x*sn+y*cs,anchor.z+z-y*.65];
    for(const sign of [-1,1]){
      const verts=[],indices=[];
      for(let i=0;i<=20;i++){const t=i/20;for(let j=0;j<=8;j++){const w=j/4-1;verts.push(...place(sign*(.004+.037*t),w*(.004+.012*Math.sin(Math.PI*t)**.45),.003+.008*Math.sin(Math.PI*t)*(1-w*w)));}}
      for(let i=0;i<20;i++)for(let j=0;j<8;j++){const a=i*9+j;indices.push(a,a+1,a+10,a,a+10,a+9);}
      const geo=new THREE.BufferGeometry();geo.setAttribute('position',new THREE.Float32BufferAttribute(verts,3));geo.setIndex(indices);attachGeometry(group,geo,fabric,'Loop'+sign);
      const tailVerts=[...place(sign*.003,-.005,.001),...place(sign*.015,-.008,.001),...place(sign*.026,-.041,-.003),...place(sign*.012,-.035,-.001)];
      const tail=new THREE.BufferGeometry();tail.setAttribute('position',new THREE.Float32BufferAttribute(tailVerts,3));tail.setIndex([0,1,2,0,2,3]);attachGeometry(group,tail,fabric,'Tail'+sign);
    }
    sphere(group,place(0,0,.006),[.006,.010,.006],fabric,'Knot');
    finish(group,id,{exclusiveGroup:'hair-ribbon',note:'Ribbon seated on the hair'});
  }
  const body=nodes.get('GEO-rain-body');
  if(!body?.isMesh)throw Error('Neckwear fitting requires the canonical body mesh.');
  const bodyPoints=face,sourceChoker=bounds(points(nodes.get('WardrobePrototype.Accessory.Choker')));
  const neckTop=sourceChoker.getCenter(new THREE.Vector3()).y;
  const bodyPositions=[];
  for(let i=0;i<head.geometry.attributes.position.count;i++){head.getVertexPosition(i,v);const world=v.clone().applyMatrix4(head.matrixWorld);if(world.y>neckTop-.08&&world.y<neckTop+.025&&world.z<.09&&Math.abs(world.x-centerX)<.10)bodyPositions.push({id:i,position:v.clone()});}
  function bindBodyItem(group){
    // Rain's neck is part of the head mesh. Bind to its actual surface,
    // rather than the body mesh whose highest vertices are the clavicles.
    for(const mesh of allMeshes(group)){
      const p=mesh.geometry.attributes.position,bindings=[],sourceCache=new Map();
      for(let i=0;i<p.count;i++){
        v.fromBufferAttribute(p,i);
        const nearest=bodyPositions.map(a=>({a,d:a.position.distanceToSquared(v)})).sort((a,b)=>a.d-b.d).slice(0,3);
        const sum=nearest.reduce((s,a)=>s+1/(a.d+.000004),0),base=new THREE.Vector3();
        const anchors=nearest.map(({a,d})=>{if(!sourceCache.has(a.id))sourceCache.set(a.id,new THREE.Vector3());const weight=(1/(d+.000004))/sum;base.addScaledVector(a.position,weight);return{id:a.id,weight,point:sourceCache.get(a.id)};});
        bindings.push({anchors,offset:v.clone().sub(base)});
      }
      mesh.geometry.computeVertexNormals();bodySurfaces.push({mesh,bindings,sourceCache});
    }
  }
  for(const [id,color,gold]of [['velvet-choker','#28212c',false],['rose-gold-collar','#c9947d',true]]){
    const group=newItem(id),material=mat(id+'.Band',color,gold?.24:.72,gold?.72:0),trim=mat(id+'.Clasp',gold?'#eac0a2':'#cda36b',.2,.78);
    const y=neckTop-(gold?.034:.023),section=bodyPoints.filter(p=>Math.abs(p.y-y)<.004&&Math.abs(p.x-centerX)<.10&&p.z<.09),b=bounds(section);
    const cx=(b.min.x+b.max.x)/2,cz=(b.min.z+b.max.z)/2,rx=(b.max.x-b.min.x)/2+.002,rz=(b.max.z-b.min.z)/2+.0025,height=gold?.014:.020;
    const verts=[],indices=[],segments=96;
    for(let row=0;row<3;row++)for(let i=0;i<=segments;i++){const a=i/segments*Math.PI*2,front=Math.max(0,Math.cos(a));verts.push(cx+Math.sin(a)*rx,y+(row/2-.5)*height-.003*front,cz+Math.cos(a)*rz);}
    for(let row=0;row<2;row++)for(let i=0;i<segments;i++){const a=row*(segments+1)+i,b=a+segments+1;indices.push(a,b,a+1,a+1,b,b+1);}
    const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(verts,3));geometry.setIndex(indices);attachGeometry(group,geometry,material,'Band');
    for(const edge of [-1,1]){const ps=[];for(let i=0;i<96;i++){const a=i/96*Math.PI*2;ps.push([cx+Math.sin(a)*rx,y+edge*height/2-.003*Math.max(0,Math.cos(a)),cz+Math.cos(a)*rz]);}tube(group,ps,gold?.0014:.0008,trim,'Edge'+edge,true);}
    sphere(group,[cx,y-.004,cz+rz+.002],[gold?.008:.006,.007,.0025],trim,'FrontClasp');
    if(!gold){tube(group,[[cx,y-.008,cz+rz+.002],[cx,y-.020,cz+rz+.004]],.0011,trim,'PendantLink');sphere(group,[cx,y-.025,cz+rz+.004],[.005,.007,.0025],trim,'Pendant');}
    bindBodyItem(group);finish(group,id,{exclusiveGroup:'neckwear',note:gold?'Fitted polished collar':'Velvet band with gold pendant'});
  }
  // A short cut needs its own continuous silhouette. Compressing the long
  // curling sheets folds their topology into shelves and exposed interiors.
  const headBox=bounds(face),smooth=x=>{x=Math.max(0,Math.min(1,x));return x*x*(3-2*x);};
  for(const [id,color,width,hem]of [['chestnut-bob','#74432e',1,0],['honey-roundbob','#eec18a',1.08,.018]]){
    const hairItem=info.slots.hair.find(i=>i.id===id);if(!hairItem)continue;
    const group=new THREE.Group();group.name='Fit.Hair.'+id;group.visible=false;avatar.add(group);fittings.push(group);
    const material=mat(id+'.Hair',color,.54);material.userData.wardrobeColorChannel='hair';
    const verts=[],indices=[],rings=48,segments=128;
    const top=headBox.max.y+.042,mid=eyeY+.035,cx=centerX,cz=.026;
    for(let row=0;row<=rings;row++)for(let col=0;col<=segments;col++){
      const angle=col/segments*Math.PI*2,front=Math.cos(angle),angleFront=Math.acos(front);
      const opening=1-smooth((angleFront-.42)/.76);
      const lower=eyeY-.145+hem+(eyeY+.090-(eyeY-.145+hem))*opening-.030*opening*Math.sin(angle*2)-.008*opening;
      const t=row/rings,y=top-(top-lower)*Math.pow(t,1.15);
      const cap=Math.sqrt(Math.max(0,1-Math.pow(Math.max(0,(y-mid)/(top-mid)),2)));
      const lowerT=smooth((mid-y)/.19),roll=1-.095*lowerT;
      const channel=.0015*Math.cos(angle*22+.35*Math.sin(t*Math.PI));
      const radius=cap*roll;
      verts.push(cx+Math.sin(angle)*(.151*width*radius+channel),y,cz+Math.cos(angle)*(.148*radius+channel));
    }
    for(let row=0;row<rings;row++)for(let col=0;col<segments;col++){const a=row*(segments+1)+col,b=a+segments+1;indices.push(a,b,a+1,a+1,b,b+1);}
    // Turned-in hem gives the haircut a finished edge from below.
    const innerStart=verts.length/3;
    for(let col=0;col<=segments;col++){const base=(rings*(segments+1)+col)*3;verts.push(cx+(verts[base]-cx)*.965,verts[base+1]+.006,cz+(verts[base+2]-cz)*.965);}
    for(let col=0;col<segments;col++){const a=rings*(segments+1)+col,b=innerStart+col;indices.push(a,b,a+1,a+1,b,b+1);}
    const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(verts,3));geometry.setIndex(indices);
    let ribbonPoint=null,ribbonDistance=Infinity;
    for(let i=0;i<verts.length;i+=3){const distance=(verts[i]-anchor.x)**2+(verts[i+1]-anchor.y)**2+(verts[i+2]-anchor.z)**2;if(distance<ribbonDistance){ribbonDistance=distance;ribbonPoint=new THREE.Vector3(verts[i],verts[i+1],verts[i+2]);}}
    if(ribbonPoint){
      const outward=new THREE.Vector3((ribbonPoint.x-cx)/(.151*width)**2,Math.max(0,ribbonPoint.y-mid)/(top-mid)**2,(ribbonPoint.z-cz)/.148**2).normalize();
      ribbonPoint.addScaledVector(outward,.006);
      const directionInverse=new THREE.Matrix3().setFromMatrix4(inverse);
      const baseNormal=new THREE.Vector3(0,.65,1).normalize().applyMatrix3(directionInverse).normalize();
      const newNormal=outward.clone().applyMatrix3(directionInverse).normalize();
      const rotation=new THREE.Quaternion().setFromUnitVectors(baseNormal,newNormal);
      const oldPivot=anchor.clone().applyMatrix4(inverse),newPivot=ribbonPoint.applyMatrix4(inverse);
      ribbonOffsets.set(id,new THREE.Matrix4().makeTranslation(newPivot.x,newPivot.y,newPivot.z).multiply(new THREE.Matrix4().makeRotationFromQuaternion(rotation)).multiply(new THREE.Matrix4().makeTranslation(-oldPivot.x,-oldPivot.y,-oldPivot.z)));
    }
    attachGeometry(group,geometry,material,'Shell');
    for(const name of hairItem.nodes){const old=nodes.get(name);if(old)old.visible=false;}
    hairItem.nodes=[group.name];hairItem.note='Rounded cut with a clean jaw opening';
    group.traverse(o=>nodes.set(o.name,o));materials.set(material.name,[material]);
  }
  // Fitted scalp underlay prevents open layered strands from exposing skin.
  // It follows the original animated head surface, not a generic helmet sphere.
  const capGeometry=new THREE.BufferGeometry();capGeometry.index=head.geometry.index;
  for(const [name,a]of Object.entries(head.geometry.attributes))capGeometry.setAttribute(name,a);
  capGeometry.morphAttributes=head.geometry.morphAttributes;capGeometry.morphTargetsRelative=head.geometry.morphTargetsRelative;
  const references=new Float32Array(head.geometry.attributes.position.count*3);
  for(let i=0;i<head.geometry.attributes.position.count;i++){head.getVertexPosition(i,v).applyMatrix4(head.matrixWorld);references.set(v.toArray(),i*3);}
  capGeometry.setAttribute('hairCapReference',new THREE.BufferAttribute(references,3));
  for(const hairItem of info.slots.hair){
    if(['source-original','chestnut-bob','honey-roundbob'].includes(hairItem.id))continue;
    const sourceMeshes=hairItem.nodes.flatMap(name=>allMeshes(nodes.get(name)));
    const hairMesh=sourceMeshes.find(m=>(m.userData.wardrobeSource||m.name).includes('Foundation'))||sourceMeshes[0];
    const sourceMaterial=Array.isArray(hairMesh.material)?hairMesh.material[0]:hairMesh.material;
    const material=new THREE.MeshStandardMaterial({name:'Fit.Scalp.'+hairItem.id,color:sourceMaterial.color.clone(),roughness:.72,side:THREE.DoubleSide});material.userData.wardrobeColorChannel='hair';
    material.onBeforeCompile=shader=>{
      shader.vertexShader='attribute vec3 hairCapReference; varying vec3 fittedScalp;\n'+shader.vertexShader;
      shader.vertexShader=shader.vertexShader.replace('#include <begin_vertex>','#include <begin_vertex>\nfittedScalp=hairCapReference;');
      shader.vertexShader=shader.vertexShader.replace('#include <displacementmap_vertex>','#include <displacementmap_vertex>\ntransformed += normalize(objectNormal)*.006;');
      shader.fragmentShader='varying vec3 fittedScalp;\n'+shader.fragmentShader;
      shader.fragmentShader=shader.fragmentShader.replace('#include <clipping_planes_fragment>',`#include <clipping_planes_fragment>
        float frontCap=smoothstep(-.015,.105,fittedScalp.z);
        if(fittedScalp.y < (${eyeY.toFixed(6)}) + .055 + .080*frontCap) discard;`);
    };material.customProgramCacheKey=()=> 'fitted-scalp-r10';
    const group=new THREE.Group();group.name='Fit.ScalpGroup.'+hairItem.id;group.visible=false;avatar.add(group);fittings.push(group);
    const cap=new THREE.Mesh(capGeometry,material);cap.name=group.name+'.Surface';cap.frustumCulled=false;cap.morphTargetInfluences=head.morphTargetInfluences;cap.morphTargetDictionary=head.morphTargetDictionary;group.add(cap);
    if(!['chestnut-bob','honey-roundbob'].includes(hairItem.id)){
      // The source swept sheets leave a narrow diagonal opening. A small
      // fitted hair surface closes it without extending the whole hairline.
      const seamMaterial=new THREE.MeshStandardMaterial({name:'Fit.FringeSeam.'+hairItem.id,color:sourceMaterial.color.clone(),roughness:.58,side:THREE.DoubleSide});seamMaterial.userData.wardrobeColorChannel='hair';
      const seamPoints=[[.015,.099],[.043,.094],[.091,.030],[.061,.046],[.036,.067],[.016,.074]];
      const seamVertices=[];
      for(const [x,y]of seamPoints){const py=eyeY+y;seamVertices.push(centerX+x,py,.152);}
      const seamGeometry=new THREE.BufferGeometry();seamGeometry.setAttribute('position',new THREE.Float32BufferAttribute(seamVertices,3));seamGeometry.setIndex([0,1,5,1,4,5,1,2,4,2,3,4]);
      const seam=attachGeometry(group,seamGeometry,seamMaterial,'FringeSeam');nodes.set(seam.name,seam);materials.set(seamMaterial.name,[seamMaterial]);
    }
    hairItem.nodes.push(group.name);nodes.set(group.name,group);nodes.set(cap.name,cap);materials.set(material.name,[material]);
  }
  info.fit={version:3,revision:'polish3-optics-neckwear-r2',eyeSpacing:spacing,lensHalfWidth:spacing*.42,lensBridgeGap:spacing*.16,lensPlaneZ:lensZ,noseBridgeZ:noseZ,neckTop,neckSource:'head surface below original choker',eyewearCount:5,sourcePreserved:true};
  const invAvatar=new THREE.Matrix4();
  const ribbonProfileMatrices=new Map();
  function ribbonMatrix(look){const item=info.slots.hair.find(i=>i.id===look?.hair),profile=item?.fitProfile;if(!profile?.ribbonAnchor||!profile?.ribbonNormal)return ribbonOffsets.get(look?.hair);const key=JSON.stringify(profile);if(ribbonProfileMatrices.has(key))return ribbonProfileMatrices.get(key);const directionInverse=new THREE.Matrix3().setFromMatrix4(inverse),baseNormal=new THREE.Vector3(0,.65,1).normalize().applyMatrix3(directionInverse).normalize(),newNormal=new THREE.Vector3(...profile.ribbonNormal).normalize().applyMatrix3(directionInverse).normalize(),rotation=new THREE.Quaternion().setFromUnitVectors(baseNormal,newNormal),oldPivot=anchor.clone().applyMatrix4(inverse),newPivot=new THREE.Vector3(...profile.ribbonAnchor).applyMatrix4(inverse);const matrix=new THREE.Matrix4().makeTranslation(newPivot.x,newPivot.y,newPivot.z).multiply(new THREE.Matrix4().makeRotationFromQuaternion(rotation)).multiply(new THREE.Matrix4().makeTranslation(-oldPivot.x,-oldPivot.y,-oldPivot.z));ribbonProfileMatrices.set(key,matrix);return matrix;}
  return {update(dt,look){avatar.updateMatrixWorld(true);invAvatar.copy(avatar.matrixWorld).invert();for(const group of fittings){group.matrix.copy(invAvatar).multiply(head.matrixWorld);const offset=ribbonMatrix(look);if(offset&&/Fit.Item.(hair-bow|midnight-ribbon)$/.test(group.name))group.matrix.multiply(offset);group.matrix.decompose(group.position,group.quaternion,group.scale);}for(const group of bodyFittings){group.matrix.copy(invAvatar).multiply(body.matrixWorld);group.matrix.decompose(group.position,group.quaternion,group.scale);}for(const {mesh,bindings,sourceCache}of bodySurfaces){if(!mesh.parent.visible)continue;for(const[id,point]of sourceCache)head.getVertexPosition(id,point);const p=mesh.geometry.attributes.position;bindings.forEach(({anchors,offset},i)=>{v.copy(offset);for(const a of anchors)v.addScaledVector(a.point,a.weight);p.setXYZ(i,v.x,v.y,v.z);});p.needsUpdate=true;mesh.geometry.computeVertexNormals();}}};
}
