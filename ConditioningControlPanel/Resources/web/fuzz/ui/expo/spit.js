import {livePanel,terminal} from './display.js';

const G=9.81,rangeOrigin=[-66.28,0,94],flagDistances=[2.9,3.4,6.1];
export function spitVelocity(charge,pitch,hooves){
  const speed=2.3+1.98*Math.max(0,Math.min(1,charge));
  const angle=Math.max(-1.55,Math.min(1.16,pitch+.05+.24*charge));
  return {along:speed*Math.cos(angle)*(hooves?2:1),up:speed*Math.sin(angle)};
}
export function createSpit(api,state){
  const {THREE:T,camera,walker,scene,cue}=api;
  const rooms=new Map(),ray=new T.Ray(),cast=new T.Raycaster(),forward=new T.Vector3(),p=new T.Vector3(),q=new T.Vector3();
  const velocity=new T.Vector3(),start=new T.Vector3(),previous=new T.Vector3(),up=new T.Vector3(0,0,1);
  let hold=-1,flight=null,clock=0,zone='';
  const texCanvas=document.createElement('canvas');texCanvas.width=texCanvas.height=96;
  const g=texCanvas.getContext('2d'),wash=g.createRadialGradient(46,43,4,48,48,40);
  wash.addColorStop(0,'rgba(164,192,164,.51)');wash.addColorStop(.78,'rgba(183,212,186,.58)');wash.addColorStop(1,'rgba(238,247,217,.84)');
  g.fillStyle=wash;g.beginPath();g.moveTo(14,55);g.bezierCurveTo(5,42,31,17,42,23);g.bezierCurveTo(67,-4,89,23,73,40);
  g.bezierCurveTo(95,66,70,86,52,76);g.bezierCurveTo(29,88,12,75,14,55);g.fill();
  g.fillStyle='#edf4d9';g.beginPath();g.ellipse(35,31,9,2.7,-.5,0,Math.PI*2);g.fill();
  const texture=new T.CanvasTexture(texCanvas);texture.colorSpace=T.SRGBColorSpace;
  const stainGeo=new T.PlaneGeometry(.21,.21),stainMat=new T.MeshBasicMaterial({map:texture,transparent:true,depthWrite:false,polygonOffset:true,polygonOffsetFactor:-2,side:T.DoubleSide});
  const stains=Array.from({length:12},()=>{const m=new T.Mesh(stainGeo,stainMat);m.visible=false;scene.add(m);return m;});
  let nextStain=0;
  const beadGeo=new T.SphereGeometry(.028,10,6),beadMat=new T.MeshBasicMaterial({color:0xc6e4bd});
  const bead=new T.Mesh(beadGeo,beadMat);bead.visible=false;scene.add(bead);
  const arcGeo=new T.BufferGeometry(),arcMat=new T.LineDashedMaterial({color:0xe3daa9,dashSize:.075,gapSize:.08,transparent:true,opacity:.76,depthWrite:false});
  const arc=new T.Line(arcGeo,arcMat),samples=new Float32Array(54),lengths=new Float32Array(18);
  arcGeo.setAttribute('position',new T.BufferAttribute(samples,3));arcGeo.setAttribute('lineDistance',new T.BufferAttribute(lengths,1));
  arc.visible=false;arc.frustumCulled=false;scene.add(arc);
  function launchData(charge){
    camera.getWorldDirection(forward);
    const speed=spitVelocity(charge,Math.asin(forward.y),state.hoofWorn);
    const horizontal=Math.hypot(forward.x,forward.z)||1;
    velocity.set(forward.x/horizontal*speed.along,speed.up,forward.z/horizontal*speed.along);
    start.copy(camera.position).addScaledVector(forward,.11);start.y-=.075;
  }
  function position(t,out){
    out.copy(start).addScaledVector(velocity,t);out.y-=G*t*t/2;return out;
  }
  function charge(){if(hold<0&&!flight){zone=walker.zone;hold=0;}}
  function cancel(){hold=-1;arc.visible=false;}
  function release(){
    if(hold<0)return;
    launchData(Math.min(1,hold/.9));cancel();
    flight={t:0,zone:walker.zone,hooves:!!state.hoofWorn,planter:walker.onPlanter,through:false};
    previous.copy(start);bead.visible=true;bead.position.copy(start);api.play('spit',.43);
  }
  function openShortcut(room){
    if(!room?.door||room.door.visible)return;
    state[room.id==='commons'?'rangeDoor':'catwalkDoor']=true;room.door.visible=true;
    api.play('door');cue('v4_spit_plate',{exhibit:'spit plate'});
  }
  function land(point,normal,target){
    const shot=flight,room=rooms.get(shot.zone),stain=stains[nextStain++%stains.length];
    flight=null;bead.visible=false;
    stain.visible=true;stain.userData.until=clock+16;stain.userData.zone=walker.zone;
    stain.position.copy(point).addScaledVector(normal,.005);stain.quaternion.setFromUnitVectors(up,normal);
    stain.rotateZ(nextStain*1.62);api.play('glass',.11);
    if(target?.type==='horn'){api.muffle?.(target.zone,target.position);cue('v4_spit_horn',{exhibit:'horn',position:target.position});}
    const far=target?.type==='plate';
    if(shot.zone==='commons'&&(Math.hypot(start.x-rangeOrigin[0],start.z-rangeOrigin[2])<2.5||far||shot.planter)){
      state.bestSpit=Math.max(state.bestSpit||0,Math.hypot(point.x-start.x,point.z-start.z));
      state.ticket.stamp('Spat');state.ticket.book('spit-range','Took part in the rematch');
      room?.score?.();
      if(shot.planter&&far){state.ticket.book('planter-spit','Entered from the planting',true);cue('v4_planter_spit');}
      else cue('v4_spit',{exhibit:'rematch'});
    }
    if(far&&(shot.hooves||shot.planter))openShortcut(room);
    if(shot.zone==='catwalk'&&shot.through&&target?.type==='dome'){
      state.domeSpat=true;state.ticket.book('oculus-spit','Marked the dome sign',true);
      cue('v4_dome_spit',{zone:'catwalk',exhibit:'oculus'});
    }
  }
  function fly(dt){
    const shot=flight;shot.t+=dt;position(shot.t,p);
    const distance=ray.direction.subVectors(p,previous).length();
    if(distance<.00001)return;
    ray.direction.multiplyScalar(1/distance);ray.origin.copy(previous);
    if(shot.zone==='catwalk'&&previous.y>=11.89&&p.y<11.89&&Math.hypot(p.x+22,p.z+5)<1.46)shot.through=true;
    const wall=walker.trace(ray,distance,label=>shot.zone==='catwalk'&&label==='Open grille');
    let closest=wall?.distance??distance,target=null,contact=wall;
    cast.ray.copy(ray);cast.near=0;cast.far=closest+.016;
    for(const room of rooms.values())for(const t of room.targets){
      if(!t.node.visible)continue;
      const hit=cast.intersectObject(t.node,true)[0];
      if(hit&&hit.distance<=closest+.012){
        closest=hit.distance;target=t;contact={point:hit.point,normal:hit.face.normal.clone().transformDirection(hit.object.matrixWorld)};
      }
    }
    // A descending drop can catch the narrow top edge of the framed sign.
    if(!contact&&shot.through&&previous.y>=1.83&&p.y<1.83&&Math.abs(p.x+22)<.18&&Math.abs(p.z+5)<.61){
      contact={point:p,normal:new T.Vector3(0,1,0)};target={type:'dome'};
    }
    if(contact){if(contact.normal.dot(ray.direction)>0)contact.normal.negate();land(contact.point,contact.normal,target);return;}
    bead.position.copy(p);bead.quaternion.setFromUnitVectors(up,ray.direction);bead.scale.set(.82,.82,1.72);
    if(shot.t>3.5||p.y< -3){flight=null;bead.visible=false;}
    else previous.copy(p);
  }
  function tick(dt){
    clock+=dt;
    if(hold>=0){if(zone!==walker.zone)cancel();else{
      hold=Math.min(.9,hold+dt);launchData(hold/.9);
      for(let i=0;i<18;i++){position(i*.06,q).toArray(samples,i*3);lengths[i]=i?lengths[i-1]+p.distanceTo(q):0;p.copy(q);}
      arcGeo.attributes.position.needsUpdate=true;arcGeo.attributes.lineDistance.needsUpdate=true;arc.visible=true;
    }}
    if(flight && flight.zone!==walker.zone){flight=null;bead.visible=false;}
    let left=Math.min(dt,.25);while(flight&&left>0){const step=Math.min(left,1/35);fly(step);left-=step;}
    for(const stain of stains)if(stain.visible&&(clock>stain.userData.until||walker.zone!==stain.userData.zone))stain.visible=false;
  }
  function mount(id,group){
    const room={id,targets:[]},undo=[];rooms.set(id,room);
    group.updateWorldMatrix(true,true);
    group.traverse(node=>{
      if(node.parent?.name.startsWith('expo_horn_'))return;
      const horn=node.name.startsWith('expo_horn_');
      if(horn||node.name==='dome_sign'||node.name.startsWith('expo_caption_')){
        room.targets.push({node,type:horn?'horn':node.name==='dome_sign'?'dome':'sign',zone:id,
          position:new T.Box3().setFromObject(node).getCenter(new T.Vector3())});
      }
    });
    if(id==='commons'||id==='catwalk')undo.push(api.annexProps(group,place=>{
      const cleanup=[],isRange=id==='commons';
      let panel;
      if(isRange){
        flagDistances.forEach(distance=>{
          const flag=place('annex_range_flag',[rangeOrigin[0]-.46,0,rangeOrigin[2]-distance]);
          const n=flag.getObjectByName('flag_face'),face=n?.isMesh?n:n?.getObjectByProperty('isMesh',true);
          if(face){const sign=livePanel(T,face,{width:256,height:108});cleanup.push(sign.dispose);
            sign.draw((g,w,h)=>{g.fillStyle='#f2e7c4';g.fillRect(0,0,w,h);g.fillStyle='#173f32';g.font='bold 62px Georgia';g.textAlign='center';g.fillText(distance.toFixed(1)+' m',w/2,78,w-16);});}
        });
        const judge=place('annex_judge',[-67.17,0,94.06]),n=judge.getObjectByName('judge_face'),face=n?.isMesh?n:n?.getObjectByProperty('isMesh',true);
        if(face){panel=livePanel(T,face,{width:1280,height:600});cleanup.push(panel.dispose);}
        room.score=()=>terminal(panel,{title:'The Rematch',eyebrow:'COMMONS  /  JUDGE',rows:['Hold Spit, aim, then release.'],
          foot:'Personal best   '+(state.bestSpit||0).toFixed(2)+' m'});room.score();
      }
      const plate=place('annex_spit_plate',isRange?[rangeOrigin[0],.01,rangeOrigin[2]-6.1]:[-22,11.96,-2.25]);
      room.targets.push({type:'plate',node:plate});
      room.door=place('annex_keeper_door',isRange?[-69.28,0,87.9]:[-23.19,11.96,-5.9],Math.PI/2);
      room.door.visible=!!state[isRange?'rangeDoor':'catwalkDoor'];
      cleanup.push(api.register(room.door,'Take the llama-side passage',()=>isRange?api.go('aquarium',[-55,0,45.5],-Math.PI/2):api.go('service',[16,0,30])));
      return ()=>cleanup.reverse().forEach(f=>f());
    }));
    return ()=>{rooms.delete(id);undo.reverse().forEach(f=>f());};
  }
  return {mount,tick,charge,release,cancel,dispose(){
    cancel();bead.removeFromParent();arc.removeFromParent();stains.forEach(n=>n.removeFromParent());
    for(const r of [texture,stainGeo,stainMat,beadGeo,beadMat,arcGeo,arcMat])r.dispose();
  }};
}
