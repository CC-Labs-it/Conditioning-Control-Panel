import * as THREE from 'three';
import { LITE } from '../perf.js';
import { createControls } from './controls.js';
import { createWalker } from './collision.js';
import { assetURL, createZones, loadPlan } from './zones.js';
import { createPost } from './post.js';
import { createSound } from './audio.js';
import { createExhibits } from './exhibits.js';
import { createExpoMedia } from './media-panes.js';
import { createDomeEffects } from './dome-effects.js';
import { LLAMA_BY_ID } from '../../data/content/llamas.js';

const CSS = `
.expo-root {position:absolute;inset:0;background:#222c25;overflow:hidden;touch-action:none;pointer-events:auto;outline:none;color:white;font:500 16px/1.35 system-ui}
.expo-canvas {width:100%;height:100%;display:block;touch-action:none}
.expo-caption,.expo-prompt,.expo-guide {position:absolute;left:50%;transform:translateX(-50%);pointer-events:none;text-align:center;margin:0}
.expo-caption {bottom:max(74px,12%);width:max-content;max-width:min(82%,680px);background:#121813e8;padding:.5em .85em;font-size:clamp(14px,2vw,20px);text-wrap:balance}
.expo-prompt {top:calc(50% + 22px);padding:.28em .65em;background:#121813d9;border-radius:3px;font-size:14px}
.expo-aim {position:absolute;top:50%;left:50%;width:4px;height:4px;background:#fafaf2;box-shadow:0 0 2px 1px #27292782;border-radius:50%;pointer-events:none}
.expo-guide {bottom:max(18px,env(safe-area-inset-bottom));color:#fff;background:#152319ce;padding:.38em .8em;border-radius:2px;font-size:13px;max-width:80%;width:max-content}
.expo-prompt:empty,.expo-caption:empty {display:none}
@media(pointer:coarse),(max-width:660px){.expo-caption{bottom:138px;max-width:94%}.expo-guide{bottom:154px}.expo-guide:not(:empty)~.expo-caption{bottom:192px}}
`;

