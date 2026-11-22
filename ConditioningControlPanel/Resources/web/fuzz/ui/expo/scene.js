import { souvenirPrint } from './souvenir-print.js';
import { exhibitProps } from './exhibit-props.js';
import * as THREE from 'three';
import { LITE } from '../perf.js';
import { createControls } from './controls.js';
import { createCameraPreferences } from './camera-preferences.js';
import { mountCameraOptions } from './camera-options.js';
import { expoGuideText, expoInteractionText } from './controller-menu.js';
import { createButtonTravel } from './button-travel.js';
import { pickExhibits } from './exhibit-picking.js';
import { mountExpoRadio, RADIO_STYLE } from './music-controls.js';
import { MOBILE_OVERLAY_STYLE } from './mobile-overlay-style.js';
import { createWalker } from './collision.js';
import {createRail} from './rail.js';
import { expoAllowed } from '../edition.js';
import { assetURL } from './asset-base.js';
import { createZones, loadPlan } from './zones.js';
import { createPost } from './post.js';
import { createExpoPaintClock } from './frame-pacing.js';
import { createSound } from './audio.js';
import { createMuseumMusic } from '../../audio/museum-music.js';
import { createExhibits } from './exhibits.js';

import { createExpoMedia } from './media-panes.js';
import { createDomeEffects } from './dome-effects.js';
import { groupPhotograph } from './group-photo.js';
import { createNarration } from './narration.js';
import { createVisitGuide } from './visit-guide.js';
import { TRAIL_STYLE } from './trail-style.js';
import { createInstruments } from './instruments.js';
import { createAmbience } from './ambience.js';
import { createCollection } from './collection.js';
import { createGuestCollection, guestInscription } from './guest-collection.js';

