import * as THREE from 'three';
import { LITE } from '../perf.js';
import { createControls } from './controls.js';
import { expoGuideText, expoInteractionText } from './controller-menu.js';
import { createButtonTravel } from './button-travel.js';
import { mountJumpButton } from './jump-button.js';
import { createWalker } from './collision.js';
import { assetURL, createZones, loadPlan } from './zones.js';
import { createPost } from './post.js';
import { createSound } from './audio.js';
import { createExhibits } from './exhibits.js';
import { createExpoMedia } from './media-panes.js';
import { createDomeEffects } from './dome-effects.js';
import { groupPhotograph } from './group-photo.js';
import { createNarration } from './narration.js';
import { createInstruments } from './instruments.js';

const CSS = `
.expo-root {position:absolute;inset:0;background:#152a2d;overflow:hidden;touch-action:none;pointer-events:auto;outline:none;color:white;font:500 16px/1.35 system-ui}
.expo-canvas {width:100%;height:100%;display:block;touch-action:none}
.expo-caption,.expo-prompt,.expo-guide {position:absolute;left:50%;transform:translateX(-50%);pointer-events:none;text-align:center;margin:0}
.expo-caption {top:max(18px,env(safe-area-inset-top));left:18px;transform:none;width:max-content;max-width:min(55%,500px);text-align:left;background:#edf3ebf5;border-left:4px solid #258375;color:#163137;padding:.48em .75em;font-size:clamp(14px,1.3vw,17px);text-wrap:pretty}
.expo-prompt {top:calc(50% + 22px);padding:.28em .65em;background:#10272be8;border-radius:3px;font-size:14px}
.expo-aim {position:absolute;top:50%;left:50%;width:4px;height:4px;background:#fafaf2;box-shadow:0 0 2px 1px #27292782;border-radius:50%;pointer-events:none}
.expo-guide {bottom:max(18px,env(safe-area-inset-bottom));color:#fff;background:#10272be0;padding:.38em .8em;border-radius:2px;font-size:13px;max-width:80%;width:max-content}
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
@media(pointer:coarse),(max-width:660px){.expo-narration{bottom:max(108px,env(safe-area-inset-bottom));width:94%}.expo-narration p{font-size:clamp(17px,4vw,22px)}}
@media(max-height:480px){.expo-narration{bottom:5px;width:min(78%,880px)}.expo-narration p{font-size:16px}}
@media(pointer:coarse),(max-width:660px){.expo-caption{top:max(52px,env(safe-area-inset-top));left:10px;max-width:min(85%,450px)}.expo-guide{bottom:154px}}
`;