/** Construction is synchronous so panic can release the renderer while the first room is still downloading. */
export function createExpo(ctx, parent, { onExit } = {}) {
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
    caption.setAttribute('aria-live', 'polite'); aim.setAttribute('aria-hidden', 'true');
    guide.textContent = win.matchMedia('(pointer:coarse)').matches ? 'Drag to look. Use the stick to walk.' : 'WASD to walk. Click to look. E to interact. Esc to pause.';
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
    const camera = new THREE.PerspectiveCamera(LITE ? 65 : 58, 1, .045, 140);
    camera.rotation.order = 'YXZ';
    const sound = createSound(ctx); clean.push(() => sound.dispose());
    const walker = createWalker(camera, sound.footstep); clean.push(() => walker.dispose());
    const targets = new Map(), pointer = new THREE.Vector2(), ray = new THREE.Raycaster(), hits = [];
    let targetMeshes = [], maxReach = 0, zones, exhibits, dome, controls, targetTime = 0, loading = false;
    function register(mesh, label, callback, reach = 2.3) {
      targets.set(mesh, { label, callback, reach }); targetMeshes = [...targets.keys()]; maxReach = Math.max(maxReach, reach);
      return () => { targets.delete(mesh); targetMeshes = [...targets.keys()]; };
    }
    function targetAt(point) {
      if (!ready || ctx.paused || dead || loading || doc.hidden) return null;
      camera.updateMatrixWorld();
      pointer.set(point?.x || 0, point?.y || 0); ray.setFromCamera(pointer, camera); ray.far = maxReach;
      hits.length = 0; ray.intersectObjects(targetMeshes, true, hits);
      for (const hit of hits) {
        let action = null, visible = true;
        for (let n = hit.object; n; n = n.parent) { visible &&= n.visible; action ||= targets.get(n); }
        if (visible && action && hit.distance <= action.reach && !walker.blocked(ray.ray, hit.distance)) return { action, hit };
      }
      return null;
    }
    function interact(point) {
      const target = targetAt(point);
      if (target) target.action.callback(target.hit);
      return !!target;
    }
    function updatePrompt(){
      const action = targetAt()?.action;
      prompt.textContent = action ? (typeof action.label === 'function' ? action.label() : action.label) : '';
    }
    function say(text, clipId) {
      if (dead || !text) return;
      caption.textContent = text; caption.dataset.lineId = clipId || '';
      captionUntil = elapsed + Math.max(4, text.length / 15);
      sound.speak(clipId);
    }
    function entered() {
      if (walker.zone === lastZone) return;
      lastZone = walker.zone; sound.setRoom(lastZone); exhibits.enter?.(lastZone);
    }
    function draw() {
      if (!ready || dead) return;
      renderer.info.reset();
      post.draw(scene, camera, ctx.gentle || ctx.reduced, dome?.weather);
      media?.draw();
    }
    function capturePhoto() {
      const gallery=zones?.resident.get('twelve')?.view, herd=exhibits.state?.file?.herd || [];
      if(!gallery || herd.length!==12)return null;
      const models=herd.map(id=>gallery.getObjectByName('statue_'+id));
      if(models.some(model=>!model || !model.visible))return null;
      const out=doc.createElement('canvas');out.width=840;out.height=554;
      const g=out.getContext('2d');g.fillStyle='#eae6d6';g.fillRect(0,0,out.width,out.height);
      g.fillStyle='#173e31';g.fillRect(0,0,out.width,43);
      g.fillStyle='#f2eddb';g.font='22px Georgia';g.fillText('The Hall of the Twelve',18,29);
      g.textAlign='right';g.font='12px system-ui';g.fillText('LLAMA EXPO 2037',822,27);g.textAlign='left';
      const plate=new THREE.PerspectiveCamera(42,canvas.width/canvas.height,.035,80);
      const centre=new THREE.Vector3(),size=new THREE.Vector3(),toward=new THREE.Vector3();
      const width=196,height=138,ratio=width/height;
      const sw=Math.min(canvas.width,canvas.height*ratio),sh=sw/ratio;
      try {
        models.forEach((model,i)=>{
          const box=new THREE.Box3().setFromObject(model,true);box.getCenter(centre);box.getSize(size);
          toward.set(-centre.x,0,-26.1-centre.z).normalize();
          const distance=Math.max(size.y*1.27,Math.max(size.x,size.z)*1.22/ratio)/(2*Math.tan(Math.PI*plate.fov/360));
          plate.position.copy(centre).addScaledVector(toward,distance);plate.position.y+=size.y*.11;
          plate.lookAt(centre);plate.updateMatrixWorld();
          post.draw(scene,plate,true);
          const x=16+(i%4)*204,y=54+Math.floor(i/4)*164;
          g.drawImage(canvas,(canvas.width-sw)/2,(canvas.height-sh)/2,sw,sh,x,y,width,height);
          g.fillStyle='#284437';g.font='13px Georgia';g.fillText(LLAMA_BY_ID[herd[i]].name,x+2,y+154,width-4);
        });
      } finally { draw(); }
      const photo=out.toDataURL('image/webp',.75);ctx.store.set('groupPhoto',photo);return photo;
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
    exhibits = createExhibits({ THREE, ctx, camera, scene, root, say, play: sound.play, setSoundDepth: sound.setDepth,
      go, exit: onExit, register, assetURL, bindMedia, capturePhoto, setColliderEnabled:walker.setColliderEnabled });
    dome = createDomeEffects({ctx,camera,say,play:sound.play,register,bindMedia,setSoundDepth:sound.setDepth,lite:LITE});
    const rooms = {
      mount(id, group) { exhibits.mount?.(id,group); dome.mount(id,group); },
      unmount(id, group) { dome.unmount(id); exhibits.unmount?.(id,group); }
    };
    clean.push(() => { dome.dispose(); media?.dispose(); exhibits.dispose?.(); targets.clear(); });
    controls = createControls({ root, canvas, camera, move: walker.move, onInteract: interact,
      onPause: () => { if (!ctx.paused) doc.getElementById('wake')?.click(); },
      isPaused: () => !ready || dead || loading || ctx.paused || doc.hidden });
    clean.push(() => controls.dispose());
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
      previous = ctx.activeMs; controls.reset(); media?.pause(hold); sound.pause(hold); exhibits.pause?.(hold);
      if (!hold && ready) root.focus({ preventScroll: true });
    }
    for (const key of ['pause','resume','suspend']) clean.push(ctx.on(key, sync));
    doc.addEventListener('visibilitychange', sync); clean.push(() => doc.removeEventListener('visibilitychange', sync));
    clean.push(ctx.frame(() => {
      const now = ctx.activeMs, dt = Math.max(0, Math.min(1, (now-previous)/1000)); previous = now;
      if (!ready || dead || ctx.paused || doc.hidden) return;
      elapsed += dt; controls.step(dt); walker.step(dt); entered(); zones.stream(dt); exhibits.tick?.(dt, walker.zone); dome.step(dt,walker.zone);
      if(dt<.12 && dt>0){budgetTime+=dt;budgetFrames++;}
      if(budgetTime>=1.75){
        const average=budgetTime/budgetFrames,goal=LITE?1/30:1/60;
        const next=average>goal*1.28?Math.max(nativeRatio*.62,pixelRatio*.86):
          average<goal*.80?Math.min(nativeRatio,pixelRatio*1.06):pixelRatio;
        if(Math.abs(next-pixelRatio)>.02){pixelRatio=next;renderer.setPixelRatio(pixelRatio);resize();}
        budgetTime=0;budgetFrames=0;
      }
      if (elapsed > captionUntil) caption.textContent = '';
      if (elapsed > 7) guide.hidden = true;
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
    const api = { ready: firstFrame, dispose };
    if (['localhost','127.0.0.1','[::1]'].includes(win.location.hostname)) Object.assign(api, {
      root, camera, scene, renderer, interact, draw, go, warp: go, exhibits, walker,
      deepen: seconds => { if (!ctx.paused && walker.zone === 'dome') { dome.step(Math.min(300,Math.max(0,seconds)),'dome'); draw(); } },
      state: () => ({ zone: walker.zone, feet: walker.feet.toArray(), surface: walker.surface, elapsed, ready,
        loaded: [...(zones?.resident.keys() || [])], textures:zones?.textureStats(), calls: renderer.info.render.calls, pixelRatio, depth:dome.state.depth, phase:dome.state.phase, seconds:dome.state.seconds, ...exhibits.debug?.() }),
      lookAt: (x,y,z) => { camera.lookAt(x,y,z); camera.rotation.z = 0; updatePrompt(); draw(); },
      move: (x,z) => { if (!ready || ctx.paused || dead) return; walker.move(x,z); walker.step(1); entered(); draw(); },
    });
    return api;
  } catch (error) { dispose(); throw error; }
}
