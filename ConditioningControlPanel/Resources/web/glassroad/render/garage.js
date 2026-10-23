import * as T from 'three/webgpu';
import {clone} from 'three/addons/utils/SkeletonUtils.js';
import {texture,uv,vec2} from 'three/tsl';
import {dressKart,riderOnly} from './wardrobe.js';
import {createEmiSkin} from './emiSkin.js';
import {garageArrows} from './garageArrows.js';
import {garageUi} from '../hud/garage.js';
import {KARTS,LOCKED,STORAGE,loadGarage,stepKart,routeTo,raceLook} from '../hud/garageState.js';

/** One menu scene on the game's existing renderer, driven by the existing frame loop. */
export async function createGarage({art,renderer,canvas,action,sound,track,laps}) {
  const base=art?.model('emi-teacup-kart');
  if(!base||KARTS.some(([id])=>!art.model('emi-vehicle-'+id)))return null;
  let storage;try{storage=localStorage;}catch{}
  const state=loadGarage(storage),scene=new T.Scene(),camera=new T.PerspectiveCamera(33,1,.03,40);
  scene.background=new T.Color('#302a28');scene.fog=new T.Fog('#302a28',7,15);
  scene.add(new T.HemisphereLight(0xf2e9ff,0x4b3640,1.5));
  const key=new T.DirectionalLight(0xfff0df,3);key.position.set(-3,6,-4);scene.add(key);
  const fill=new T.DirectionalLight(0xb7ccff,1.1);fill.position.set(4,2,1);scene.add(fill);
  const floor=new T.Mesh(new T.PlaneGeometry(70,70),new T.MeshStandardNodeMaterial({color:0x302a28,roughness:.87}));floor.rotation.x=-Math.PI/2;floor.position.y=-.03;scene.add(floor);
  const env=new T.Scene();env.background=new T.Color('#777078');
  for(const [p,s,c]of[[[-3,3,-3],[3,3,1],0xffffff],[[3,2,1],[2,3,1],0xb5caff],[[0,4,0],[4,1,4],0xffece6]]){const m=new T.Mesh(new T.BoxGeometry(...s),new T.MeshBasicNodeMaterial({color:c}));m.position.set(...p);env.add(m);}
  const pmrem=new T.PMREMGenerator(renderer.three),environment=pmrem.fromScene(env,.04);scene.environment=environment.texture;pmrem.dispose();
  env.traverse(m=>{m.geometry?.dispose();m.material?.dispose();});
  const model=clone(base.scene),rider=new T.Group();rider.add(model);scene.add(rider);
  const atlas=art.tex('emi-faces');
  const owned=[];
  model.traverse(m=>{if(!m.isMesh)return;m.frustumCulled=false;
    if(m.material.name==='emi_screen'&&atlas){const material=new T.MeshBasicNodeMaterial({name:'emi_screen'});material.colorNode=texture(atlas,uv().mul(.25).add(vec2(.25,0))).rgb;m.material=material;owned.push(material);}
  });
  const skin=createEmiSkin(model);riderOnly(model);
  const mixer=new T.AnimationMixer(model),clip=base.animations.find(c=>c.name==='hop_squash'),hop=clip?mixer.clipAction(clip):null;
  if(hop){hop.setLoop(T.LoopOnce,1);hop.clampWhenFinished=true;}
  const collection=KARTS.map(([id],index)=>{
    const holder=new T.Group(),asset=clone(art.model('emi-vehicle-'+id).scene);holder.add(asset);holder.userData.kartIndex=index;
    asset.traverse(m=>{if(!m.isMesh)return;m.frustumCulled=false;if(LOCKED.has(id)){m.material=new T.MeshBasicNodeMaterial({color:0x211e24});owned.push(m.material);}});scene.add(holder);return holder;
  });
  let selected=KARTS.findIndex(k=>k[0]===state.kart),orbit=-selected*Math.PI/4,yaw=0,custom=false,visible=false,moving=null,queued=null,time=0,reduced=true,dress=null;
  const arrows=garageArrows(scene),ray=new T.Raycaster(),pointer=new T.Vector2(),particles=[];
  const particleGeo=new T.OctahedronGeometry(.014),particleMaterial=new T.MeshBasicNodeMaterial({color:0xffd4ae});
  const ui=garageUi({change,select,customize,action:name=>{if(!moving){sound('menu-ok');action(name);}},hover:(dir,on)=>arrows.hover(dir,on)});ui.track(track,laps);
  function save(){try{storage?.setItem(STORAGE,JSON.stringify(state));}catch{}}
  function paint(){skin.set(state);ui.paint(state,{custom,busy:!!moving,locked:LOCKED});save();}
  function change(values,colors=false){const previous=state.outfit;Object.assign(state,values);if(colors){state.antenna=state.secondary;state.shoes=state.secondary;state.hands='#'+new T.Color(state.primary).lerp(new T.Color('#ffffff'),.24).getHexString();state.face='#'+new T.Color(state.primary).lerp(new T.Color('#ffffff'),.65).getHexString();}
    if(previous!==state.outfit)redress();paint();sound('menu-toggle');}
  function redress(){dress?.dispose();dress=dressKart(art,model,{outfit:raceLook(state).outfit,vehicle:null});}
  function customize(on){if(moving)return;custom=on;yaw=0;paint();resize();ui.customFocus(on);sound(on?'menu-ok':'menu-back');}
  function select(value,relative=false){if(custom)return;if(moving){queued={value,relative};return;}
    const route=relative?stepKart(selected,value):routeTo(selected,value);if(!route){sound('menu-deny');return;}
    moving={elapsed:0,duration:reduced?.32:1.12,from:orbit,to:orbit-route.steps*Math.PI/4,index:route.index,yaw,landed:false};
    arrows.press(Math.sign(route.steps));sound('menu-move');if(hop){mixer.stopAllAction();hop.reset().play();}paint();}
  function sparkle(){if(reduced)return;for(let i=0;i<20;i++){const mesh=new T.Mesh(particleGeo,particleMaterial);mesh.position.set((Math.random()-.5)*.4,.25,0);scene.add(mesh);particles.push({mesh,v:new T.Vector3((Math.random()-.5)*.9,Math.random()*.7+.2,(Math.random()-.5)*.7),life:0});}}
  function resize(){const w=canvas.clientWidth,h=canvas.clientHeight;camera.aspect=w/h;camera.updateProjectionMatrix();const phone=w<650;
    if(custom){camera.position.set(0,phone?1.2:1.3,phone?-4.4:-2.9);camera.lookAt(phone?new T.Vector3(0,.53,0):new T.Vector3(0,.65,0));camera.setViewOffset(w,h,0,phone?h*.18:0,w,h);rider.position.set(phone?0:.72,phone?-.12:-.30,0);}
    else{camera.clearViewOffset();camera.position.set(0,phone?1.65:1.9,phone?-4.5:-4.7);camera.lookAt(0,.5,0);rider.position.set(0,0,0);}}
  const ease=x=>x*x*(3-2*x);
  function update(dt,motionReduced){reduced=motionReduced;time+=dt;
    if(moving){const m=moving;m.elapsed+=dt;const u=Math.min(1,m.elapsed/m.duration);yaw=T.MathUtils.lerp(m.yaw,0,ease(Math.min(1,u/.2)));
      orbit=T.MathUtils.lerp(m.from,m.to,ease(reduced?u:T.MathUtils.clamp((u-.18)/.60,0,1)));
      if(!reduced){const lift=Math.sin(T.MathUtils.clamp((u-.08)/.8,0,1)*Math.PI);rider.position.y=lift*.32;camera.position.z=(canvas.clientWidth<650?-4.5:-4.7)-lift*.5;camera.lookAt(0,.5+lift*.10,0);if(hop)mixer.setTime(u*clip.duration);}
      if(u>.9&&!m.landed){m.landed=true;sparkle();}
      if(u===1){orbit=m.to;selected=m.index;state.kart=KARTS[selected][0];moving=null;mixer.stopAllAction();mixer.update(0);resize();paint();if(queued){const q=queued;queued=null;select(q.value,q.relative);}}
    }else if(!custom)rider.position.y=reduced?0:Math.sin(time*1.5)*.008;
    for(let i=0;i<collection.length;i++){const h=collection[i],a=i*Math.PI/4+orbit;h.position.set(Math.sin(a)*2.35,-.005,2.35-Math.cos(a)*2.35);h.rotation.y=-a+(i===selected?yaw:0);h.scale.setScalar(.72+Math.max(0,Math.cos(a))*.28);h.visible=!custom;}
    rider.rotation.y=yaw;arrows.update(dt,time,camera,{custom,reduced,busy:!!moving});
    for(let i=particles.length-1;i>=0;i--){const p=particles[i];p.life+=dt;p.v.y-=dt*.6;p.mesh.position.addScaledVector(p.v,dt);p.mesh.scale.setScalar(Math.max(0,1-p.life));if(p.life>=1){scene.remove(p.mesh);particles.splice(i,1);}}
  }
  function hit(e){const r=canvas.getBoundingClientRect();pointer.set((e.clientX-r.left)/r.width*2-1,-(e.clientY-r.top)/r.height*2+1);ray.setFromCamera(pointer,camera);scene.updateMatrixWorld(true);
    const targets=custom?[rider]:[...arrows.arrows.map(a=>a.root),rider,...collection];
    return ray.intersectObjects(targets,true).find(h=>{for(let p=h.object;p;p=p.parent)if(p.visible===false)return false;return true;})?.object;}
  function owner(o,key){for(let p=o;p;p=p.parent)if(p.userData[key]!==undefined)return p.userData[key];return null;}
  let gesture=null;
  const down=e=>{if(!visible||e.button!==0||moving)return;gesture={x:e.clientX,y:e.clientY,yaw,moved:false,target:hit(e)};canvas.setPointerCapture(e.pointerId);};
  const move=e=>{if(!visible)return;if(gesture){const dx=e.clientX-gesture.x;if(Math.hypot(dx,e.clientY-gesture.y)>6)gesture.moved=true;if(gesture.moved)yaw=gesture.yaw+dx*.012;}else{const o=hit(e),dir=owner(o,'garageDirection');for(const d of [-1,1])arrows.hover(d,dir===d);canvas.style.cursor=o?'pointer':'grab';}};
  const up=()=>{if(!gesture)return;const g=gesture;gesture=null;if(g.moved)return;const dir=owner(g.target,'garageDirection'),index=owner(g.target,'kartIndex');
    if(dir!==null)select(dir,true);else if(index!==null)select(index);else if(g.target&&!custom)customize(true);};
  const cancel=()=>{gesture=null;};
  for(const [event,fn]of [['pointerdown',down],['pointermove',move],['pointerup',up],['pointercancel',cancel],['lostpointercapture',cancel]])canvas.addEventListener(event,fn);
  function nav(dir){if(!visible||!['up','down','left','right','ok','back'].includes(dir))return;if((dir==='left'||dir==='right')&&!custom){select(dir==='left'?-1:1,true);return;}
    if(dir==='back'){if(custom)customize(false);return;}
    if(dir==='ok'){if(ui.root.contains(document.activeElement))document.activeElement.click?.();else ui.root.querySelector('[data-g=race]').click();return;}
    const buttons=[...ui.root.querySelectorAll('button,input')].filter(b=>!b.disabled&&b.getClientRects().length&&getComputedStyle(b).visibility!=='hidden');const i=buttons.indexOf(document.activeElement);buttons[(i+(['up','left'].includes(dir)?-1:1)+buttons.length)%buttons.length]?.focus();}
  const keydown=e=>{if(!visible||e.altKey||e.ctrlKey||e.metaKey)return;if(e.key==='Escape'){if(custom)customize(false);return;}if(['INPUT'].includes(e.target.tagName))return;
    const dir={ArrowLeft:'left',ArrowRight:'right',ArrowUp:'up',ArrowDown:'down'}[e.key];if(dir){e.preventDefault();e.stopImmediatePropagation();nav(dir);}};
  document.addEventListener('keydown',keydown,true);
  const observer=new ResizeObserver(resize);observer.observe(canvas);
  redress();paint();resize();update(0,true);
  // The full wardrobe warms before the loading card lifts; no shader compile on an outfit click.
  const initial=state.outfit;
  for(const id of ['none','varsity','croupier','drone-protocol','builder','cheer','pool','snow-day','swim']){state.outfit=id;redress();await renderer.three.compileAsync(scene,camera);}
  state.outfit=initial;redress();paint();
  return {
    scene,camera,nav,get state(){return {...state};},get yaw(){return yaw;},get custom(){return custom;},get busy(){return !!moving;},get selected(){return KARTS[selected][0];},select,customize,change,
    setVisible(on){if(on!==visible){visible=on;gesture=null;ui.setVisible(on);if(on)resize();}},
    render(dt,reduced){update(dt,reduced);renderer.three.render(scene,camera);},
    dispose(){observer.disconnect();document.removeEventListener('keydown',keydown,true);for(const [event,fn]of [['pointerdown',down],['pointermove',move],['pointerup',up],['pointercancel',cancel],['lostpointercapture',cancel]])canvas.removeEventListener(event,fn);dress?.dispose();skin.dispose();arrows.dispose();ui.dispose();mixer.stopAllAction();environment.dispose();floor.geometry.dispose();floor.material.dispose();particleGeo.dispose();particleMaterial.dispose();for(const m of owned)m.dispose();},
  };
}
