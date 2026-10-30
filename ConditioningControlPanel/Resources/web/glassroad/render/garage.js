import * as T from 'three/webgpu';
import {garageLayout,customFraming,advanceGarageView} from './garageLayout.js';
import {clone} from 'three/addons/utils/SkeletonUtils.js';
import {texture,uv,vec2} from 'three/tsl';
import {createVehicleLife} from './vehicleLife.js';
import {createEmiLife} from './emiLife.js';
import {createGarageStyle} from './garageStyle.js';
import {racerFinish} from './racerFinish.js';
import {faceCell} from './faces.js';
import {uniform,float,smoothstep} from 'three/tsl';
import {dressKart,riderOnly,VEHICLE_MECHANISMS} from './wardrobe.js';
import {createEmiSkin} from './emiSkin.js';
import {garageArrows} from './garageArrows.js';
import {garageUi} from '../hud/garage.js';
import {createGarageNav} from '../hud/garageNav.js';
import {KARTS,OUTFITS,STORAGE,loadGarage,cleanGarage,stepKart,routeTo,raceLook,lockedKarts,ownedKarts,ownedLooks} from '../hud/garageState.js';
import {loadRecord} from '../meta/record.js';
import {unlockedSet} from '../meta/unlocks.js';
import {warmGarageWardrobe} from './garageWarm.js';
import {createGarageBackdrop} from './garageBackdrop.js';
import {cascadeIn} from './worldFeel.js';

/** One menu scene on the game's existing renderer, driven by the existing frame loop. */
// The ring: one STEP per kart; the radius grows with the count so neighbours keep the eight-kart spacing.
const STEP=2*Math.PI/KARTS.length,RING=2.35*KARTS.length/8;
/** `owned()` = the account's Prize Counter grants (meta/owned.js); only owned, earned or free karts are selectable,
 * and the shop outfits, paints, umbrella and trail are worn only when owned. `counter`: the host opens the Prize Counter. */