/** Construction is synchronous so panic can release the renderer while the first room is still downloading. */
export function createExpo(ctx, parent, { onExit, arrival = false, initialInputMode, onInputMode } = {}) {
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
    ctx.css(CSS);
    const element = (tag, cls) => { const e = doc.createElement(tag); e.className = cls; root.append(e); return e; };
    const canvas = element('canvas', 'expo-canvas');
    const guide = element('div', 'expo-guide'), caption = element('p', 'expo-caption');
    const aim = element('div', 'expo-aim'), prompt = element('div', 'expo-prompt');
    const stepDown=element('button','expo-step-down');stepDown.type='button';stepDown.hidden=true;
    stepDown.textContent='Step down';
    caption.setAttribute('aria-live', 'polite'); aim.setAttribute('aria-hidden', 'true');
    let inputMode = initialInputMode || (win.matchMedia('(pointer:coarse)').matches ? 'touch' : 'keyboard');
    let guideUntil = 7;
    guide.textContent = expoGuideText(inputMode);
    function inputChanged(next) {
      inputMode = next;
      onInputMode?.(next);
      const text = expoGuideText(next);
      if (guide.textContent !== text) { guide.textContent = text; guide.hidden = false; guideUntil = elapsed + 7; }
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
    const post = createPost(renderer, LITE); clean.push(() => post.dispose());
    const scene = new THREE.Scene(); scene.background = new THREE.Color('#dbe4de');
    const camera = new THREE.PerspectiveCamera(LITE ? 65 : 58, 1, .08, 140);
    camera.rotation.order = 'YXZ';
    const sound = createSound(ctx); clean.push(() => sound.dispose());
    const walker = createWalker(camera, sound.footstep, id=>narration?.planter?.(id)); clean.push(() => walker.dispose());
    const buttons=createButtonTravel(ctx);
    const targets = new Map(), pointer = new THREE.Vector2(), ray = new THREE.Raycaster(), hits = [];
    let targetMeshes = [], maxReach = 0, zones, exhibits, dome, controls, narration, returnButton, targetTime = 0, loading = false;
    function register(mesh, label, callback, reach = 2.3) {
      const {roots,activate}=buttons.bind(mesh,callback);
      const action={label,reach,owner:mesh,callback:activate};
      for(const target of roots)if(target===mesh||!targets.has(target))targets.set(target,action);
      targetMeshes=[...targets.keys()];maxReach=Math.max(maxReach,reach);
      return ()=>{for(const target of roots)if(targets.get(target)===action)targets.delete(target);targetMeshes=[...targets.keys()];};
    }
    function targetAt(point) {
      if (!ready || ctx.paused || dead || loading || returnButton || doc.hidden) return null;
      camera.updateMatrixWorld();
      pointer.set(point?.x || 0, point?.y || 0); ray.setFromCamera(pointer, camera); ray.far = maxReach;
      hits.length = 0; ray.intersectObjects(targetMeshes, true, hits);
      for (const hit of hits) {
        let action = null, visible = true;
        for (let n = hit.object; n; n = n.parent) { visible &&= n.visible; action ||= targets.get(n); }
        for (let n = action?.owner; visible && n; n = n.parent) visible = n.visible;
        if (visible && action && hit.distance <= action.reach && !walker.blocked(ray.ray, hit.distance)) return { action, hit };
      }
      return null;
    }
    function interact(point) {
      if (!ctx.paused && !doc.hidden && walker.stepOffPlanter()) { updatePrompt(); return true; }
      const target = targetAt(point);
      if (target) target.action.callback(target.hit);
      return !!target;
    }
    function updatePrompt(){
      stepDown.hidden=!walker.planter.released;
      if (!stepDown.hidden) { prompt.textContent = inputMode === 'gamepad' ? expoInteractionText('Step down', inputMode) : 'E to step down'; return; }
      const action = targetAt()?.action;
      const label = action ? (typeof action.label === 'function' ? action.label() : action.label) : '';
      prompt.textContent = expoInteractionText(label, inputMode);
    }
    stepDown.addEventListener('click',()=>{if(interact())root.focus({preventScroll:true});});
    function say(text) {
      if (dead || !text) return;
      caption.textContent = text;
      captionUntil = elapsed + Math.max(4, text.length / 15);
    }
    function farewell(leave) {
      if (dead || returnButton) return;
      controls.reset();
      const panel=element('section','expo-farewell'),title=doc.createElement('h2');
      title.textContent='Leaving Llama Expo 2037';panel.setAttribute('aria-label',title.textContent);
      returnButton=doc.createElement('button');returnButton.type='button';returnButton.className='expo-return-recap';
      returnButton.textContent='Return to recap';returnButton.addEventListener('click',()=>{if(!ctx.paused&&!doc.hidden)leave();});
      panel.append(title,returnButton);root.classList.add('expo-leaving');returnButton.focus({preventScroll:true});
    }
    function entered() {
      if (walker.zone === lastZone) return;
      lastZone = walker.zone; sound.setRoom(lastZone); exhibits.enter?.(lastZone);instruments.enter(lastZone);
    }
    let arrivalStart = null;
    function draw() {
      if (!ready || dead) return;
      renderer.info.reset();
      const calm = ctx.gentle || ctx.reduced;
      const sinceArrival = arrivalStart === null ? -1 : (ctx.activeMs - arrivalStart)/1000;
      post.draw(scene, camera, calm, dome?.weather, calm ? -1 : sinceArrival);
      if (sinceArrival >= 1.25) arrivalStart = null;
      media?.draw();
    }
    function capturePhoto() {
      const gallery=zones?.resident.get('twelve')?.view;
      if(!gallery || exhibits.state?.file?.herd.length!==12)return null;
      const photo=groupPhotograph({gallery,scene,renderer,doc,draw});
      if(photo)ctx.store.set('groupPhoto',photo);
      return photo;
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
    narration=createNarration({ctx,camera,scene,root,register,walker,sound,assetURL,lite:LITE,owned:()=>exhibits.state.file.herd});
    const instruments=createInstruments({ctx,camera,register,play:sound.play,meter:narration.meter,lite:LITE});
    exhibits = createExhibits({ THREE, ctx, camera, scene, root, say, cue:narration.cue, play: sound.play, setSoundDepth: sound.setDepth,
      go, exit: onExit, farewell, register, assetURL, bindMedia, capturePhoto, setColliderEnabled:walker.setColliderEnabled,
      setButtonAvailable:buttons.setAvailable });
    dome = createDomeEffects({ctx,camera,cue:narration.cue,play:sound.play,register,bindMedia,setSoundDepth:sound.setDepth,lite:LITE});
    const rooms = {
      mount(id, group) { buttons.mount(id,group);exhibits.mount?.(id,group); dome.mount(id,group); narration.mount(id,group);instruments.mount(id,group); },
      unmount(id, group) { instruments.unmount(id);narration.unmount(id); dome.unmount(id); exhibits.unmount?.(id,group);buttons.unmount(id); }
    };
    clean.push(() => { instruments.dispose();narration.dispose(); dome.dispose(); media?.dispose(); exhibits.dispose?.(); targets.clear(); });
    controls = createControls({ root, canvas, camera, initialInputMode:inputMode, onInputMode:inputChanged,
      move: walker.move, onInteract: interact, onJump: () => walker.jump?.(),
      onPause: () => { if (!ctx.paused) doc.getElementById('wake')?.click(); },
      isPaused: () => !ready || dead || loading || !!returnButton || ctx.paused || doc.hidden });
    clean.push(() => controls.dispose());
    clean.push(mountJumpButton(root, () => {
      if (ready && !ctx.paused && !dead && !loading && !returnButton && !doc.hidden) walker.jump();
    }));
    const drawingSize = new THREE.Vector2();
    const resize = () => {
      const { width:w, height:h } = root.getBoundingClientRect();
      if (!w || !h || dead) return;
      renderer.setSize(w, h, false); renderer.getDrawingBufferSize(drawingSize);
      post.resize(drawingSize.x, drawingSize.y); camera.aspect = w/h; camera.updateProjectionMatrix(); draw();
    };
    resize(); const observer = new ResizeObserver(resize); observer.observe(root); clean.push(() => observer.disconnect());
    let previous = ctx.activeMs, paintTime = 0;
    function sync() {
      const hold = ctx.paused || doc.hidden;
      previous = ctx.activeMs; controls.reset(); media?.pause(hold); sound.pause(hold); narration.pause(hold); exhibits.pause?.(hold);
      if (!hold && ready) (returnButton||root).focus({ preventScroll: true });
    }
    for (const key of ['pause','resume','suspend']) clean.push(ctx.on(key, sync));
    doc.addEventListener('visibilitychange', sync); clean.push(() => doc.removeEventListener('visibilitychange', sync));
    clean.push(ctx.frame(() => {
      const now = ctx.activeMs, dt = Math.max(0, Math.min(1, (now-previous)/1000)); previous = now;
      if (!ready || dead || ctx.paused || doc.hidden) return;
      elapsed += dt; controls.step(dt); walker.step(dt); entered(); sound.setListener(walker.feet);
      zones.stream(dt); exhibits.tick?.(dt, walker.zone); dome.step(dt,walker.zone); narration.tick(dt);instruments.tick(dt,walker.zone);buttons.tick(dt);
      if(dt<.12 && dt>0){budgetTime+=dt;budgetFrames++;}
      if(budgetTime>=1.75){
        const average=budgetTime/budgetFrames,goal=LITE?1/30:1/60;
        const next=average>goal*1.28?Math.max(nativeRatio*.62,pixelRatio*.86):
          average<goal*.80?Math.min(nativeRatio,pixelRatio*1.06):pixelRatio;
        if(Math.abs(next-pixelRatio)>.02){pixelRatio=next;renderer.setPixelRatio(pixelRatio);resize();}
        budgetTime=0;budgetFrames=0;
      }
      if (elapsed > captionUntil) caption.textContent = '';
      if (elapsed > guideUntil) guide.hidden = true;
      targetTime -= dt;
      if (targetTime <= 0) {
        updatePrompt();
        targetTime = .09;
      }
      paintTime += dt;
      if (!LITE || paintTime >= 1/30) { paintTime %= 1/30; draw(); }
    }));
    const firstFrame = (async () => {
      const plan = await loadPlan(abort.signal);
      if (dead) return;
      zones = createZones({ scene, renderer, walker, exhibits:rooms, plan, lite: LITE, report: error => console.warn(error) });
      clean.push(() => zones.dispose());
      await zones.ensure(plan.start.id);
      if (dead) return;
      const { id, at, yaw } = plan.start; walker.place(id, at, yaw); ready = true; entered(); resize(); sync();
    })();
    const api = { ready: firstFrame, dispose, beginArrival() {
      if (arrival && !dead) { arrival = false; arrivalStart = ctx.activeMs; draw(); }
    } };
    if (['localhost','127.0.0.1','[::1]'].includes(win.location.hostname)) Object.assign(api, {
      root, camera, scene, renderer, interact, draw, go, warp: go, exhibits, walker, narrationState:narration.debug,plantState:instruments.debug,footfalls:sound.footfalls,buttons:buttons.debug,
      focusTarget:()=>{
        const target=targetAt();if(!target)return null;
        const lineage=[];for(let n=target.hit.object;n;n=n.parent)if(n.name)lineage.push(n.name);
        return {owner:target.action.owner.name,lineage,distance:target.hit.distance};
      },
      deepen: seconds => { if (!ctx.paused && walker.zone === 'dome') { dome.step(Math.min(300,Math.max(0,seconds)),'dome'); draw(); } },
      state: () => ({ zone: walker.zone, feet: walker.feet.toArray(), surface: walker.surface, elapsed, ready,
        loaded: [...(zones?.resident.keys() || [])], textures:zones?.textureStats(), calls: renderer.info.render.calls, pixelRatio, depth:dome.state.depth, phase:dome.state.phase, seconds:dome.state.seconds, planter:walker.planter.state(), ...exhibits.debug?.() }),
      lookAt: (x,y,z) => { camera.lookAt(x,y,z); camera.rotation.z = 0; updatePrompt(); draw(); },
      move: (x,z) => { if (!ready || ctx.paused || dead) return; walker.move(x,z); walker.step(1); entered(); draw(); },
    });
    return api;
  } catch (error) { dispose(); throw error; }
}