const CSS = `
.expo-root {position:absolute;inset:0;background:#152a2d;overflow:hidden;touch-action:none;pointer-events:auto;outline:none;color:white;font:500 16px/1.35 system-ui}
.expo-canvas {width:100%;height:100%;display:block;touch-action:none}
.expo-caption,.expo-prompt,.expo-guide {position:absolute;left:50%;transform:translateX(-50%);pointer-events:none;text-align:center;margin:0}
.expo-caption {top:max(18px,env(safe-area-inset-top));left:18px;transform:none;width:max-content;max-width:min(55%,500px);text-align:left;background:#edf3ebf5;border-left:4px solid #258375;color:#163137;padding:.48em .75em;font-size:clamp(14px,1.3vw,17px);text-wrap:pretty}
.expo-prompt {top:calc(50% + 22px);padding:.28em .65em;background:#10272be8;border-radius:3px;font-size:14px}
.expo-aim {position:absolute;top:50%;left:50%;width:4px;height:4px;background:#fafaf2;box-shadow:0 0 2px 1px #27292782;border-radius:50%;pointer-events:none}
.expo-aim[data-action]{width:12px;height:12px;box-sizing:border-box;transform:translate(-50%,-50%) rotate(45deg);border:2px solid #172b2b;outline:1.5px solid #fff2d4;border-radius:2px;background:#f66b3c;box-shadow:none}
.expo-root[data-free-look] .expo-aim{visibility:hidden}
.expo-root[data-free-look] .expo-canvas{cursor:grab}
.expo-root[data-free-look]:has(.expo-aim[data-action]) .expo-canvas{cursor:pointer}
.expo-root[data-free-look] .expo-canvas:active{cursor:grabbing}
.expo-root[data-free-look] .expo-prompt{top:auto;bottom:22%;max-width:80%}
.expo-aim[data-pressed]{background:#fff0c4;border-color:#ad4328}
.expo-aim[data-pressed=full]{width:15px;height:15px}
.expo-guide {position:static;transform:none;color:#fff;background:#10272be0;padding:.38em .8em;border-radius:2px;font-size:13px;max-width:100%;width:fit-content}
.expo-prompt:empty,.expo-caption:empty {display:none}
.expo-step-down {position:absolute;left:50%;bottom:max(20%,95px);transform:translateX(-50%);color:white;background:#153f40;border:1px solid #bdd1b9;border-radius:4px;padding:.62em 1.15em;font:600 16px system-ui;pointer-events:auto}
.expo-narration{position:absolute;z-index:2;bottom:max(30px,7%,env(safe-area-inset-bottom));left:50%;transform:translateX(-50%);width:min(88%,820px);display:grid;justify-items:center;gap:3px;pointer-events:none}
.expo-narration p{margin:0;padding:.33em .68em;color:#fff;background:#102124f5;text-align:center;text-wrap:pretty;border-radius:3px;font-size:clamp(19px,1.9vw,28px);line-height:1.31}
.expo-narration p[data-channel=pa]{color:#e0efbe}
.expo-narration p:empty{display:none}
.expo-farewell{position:absolute;inset:0;display:grid;align-content:center;justify-items:center;gap:14px;background:radial-gradient(ellipse at center,#11261c29,#0c1b16c2);color:#fff;pointer-events:auto}
.expo-farewell h2{margin:0;padding:.20em .55em;background:#123037ed;font:600 clamp(24px,3.3vw,40px)/1.16 system-ui}
.expo-return-recap{padding:.55em 1.15em;border:1px solid #afcdb7;border-radius:3px;background:#e4efdb;color:#153537;font:600 16px/1.25 system-ui;cursor:pointer}
.expo-return-recap:focus-visible{outline:3px solid white;outline-offset:4px}
.expo-leaving .expo-aim,.expo-leaving .expo-prompt,.expo-leaving .expo-guide,.expo-leaving .expo-touch{display:none!important}

.expo-shop-receipt{position:absolute;z-index:3;right:2%;top:12%;width:min(305px,76%);max-height:58%;overflow:auto;background:#fff6de;border-top:5px double #506450;color:#233a32;padding:12px 16px;font:13px/1.35 Consolas,monospace;touch-action:pan-y}.expo-shop-receipt h3{margin:0 0 8px;font:bold 20px Georgia}.expo-shop-receipt p{display:flex;justify-content:space-between;gap:10px;margin:4px 0}.expo-shop-receipt b{text-align:right}.expo-shop-receipt [hidden]{display:none}
.expo-reading-card{position:absolute;z-index:4;top:max(12px,env(safe-area-inset-top));left:max(12px,env(safe-area-inset-left));box-sizing:border-box;width:min(465px,calc(100% - 24px));max-height:62%;overflow:auto;padding:17px 22px 15px;border:1px solid #a29d86;border-top:4px solid #476e60;border-radius:3px;background:#f1ecda;color:#173135;box-shadow:0 4px 20px #12252955;touch-action:pan-y}
.expo-reading-card[hidden],.expo-root:has(.expo-reading-card:not([hidden])) .expo-route-guide{display:none}
.expo-reading-card p,.expo-reading-card h2{margin:0}.expo-reading-card .expo-reading-date{font:600 13px/1.3 "Segoe UI",sans-serif;letter-spacing:.015em;color:#3c5951}
.expo-reading-card p:empty{display:none}
.expo-reading-card h2{font:bold clamp(23px,2.2vw,34px)/1.12 Georgia,serif;padding:6px 0 9px;border-bottom:1px solid #a8b6a3}
.expo-reading-card h2+p{margin:9px 0 10px;font:500 clamp(16px,1.3vw,19px)/1.43 "Segoe UI",sans-serif;white-space:pre-line}
.expo-reading-card button{font:650 14px/1.2 "Segoe UI",sans-serif;padding:6px 13px;border:1px solid #a55a3b;border-radius:3px;background:#f28b60;color:#242d2c;cursor:pointer}
@media(max-height:480px){.expo-reading-card{top:5px;left:5px;width:min(47%,450px);max-height:70%;padding:7px 12px}.expo-reading-card h2+p{font-size:15px;line-height:1.29}.expo-reading-card h2{padding:3px 0 4px}}

@media(pointer:coarse),(max-width:660px){.expo-narration{bottom:max(168px,calc(env(safe-area-inset-bottom) + 140px));width:94%}.expo-narration p{font-size:clamp(17px,4vw,22px)}}
@media(max-height:480px){.expo-narration{bottom:5px;width:min(78%,880px)}.expo-narration p{font-size:16px}}
@media(pointer:coarse) and (max-height:480px) and (min-width:640px){.expo-narration{left:calc(50% - 30px);width:calc(100% - 350px)}}
@media(pointer:coarse),(max-width:660px){.expo-caption{top:calc(max(8px,env(safe-area-inset-top)) + 52px);left:10px;max-width:min(85%,450px)}}
`;

