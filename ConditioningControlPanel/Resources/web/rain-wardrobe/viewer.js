import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { expandWardrobe as expandFoundation } from './wardrobe-foundation.js';
import { expandWardrobe as expandItems } from './wardrobe-expansion.js';
import { expandWardrobe as expandTailoring } from './wardrobe-tailoring.js';
import { createWardrobeColors } from './wardrobe-colors.js';
import { expandWardrobe as expandJewelry } from './wardrobe-jewelry.js';
import { installReviewStudio } from './wardrobe-review.js';
import { hosted, BIG_ASSETS, reviewAllowed, qaAllowed, hostItems, hostColors, postLook, postHost, isLocked, toHostLook } from './host-bridge.js';
import { installBaker } from './bake.js';
import { createShop, listenToHost, paintShop, STR as SHOP } from './wardrobe-shop.js';
const shop=createShop({isLocked,post:postHost,nameOf:key=>{const at=key.indexOf(':');const item=findItem(key.slice(0,at),key.slice(at+1));return item?.name||item?.label||'';}});
const COLOR_CHANNELS=['fabric','trim','hair','skin','eyes','lips','nails','shoes','accessories'];

const $ = id => document.getElementById(id);
const canvas = $('preview');
const stage = canvas.parentElement;
const STORE = 'rain-wardrobe-v1';
const COLOR_STORE='rain-wardrobe-colors-v1';
const reviewMode=reviewAllowed;
let colorController,colorOverrides={};
if(hosted)colorOverrides=hostColors(['fabric','trim','hair','skin','eyes','lips','nails','shoes','accessories']);else try{const saved=JSON.parse(localStorage.getItem(COLOR_STORE)||'{}');if(saved&&typeof saved==='object'&&!Array.isArray(saved))for(const id of ['fabric','trim','hair','skin','eyes','lips','nails','shoes','accessories'])if(typeof saved[id]==='string'&&/^#[0-9a-f]{6}$/i.test(saved[id]))colorOverrides[id]=saved[id].toLowerCase();}catch{}
if(reviewMode)colorOverrides={};
const errors = [];
const qaMode = qaAllowed;
let qaPanel,qaLast=0,frameCount=0,perfWindow=performance.now(),frameCpuTotal=0;
const perf={fps:0,frameCpuMs:0,frameIntervalMs:0};
if(qaMode){qaPanel=document.createElement('pre');qaPanel.id='wardrobe-qa';qaPanel.setAttribute('aria-label','Wardrobe live diagnostics');document.body.append(qaPanel);}
const motionPreference=matchMedia('(prefers-reduced-motion: reduce)');
const state = { ready:false, slot:'dress', look:{}, animation:'idle', paused:motionPreference.matches, tube:false, swaps:0, demo:false,transition:{active:false,progress:1,sequence:0} };
const singleSlots=['dress','hair','skin','shoes','legwear','arms','ankles'];
const meshSlots=['dress','hair','accessories','shoes','legwear','arms','ankles'];
let expansionUpdates=[],coveredNodes=new Set(),transitionStart=0,transitionTimer,transitionOrbit=0;
const garmentUniforms={mode:{value:1},color:{value:new THREE.Color(.075,.012,.038)},opacity:{value:.78},density:{value:5},lower:{value:.129},upper:{value:.713},hideBelow:{value:-1},bodyMaskRange:{value:new THREE.Vector2(0,-1)},bodyMaskHem:{value:new THREE.Vector4(0,0,0,-1)}};
let demoTimers=[],demoPrevious=null;
const titles = {dress:['The dress.','Pick a silhouette.'],hair:['A new mood.','Keep it loose. Change it up.'],skin:['Your palette.','A little warmth. A different glow.'],accessories:['Little details.','Wear one. Wear a few.'],shoes:['From the ground up.','A different finishing touch.'],legwear:['The next layer.','Sheer, patterned or tailored.'],looks:['The whole look.','One touch. Everything together.']};
titles.arms=['On your arms.','Bracelets, cuffs and bands.'];titles.ankles=['A little glint.','Anklets, ribbons and cuffs.'];
const knownAnimations = [{id:'idle',name:'Idle'},{id:'greeting',name:'Hello'},{id:'listening',name:'Listen'},{id:'giggle',name:'Giggle'},{id:'smirk',name:'Smirk'},{id:'jump',name:'Cheer'}];
let info, avatar, mixer, action, clips=[], materials=new Map(), originals=new Map(), nodes=new Map();
let width=1,height=1,last=performance.now(),elapsed=0;
let yaw=-.349, pitch=.035, distance=4.3, dragging=false, pointerX=0,pointerY=0;
let cameraGoal=null;
const target=new THREE.Vector3(0,.9,0);
const controlSpace=74;
const scene=new THREE.Scene();
const renderer=new THREE.WebGLRenderer({canvas,antialias:true,alpha:true,powerPreference:'high-performance'});
renderer.debug.checkShaderErrors=true;
renderer.debug.onShaderError=(gl,program,vertexShader,fragmentShader)=>{
  const logs=[['program',gl.getProgramInfoLog(program)],['vertex',gl.getShaderInfoLog(vertexShader)],['fragment',gl.getShaderInfoLog(fragmentShader)]].filter(([,log])=>log?.trim()).map(([kind,log])=>`${kind}: ${log.trim()}`);
  const message=`Shader compilation failed. ${logs.join('\n')||'WebGL returned no shader diagnostic.'}`;
  if(!errors.includes(message)){errors.push(message);console.error(message.slice(0,400));}
  refreshQa();
};
renderer.setPixelRatio(Math.min(devicePixelRatio,2));
renderer.outputColorSpace=THREE.SRGBColorSpace;
renderer.toneMapping=THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure=1.05;
renderer.setClearColor(0xede9e2,0);
const camera=new THREE.PerspectiveCamera(32,1,.01,100);
const tubeCamera=new THREE.PerspectiveCamera(32,1,.01,100);
scene.add(new THREE.HemisphereLight(0xfff6ec,0xc4b9b5,2.1));
const key=new THREE.DirectionalLight(0xfff3e5,3.0);key.position.set(-3,5,4);scene.add(key);
const fill=new THREE.DirectionalLight(0xdce7ff,.8);fill.position.set(3,2,3);scene.add(fill);
const rim=new THREE.DirectionalLight(0xffe5dc,1.6);rim.position.set(1,3,-3);scene.add(rim);
const ground=new THREE.Mesh(new THREE.CircleGeometry(.7,64),new THREE.MeshBasicMaterial({color:0xb8aaa0,transparent:true,opacity:.13,depthWrite:false}));
ground.rotation.x=-Math.PI/2;ground.position.y=.002;scene.add(ground);

function fail(error){const message=error?.message || String(error);errors.push(message);postHost({type:'boot-error',msg:message});state.ready=false;console.error(error);$('loading').hidden=false;$('loading').classList.add('error');$('loadMessage').textContent=`The live model could not load. ${message}`;$('liveLabel').textContent='PREVIEW UNAVAILABLE';}
function resize(){width=stage.clientWidth;height=stage.clientHeight;renderer.setSize(width,height,false);camera.aspect=width/Math.max(1,height-controlSpace);camera.updateProjectionMatrix();}
new ResizeObserver(resize).observe(stage);resize();

function updateCamera(dt){
  if(cameraGoal){const a=1-Math.exp(-dt*9);yaw+=((cameraGoal.yaw-yaw+Math.PI*3)%(Math.PI*2)-Math.PI)*a;pitch+=(cameraGoal.pitch-pitch)*a;distance+=(cameraGoal.distance-distance)*a;if(Math.abs(cameraGoal.yaw-yaw)<.001&&Math.abs(cameraGoal.distance-distance)<.001)cameraGoal=null;}
  camera.position.set(target.x+Math.sin(yaw+transitionOrbit)*Math.cos(pitch)*distance,target.y+Math.sin(pitch)*distance,target.z+Math.cos(yaw+transitionOrbit)*Math.cos(pitch)*distance);camera.lookAt(target);
}
function render(now){
  requestAnimationFrame(render);const interval=now-last;const dt=Math.min(interval/1000,.05);last=now;
  if(document.hidden||reviewMode)return;
  const frameStart=performance.now();
  updateTransition(now);updateCamera(dt);if(mixer&&!state.paused){mixer.update(dt);elapsed+=dt;}
  for(const update of expansionUpdates)update(state.paused?0:dt,state.look);
  drawPreview();
  frameCount++;frameCpuTotal+=performance.now()-frameStart;
  if(now-perfWindow>=1000){perf.fps=Math.round(frameCount*1000/(now-perfWindow));perf.frameCpuMs=Number((frameCpuTotal/frameCount).toFixed(2));perf.frameIntervalMs=Number(interval.toFixed(2));frameCount=0;frameCpuTotal=0;perfWindow=now;}
  if(qaPanel&&now-qaLast>=500){qaPanel.textContent=JSON.stringify(getDebugState(),null,2);qaLast=now;}
}
function drawPreview(){
  renderer.setScissorTest(false);renderer.setViewport(0,0,width,height);renderer.setClearColor(0xede9e2,0);renderer.clear();renderer.setViewport(0,controlSpace,width,height-controlSpace);renderer.render(scene,camera);
  if(state.tube&&avatar){const tw=118,th=190,x=25,y=132;renderer.setScissorTest(true);renderer.setScissor(x,y,tw,th);renderer.setViewport(x,y,tw,th);renderer.setClearColor(0xf8f5ef,1);renderer.clear();tubeCamera.aspect=tw/th;tubeCamera.position.set(target.x,1.04,3.65);tubeCamera.lookAt(0,1.04,0);tubeCamera.updateProjectionMatrix();renderer.render(scene,tubeCamera);renderer.setScissorTest(false);}
}
requestAnimationFrame(render);
if(qaMode){
  const toolbar=document.createElement('label');toolbar.id='qa-scrub';toolbar.textContent='Pose time ';const input=document.createElement('input');input.type='number';input.min='0';input.max='6';input.step='.05';input.value='0';input.setAttribute('aria-label','QA pose time');
  input.addEventListener('input',()=>{if(!mixer||!state.paused)return;const time=THREE.MathUtils.clamp(Number(input.value)||0,0,6);mixer.setTime(time);for(const update of expansionUpdates)update(0,state.look);avatar.updateMatrixWorld(true);state.sampleTime=time;drawPreview();refreshQa();});toolbar.append(input,document.createTextNode(' s · pause first'));document.body.append(toolbar);
}

canvas.addEventListener('pointerdown',e=>{if(e.button!==0)return;dragging=true;pointerX=e.clientX;pointerY=e.clientY;cameraGoal=null;canvas.setPointerCapture(e.pointerId);});
canvas.addEventListener('pointermove',e=>{if(!dragging)return;yaw-=(e.clientX-pointerX)*.007;pitch=THREE.MathUtils.clamp(pitch+(e.clientY-pointerY)*.004,-.2,.5);pointerX=e.clientX;pointerY=e.clientY;document.querySelectorAll('[data-view]').forEach(b=>b.classList.remove('active'));});
canvas.addEventListener('pointerup',()=>dragging=false);canvas.addEventListener('pointercancel',()=>dragging=false);
canvas.addEventListener('wheel',e=>{e.preventDefault();cameraGoal=null;distance=THREE.MathUtils.clamp(distance*Math.exp(e.deltaY*.001),1.8,7);},{passive:false});
canvas.addEventListener('keydown',e=>{if(e.key==='ArrowLeft')yaw-=.15;else if(e.key==='ArrowRight')yaw+=.15;else if(e.key==='+'||e.key==='=')distance=Math.max(1.8,distance-.2);else if(e.key==='-')distance=Math.min(7,distance+.2);else return;cameraGoal=null;e.preventDefault();});
document.querySelectorAll('[data-view]').forEach(button=>button.addEventListener('click',()=>{cameraGoal={yaw:{front:0,quarter:-.349,side:Math.PI/2,back:Math.PI}[button.dataset.view],pitch:.035,distance};document.querySelectorAll('[data-view]').forEach(b=>b.classList.toggle('active',b===button));}));
function frameView(x,y,z,zoom){target.set(x,y,z);distance=zoom;if(cameraGoal)cameraGoal.distance=zoom;updateCamera(0);drawPreview();}
$('headView').addEventListener('click',()=>frameView(0,1.48,0,1));
$('handsView').addEventListener('click',()=>frameView(.25,.88,.08,.95));
$('feetView').addEventListener('click',()=>frameView(0,.22,0,1.05));
$('outfitView').addEventListener('click',()=>frameView(0,.98,0,2.5));
$('fullView').addEventListener('click',()=>frameView(0,.9,0,4.3));
$('tube').addEventListener('click',()=>{state.tube=!state.tube;$('tube').setAttribute('aria-pressed',String(state.tube));$('tubeLabel').hidden=!state.tube;});
function paintPause(){$('pause').textContent=state.paused?'▶':'Ⅱ';$('pause').setAttribute('aria-label',state.paused?'Play animation':'Pause animation');$('pause').title=state.paused?'Play animation':'Pause animation';if(state.paused)finishTransition();refreshQa();}
$('pause').addEventListener('click',()=>{state.paused=!state.paused;paintPause();});paintPause();

function slotItems(slot){return slot==='looks'?(info?.looks||[]):(info?.slots?.[slot] || []);}
function findItem(slot,id){return slotItems(slot).find(i=>i.id===id);}
function itemNodes(item){return item.nodes || [];}
function accessoryGroup(id){return findItem('accessories',id)?.exclusiveGroup||({'hoops-original':'earrings','silver-drops':'earrings','glasses':'eyewear','cat-eye-frames':'eyewear','hazmat-mask':'eyewear','velvet-choker':'neckwear','rose-gold-collar':'neckwear','hair-bow':'hair-ribbon','midnight-ribbon':'hair-ribbon','back-katana':'back'})[id]||id;}
function coveredByHood(id,look=state.look){return look.dress==='hazmat-yellow'&&['earrings','neck','hair','neckwear','hair-ribbon'].includes(accessoryGroup(id));}
function coveredSlot(slot){return singleSlots.some(key=>(findItem(key,state.look[key])?.coverage?.hideSlots||[]).includes(slot));}
function validLook(input={}){
  const look={};for(const slot of singleSlots){look[slot]=findItem(slot,input[slot])?.id || findItem(slot,info.defaultLook?.[slot])?.id || slotItems(slot)[0]?.id;}
  const requested=Array.isArray(input.accessories)?input.accessories:[...(info.defaultLook?.accessories||[])];
  const groups=new Map();for(const id of requested)if(findItem('accessories',id)&&!coveredByHood(id,look))groups.set(accessoryGroup(id),id);
  look.accessories=[...groups.values()];return look;
}
function selected(slot,id){if(slot==='looks'){const bundle=findItem(slot,id)?.look;return bundle&&singleSlots.every(s=>!bundle[s]||bundle[s]===state.look[s])&&JSON.stringify([...(bundle.accessories||[])].sort())===JSON.stringify([...(state.look.accessories||[])].sort());}return slot==='accessories'?state.look.accessories?.includes(id):state.look[slot]===id;}
function finishTransition(){clearTimeout(transitionTimer);transitionOrbit=0;state.transition.active=false;state.transition.progress=1;$('swapFx')?.classList.remove('playing');}
function beginTransition(){finishTransition();state.transition.sequence++;if(!state.ready||state.paused||motionPreference.matches||shop.calm)return;transitionStart=performance.now();state.transition.active=true;state.transition.progress=0;const fx=$('swapFx');void fx.offsetWidth;fx.classList.add('playing');transitionTimer=setTimeout(()=>{finishTransition();refreshQa();},850);}
function updateTransition(now){if(!state.transition.active)return;const t=Math.min(1,(now-transitionStart)/850);state.transition.progress=Number(t.toFixed(3));transitionOrbit=Math.sin(t*Math.PI)*.14*Math.sin(t*Math.PI*2);if(t>=1)finishTransition();}
motionPreference.addEventListener('change',()=>{if(motionPreference.matches)finishTransition();});
function applyLook(input,{persist=!reviewMode}={}){
  if(!info||!avatar)return false;
  const look=validLook(input);
  for(const node of coveredNodes)node.visible=true;coveredNodes.clear();
  for(const slot of meshSlots)for(const item of slotItems(slot))for(const name of itemNodes(item)){
    const node=nodes.get(name);if(!node){const message=`Missing wardrobe node: ${name}`;if(!errors.includes(message)){errors.push(message);console.error(message);}continue;}
    node.visible=slot==='accessories'?look.accessories.includes(item.id):look[slot]===item.id;
  }
  const equipped=[...singleSlots.map(slot=>findItem(slot,look[slot])),...look.accessories.map(id=>findItem('accessories',id))].filter(Boolean);
  for(const item of equipped)for(const name of item.coverage?.hiddenNodes||[]){const node=nodes.get(name);if(node){node.visible=false;coveredNodes.add(node);}}
  for(const item of equipped)for(const slot of item.coverage?.hideSlots||[])for(const piece of slotItems(slot))for(const name of itemNodes(piece)){const node=nodes.get(name);if(node){node.visible=false;coveredNodes.add(node);}}
  const legwear=findItem('legwear',look.legwear)?.legwear || {mode:'fishnet'};
  garmentUniforms.mode.value=legwear.mode==='fishnet'?1:legwear.mode==='tights'?2:0;
  garmentUniforms.color.value.set(legwear.color||'#4d1d37');garmentUniforms.opacity.value=legwear.opacity??.78;garmentUniforms.density.value=legwear.density??5;garmentUniforms.lower.value=legwear.lower??.129;garmentUniforms.upper.value=legwear.upper??.713;
  garmentUniforms.hideBelow.value=Math.min(.86,Math.max(-1,...equipped.map(item=>item.coverage?.hideBodyBelow??-1)));
  const bodyMaskRange=findItem('dress',look.dress)?.coverage?.bodyMaskRange;
  if(Array.isArray(bodyMaskRange)&&bodyMaskRange.length===2&&bodyMaskRange.every(Number.isFinite)&&bodyMaskRange[0]<=bodyMaskRange[1])garmentUniforms.bodyMaskRange.value.fromArray(bodyMaskRange);else garmentUniforms.bodyMaskRange.value.set(0,-1);
  const hem=findItem('dress',look.dress)?.coverage?.bodyMaskHem;
  if(hem&&[hem.base,hem.x??0,hem.z??0,hem.top??1.04].every(Number.isFinite)){garmentUniforms.bodyMaskHem.value.set(hem.base,hem.x??0,hem.z??0,hem.top??bodyMaskRange?.[1]??1.04);garmentUniforms.bodyMaskRange.value.set(0,-1);}else garmentUniforms.bodyMaskHem.value.set(0,0,0,-1);
  for(const [mat,original] of originals){mat.color?.copy(original.color);if(original.roughness!==undefined)mat.roughness=original.roughness;}
  const skin=findItem('skin',look.skin);
  for(const [name,props] of Object.entries(skin?.materials||{})){
    const matches=materials.get(name)||[];
    if(!matches.length){const message=`Missing skin material: ${name}`;if(!errors.includes(message)){errors.push(message);console.error(message);}}
    for(const mat of matches){const values=typeof props==='string'?{color:props}:props;if(values.color&&mat.color)mat.color.set(values.color);if(values.roughness!==undefined)mat.roughness=values.roughness;}
  }
  colorController?.apply(colorOverrides,look);
  const changed=JSON.stringify(state.look)!==JSON.stringify(look);state.look=look;state.swaps++;if(changed)beginTransition();if(hosted)shopSync(persist);else if(persist){try{localStorage.setItem(STORE,JSON.stringify(look));$('saveStatus').textContent='Saved on this device';}catch{$('saveStatus').textContent='Saved for this visit';}}
  for(const update of expansionUpdates)update(0,look);
  paintChoices();paintEquipped();paintColorControls();refreshQa();return true;
}
function toggleItem(slot,id){stopDemo(false);if(slot==='looks'){applyLook({...info.defaultLook,...findItem(slot,id).look});return;}const next=structuredClone(state.look);if(slot==='skin'&&colorOverrides.skin){delete colorOverrides.skin;saveColors();}if(slot==='accessories'){if(coveredByHood(id))return;next.accessories=next.accessories.includes(id)?next.accessories.filter(x=>x!==id):[...next.accessories.filter(x=>accessoryGroup(x)!==accessoryGroup(id)),id];}else next[slot]=id;applyLook(next);}
function stopDemo(restore=true){
  for(const timer of demoTimers)clearTimeout(timer);demoTimers=[];
  if(state.demo&&restore&&demoPrevious)applyLook(demoPrevious,{persist:false});
  state.demo=false;demoPrevious=null;$('demo').innerHTML='Show swaps <span aria-hidden="true">▷</span>';refreshQa();
}
function showSwaps(){
  if(!state.ready)return;if(state.demo){stopDemo();return;}
  demoPrevious=structuredClone(state.look);state.demo=true;$('demo').innerHTML='Stop preview <span aria-hidden="true">□</span>';
  const looks=[info.defaultLook,{...info.defaultLook,dress:'midnight-cocktail',hair:'chestnut-bob',shoes:'knee-high-boots',legwear:'opaque-black',accessories:['hoops-original','glasses']},findItem('looks','hazmat').look,findItem('looks','action').look];
  applyLook(looks[0],{persist:false});
  looks.slice(1).forEach((look,index)=>demoTimers.push(setTimeout(()=>applyLook(look,{persist:false}),(index+1)*3000)));
  demoTimers.push(setTimeout(()=>stopDemo(true),looks.length*3000));
}
$('demo').addEventListener('click',showSwaps);
const icons={
 dress:'<path d="M29 8h6l3 13 7 5-7 20 11 34H11l11-34-7-20 7-5 3-13h4l1 12z"/>',
 hair:'<path d="M30 5C9 5 7 23 8 42L5 70l16 7 2-30c-8-4-8-19-4-24 5 2 17 0 22-5 8 11 6 24-4 29l2 30 17-7-5-28C54 21 48 5 30 5z"/>',
 shoes:'<path d="M12 18h18l-2 26 20 9q8 4 7 13H9V48zM9 68h46v5H9z"/>',
 legwear:'<path d="M14 10h32l-4 66H31l-1-47-3 47H16z"/>',
 looks:'<path d="m22 10 8 5 8-5 13 15-9 8-4-7 8 50H14l8-50-4 7-9-8z"/>',
 mask:'<path d="M10 29q20-20 40 0v25L30 70 10 54z"/><g fill="#f5eee0"><ellipse cx="21" cy="36" rx="7" ry="5"/><ellipse cx="39" cy="36" rx="7" ry="5"/><circle cx="30" cy="55" r="8"/></g>',
 katana:'<path d="m14 73-4-3 32-51 6-9-1 12-28 45zM25 53l-4 7 17 10 4-7z"/>',
 glasses:'<path fill="none" stroke="currentColor" stroke-width="3" d="M5 34h20v16H8zM35 34h20l-3 16H35zM25 40h10M3 35l-3-4M56 35l4-4"/>',
 choker:'<path fill="none" stroke="currentColor" stroke-width="7" d="M8 38q22 17 44 0"/><path d="m30 49 5 7-5 7-5-7z"/>',
 bow:'<path d="M29 38C5 13 3 31 8 51l20-7-10 25 12-7 12 7-9-25 20 7c5-20 3-38-21-13z"/>',
 hoops:'<g fill="none" stroke="currentColor" stroke-width="3"><ellipse cx="17" cy="42" rx="10" ry="18"/><ellipse cx="43" cy="42" rx="10" ry="18"/></g>'
};
function thumbnail(item,slot){
  const id=item.id;
  if(slot==='arms'||slot==='ankles'){
    if(id==='none')return '<path d="m14 25 32 32M46 25 14 57" stroke="currentColor" stroke-width="2"/>';
    const ring='<ellipse cx="30" cy="43" rx="21" ry="12" fill="none" stroke="currentColor" stroke-width="2.5"/>';
    if(/pearl/.test(id))return ring+Array.from({length:12},(_,i)=>`<circle cx="${30+21*Math.cos(i*Math.PI/6)}" cy="${43+12*Math.sin(i*Math.PI/6)}" r="3.5"/>`).join('');
    if(/open-cuff/.test(id))return '<path d="M23 32C0 32 2 57 26 57M37 32c23 0 21 25-3 25" fill="none" stroke="currentColor" stroke-width="7"/>';
    if(/gem/.test(id))return ring+'<path d="m30 49 6 8-6 9-6-9z"/><path d="m30 51 0 12" stroke="#fff" stroke-opacity=".5"/>';
    if(/ribbon/.test(id))return ring+'<path d="M30 52c-22-17-19 17 0 0 22-17 19 17 0 0l-6 23 8-5 4 4z"/>';
    if(/wrap|spiral/.test(id))return '<path d="M11 23c47-10 47 12 0 20s0 23 38 14M11 35c47-10 47 12 0 20s0 23 38 14" fill="none" stroke="currentColor" stroke-width="3"/>';
    return ring+(/double|bands/.test(id)?'<ellipse cx="30" cy="34" rx="21" ry="12" fill="none" stroke="currentColor" stroke-width="3"/>':'');
  }
  const bodice='<path d="M22 9h5l3 12 3-12h5l3 16-6 16H25l-6-16z"/>';
  const trim='<path d="M24 40h12" fill="none" stroke="#fff4dc" stroke-opacity=".55" stroke-width="1.5"/>';
  const coverall='<path d="M23 5q7-5 14 0l4 15 10 21-7 4-6-12 2 43H31l-1-24-1 24H19l3-43-6 12-7-4 10-21z"/><path d="M30 19v29M21 48h18" fill="none" stroke="#463a23" stroke-width="1.2"/><ellipse cx="30" cy="13" rx="6" ry="7" fill="#f0ddbc"/>';
  if(slot==='dress'||slot==='looks'){
    const dressId=slot==='looks'?item.look?.dress:id;
    if(dressId==='hazmat-yellow')return coverall+(slot==='looks'?'<g transform="translate(20 5) scale(.33)">'+icons.mask+'</g>':'');
    let skirt='<path d="M25 40h10l6 28H19z"/>';
    if(/midi|cocktail/.test(dressId||''))skirt='<path d="M25 40h10l9 41H16z"/>';
    if(/bell|flare/.test(dressId||''))skirt='<path d="M25 40h10q4 18 17 31-22 8-44 0 13-13 17-31z"/>';
    if(/tulip/.test(dressId||''))skirt='<path d="M25 40h10c15 13 12 26 2 34H23c-10-9-13-22 2-34z"/><path d="m36 41-15 27" fill="none" stroke="#fff4dc" stroke-opacity=".5"/>';
    if(/asymmetric/.test(dressId||''))skirt='<path d="M25 40h10l12 31-30-11z"/>';
    if(/highlow/.test(dressId||''))skirt='<path d="M25 40h10l13 37-13-10-5-7-5 7-13 10z"/>';
    let details=dressId==='action-yellow'?'<path d="M23 25h3l1 41h-4zM34 25h3l1 41h-4z" fill="#28272a"/>':'';
    if(slot==='looks'&&item.look?.accessories?.includes('back-katana'))details+='<g transform="translate(6 1) rotate(-12 30 43)" fill="#38343b">'+icons.katana+'</g>';
    if(slot==='looks'&&item.look?.accessories?.includes('hair-bow'))details+='<g transform="translate(30 3) scale(.35)" fill="#ad6278">'+icons.bow+'</g>';
    return bodice+skirt+trim+details;
  }
  if(slot==='hair'){
    if(id==='ink-sleek')return '<path d="M30 9C8 8 8 32 15 48l8-5c-9-20-3-22 13-19l9-8c6 5 6 22 0 31l6 4c14-27 0-42-21-42zM42 16c22-4 15 25 10 45-2 10-7 16-11 18 8-21-2-39 1-63z"/>';
    if(id==='rose-waves')return '<path d="M30 8C8 7 9 30 16 42l5-8-2-12q12 6 22-4l3 18 5 6C57 17 47 8 30 8zM14 28C-2 27 5 53 4 73l13-9 3-29zM46 28c16-1 9 25 10 45l-13-9-3-29z"/>';
    if(id==='platinum-long')return '<circle cx="44" cy="49" r="13"/><path d="M30 8C6 8 7 34 16 47l7-5c-12-19-6-20 15-24 7 8 6 16 1 25l8 3C58 25 49 8 30 8z"/><path d="m40 42 9 13m-12-8 11 10" stroke="#fff4dc" stroke-opacity=".4"/>';
    if(id==='espresso-sidesweep')return '<path d="M30 6C8 5 5 25 12 51l7 7 5-14c-10-7-8-20-4-23 9 2 14-3 19-7 12 20 5 34-5 58l20-3-2-31C57 17 47 6 30 6z"/>';
    if(/roundbob/.test(id))return '<path d="M30 7C7 7 3 29 9 47q21 12 42 0C57 29 53 7 30 7z"/><path d="M20 24q10 5 20-4v20q-10 15-20 0z" fill="#efe7dc"/><path d="M13 23q-4 17 1 24M46 22q5 16 0 25" fill="none" stroke="#fff4dc" stroke-opacity=".35"/>';
    if(/bob/.test(id))return '<path d="M30 6C12 6 7 20 9 43l7 9 10-8-7-12 1-9q13 4 22-7l1 19-8 9 10 8 7-9C56 19 48 6 30 6z"/><path d="M13 22v17M47 24v15" fill="none" stroke="#fff4dc" stroke-opacity=".35"/>';
    if(/lob|sidesweep/.test(id))return '<path d="M30 5C9 6 7 22 9 43L6 61l17 4 1-19c-7-5-9-14-4-24 12 4 17-4 22-7 7 16 4 26-7 31l1 19 18-4-5-19C55 21 49 4 30 5z"/><path d="M15 28q-2 21-3 29M44 28q3 18 3 28" fill="none" stroke="#fff4dc" stroke-opacity=".35"/>';
    const long=icons.hair;
    if(/waves|volume|original-blonde/.test(id))return long+'<g fill="none" stroke="#fff4dc" stroke-opacity=".4" stroke-width="1.2"><path d="M15 28q-6 8 0 15t0 16q-5 7 0 14M45 26q6 8 0 15t0 16q5 7 0 14"/></g>';
    return long+'<path d="m15 28-2 40m32-40 2 40" fill="none" stroke="#fff4dc" stroke-opacity=".35"/>';
  }
  if(slot==='shoes'){
    if(/knee-high|midcalf/.test(id)){const top=/knee-high/.test(id)?6:23;return `<path d="M14 ${top}h19l-3 43 20 12q5 3 5 10H10V54z"/><path d="M11 67h44v6H29l-8-5-4 13h-5z"/><path d="M15 ${top+4}h16" fill="none" stroke="#fff4dc" stroke-opacity=".55"/>`;}
    if(/rose-shoes|yellow-trainers/.test(id))return icons.shoes+'<path d="M13 22h16v9H13z" fill="#fff4dc" opacity=".35"/>';
    return icons.shoes+'<path d="M14 23h13m-4 3v20" fill="none" stroke="#fff4dc" stroke-opacity=".5" stroke-width="1.2"/>';
  }
  if(slot==='legwear'){
    const legs='M16 9h28l-3 66H31l-1-46-3 46H17z';
    if(item.legwear?.mode==='fishnet'){const name=`fishnet-${id}`,size=/fine/.test(id)?5:8;return `<defs><pattern id="${name}" width="${size}" height="${size}" patternUnits="userSpaceOnUse"><path d="M0 0L${size} ${size}M${size} 0L0 ${size}" fill="none" stroke="currentColor" stroke-width=".7"/></pattern></defs><path d="${legs}" fill="#e4c6af"/><path d="${legs}" fill="url(#${name})"/><path d="M16 10h28v5H16z"/>`;}
    if(item.legwear?.mode==='pants')return `<path d="${legs}"/><path d="M16 15h28M30 15v14M18 17v8l7-5" fill="none" stroke="#fff4dc" stroke-opacity=".5"/>`+(id==='action-leggings'?'<path d="m17 16 2 57m23-57-2 57" stroke="#27272b" stroke-width="3"/>':'');
    return `<path d="${legs}" fill="#e4c6af"/><path d="${legs}" opacity="${item.legwear?.opacity??1}"/>`+(item.legwear?.mode==='bare'?'':'<path d="M16 10h28v5H16z" opacity=".7"/>');
  }
  if(slot==='accessories'){
    if(/heart/.test(id))return '<path d="M27 37C14 20 0 37 12 48l10 9 9-17M33 37c13-17 27 0 15 11l-10 9-9-17M26 40h8" fill="'+(/shades/.test(id)?'#3e3543':'none')+'" stroke="currentColor" stroke-width="3"/><path d="m11 36 8 7m22-7 5 6" stroke="#fff" stroke-opacity=".6" stroke-width="2"/>';
    if(id==='sunglasses')return '<path d="M5 32h21v19H9zm50 0H34v19h17zM26 38h8" fill="#303440" stroke="currentColor" stroke-width="3"/><path d="m10 36 10 9m18-9 10 9" stroke="#fff" stroke-opacity=".55" stroke-width="2"/>';
    if(/cat-eye/.test(id))return '<path d="m4 31 22 6v13H10zm52 0-22 6v13h16zM26 41h8" fill="none" stroke="currentColor" stroke-width="3"/>';
    if(/silver-drops/.test(id))return '<path d="M16 23c0 15-10 15-8 26s15 11 16 0-8-11-8-26zM44 23c0 15-10 15-8 26s15 11 16 0-8-11-8-26z"/>';
    return /mask/.test(id)?icons.mask:/katana/.test(id)?icons.katana:/glass|frames/.test(id)?icons.glasses:/choker|collar/.test(id)?icons.choker:/bow|ribbon/.test(id)?icons.bow:icons.hoops;
  }
  return icons[slot]||icons.dress;
}
function art(item,slot){
  const wrapper=document.createElement('span');wrapper.className=`choice-art ${slot}`;
  const swatch=item.swatch || item.color || ({dress:'#b28176',hair:'#d4b679',skin:'#e5b79c',accessories:'#b79c59',shoes:'#55313d',legwear:'#38303c',looks:'#c28b4a'}[slot]);
  if(slot==='skin'){const disc=document.createElement('span');disc.className='skin-disc';disc.style.background=swatch;wrapper.append(disc);}else{wrapper.innerHTML=`<svg viewBox="0 0 60 86" fill="currentColor" aria-hidden="true">${thumbnail(item,slot)}</svg>`;wrapper.style.color=swatch;}
  if(selected(slot,item.id)){const check=document.createElement('span');check.className='check';check.textContent='✓';wrapper.append(check);}
  return wrapper;
}
function paintChoices(){
  const slot=state.slot,covered=coveredSlot(slot);$('categoryTitle').textContent=titles[slot][0];$('categoryHint').textContent=covered?'Change the outfit to see this layer.':titles[slot][1];$('choices').replaceChildren();$('choices').setAttribute('aria-label',slot);
  for(const item of slotItems(slot)){const b=document.createElement('button');b.className='choice';b.dataset.item=item.id;b.setAttribute('aria-pressed',String(!covered&&selected(slot,item.id)));const badge=hosted?shop.badge(slot,item.id):null;if(badge){b.classList.add('stamp-'+badge.kind);if(!badge.owned)b.classList.add('unowned');}b.disabled=covered||(slot==='accessories'&&coveredByHood(item.id));b.append(art(item,slot));if(covered)b.querySelector('.check')?.remove();const name=document.createElement('span');name.className='choice-name';name.textContent=item.name || item.label || item.id;b.append(name);const note=document.createElement('span');note.className='choice-note';note.textContent=covered?'Covered by the outfit or boots':b.disabled?'Covered by the hood':badge?badge.text:item.note || (slot==='accessories'?(selected(slot,item.id)?'Wearing':'Add a detail'):(selected(slot,item.id)?'Wearing now':'Try on'));b.append(note);b.addEventListener('click',()=>toggleItem(slot,item.id));$('choices').append(b);}
}
function paintEquipped(){
  $('equipped').replaceChildren();for(const slot of [...singleSlots,'accessories'])for(const id of slot==='accessories'?state.look.accessories:[state.look[slot]]){const item=findItem(slot,id);if(!item||id==='none'||coveredSlot(slot))continue;const pill=document.createElement('span');pill.className='equipped-pill';const dot=document.createElement('i');dot.className='equipped-dot';dot.style.background=item.swatch || item.color || '#a76570';pill.append(dot,document.createTextNode(item.name||item.id));$('equipped').append(pill);}
  $('lookCaption').textContent=findItem('dress',state.look.dress)?.name || 'Your look';
}
document.querySelectorAll('[data-slot]').forEach(b=>b.addEventListener('click',()=>{state.slot=b.dataset.slot;document.querySelectorAll('[data-slot]').forEach(tab=>tab.setAttribute('aria-selected',String(tab===b)));paintChoices();}));
$('reset').addEventListener('click',()=>{stopDemo(false);colorOverrides={};saveColors();applyLook(info?.defaultLook||{});});

const colorPresets={
  fabric:['#db4c9e','#202d49','#ece1ce','#b63842','#387f7a','#dab32d','#4b315c','#151923'],
  trim:['#d2ac67','#c6ccd0','#f2dfbf','#20252d','#be687c','#776b9e','#4f766b','#efbd3c'],
  hair:['#e6c48f','#f1e7d5','#9c5b37','#50352b','#25232a','#b86583','#834d35','#a197a5'],
  skin:['#edc9b2','#d7a98c','#c99571','#b47e5d','#9b684b','#85543e','#684333','#51352b'],
  eyes:['#327b9c','#58856d','#826434','#594238','#718b99','#8b6c9e','#b88e48','#2c526b'],
  lips:['#b5286a','#b9474e','#9e3c52','#bf6d6a','#73364c','#d48589','#7f262e','#a55c54'],
  nails:['#d3658c','#b23349','#eee3d7','#35303c','#caa158','#774b7a','#486b71','#ed9a91'],
  shoes:['#7d294f','#202128','#ddd5c2','#702430','#a5b3bd','#be6784','#25465b','#e6b62e'],
  accessories:['#d2a953','#c7d2da','#322331','#b44978','#74364a','#d6a18b','#4f4865','#647d83']
};
function saveColors(){if(hosted){shopSync(true);return;}try{localStorage.setItem(COLOR_STORE,JSON.stringify(colorOverrides));}catch{}}
function changeColor(value){const id=$('colorChannel').value;if(!/^#[0-9a-f]{6}$/i.test(value))return;stopDemo(false);colorOverrides[id]=value.toLowerCase();saveColors();applyLook(state.look,{persist:false});drawPreview();}
function paintColorControls(){
  if(!colorController)return;const id=$('colorChannel').value||'fabric';const slot=({fabric:'dress',hair:'hair',skin:'skin',shoes:'shoes'})[id];const value=colorOverrides[id]||(slot&&findItem(slot,state.look[slot])?.swatch)||colorController.defaultColor(id);
  $('colorPicker').value=value;$('colorHex').value=value;$('colorHex').removeAttribute('aria-invalid');
  $('colorStatus').textContent=Object.keys(colorOverrides).length?'Custom colors saved':'Original palette';
  $('resetChannel').disabled=!colorOverrides[id];$('resetColors').disabled=!Object.keys(colorOverrides).length;
  $('colorPresets').replaceChildren();for(const color of colorPresets[id]||[]){const b=document.createElement('button');b.type='button';b.style.background=color;b.setAttribute('aria-label',`${id} ${color}`);b.setAttribute('aria-pressed',String(colorOverrides[id]===color));b.addEventListener('click',()=>changeColor(color));$('colorPresets').append(b);}
}
$('colorChannel').addEventListener('change',paintColorControls);
$('colorPicker').addEventListener('input',e=>changeColor(e.target.value));
$('colorHex').addEventListener('input',e=>{const value=e.target.value.startsWith('#')?e.target.value:'#'+e.target.value;if(/^#[0-9a-f]{6}$/i.test(value))changeColor(value);});
$('colorHex').addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();const value=e.target.value.startsWith('#')?e.target.value:'#'+e.target.value;changeColor(value);}});
$('colorHex').addEventListener('change',e=>{const value=e.target.value.startsWith('#')?e.target.value:'#'+e.target.value;if(/^#[0-9a-f]{6}$/i.test(value))changeColor(value);else e.target.setAttribute('aria-invalid','true');});
$('resetChannel').addEventListener('click',()=>{stopDemo(false);delete colorOverrides[$('colorChannel').value];saveColors();applyLook(state.look,{persist:false});drawPreview();});
$('resetColors').addEventListener('click',()=>{stopDemo(false);colorOverrides={};saveColors();applyLook(state.look,{persist:false});drawPreview();});

function animationDefinition(id){return (info?.animations||[]).find(x=>(typeof x==='string'?x:x.id)===id);}
function getClip(id){const def=animationDefinition(id);const name=typeof def==='string'?def:(def?.clip || def?.name || id);return clips.find(c=>c.name===name)||clips.find(c=>c.name.toLowerCase().includes(id.toLowerCase()));}
function setAnimation(id){
  if(!mixer)return false;const clip=getClip(id);if(!clip)return false;
  if(action?.getClip()===clip){refreshQa();return true;}
  if(state.paused)mixer.stopAllAction();
  const next=mixer.clipAction(clip);next.reset().stopFading().stopWarping().setEffectiveTimeScale(1).setEffectiveWeight(1).setLoop(THREE.LoopRepeat,Infinity).play();if(action&&!state.paused){next.crossFadeFrom(action,.35,true);}action=next;state.animation=id;
  if(state.paused){mixer.update(0);for(const update of expansionUpdates)update(0,state.look);avatar.updateMatrixWorld(true);drawPreview();}
  $('animations').querySelectorAll('button').forEach(b=>b.classList.toggle('active',b.dataset.animation===id));refreshQa();return true;
}
function paintAnimations(){
  $('animations').replaceChildren();for(const entry of knownAnimations){if(!getClip(entry.id))continue;const b=document.createElement('button');b.dataset.animation=entry.id;b.textContent=entry.name;b.title=entry.name;b.addEventListener('click',()=>setAnimation(entry.id));$('animations').append(b);}
}

function effectivelyVisible(node){for(let p=node;p;p=p.parent)if(!p.visible)return false;return true;}
function refreshQa(){if(qaPanel){qaPanel.textContent=JSON.stringify(getDebugState(),null,2);qaLast=performance.now();}}
function poseFingerprint(){
  const result=[];const position=new THREE.Vector3();
  const meshes=[...new Set(nodes.values())].filter(n=>n.isMesh&&effectivelyVisible(n)&&n.geometry?.attributes?.position);
  for(const mesh of meshes.slice(0,4)){
    const count=mesh.geometry.attributes.position.count;const vertices=[];
    for(const i of [0,Math.floor(count*.37),Math.floor(count*.73)]){mesh.getVertexPosition(i,position);position.applyMatrix4(mesh.matrixWorld);vertices.push(position.toArray().map(v=>Number(v.toFixed(5))));}
    result.push({node:mesh.userData.name||mesh.name,vertices,morphs:(mesh.morphTargetInfluences||[]).map((weight,index)=>({index,weight:Number(weight.toFixed(5))})).filter(x=>Math.abs(x.weight)>.00001).slice(0,8)});
  }
  return result;
}
function getDebugState(){return {...structuredClone(state),animationTime:Number((action?.time||0).toFixed(5)),elapsed:Number(elapsed.toFixed(5)),clip:action?.getClip().name||null,clips:clips.map(c=>({name:c.name,duration:c.duration,tracks:c.tracks.length})),errors:[...errors],colorOverrides:{...colorOverrides},colorRegions:colorController?.diagnostics(),revisions:{fit:info?.fit?.revision??null,tailoring:info?.tailoring?.revision??null,hair:info?.hairstyles?.revision??null,jewelry:info?.jewelry?.revision??null,foundation:info?.foundationAccessoryFit?.revision??null},generatedGroups:[...new Set(nodes.values())].filter(n=>n.isGroup&&/^(Foundation|Expanded)[.]/.test(n.name)).length,totalMeshes:[...new Set(nodes.values())].filter(n=>n.isMesh).length,catalogCounts:Object.fromEntries([...singleSlots,'accessories','looks'].map(slot=>[slot,slotItems(slot).length])),coverage:{hiddenNodes:[...coveredNodes].map(n=>n.name),hideBodyBelow:garmentUniforms.hideBelow.value,bodyMaskRange:garmentUniforms.bodyMaskRange.value.y>=garmentUniforms.bodyMaskRange.value.x?garmentUniforms.bodyMaskRange.value.toArray():null,bodyMaskHem:garmentUniforms.bodyMaskHem.value.toArray(),legwearMode:garmentUniforms.mode.value},activeGroups:Object.fromEntries(meshSlots.map(slot=>[slot,slotItems(slot).filter(item=>itemNodes(item).some(name=>nodes.get(name)&&effectivelyVisible(nodes.get(name)))).map(item=>({id:item.id,nodes:itemNodes(item)}))])),render:{...perf,calls:renderer.info.render.calls,triangles:renderer.info.render.triangles,geometries:renderer.info.memory.geometries,textures:renderer.info.memory.textures},poseFingerprint:poseFingerprint(),visibleNodes:[...new Set(nodes.values())].filter(n=>n.isMesh&&effectivelyVisible(n)).map(n=>n.userData.name||n.name)};}
window.wardrobeDebug={getState:getDebugState,setAnimation,setLook:look=>{stopDemo(false);return applyLook({...state.look,...look});},setPaused:value=>{state.paused=Boolean(value);paintPause();},getManifest:()=>info};

function validateManifest(){
  const missing=[];
  for(const slot of meshSlots)for(const item of slotItems(slot))for(const name of itemNodes(item))if(!nodes.has(name))missing.push(name);
  if(missing.length)throw new Error(`Wardrobe node names do not match the model: ${missing.slice(0,5).join(', ')}${missing.length>5?` (+${missing.length-5} more)`:''}.`);
  for(const slot of ['dress','hair'])if(!slotItems(slot).length||!slotItems(slot).some(item=>itemNodes(item).length))throw new Error(`No live ${slot} pieces were exported.`);
  const missingMaterials=[];for(const item of slotItems('skin'))for(const name of Object.keys(item.materials||{}))if(!materials.has(name))missingMaterials.push(name);
  if(missingMaterials.length)throw new Error(`Skin material names do not match the model: ${[...new Set(missingMaterials)].join(', ')}.`);
}

function preserveStockingsMaterial(){
  const body=nodes.get('GEO-rain-body');
  if(!body?.geometry?.attributes.wardrobeLowerBody)throw new Error('Body coverage requires the native lower-body region mask.');
  const position=body.geometry.attributes.position,reference=new Float32Array(position.count*3),rest=new THREE.Vector3();
  for(let i=0;i<position.count;i++)rest.fromBufferAttribute(position,i).applyMatrix4(body.matrixWorld).toArray(reference,i*3);
  body.geometry.setAttribute('wardrobeBodyReference',new THREE.BufferAttribute(reference,3));
  for(const name of ['Companion.Complexion.Stockings','Companion.MakeupV10.Skin'])for(const material of materials.get(name)||[]){
    if(!material.userData.wardrobeBody)continue;
    const stockings=name==='Companion.Complexion.Stockings';
    material.onBeforeCompile=shader=>{
      for(const [name,value] of Object.entries(garmentUniforms))shader.uniforms[`wardrobe_${name}`]=value;
      shader.vertexShader='attribute vec3 wardrobeBodyReference;\nattribute float wardrobeLowerBody;\nvarying vec3 wardrobeRest;\nvarying vec3 wardrobeBodyRest;\nvarying float wardrobeBodyRegion;\n'+shader.vertexShader;
      shader.vertexShader=shader.vertexShader.replace('#include <begin_vertex>','#include <begin_vertex>\nwardrobeRest = position;\nwardrobeBodyRest = wardrobeBodyReference;\nwardrobeBodyRegion = wardrobeLowerBody;');
      shader.fragmentShader='varying vec3 wardrobeRest;\nvarying vec3 wardrobeBodyRest;\nvarying float wardrobeBodyRegion;\nuniform float wardrobe_mode;\nuniform vec3 wardrobe_color;\nuniform float wardrobe_opacity;\nuniform float wardrobe_density;\nuniform float wardrobe_lower;\nuniform float wardrobe_upper;\nuniform float wardrobe_hideBelow;\nuniform vec2 wardrobe_bodyMaskRange;\nuniform vec4 wardrobe_bodyMaskHem;\n'+shader.fragmentShader;
      shader.fragmentShader=shader.fragmentShader.replace('#include <color_fragment>',`#include <color_fragment>
        float wardrobeZ = wardrobeRest.y / 1.03;
        if (wardrobeBodyRegion > .5) {
          if (wardrobeBodyRest.y < wardrobe_hideBelow) discard;
          if (wardrobeBodyRest.y >= wardrobe_bodyMaskRange.x && wardrobeBodyRest.y <= wardrobe_bodyMaskRange.y) discard;
          float hemRadius=max(.001,length(vec2(wardrobeBodyRest.x,wardrobeBodyRest.z-.027)));
          float hemHeight=wardrobe_bodyMaskHem.x+wardrobe_bodyMaskHem.y*wardrobeBodyRest.x/hemRadius+wardrobe_bodyMaskHem.z*(wardrobeBodyRest.z-.027)/hemRadius;
          if(wardrobe_bodyMaskHem.w>=wardrobe_bodyMaskHem.x && wardrobeBodyRest.y>=hemHeight && wardrobeBodyRest.y<=wardrobe_bodyMaskHem.w) discard;
        }
        ${stockings?`
        float wardrobeTheta = atan(wardrobeRest.x - sign(wardrobeRest.x) * .075, -wardrobeRest.z + .006);
        float wardrobeA = abs(sin(wardrobeTheta * wardrobe_density + wardrobeZ * wardrobe_density * 10.0));
        float wardrobeB = abs(sin(wardrobeTheta * wardrobe_density - wardrobeZ * wardrobe_density * 10.0));
        float wardrobeGrid = max(1.0 - smoothstep(.092 - fwidth(wardrobeA), .092 + fwidth(wardrobeA), wardrobeA), 1.0 - smoothstep(.092 - fwidth(wardrobeB), .092 + fwidth(wardrobeB), wardrobeB));
        float wardrobeMask = wardrobe_mode < .5 ? 0.0 : (wardrobe_mode < 1.5 ? max(wardrobeGrid * wardrobe_opacity, step(wardrobe_upper-.029, wardrobeZ) * wardrobe_opacity) : wardrobe_opacity);
        wardrobeMask *= step(wardrobe_lower, wardrobeZ) * (1.0 - step(wardrobe_upper, wardrobeZ)) * step(.5, wardrobeBodyRegion);
        diffuseColor.rgb = mix(diffuseColor.rgb, wardrobe_color, wardrobeMask);`:''}
      `);
    };
    material.customProgramCacheKey=()=> `rain-legwear-v3-${stockings}`;
    material.needsUpdate=true;
  }
}

function isolateBodyMaterials(){
  const body=nodes.get('GEO-rain-body');
  if(!body)throw new Error('Body material isolation needs GEO-rain-body.');
  body.traverse(mesh=>{
    if(!mesh.isMesh)return;
    const replace=material=>{
      if(!['Companion.Complexion.Stockings','Companion.MakeupV10.Skin'].includes(material.name))return material;
      const clone=material.clone();clone.name=material.name;clone.userData={...clone.userData,wardrobeBody:true};
      materials.get(material.name).push(clone);return clone;
    };
    mesh.material=Array.isArray(mesh.material)?mesh.material.map(replace):replace(mesh.material);
  });
}

function expandPalettesAndLooks(){
  const original=findItem('skin','original');
  const palettes=[['porcelain','Porcelain','#fff0e7','#efcfc1'],['peach','Peach','#f6d5bc','#e1ad94'],['olive','Olive','#d9c6a0','#bd9771'],['honey','Honey','#e4b384','#cc9868'],['caramel','Caramel','#c99d78','#ae7750'],['copper','Copper','#bd8b6d','#9b6244'],['espresso','Espresso','#795b4d','#624333']];
  for(const [id,name,tint,swatch] of palettes)if(!findItem('skin',id))info.slots.skin.push({id,name,swatch,materials:Object.fromEntries(Object.keys(original?.materials||{}).map(key=>[key,{color:tint}]))});
  const base=info.defaultLook;
  info.looks=[
    {id:'original',name:'The original',swatch:'#db4c9e',look:{...base}},
    {id:'hazmat',name:'Yellow alert',swatch:'#dfbc2f',note:'Suit, mask and boots',look:{...base,dress:'hazmat-yellow',shoes:'hazmat-boots',legwear:'hazmat-trousers',accessories:['hazmat-mask']}},
    {id:'action',name:'Action yellow',swatch:'#e7bf31',note:'Full outfit + katana',look:{...base,dress:'action-yellow',hair:'chestnut-bob',shoes:'yellow-trainers',legwear:'action-leggings',accessories:['back-katana']}},
    {id:'after-dark',name:'After dark',swatch:'#23324c',look:{...base,dress:'midnight-cocktail',hair:'chestnut-bob',accessories:['hoops-original','glasses']}},
    {id:'rose-gold',name:'Rose gold',swatch:'#ac3e68',look:{...base,dress:'rose-flare',skin:'warm',accessories:['hoops-original','hair-bow']}},
    {id:'soft-rose',name:'Soft rose',swatch:'#d397ad',look:{...base,dress:'rose-flare',skin:'porcelain',accessories:['hair-bow']}},
    {id:'bronze-night',name:'Bronze night',swatch:'#885637',look:{...base,dress:'midnight-cocktail',skin:'deep',accessories:['velvet-choker']}},
    {id:'daylight',name:'Daylight',swatch:'#d6ad83',look:{...base,skin:'honey',accessories:['glasses']}},
    {id:'copper-rose',name:'Copper rose',swatch:'#ad6b54',look:{...base,dress:'rose-flare',hair:'chestnut-bob',skin:'copper',accessories:['hoops-original']}},
    {id:'satin-evening',name:'Satin evening',swatch:'#b06c8f',look:{...base,skin:'caramel',accessories:['velvet-choker','glasses']}}
  ];
}

async function init(){
  const response=await fetch('./assets/runtime-info.json',{cache:'no-store'});if(!response.ok)throw new Error(`Wardrobe manifest: HTTP ${response.status}`);info=await response.json();
  const gltf=await new GLTFLoader().loadAsync(BIG_ASSETS+'avatar.glb',event=>{if(event.total){$('loadProgress').value=event.loaded/event.total;$('loadMessage').textContent=`Getting dressed · ${Math.round(event.loaded/event.total*100)}%`;}});
  avatar=gltf.scene;clips=gltf.animations;scene.add(avatar);
  avatar.traverse(obj=>{nodes.set(obj.name,obj);const originalName=gltf.parser.json.nodes?.[gltf.parser.associations.get(obj)?.nodes]?.name;if(originalName)nodes.set(originalName,obj);if(obj.userData.name)nodes.set(obj.userData.name,obj);if(!obj.isMesh)return;obj.frustumCulled=false;for(const mat of Array.isArray(obj.material)?obj.material:[obj.material]){if(!materials.has(mat.name))materials.set(mat.name,[]);if(!materials.get(mat.name).includes(mat))materials.get(mat.name).push(mat);if(!originals.has(mat))originals.set(mat,{color:mat.color?.clone(),roughness:mat.roughness});}});
  mixer=new THREE.AnimationMixer(avatar);setAnimation('idle');mixer.update(0);avatar.updateMatrixWorld(true);
  for(const expand of [expandFoundation,expandItems,expandTailoring,expandJewelry]){const result=await expand({THREE,avatar,nodes,materials,info});if(result?.update)expansionUpdates.push(result.update);}
  isolateBodyMaterials();
  for(const list of materials.values())for(const mat of list)if(!originals.has(mat))originals.set(mat,{color:mat.color?.clone(),roughness:mat.roughness});
  expandPalettesAndLooks();
  validateManifest();
  preserveStockingsMaterial();
  colorController=createWardrobeColors({THREE,avatar,nodes,materials,info});
  if(colorController.update)expansionUpdates.push(colorController.update);
  for(const list of materials.values())for(const mat of list)if(!originals.has(mat))originals.set(mat,{color:mat.color?.clone(),roughness:mat.roughness});
  for(const channel of colorController.channels){const option=document.createElement('option');option.value=channel.id;option.textContent=channel.name;option.disabled=!channel.available;$('colorChannel').append(option);}
  // Preserve source facing while reserving stage space for feet, jump peaks and controls.
  if(info.camera?.target){target.fromArray(info.camera.target);target.y=.9;}if(info.camera?.position){const offset=new THREE.Vector3().fromArray(info.camera.position).sub(new THREE.Vector3().fromArray(info.camera.target||[0,1.08,0]));yaw=Math.atan2(offset.x,offset.z);}distance=4.3;pitch=.035;
  paintAnimations();let saved;if(hosted){const early=shop.source==='init'?shop.applied:undefined;if(early)colorOverrides=hostColors(COLOR_CHANNELS,early);saved=hostItems(early,!early);}else try{if(!reviewMode)saved=JSON.parse(localStorage.getItem(STORE)||'null');}catch{}
  applyLook(saved||info.defaultLook||{},{persist:!hosted&&!reviewMode});if(hosted){shop.markApplied();paintShop(document,shop);}setAnimation('idle');if(!action&&clips.length){const first=(info.animations||[])[0];setAnimation(typeof first==='string'?first:first?.id);}
  // Bake on Apply (bake.js): its own renderer, camera and mixer; every frame hands the visible pose back.
  const settle=()=>{for(const update of expansionUpdates)update(0,state.look);avatar.updateMatrixWorld(true);};
  window.wardrobeDebug.bake=installBaker({THREE,scene,avatar,clips,getClip,hide:[ground],prepare:finishTransition,sync:settle,restore:()=>{mixer.update(0);settle();},post:postHost});
  state.ready=true;postHost({type:'rain-ready'});$('demo').disabled=false;$('loading').hidden=true;updateCamera(1/60);
  if(!clips.length){errors.push('No animation clips in avatar.glb');$('liveLabel').textContent='NO ANIMATIONS EXPORTED';}
  if(reviewMode)installReviewStudio({info,errors,renderCase:renderReviewCase});
}
function renderReviewCase({look,clip,time,view,frame,width:rw,height:rh}){
  state.paused=true;finishTransition();mixer.stopAllAction();action=mixer.clipAction(getClip(clip));action.reset().stopFading().stopWarping().setEffectiveWeight(1).play();state.animation=clip;mixer.setTime(time);applyLook(look,{persist:false});for(const update of expansionUpdates)update(0,state.look);avatar.updateMatrixWorld(true);
  const spec={head:[1.44,1.48],upper:[1.13,2.35],full:[.94,3.65],arms:[1.08,2.8],legs:[.35,1.65],feet:[.19,.9],hem:[.68,1.15]}[frame]||[.94,3.65];
  const aim=new THREE.Vector3(0,spec[0],0);let zoom=spec[1];
  if(frame==='arms'||frame==='legs'&&look.ankles&&look.ankles!=='none'){
    const slot=frame==='arms'?'arms':'ankles',piece=findItem(slot,look[slot]),box=new THREE.Box3(),p=new THREE.Vector3();
    for(const name of piece?.nodes||[])nodes.get(name)?.traverse(mesh=>{if(!mesh.isMesh||/-1$/.test(mesh.name))return;const a=mesh.geometry.attributes.position;for(let i=0;i<a.count;i++){mesh.getVertexPosition(i,p).applyMatrix4(mesh.matrixWorld);box.expandByPoint(p);}});
    if(!box.isEmpty()){box.getCenter(aim);const size=box.getSize(new THREE.Vector3());zoom=Math.max(.38,Math.max(size.y,size.x*rh/rw)*2.6);}
  }
  const rc=new THREE.PerspectiveCamera(32,rw/rh,.01,100);rc.position.set(aim.x+Math.sin(view)*zoom,aim.y+.035*zoom,aim.z+Math.cos(view)*zoom);rc.lookAt(aim);renderer.setPixelRatio(1);renderer.setSize(rw,rh,false);renderer.setScissorTest(false);renderer.setViewport(0,0,rw,rh);renderer.setClearColor(0xeee9df,1);renderer.clear();renderer.render(scene,rc);return renderer.domElement;
}
window.addEventListener('rain-locks',()=>{if(state.ready){paintChoices();paintShop(document,shop);}});
// Shop layer: the draft is what Rain wears here, the applied look is what the tube wears.
function shopSync(save){if(!state.demo)shop.setDraft(toHostLook(state.look,colorOverrides));if(save&&!shop.applyMode&&shop.status().wearable){postLook(state.look,colorOverrides);shop.markApplied();$('saveStatus').textContent=SHOP.saved;}paintShop(document,shop);}
function showHostLook(look){colorOverrides=hostColors(COLOR_CHANNELS,look);applyLook(hostItems(look,shop.source!=='init')||info?.defaultLook||{},{persist:false});shop.markApplied();paintShop(document,shop);}
if(hosted){
  document.body.classList.add('shop-on');$('lookBar').hidden=false;
  fetch('./items.json',{cache:'no-store'}).then(r=>r.ok?r.json():null).then(json=>{if(!json)return;shop.setItems(json);if(state.ready){paintChoices();paintShop(document,shop);}}).catch(()=>{});
  listenToHost(window,shop,(kind,data)=>{if(kind==='init'&&state.ready&&shop.applied){showHostLook(shop.applied);return;}if(kind==='applied')$('saveStatus').textContent=SHOP.applied;
    // A host clamp sends the look it would wear back as the draft (never applied, never baked).
    if(kind==='apply-failed'&&state.ready&&data&&data.look&&typeof data.look==='object'){stopDemo(false);colorOverrides=hostColors(COLOR_CHANNELS,data.look);applyLook(hostItems(data.look,false)||info?.defaultLook||{},{persist:false});}if(state.ready)paintChoices();paintShop(document,shop);});
  $('applyLook').addEventListener('click',()=>{if(!shop.apply())return;if(!shop.applyMode){postLook(state.look,colorOverrides);$('saveStatus').textContent=SHOP.saved;}paintShop(document,shop);});
  $('discardLook').addEventListener('click',()=>{stopDemo(false);const look=shop.discard();if(look&&state.ready)showHostLook(look);});
  $('openCounter').addEventListener('click',()=>shop.openCounter());
}
init().catch(fail);