export async function createGarage({art,renderer,canvas,action,sound,track,laps,trackId='threshold',online=true,changeTrack,onProgress,owned:ownedGrants=()=>[],counter=false}) {
  const base=art?.model('emi-teacup-kart');
  if(!base||KARTS.some(([id])=>!art.model('emi-vehicle-'+id)))return null;
  let storage;try{storage=localStorage;}catch{}
  let layout=garageLayout(canvas.clientWidth,canvas.clientHeight);
  const darkNow=()=>lockedKarts(unlockedSet(loadRecord(storage)),ownedKarts(ownedGrants()));let locked=darkNow(),owns=ownedLooks(ownedGrants());
  const state=loadGarage(storage,locked,owns),scene=new T.Scene(),camera=new T.PerspectiveCamera(33,1,.03,40);
  scene.background=new T.Color('#ffe3ec');scene.fog=new T.Fog('#f8d2e1',18,35);
  const backdrop=createGarageBackdrop(scene);
  const style=createGarageStyle();
  scene.add(new T.HemisphereLight(0xf5fff0,0x27566b,.95));
  const key=new T.DirectionalLight(0xfff2d9,2.4);key.position.set(-3,6,-4);scene.add(key);
  const fill=new T.DirectionalLight(0xffc7e7,.55);fill.position.set(4,2,1);scene.add(fill);
  const env=new T.Scene();env.background=new T.Color('#777078');
  for(const [p,s,c]of[[[-3,3,-3],[3,3,1],0xffffff],[[3,2,1],[2,3,1],0xb5caff],[[0,4,0],[4,1,4],0xffece6]]){const m=new T.Mesh(new T.BoxGeometry(...s),new T.MeshBasicNodeMaterial({color:c}));m.position.set(...p);env.add(m);}
  const pmrem=new T.PMREMGenerator(renderer.three),environment=pmrem.fromScene(env,.04);scene.environment=environment.texture;pmrem.dispose();
  env.traverse(m=>{m.geometry?.dispose();m.material?.dispose();});
  const model=clone(base.scene),rider=new T.Group();rider.add(model);scene.add(rider);
  const atlas=art.tex('emi-faces'),faceUv=uniform(new T.Vector2(.25,0));
  const owned=[];
  model.traverse(m=>{if(!m.isMesh)return;m.frustumCulled=false;
    if(m.material.name==='emi_screen'&&atlas){const material=new T.MeshBasicNodeMaterial({name:'emi_screen'});material.colorNode=texture(atlas,uv().mul(.25).add(faceUv)).rgb;m.material=material;owned.push(material);}
  });
  const skin=createEmiSkin(model);riderOnly(model);style.apply(model);
  const mixer=new T.AnimationMixer(model),clip=base.animations.find(c=>c.name==='hop_squash'),hop=clip?mixer.clipAction(clip):null;
  if(hop){hop.setLoop(T.LoopOnce,1);hop.clampWhenFinished=true;}
  const emiLife=createEmiLife(model),lifeSignals={},vehicleLives=[];
  const contactGeo=new T.PlaneGeometry(2.2,2.2),contactMat=new T.MeshBasicNodeMaterial({color:0x100a0e,transparent:true,depthWrite:false});
  contactMat.opacityNode=float(.32).mul(float(1).sub(smoothstep(.05,.5,uv().sub(.5).length())));
  const dark=new T.MeshBasicNodeMaterial({color:0x211e24});owned.push(dark);
  const collection=KARTS.map(([id],index)=>{
    const holder=new T.Group(),asset=clone(art.model('emi-vehicle-'+id).scene);holder.add(asset);holder.userData.kartIndex=index;
    const meshes=[];asset.traverse(m=>{if(m.isSkinnedMesh){m.material=racerFinish(m.material);meshes.push(m);}});
    vehicleLives.push(createVehicleLife(meshes,{...VEHICLE_MECHANISMS?.vehicles?.[id],vehicleId:id},{seed:index,art}));
    const contact=new T.Mesh(contactGeo,contactMat);contact.rotation.x=-Math.PI/2;contact.position.y=-.014;holder.add(contact);
    holder.userData.asset=asset;asset.traverse(m=>{if(!m.isMesh)return;m.frustumCulled=false;if(locked.has(id)){m.userData.garageLit=m.material;m.material=dark;}});style.apply(asset,{locked:locked.has(id)});scene.add(holder);return holder;
  });
  let selected=KARTS.findIndex(k=>k[0]===state.kart),orbit=-selected*STEP,yaw=0,custom=false,visible=false,moving=null,queued=null,time=0,reduced=true,dress=null,view=0,viewBusy=false,customBounds=null,customFrame=null,wardrobeBounds=null;
  const arrows=garageArrows(scene),ray=new T.Raycaster(),pointer=new T.Vector2(),particles=[];
  const particleGeo=new T.OctahedronGeometry(.014),particleMaterial=new T.MeshBasicNodeMaterial({color:0xffd4ae});
  for(const arrow of arrows.arrows)style.apply(arrow.root);
  const ui=garageUi({change,select,customize,trackId,online,counter,deny:()=>sound('menu-deny'),changeTrack:id=>changeTrack?.(id),action:name=>{if(!moving&&!viewBusy){sound('menu-ok');action(name);}},hover:(dir,on)=>arrows.hover(dir,on)});ui.track(track,laps);
  function save(){try{storage?.setItem(STORAGE,JSON.stringify(state));}catch{}}
  function paint(){skin.set(state);ui.paint(state,{custom:custom||view>0,busy:!!moving||viewBusy,locked,owned:owns});save();}
  // A kart earned in the logbook or bought in the Prize Counter since the last look gets its own paint back; nothing re-darkens until the next page.
  function refreshLocks(){const next=darkNow();let lifted=false;owns=ownedLooks(ownedGrants());
    for(const [index,[id]]of KARTS.entries())if(locked.has(id)&&!next.has(id)){lifted=true;const asset=collection[index].userData.asset;
      asset.traverse(m=>{if(m.isMesh&&m.userData.garageLit){m.material=m.userData.garageLit;delete m.userData.garageLit;}});style.apply(asset);}
    if(lifted){locked=new Set([...locked].filter(id=>next.has(id)));renderer.three.compileAsync?.(scene,camera)?.catch?.(()=>{});}
    paint();}
  function change(values,colors=false){if(viewBusy)return;const previous=state.outfit+'|'+state.umbrella+'|'+state.trail;Object.assign(state,values);if(colors){state.antenna=state.secondary;state.shoes=state.secondary;state.hands='#'+new T.Color(state.primary).lerp(new T.Color('#ffffff'),.24).getHexString();state.face='#'+new T.Color(state.primary).lerp(new T.Color('#ffffff'),.65).getHexString();}
    if(previous!==state.outfit+'|'+state.umbrella+'|'+state.trail)redress();paint();sound('menu-toggle');}
  // the garage shows an owned umbrella open; the trail ribbons never count toward the framing bounds
  // the lace umbrella glides open when it is picked (update), and stays open through any other re-dress
  function redress(){const hadUmbrella=dress?.umbrella;dress?.dispose();const look=raceLook(state,owns);dress=dressKart(art,model,{outfit:look.outfit,vehicle:null,umbrella:look.umbrella,trail:look.trail});if(dress.umbrella&&dress.umbrella===hadUmbrella)dress.showUmbrella(true);style.apply(model);customBounds=null;if(custom)resize();}
  function riderBounds(){
    if(wardrobeBounds)return wardrobeBounds;if(customBounds)return customBounds;
    scene.updateMatrixWorld(true);const inverse=rider.matrixWorld.clone().invert(),box=new T.Box3(),point=new T.Vector3(),local=new T.Matrix4();
    model.traverse(m=>{if(!m.isMesh||m.userData.garageInk||m.userData.kartTrail||!m.visible)return;const positions=m.geometry?.getAttribute('position');if(!positions)return;
      local.multiplyMatrices(inverse,m.matrixWorld);const index=m.geometry.index,vertices=index?new Set(index.array):Array.from({length:positions.count},(_,i)=>i);
      for(const i of vertices){m.getVertexPosition(i,point);box.expandByPoint(point.applyMatrix4(local));}});
    customBounds={min:box.min.toArray(),max:box.max.toArray()};return customBounds;
  }
  function customRectangle(w,h){
    const panel=ui.root.querySelector('.g-controls').getBoundingClientRect(),heading=ui.root.querySelector('.g-custom-top p').getBoundingClientRect();
    const margin=Math.max(16,Math.min(w,h)*.03),top=Math.max(heading.bottom+14,h*.12);
    if(layout.mobile)return {left:margin,top,width:w-margin*2,height:Math.max(60,panel.top-top-14)};
    return {left:margin,top,width:Math.max(80,panel.left-margin*2),height:Math.max(80,h-top-margin)};
  }
  function customize(on){if(moving||on===custom)return;custom=on;gesture=null;viewBusy=true;paint();resize();ui.customFocus(on);if(on)cascadeIn(ui.root.querySelectorAll('.g-outfits > *, .g-patterns > *'),entrance());sound(on?'menu-ok':'menu-back');}
  function select(value,relative=false){if(custom||viewBusy)return;if(moving){queued={value,relative};return;}
    const route=relative?stepKart(selected,value,locked):routeTo(selected,value,locked);if(!route){if(!relative&&KARTS[value]&&locked.has(KARTS[value][0]))ui.lockNote(KARTS[value][0]);sound('menu-deny');return;}
    moving={elapsed:0,duration:reduced?.32:1.12,from:orbit,to:orbit-route.steps*STEP,index:route.index,yaw,landed:false};
    arrows.press(Math.sign(route.steps));sound('menu-move');if(hop){mixer.stopAllAction();hop.reset().play();}paint();}
  function sparkle(){if(reduced)return;for(let i=0;i<20;i++){const mesh=new T.Mesh(particleGeo,particleMaterial);mesh.position.set((Math.random()-.5)*.4,.25,0);scene.add(mesh);particles.push({mesh,v:new T.Vector3((Math.random()-.5)*.9,Math.random()*.7+.2,(Math.random()-.5)*.7),life:0});}}
  function resize(){const w=Math.max(1,canvas.clientWidth),h=Math.max(1,canvas.clientHeight);layout=garageLayout(w,h);ui.layout(layout);camera.aspect=w/h;
    if(custom||view>0)customFrame=customFraming(w,h,riderBounds(),customRectangle(w,h));
    frameView(reduced?(view<.5?0:1):ease(view));}
  const fromPosition=new T.Vector3(),toPosition=new T.Vector3(),fromTarget=new T.Vector3(),toTarget=new T.Vector3();
  function frameView(amount){const w=Math.max(1,canvas.clientWidth),h=Math.max(1,canvas.clientHeight),phone=layout.mobile;
    fromPosition.set(0,phone?1.65:1.9,phone?-5.6:-4.7);fromTarget.set(0,.5,0);
    toPosition.fromArray(customFrame?.position||fromPosition.toArray());toTarget.fromArray(customFrame?.target||fromTarget.toArray());
    camera.position.lerpVectors(fromPosition,toPosition,amount);camera.lookAt(fromTarget.lerp(toTarget,amount));
    camera.setViewOffset(w,h,T.MathUtils.lerp(layout.offsetX,customFrame?.offsetX||0,amount),T.MathUtils.lerp(layout.offsetY,customFrame?.offsetY||0,amount),w,h);
    rider.position.set(0,0,0);if(customFrame)rider.position.fromArray(customFrame.rider).multiplyScalar(amount);
  }
  const ease=x=>x*x*(3-2*x);
  // feel wave (lane C): list rows tween in (render/worldFeel.js cascadeIn); OS reduced motion or Gentle reads as 'less'
  let motion='full';const entrance=()=>motion==='off'?'off':reduced&&motion==='full'?'less':motion;
  function update(dt,motionReduced,gentle=false){dt=Math.max(0,Math.min(.05,Number.isFinite(dt)?dt:0));reduced=motionReduced||gentle;ui.root.classList.toggle('g-calm',reduced);time+=dt;emiLife.beforePose();dress?.glideUmbrella?.(true,dt,gentle);
    backdrop.update(dt,{reducedMotion:motionReduced,gentle});
    const screen=advanceGarageView(view,custom,dt,reduced),sceneProgress=screen.framing,wasBusy=viewBusy;view=screen.value;viewBusy=screen.busy;
    ui.root.style.setProperty('--g-custom-progress',String(screen.progress));ui.root.classList.toggle('g-scene-transition',viewBusy);
    canvas.style.opacity=screen.fade===1?'':String(screen.fade);frameView(screen.framing);if(wasBusy&&!viewBusy)paint();
    if(!reduced&&viewBusy)yaw=T.MathUtils.lerp(yaw,0,Math.min(1,dt*10));
    lifeSignals.time=time;lifeSignals.menu=true;lifeSignals.selected=true;lifeSignals.custom=custom;lifeSignals.motionOff=motionReduced;lifeSignals.gentle=gentle;lifeSignals.near=true;lifeSignals.outfit=state.outfit;lifeSignals.transition=0;lifeSignals.land=0;
    lifeSignals.clip=moving?'hop_squash':null;
    if(moving){const m=moving;m.elapsed+=dt;const u=Math.min(1,m.elapsed/m.duration);yaw=T.MathUtils.lerp(m.yaw,0,ease(Math.min(1,u/.2)));
      orbit=T.MathUtils.lerp(m.from,m.to,ease(reduced?u:T.MathUtils.clamp((u-.18)/.60,0,1)));
      if(!reduced){const lift=Math.sin(T.MathUtils.clamp((u-.08)/.8,0,1)*Math.PI);rider.position.y=lift*.32;camera.position.z=(layout.mobile?-5.6:-4.7)-lift*.5;camera.lookAt(0,.5+lift*.10,0);if(hop)mixer.setTime(u*clip.duration);}
      lifeSignals.transition=u;
      if(u>.9&&!m.landed){m.landed=true;lifeSignals.land=1;sparkle();}
      if(u===1){orbit=m.to;selected=m.index;state.kart=KARTS[selected][0];moving=null;mixer.stopAllAction();mixer.update(0);resize();paint();if(queued){const q=queued;queued=null;select(q.value,q.relative);}}
    }else if(!custom&&!viewBusy)rider.position.y=reduced?0:Math.sin(time*1.5)*.008;
    for(let i=0;i<collection.length;i++){const h=collection[i],a=i*STEP+orbit;h.position.set(Math.sin(a)*RING,-.005,RING-Math.cos(a)*RING);h.rotation.y=-a+(i===selected?yaw:0);h.position.y-=sceneProgress*.6;h.scale.setScalar((.72+Math.max(0,Math.cos(a))*.28)*(1-sceneProgress));h.visible=sceneProgress<.995;}
    emiLife.update(dt,lifeSignals);
    const face=faceCell(reduced?'happy':moving?'grin':custom?'happy':time%13>10.8&&time%13<11.6?'wink':'happy');faceUv.value.set(face.col*.25,face.row*.25);
    for(let i=0;i<vehicleLives.length;i++){lifeSignals.selected=i===selected;vehicleLives[i].update(dt,lifeSignals);}
    rider.rotation.y=yaw;arrows.update(dt,time,camera,{custom:sceneProgress>.995,reduced,busy:!!moving||viewBusy,layout});for(const a of arrows.arrows)a.root.scale.multiplyScalar(1-sceneProgress);
    for(let i=particles.length-1;i>=0;i--){const p=particles[i];p.life+=dt;p.v.y-=dt*.6;p.mesh.position.addScaledVector(p.v,dt);p.mesh.scale.setScalar(Math.max(0,1-p.life));if(p.life>=1){scene.remove(p.mesh);particles.splice(i,1);}}
  }
  function hit(e){const r=canvas.getBoundingClientRect();pointer.set((e.clientX-r.left)/r.width*2-1,-(e.clientY-r.top)/r.height*2+1);ray.setFromCamera(pointer,camera);scene.updateMatrixWorld(true);
    const targets=custom?[rider]:[...arrows.arrows.map(a=>a.root),rider,...collection];
    return ray.intersectObjects(targets,true).find(h=>{for(let p=h.object;p;p=p.parent)if(p.visible===false)return false;return true;})?.object;}
  function owner(o,key){for(let p=o;p;p=p.parent)if(p.userData[key]!==undefined)return p.userData[key];return null;}
  let gesture=null;
  const down=e=>{if(!visible||e.button!==0||moving||viewBusy)return;gesture={x:e.clientX,y:e.clientY,yaw,moved:false,target:hit(e)};canvas.setPointerCapture(e.pointerId);};
  const move=e=>{if(!visible||viewBusy)return;if(gesture){const dx=e.clientX-gesture.x;if(Math.hypot(dx,e.clientY-gesture.y)>6)gesture.moved=true;if(gesture.moved)yaw=gesture.yaw+dx*.012;}else{const o=hit(e),dir=owner(o,'garageDirection');for(const d of [-1,1])arrows.hover(d,dir===d);canvas.style.cursor=o?'pointer':'grab';}};
  const up=()=>{if(!gesture)return;const g=gesture;gesture=null;if(g.moved)return;const dir=owner(g.target,'garageDirection'),index=owner(g.target,'kartIndex');
    if(dir!==null)select(dir,true);else if(index!==null)select(index);else if(g.target&&!custom)customize(true);};
  const cancel=()=>{gesture=null;};
  for(const [event,fn]of [['pointerdown',down],['pointermove',move],['pointerup',up],['pointercancel',cancel],['lostpointercapture',cancel]])canvas.addEventListener(event,fn);
  // the track picker and the Record panel both take the pad and arrows while open
  const nav=createGarageNav({root:ui.root,select,customize,isCustom:()=>custom,isVisible:()=>visible,
    isBusy:()=>viewBusy,trackPicking:()=>ui.trackPicking||ui.recordOpen,trackNav:dir=>(ui.recordOpen?ui.recordNav(dir):ui.trackNav(dir))});
  const keydown=e=>{if(!visible||e.altKey||e.ctrlKey||e.metaKey)return;if(ui.trackPicking||ui.recordOpen){const dir={Escape:'back',Enter:'ok',ArrowLeft:'left',ArrowRight:'right',ArrowUp:'up',ArrowDown:'down'}[e.key];if(dir){e.preventDefault();e.stopImmediatePropagation();(ui.trackPicking?ui.trackNav:ui.recordNav)(dir);}return;}if(e.key==='Escape'){if(custom)customize(false);return;}if(['INPUT'].includes(e.target.tagName))return;
    const dir={ArrowLeft:'left',ArrowRight:'right',ArrowUp:'up',ArrowDown:'down'}[e.key];if(dir){e.preventDefault();e.stopImmediatePropagation();nav(dir);}};
  document.addEventListener('keydown',keydown,true);
  const observer=new ResizeObserver(resize);observer.observe(canvas);
  redress();paint();resize();update(0,true);
  // The full wardrobe warms before the loading card lifts; no shader compile on an outfit click.
  dress?.dispose();dress=null;
  wardrobeBounds=await warmGarageWardrobe({
    outfits:OUTFITS.filter(([id])=>id!=='none').map(([id])=>id),
    attach:id=>dressKart(art,model,{outfit:raceLook({...state,outfit:id}).outfit,vehicle:null}),
    measure(){style.apply(model);customBounds=null;return riderBounds();},
    compile:()=>renderer.three.compileAsync(scene,camera,null,event=>onProgress?.(event.loaded,event.total)),
    restore(){redress();paint();},
  });
  return {
    scene,camera,nav,backdrop,life:{emi:emiLife.metrics,vehicles:vehicleLives.map(l=>l.metrics)},get state(){return cleanGarage(state,new Set(),owns);},get yaw(){return yaw;},get custom(){return custom;},get busy(){return !!moving||viewBusy;},get transition(){return {progress:view,busy:viewBusy,framing:customFrame};},get selected(){return KARTS[selected][0];},select,customize,change,
    setMedia(getter){ui.setMedia(getter);},
    refreshMedia(){ui.refreshMedia();},
    setCourse(course){ui.setCourse(course);},
    track(name,laps){ui.track(name,laps);},
    setHandlers(next={}){
      if('action' in next)action=typeof next.action==='function'?next.action:()=>{};
      if('sound' in next)sound=typeof next.sound==='function'?next.sound:()=>{};
      if('changeTrack' in next)changeTrack=typeof next.changeTrack==='function'?next.changeTrack:()=>{};
    },
    setVisible(on){if(on!==visible){visible=on;gesture=null;if(on)refreshLocks();ui.setVisible(on);if(on){resize();cascadeIn(ui.root.querySelectorAll('.g-menu-row'),entrance());}}},
    refreshLocks,
    setMotion(m){motion=m||'full';},
    render(dt,reduced,{gentle=false}={}){update(dt,reduced,gentle);renderer.three.render(scene,camera);},
    dispose(){canvas.style.opacity='';observer.disconnect();document.removeEventListener('keydown',keydown,true);for(const [event,fn]of [['pointerdown',down],['pointermove',move],['pointerup',up],['pointercancel',cancel],['lostpointercapture',cancel]])canvas.removeEventListener(event,fn);contactGeo.dispose();contactMat.dispose();emiLife.dispose();for(const l of vehicleLives)l.dispose();dress?.dispose();style.dispose();skin.dispose();arrows.dispose();ui.dispose();mixer.stopAllAction();environment.dispose();backdrop.dispose();particleGeo.dispose();particleMaterial.dispose();for(const m of owned)m.dispose();},
  };
}