/** Construction is synchronous so panic can release the renderer while the first room is still downloading. */
export function createExpo(ctx, parent, { onExit, arrival = false, initialInputMode, onInputMode } = {}) {
  if (!expoAllowed()) throw new Error('The Expo transmission is unavailable.');
  const doc = parent.ownerDocument, win = doc.defaultView, clean = [];
  const root = doc.createElement('div');
  root.className = 'expo-root'; root.tabIndex = -1; root.setAttribute('aria-label', 'Llama Expo 2037'); parent.append(root);
  let dead = false, ready = false, media = null, lastZone = '', elapsed = 0, captionUntil = 0;
  const abort = new AbortController(); clean.push(() => root.remove(), () => abort.abort());
  function dispose() {
    if (dead) return;
    dead = true;
    for (const fn of clean.reverse()) try { fn(); } catch (e) { console.warn(e); }
  }
  try {
    ctx.css(CSS+TRAIL_STYLE+MOBILE_OVERLAY_STYLE+RADIO_STYLE+
      '.expo-paged-lore .expo-full-line{position:absolute;clip-path:inset(50%);width:1px;height:1px;overflow:hidden}.expo-paged-lore::after{content:attr(data-phrase)}');
    root.toggleAttribute('data-calm',!!(ctx.gentle||ctx.reduced));
    const element = (tag, cls) => { const e = doc.createElement(tag); e.className = cls; root.append(e); return e; };
    const canvas = element('canvas', 'expo-canvas');
    const guide = element('div', 'expo-guide'), caption = element('p', 'expo-caption');
    const aim = element('div', 'expo-aim'), prompt = element('div', 'expo-prompt');
    const stepDown=element('button','expo-step-down');stepDown.type='button';stepDown.hidden=true;
    stepDown.textContent='Step down';
    caption.setAttribute('aria-live', 'polite'); aim.setAttribute('aria-hidden', 'true');
    let inputMode = initialInputMode || (win.matchMedia('(pointer:coarse)').matches ? 'touch' : 'keyboard');
    const cameraPrefs = createCameraPreferences(win);
    let controls, cameraMenu, guideUntil = 7;
    function refreshGuide() {
      const text = expoGuideText(inputMode, cameraPrefs.options, controls?.freePointer);
      if (guide.textContent !== text) { guide.textContent = text; guide.hidden = false; guideUntil = elapsed + 7; }
    }
    refreshGuide();
    function inputChanged(next) {
      inputMode = next;
      onInputMode?.(next);
      cameraMenu?.setMode(next);
      refreshGuide();
      updatePrompt();
    }
    const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
    clean.push(() => { renderer.dispose(); renderer.forceContextLoss(); });
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.info.autoReset = false;
    const nativeRatio=Math.min(win.devicePixelRatio || 1,LITE ? 1 : 1.5);
    let pixelRatio=nativeRatio,budgetTime=0,budgetFrames=0;
    renderer.setPixelRatio(pixelRatio);
    const post = createPost(renderer); clean.push(() => post.dispose());
    const scene = new THREE.Scene(); scene.background = new THREE.Color('#dbe4de');
    const camera = new THREE.PerspectiveCamera(LITE ? 65 : 58, 1, .08, 140);
    camera.rotation.order = 'YXZ';
    const sound = createSound(ctx); clean.push(() => sound.dispose());
    const visitorGuide=createVisitGuide(root,{
      onOpen:()=>controls?.reset(),canOpen:()=>ready&&!dead&&!loading&&!returnButton&&!ctx.paused&&!exhibits?.state.pressWatching
    });clean.push(()=>visitorGuide.dispose());
    let waitingForEntrance=arrival;
    const walker = createWalker(camera, sound.footstep, id=>narration?.planter?.(id),
      () => ctx.store.claimLifetime('planterDetour')); clean.push(() => walker.dispose());
    const buttons=createButtonTravel(ctx);
    const targets = new Map(), pointer = new THREE.Vector2(), ray = new THREE.Raycaster(), hits = [];
    let maxReach = 0, zones, exhibits, dome, narration, rail, returnButton, targetTime = 0, loading = false;
    function register(mesh, label, callback, reach = 2.3) {
      const {roots,activate}=buttons.bind(mesh,callback);
      const action={label,reach,owner:mesh,callback:activate};
      for(const target of roots)if(target===mesh||!targets.has(target))targets.set(target,action);
      maxReach=Math.max(maxReach,reach);
      return ()=>{for(const target of roots)if(targets.get(target)===action)targets.delete(target);};
    }
    function targetAt(point) {
      if (!ready || ctx.paused || dead || loading || returnButton || visitorGuide.opened || doc.hidden) return null;
      camera.updateMatrixWorld();
      pointer.set(point?.x || 0, point?.y || 0); ray.setFromCamera(pointer, camera); ray.far = maxReach;
      pickExhibits(targets, ray, hits);
      for (const hit of hits) {
        let action = null, visible = true;
        for (let n = hit.object; n; n = n.parent) { visible &&= n.visible; action ||= targets.get(n); }
        for (let n = action?.owner; visible && n; n = n.parent) visible = n.visible;
        if (visible && action && hit.distance <= action.reach && !walker.blocked(ray.ray, hit.distance,!!action.owner.userData.stockBay)) return { action, hit };
      }
      return null;
    }
    const placePoint=new THREE.Vector3();
    function floorPlacement(point){
      if(!exhibits?.hands.canPlace())return null;
      camera.updateMatrixWorld();pointer.set(point?.x||0,point?.y||0);ray.setFromCamera(pointer,camera);
      if(ray.ray.direction.y>=-.4)return null;
      const distance=(walker.feet.y-ray.ray.origin.y)/ray.ray.direction.y;
      if(distance<=0||distance>2.3||walker.blocked(ray.ray,distance))return null;
      return ray.ray.at(distance,placePoint);
    }
    let pressedUntil = 0, actionSerial=0;
    function interact(point) {
      if(rail?.welcome&&!ctx.paused&&!doc.hidden){say('Listen to the welcome');return true;}
      if(exhibits?.state.pressWatching&&!ctx.paused&&!doc.hidden){sound.play('press',.24);say('The exhibit is moving.');return true;}
      if (!ctx.paused && !doc.hidden && walker.stepOffPlanter()) { updatePrompt(); return true; }
      const target = targetAt(point);
      if (target) {
        if(exhibits.refuseHoof(target.action,target.hit))return true;
        // A press that does nothing leaves the reticle and the cap alone: its action answers false.
        if(target.action.callback(target.hit)!==false){
          actionSerial++;pressedUntil=elapsed+.19; aim.dataset.pressed=ctx.gentle||ctx.reduced?'gentle':'full';
        }
        rail?.tick(0);
      }
      if(!target && ready && !dead && !loading && !returnButton && !ctx.paused && !doc.hidden){const floor=floorPlacement(point);if(floor)return exhibits.hands.place(floor);}
      return !!target;
    }
    // Undefined aims at the reticle. Null means the free cursor is off the scene or dragging.
    function lookPoint() { return controls?.freePointer ? controls.aimPoint : undefined; }
    function updatePrompt(){
      const free = Boolean(controls?.freePointer);
      if (root.hasAttribute('data-free-look') !== free) {
        root.toggleAttribute('data-free-look', free);
        refreshGuide();
      }
      stepDown.hidden=!walker.planter.released;
      if (elapsed>=pressedUntil) aim.removeAttribute('data-pressed');
      if (!stepDown.hidden) {
        aim.setAttribute('data-action','');root.removeAttribute('data-hoof-target');
        controls?.setTouchAction('Step down');
        prompt.textContent=inputMode==='gamepad'?expoInteractionText('Step down',inputMode):'E to step down';
        return;
      }
      const aimPoint = lookPoint();
      const action = aimPoint === null ? null : targetAt(aimPoint)?.action;
      root.toggleAttribute('data-hoof-target',Boolean(action?.owner.userData.hoofPanel));
      prompt.toggleAttribute('data-loan-card',action?.owner.name==='First Press loan card');
      const label = action ? (typeof action.label === 'function' ? action.label() : action.label) : aimPoint !== null && floorPlacement(aimPoint)?'Set down '+exhibits.state.carry.label:'';
      controls?.setTouchAction(label,{
        spit:walker.zone==='commons',
        jump:walker.zone==='hall'&&exhibits?.state.firstPressCount===2&&Math.hypot(walker.feet.x,walker.feet.z+9)<4.4
      });
      aim.toggleAttribute('data-action', !!label);
      prompt.textContent = expoInteractionText(label, inputMode);
    }
    stepDown.addEventListener('click',()=>{if(interact())root.focus({preventScroll:true});});
    function say(text) {
      if (dead || !text) return;
      caption.textContent = text;
      captionUntil = elapsed + Math.max(4, text.length / 15);
    }
    function farewell(leave,label='Leaving Llama Expo 2037') {
      if (dead || returnButton) return;
      rail?.pause(true);controls.reset();
      const panel=element('section','expo-farewell'),title=doc.createElement('h2');
      title.textContent=label;panel.setAttribute('aria-label',title.textContent);
      returnButton=doc.createElement('button');returnButton.type='button';returnButton.className='expo-return-recap';
      returnButton.textContent='Return to recap';returnButton.addEventListener('click',()=>{if(!ctx.paused&&!doc.hidden)leave();});
      panel.append(title,returnButton);root.classList.add('expo-leaving');returnButton.focus({preventScroll:true});
    }
    function entered() {
      if (walker.zone === lastZone) return;
      lastZone = walker.zone; sound.setRoom(lastZone);
      exhibits.enter(lastZone,zones.resident.get(lastZone).environment.texture);instruments.enter(lastZone);
    }
    let arrivalStart = null;
    function draw(allRoomDetails = false) {
      if (!ready || dead) return;
      renderer.info.reset();
      const calm = ctx.gentle || ctx.reduced;
      const sinceArrival = arrivalStart === null ? -1 : (ctx.activeMs - arrivalStart)/1000;
      if (allRoomDetails !== true) zones.visibility.prepare();
      try { post.draw(scene, camera, calm, dome?.weather, calm ? -1 : sinceArrival); }
      finally { zones.visibility.restore(); }
      if (sinceArrival >= 1.25) arrivalStart = null;
      media?.draw();
    }
    function capturePhoto(visitor=null,mark=null) {
      const gallery=zones?.resident.get('twelve')?.view;
      if(!gallery || exhibits.state?.file?.herd.length!==12)return null;
      const photo=groupPhotograph({gallery,scene,renderer,doc,draw,visitor,mark,name:exhibits.state.file.name});
      if(photo)ctx.store.set('groupPhoto',photo);
      return photo;
    }
    async function finalPhoto(){
      if(dead)return;
      try{
        const portrait=await zones.ensure('twelve');
        if(!dead&&portrait)await annex.using(portrait.view,['annex_photo_visitor','annex_agenda'],capturePhoto);
      }catch(err){if(!dead)console.warn('Group photo',err);}
    }
    async function go(id, at, yaw = 0) {
      if (dead || !zones) return false;
      loading = true;
      try {
        await zones.ensure(id);
        if (dead) return false;
        walker.place(id, at, yaw); entered(); draw(); return true;
      } finally { loading = false; }
    }
    function bindMedia(dome) {
      media?.dispose(); media = dome ? createExpoMedia(ctx, root, camera, dome, { lite: LITE }) : null;
      return media;
    }
    narration=createNarration({ctx,camera,scene,root,register,walker,sound,assetURL,lite:LITE,owned:()=>exhibits.state.file.herd,
      viewing:entry=>rail?.view(entry),releaseView:entry=>rail?.clearView(entry),caps:{hold:buttons.setHeld,light:buttons.setLit},
      // The gift shop's screen stands beside the exit door. Once the sale is filed it waits for its button, so the
      // viewing rail never closes on a visitor who is on the way out.
      autoStart:zone=>!(zone==='shop'&&exhibits?.state.contract)});
    const voiceCaptions=root.querySelector('.expo-narration'),messages=element('div','expo-visitor-messages');
    messages.append(caption,voiceCaptions);voiceCaptions.prepend(guide);
    narration.pause(waitingForEntrance || ctx.paused || doc.hidden);
    const instruments=createInstruments({ctx,camera,register,play:sound.play,meter:narration.meter,lite:LITE});
    const ambience=createAmbience({ctx,renderer,lite:LITE,meter:narration.meter});
    const kit=exhibitProps(assetURL),annex=exhibitProps(assetURL,'visitor-services.glb');clean.push(()=>kit.dispose(),()=>annex.dispose());
    exhibits = createExhibits({ THREE, ctx, camera, scene, root, draw, say, walker, renderer, muffle:narration.muffle, setHeaterPower:sound.setHeaterPower, pauseMusic:on=>music.pause(on), signal:instruments.signal, props:kit.mount, annexProps:annex.mount, records:()=>narration.story?.completed||0, recordTotal:()=>narration.story?.total||0, voiceBusy:narration.busy,
      storeSouvenir:(obj,label)=>souvenirPrint({THREE,renderer,draw,ctx},obj,label), actionSerial:()=>actionSerial, cue:narration.cue, narrationAvailable:narration.available, closingLine:narration.closingLine, play: sound.play, setSoundDepth: sound.setDepth,
      go:(id,...args)=>rail.allows(id)?go(id,...args):Promise.resolve(false), exit: onExit, farewell, register, assetURL, bindMedia, capturePhoto, finalPhoto, setColliderEnabled:walker.setColliderEnabled,
      lookPoint, holdPressView:()=>controls?.stopMoving(),
      buttonAssembly:buttons.assemblyFor,setButtonAvailable:buttons.setAvailable,setButtonHeld:buttons.setHeld,setButtonLit:buttons.setLit,registerDoorLeaf:(...args)=>zones?.registerDoorLeaf(...args) });
    rail=createRail({ctx,scene,walker,state:exhibits.state,voice:()=>narration,play:sound.play});
    walker.setRail(rail);clean.push(()=>{walker.setRail(null);rail.dispose();});
    dome = createDomeEffects({ctx,camera,cue:narration.cue,play:sound.play,register,bindMedia,setSoundDepth:sound.setDepth,lite:LITE,
      visit:exhibits.state,finish:exhibits.endVisit,doorOpening:id=>zones?.doorOpening(id)??0});
    const guests=createGuestCollection(assetURL);
    const collection=createCollection({assetURL,walker,register,
      onLabel:(mesh,label)=>narration.registerPlaque(mesh,guestInscription(mesh,label)),
      onAnchor(name,anchor) { guests.mount(name,anchor); return () => guests.unmount(anchor); },
      onAssembly:group => ambience.fitCollection?.(group)});
    const rooms = {
      mount(id, group) { buttons.mount(id,group);exhibits.mount?.(id,group); dome.mount(id,group); narration.mount(id,group);instruments.mount(id,group);ambience.mount(id,group);collection.mount(id,group); },
      unmount(id, group) { collection.unmount(id);ambience.unmount(id);instruments.unmount(id);narration.unmount(id); dome.unmount(id); exhibits.unmount?.(id,group);buttons.unmount(id); }
    };
    clean.push(() => { instruments.dispose();narration.dispose(); dome.dispose(); media?.dispose(); exhibits.dispose?.(); targets.clear(); });
    clean.push(() => ambience.dispose());
    clean.push(() => { collection.dispose(); guests.dispose(); });
    visitorGuide.watch(exhibits.state.ticket);
    controls = createControls({ root, canvas, camera, trail:visitorGuide, initialInputMode:inputMode, onInputMode:inputChanged, cameraOptions:cameraPrefs.options,
      move: walker.move, spit:exhibits.spit, onInteract: interact, onJump: () => walker.jump?.(),
      holdingView:()=>exhibits.state.pressWatching,
      onPause: () => { if (!ctx.paused) doc.getElementById('wake')?.click(); },
      isPaused: () => !ready || dead || loading || !!returnButton || ctx.paused || doc.hidden });
    clean.push(() => controls.dispose());
    cameraMenu = mountCameraOptions(doc, cameraPrefs.options, (key, value) => {
      if (!cameraPrefs.set(key, value)) return;
      controls.applyCameraOptions();
      refreshGuide();
      updatePrompt();
    });
    cameraMenu.setMode(inputMode);
    clean.push(() => cameraMenu.dispose());

    const drawingSize = new THREE.Vector2();
    const sizeRenderer = () => {
      const { width:w, height:h } = root.getBoundingClientRect();
      if (!w || !h || dead) return false;
      renderer.setSize(w, h, false); renderer.getDrawingBufferSize(drawingSize);
      post.resize(drawingSize.x, drawingSize.y); camera.aspect = w/h; camera.updateProjectionMatrix();
      return true;
    };
    const resize = () => { if (sizeRenderer()) draw(); };
    resize(); const observer = new ResizeObserver(resize); observer.observe(root); clean.push(() => observer.disconnect());
    const paintDue = createExpoPaintClock(LITE);
    let previous = ctx.activeMs;
    function sync() {
      const hold = ctx.paused || doc.hidden;
      rail.pause(hold||!!returnButton);
      previous = ctx.activeMs; controls.reset(); media?.pause(hold); sound.pause(hold); narration.pause(hold || waitingForEntrance); exhibits.pause?.(hold);
      if (!hold) cameraMenu.close();
      if (!hold && ready) (returnButton||root).focus({ preventScroll: true });
    }
    for (const key of ['pause','resume','suspend']) clean.push(ctx.on(key, sync));
    doc.addEventListener('visibilitychange', sync); clean.push(() => doc.removeEventListener('visibilitychange', sync));
    clean.push(ctx.frame(() => {
      const now = ctx.activeMs, dt = Math.max(0, Math.min(1, (now-previous)/1000)); previous = now;
      if (!ready || dead || ctx.paused || doc.hidden) return;
      elapsed += dt; controls.step(dt); walker.step(dt); entered(); sound.setListener(walker.feet);
      zones.stream(dt); exhibits.tick?.(dt, walker.zone); dome.step(dt,walker.zone); narration.tick(dt);instruments.tick(dt,walker.zone);buttons.tick(dt);
      rail.tick(dt);music.setSpeaking(narration.speaking);
      ambience.tick(dt);collection.tick(dt,ctx.gentle||ctx.reduced);
      let resized=false;
      if(dt<.12 && dt>0){budgetTime+=dt;budgetFrames++;}
      if(budgetTime>=1.75){
        const average=budgetTime/budgetFrames,goal=LITE?1/30:1/60;
        const next=average>goal*1.28?Math.max(nativeRatio*.62,pixelRatio*.86):
          average<goal*.80?Math.min(nativeRatio,pixelRatio*1.06):pixelRatio;
        if(Math.abs(next-pixelRatio)>.02){pixelRatio=next;renderer.setPixelRatio(pixelRatio);resized=sizeRenderer();}
        budgetTime=0;budgetFrames=0;
      }
      if (elapsed > captionUntil) caption.textContent = '';
      if (elapsed > guideUntil) guide.hidden = true;
      targetTime -= dt;
      if (targetTime <= 0) {
        visitorGuide.update(narration.story,walker.zone,walker.feet,exhibits.state,elapsed,camera.rotation.y,inputMode);
        updatePrompt();
        targetTime = .09;
      }
      if (paintDue(dt,resized)) draw();
    }));
    const firstFrame = (async () => {
      const plan = await loadPlan(abort.signal);
      if (dead) return;
      // Trail directions are independent of projector playback.
      zones = createZones({ scene, renderer, walker, exhibits:rooms, plan, lite: LITE,ctx,camera,compileRoom:post.compileRoom,play:sound.play, report: error => console.warn(error) });
      clean.push(() => zones.dispose());
      await zones.ensure(plan.start.id);
      if (dead) return;
      const {id,at,yaw}=plan.start,first=await rail.firstArrival();
      if(dead)return;
      walker.place(id,first?[at[0],at[1],at[2]+1.35]:at,first?0:yaw);
      if(first)rail.greet();
      ready=true;entered();resize();sync();
    })();
    const music = createMuseumMusic(ctx, {
      document: doc, ready: firstFrame, root:assetURL('audio/radio/'), source: 'expo-music', label: 'Expo music', level: .22, speechLevel: .06,
    });
    clean.push(() => music.dispose(),mountExpoRadio(doc,music,event=>ctx.audio.armFromUserPress?.(event)));
    const api = { ready: firstFrame, dispose, beginArrival() {
      if (arrival && !dead) { arrival = false; arrivalStart = ctx.activeMs; draw(); }
    },finishArrival() {
      waitingForEntrance=false;
      if(!dead)narration.pause(ctx.paused || doc.hidden);
    } };
    if (['localhost','127.0.0.1','[::1]'].includes(win.location.hostname)) Object.assign(api, {
      root, camera, scene, renderer, interact, draw, go, warp: go, exhibits, walker, narrationState:narration.debug,musicState:music.debug,plantState:instruments.debug,ambience:ambience.debug,footfalls:sound.footfalls,buttons:buttons.debug,
      drawAllRoomDetails:()=>draw(true),
      focusTarget:()=>{
        const target=targetAt();if(!target)return null;
        const lineage=[];for(let n=target.hit.object;n;n=n.parent)if(n.name)lineage.push(n.name);
        return {owner:target.action.owner.name,lineage,distance:target.hit.distance};
      },
      doors:()=>zones.doors(),rails:rail.debug,
      deepen: seconds => { if (!ctx.paused && walker.zone === 'dome') { dome.step(Math.min(300,Math.max(0,seconds)),'dome'); draw(); } },
      state: () => ({ zone: walker.zone, feet: walker.feet.toArray(), surface: walker.surface, elapsed, ready,
        loaded: [...(zones?.resident.keys() || [])], textures:zones?.textureStats(), calls: renderer.info.render.calls, pixelRatio, depth:dome.state.depth, phase:dome.state.phase, seconds:dome.state.seconds, planter:walker.planter.state(), ...exhibits.debug?.() }),
      lookAt: (x,y,z) => { camera.lookAt(x,y,z); camera.rotation.z = 0; updatePrompt(); draw(); },
      move: (x,z) => { if (!ready || ctx.paused || dead) return; walker.move(x,z); walker.step(1); entered(); draw(); },
    });
    return api;
  } catch (error) { dispose(); throw error; }
}
