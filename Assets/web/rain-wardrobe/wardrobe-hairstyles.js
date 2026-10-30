/* Authored hairstyle silhouettes. Geometry is fitted in the measured neutral
 * frame, then follows the animated head. Long pieces borrow source hair sway. */
export function expandWardrobe({THREE,avatar,nodes,materials,info}){
  avatar.updateMatrixWorld(true);
  const head=nodes.get('GEO-rain-head'),neutral=head.matrixWorld.clone(),inverse=neutral.clone().invert();
  const directionInverse=new THREE.Matrix3().setFromMatrix4(inverse),groups=[],tails=[];
  const p=new THREE.Vector3(),box=new THREE.Box3();
  for(let i=0;i<head.geometry.attributes.position.count;i++){head.getVertexPosition(i,p).applyMatrix4(neutral);box.expandByPoint(p);}
  const eyes=nodes.get('GEO-rain-eyes'),eyeBox=new THREE.Box3();
  for(let i=0;i<eyes.geometry.attributes.position.count;i++){eyes.getVertexPosition(i,p).applyMatrix4(eyes.matrixWorld);eyeBox.expandByPoint(p);}
  const cx=(eyeBox.min.x+eyeBox.max.x)/2,ey=(eyeBox.min.y+eyeBox.max.y)/2,cz=.026;
  const smooth=x=>{x=Math.max(0,Math.min(1,x));return x*x*(3-2*x);};
  const source=nodes.get('Companion.HairV9.Foundation');let sampleIndex=0,sampleDistance=Infinity;
  for(let i=0;i<source.geometry.attributes.position.count;i++){source.getVertexPosition(i,p).applyMatrix4(source.matrixWorld);const d=p.distanceToSquared(new THREE.Vector3(cx-.15,1.22,.05));if(d<sampleDistance){sampleDistance=d;sampleIndex=i;}}
  const sampleRest=new THREE.Vector3();source.getVertexPosition(sampleIndex,sampleRest).applyMatrix4(source.matrixWorld);
  function material(id,color){const m=new THREE.MeshStandardMaterial({name:'Hairstyle.'+id,color,roughness:.53,side:THREE.DoubleSide});m.userData.wardrobeColorChannel='hair';materials.set(m.name,[m]);return m;}
  function mesh(group,geometry,mat,name,pivot=null){
    const a=geometry.attributes.position;
    for(let i=0;i<a.count;i++){p.fromBufferAttribute(a,i).applyMatrix4(inverse);if(pivot)p.sub(pivot);a.setXYZ(i,p.x,p.y,p.z);}
    geometry.computeVertexNormals();geometry.computeBoundingSphere();const result=new THREE.Mesh(geometry,mat);result.name=group.name+'.'+name;result.frustumCulled=false;group.add(result);nodes.set(result.name,result);return result;
  }
  function shell(group,mat,{bottom=ey-.045,width=1,front=ey+.09,sweep=0,flare=0,tight=false,sideFall=false,shoulderClearance=0}={}){
    const verts=[],indices=[],rows=48,cols=112,top=box.max.y+(tight?.026:.042),mid=ey+.035;
    for(let row=0;row<=rows;row++)for(let col=0;col<=cols;col++){
      const angle=col/cols*Math.PI*2,opening=1-smooth((Math.acos(Math.cos(angle))-.42)/.76);
      const fall=sideFall?smooth((-Math.sin(angle)-.12)/.72):0;
      const ear=tight?Math.exp(-Math.pow((Math.acos(Math.cos(angle))-Math.PI/2)/.40,2))*(1-fall):0;
      const shoulderLift=shoulderClearance*Math.exp(-Math.pow((Math.acos(Math.cos(angle))-Math.PI/2)/.60,2));
      const hem=bottom+(front-bottom)*opening-sweep*opening*Math.sin(angle*2)+.105*ear+(flare?.012*Math.cos(angle*3):0)-.235*fall*(1-opening)+shoulderLift;
      const t=row/rows,y=top-(top-hem)*Math.pow(t,1.15),lower=smooth((mid-y)/Math.max(.08,mid-bottom));
      const nape=tight?(1-fall)*Math.sqrt(Math.max(.1,1-Math.pow(Math.max(0,mid-y)/.185,2)))+fall*(1-.05*lower):1;
      const radius=Math.sqrt(Math.max(0,1-Math.pow(Math.max(0,(y-mid)/(top-mid)),2)))*(1-.06*lower+flare*lower)*nape;
      const strand=.0014*Math.cos(angle*26+.5*Math.sin(t*Math.PI))*radius;
      const rx=(tight?.143:.151)*width,rz=tight?.135:.148;
      verts.push(cx+Math.sin(angle)*(rx*radius+strand),y,cz+Math.cos(angle)*(rz*radius+strand)-.055*flare*lower*(1-opening)+.12*fall*smooth((ey-.02-y)/.24));
    }
    for(let row=0;row<rows;row++)for(let col=0;col<cols;col++){const a=row*(cols+1)+col,b=a+cols+1;indices.push(a,b,a+1,a+1,b,b+1);}
    const inside=verts.length/3;
    for(let col=0;col<=cols;col++){const a=(rows*(cols+1)+col)*3;verts.push(cx+(verts[a]-cx)*.973,verts[a+1]+.005,cz+(verts[a+2]-cz)*.973);}
    for(let col=0;col<cols;col++){const a=rows*(cols+1)+col,b=inside+col;indices.push(a,b,a+1,a+1,b,b+1);}
    const g=new THREE.BufferGeometry();g.setAttribute('position',new THREE.Float32BufferAttribute(verts,3));g.setIndex(indices);return mesh(group,g,mat,'Crown');
  }
  function lock(group,mat,points,radius,name,{flat=1,sway=0,corrugation=.06}={}){
    const path=new THREE.CatmullRomCurve3(points.map(a=>new THREE.Vector3(...a))),steps=72,sides=20,frames=path.computeFrenetFrames(steps,false),verts=[],indices=[];
    let owner=group,pivot=null;
    if(sway){owner=new THREE.Group();owner.name=group.name+'.'+name+'Anchor';pivot=points[0].length?new THREE.Vector3(...points[0]).applyMatrix4(inverse):null;owner.position.copy(pivot);group.add(owner);nodes.set(owner.name,owner);tails.push({node:owner,strength:sway,root:group});}
    for(let i=0;i<=steps;i++){
      const t=i/steps,center=path.getPointAt(t),r=radius*(.16+.84*smooth(t/.14))*Math.pow(1-t,.65);
      for(let j=0;j<=sides;j++){const a=j/sides*Math.PI*2,g=1+corrugation*Math.cos(a*8+t*2);p.copy(center).addScaledVector(frames.normals[i],Math.cos(a)*r*g).addScaledVector(frames.binormals[i],Math.sin(a)*r*g*flat);verts.push(p.x,p.y,p.z);}
    }
    for(let i=0;i<steps;i++)for(let j=0;j<sides;j++){const a=i*(sides+1)+j,b=a+sides+1;indices.push(a,a+1,b,a+1,b+1,b);}
    const g=new THREE.BufferGeometry();g.setAttribute('position',new THREE.Float32BufferAttribute(verts,3));g.setIndex(indices);mesh(owner,g,mat,name,pivot);
  }
  function bun(group,mat,center){
    const g=new THREE.SphereGeometry(1,48,32);g.scale(.078,.060,.048);g.translate(...center);mesh(group,g,mat,'Bun core');
    for(let strand=0;strand<5;strand++){
      const pts=[];for(let i=0;i<40;i++){const t=i/39,a=t*Math.PI*2.5+strand*.32,r=.048*(1-.64*t);pts.push([center[0]+Math.cos(a)*r,center[1]+Math.sin(a)*r,center[2]-.050-.018*Math.sin(t*Math.PI)]);}
      lock(group,mat,pts,.011,'Bun twist '+strand,{flat:.72,corrugation:0});
    }
  }
  const definitions=[
    {id:'honey-roundbob',name:'Blunt bob',color:'#eec18a',kind:'blunt'},
    {id:'copper-lob',name:'Shoulder cut',color:'#cf8754',kind:'shoulder'},
    {id:'ink-sleek',name:'High ponytail',color:'#493f4b',kind:'pony'},
    {id:'rose-waves',name:'Twin tails',color:'#d78eaa',kind:'twins'},
    {id:'espresso-sidesweep',name:'Side-swept length',color:'#8a6a59',kind:'side'},
    {id:'platinum-long',name:'Low twisted bun',color:'#f4efe5',kind:'bun'},
    {id:'auburn-volume',name:'Full curls',color:'#b87956',kind:'curls'}
  ];
  for(const def of definitions){
    const item=info.slots.hair.find(i=>i.id===def.id);if(!item)continue;
    for(const name of item.nodes)if(nodes.has(name))nodes.get(name).visible=false;
    const group=new THREE.Group();group.name='Hairstyles.'+def.id;group.visible=false;avatar.add(group);nodes.set(group.name,group);groups.push(group);const mat=material(def.id,def.color);
    if(def.kind==='blunt')shell(group,mat,{bottom:ey-.12,front:ey+.042,width:1.03});
    else if(def.kind==='shoulder')shell(group,mat,{bottom:ey-.225,front:ey+.09,sweep:.028,width:1.0,flare:.07,shoulderClearance:.045});
    else{
      shell(group,mat,{bottom:ey-.080,front:ey+.095,sweep:.014,tight:true,sideFall:def.kind==='side'});
      if(def.kind==='pony')lock(group,mat,[[cx,ey+.12,-.060],[cx+.025,ey+.17,-.14],[cx+.085,ey+.075,-.20],[cx+.12,ey-.12,-.19],[cx+.085,ey-.28,-.16]],.052,'Ponytail',{flat:.8,sway:1});
      if(def.kind==='twins')for(const sign of [-1,1])lock(group,mat,[[cx+sign*.095,ey+.09,-.015],[cx+sign*.15,ey+.075,-.075],[cx+sign*.175,ey-.055,-.09],[cx+sign*.155,ey-.19,-.05],[cx+sign*.12,ey-.26,-.015]],.042,'Tail '+sign,{flat:.8,sway:sign});
      if(def.kind==='bun')bun(group,mat,[cx,ey-.005,-.109]);
      if(def.kind==='curls')for(let j=0;j<15;j++){
        const angle=.90+j*(Math.PI*2-1.8)/14,pts=[];
        for(let i=0;i<28;i++){const t=i/27,phase=t*Math.PI*(3.3+(j%3)*.25)+j*.72,rad=.060+.105*smooth(t/.32),curl=.013*Math.sin(t*Math.PI),length=.35+.025*Math.sin(j*2);pts.push([cx+Math.sin(angle)*rad+Math.cos(phase)*curl,ey+.16-length*t,cz+Math.cos(angle)*rad+Math.sin(phase)*curl+.035*t*Math.max(0,Math.cos(angle))]);}
        lock(group,mat,pts,.034,'Curl '+j,{flat:.78,sway:.14,corrugation:.02});
      }
    }
    const target=new THREE.Vector3(cx+.103,ey+.151,.116),profileAnchor=new THREE.Vector3(),normal=new THREE.Vector3();
    const crown=group.children.find(o=>o.isMesh),normalMatrix=new THREE.Matrix3().getNormalMatrix(neutral);let distance=Infinity;
    for(let i=0;i<crown.geometry.attributes.position.count;i++){
      p.fromBufferAttribute(crown.geometry.attributes.position,i).applyMatrix4(neutral);const d=p.distanceToSquared(target);
      if(d<distance){distance=d;profileAnchor.copy(p);normal.fromBufferAttribute(crown.geometry.attributes.normal,i).applyMatrix3(normalMatrix).normalize();}
    }
    const localGroup=new THREE.Matrix4().copy(avatar.matrixWorld).invert().multiply(neutral);localGroup.decompose(group.position,group.quaternion,group.scale);group.updateMatrixWorld(true);
    const outward=new THREE.Vector3(.55,.70,.60).normalize(),ray=new THREE.Raycaster(target.clone().addScaledVector(outward,.30),outward.clone().negate());
    const hits=ray.intersectObject(group,true);if(hits.length){profileAnchor.copy(hits[0].point);normal.copy(hits[0].face.normal).applyMatrix3(new THREE.Matrix3().getNormalMatrix(hits[0].object.matrixWorld)).normalize();if(normal.dot(outward)<0)normal.negate();}
    profileAnchor.addScaledVector(normal,.004);
    item.name=def.name;item.nodes=[group.name];item.note='Distinct fitted silhouette';
    item.fitProfile={revision:'hair-silhouettes-4',space:'neutral-world',ribbonAnchor:profileAnchor.toArray(),ribbonNormal:normal.toArray(),kind:def.kind};
  }
  const bob=info.slots.hair.find(i=>i.id==='chestnut-bob');if(bob){bob.name='Swept bob';bob.note='Soft diagonal fringe';}
  const inverseAvatar=new THREE.Matrix4(),currentHeadInverse=new THREE.Matrix4(),sample=new THREE.Vector3(),delta=new THREE.Vector3();
  const axisX=new THREE.Vector3(1,0,0).applyMatrix3(directionInverse).normalize(),axisZ=new THREE.Vector3(0,0,1).applyMatrix3(directionInverse).normalize(),qx=new THREE.Quaternion(),qz=new THREE.Quaternion();
  info.hairstyles={revision:'hair-silhouettes-4',styles:definitions.map(d=>({id:d.id,name:d.name,kind:d.kind})),sourcePreserved:true};
  return {update(){
    avatar.updateMatrixWorld(true);inverseAvatar.copy(avatar.matrixWorld).invert();
    for(const group of groups){group.matrix.copy(inverseAvatar).multiply(head.matrixWorld);group.matrix.decompose(group.position,group.quaternion,group.scale);}
    currentHeadInverse.copy(head.matrixWorld).invert();source.getVertexPosition(sampleIndex,sample).applyMatrix4(source.matrixWorld).applyMatrix4(currentHeadInverse).applyMatrix4(neutral);delta.copy(sample).sub(sampleRest);
    for(const tail of tails)if(tail.root.visible){qx.setFromAxisAngle(axisX,THREE.MathUtils.clamp(delta.z*2,-.07,.07)*Math.abs(tail.strength));qz.setFromAxisAngle(axisZ,THREE.MathUtils.clamp(delta.x*2,-.07,.07)*tail.strength);tail.node.quaternion.copy(qx).multiply(qz);}
  }};
}
