import { createExpoGamepad } from './gamepad.js';
import { createTouchActions } from './touch-actions.js';

const WALK_SPEED = 4;
const SPRINT_SPEED = 6;
const DOUBLE_TAP_MS = 300;
const LOOK_SPEED = 0.0025;
const PAD_YAW_SPEED = 2.25;
const PAD_PITCH_SPEED = 1.70;
const MOVE_KEYS = new Set(['KeyW', 'KeyA', 'KeyS', 'KeyD', 'ArrowUp', 'ArrowLeft', 'ArrowDown', 'ArrowRight']);
const UI = 'button,a,input,textarea,select,[contenteditable=true],[role=button]';
const EDITOR = 'input,textarea,select,[contenteditable=true],[role=slider],[role=menu],[role=listbox],[role=tablist]';
const keyCodes={w:'KeyW',a:'KeyA',s:'KeyS',d:'KeyD',e:'KeyE',q:'KeyQ',t:'KeyT',' ':'Space',Spacebar:'Space',
  Enter:'Enter',ArrowUp:'ArrowUp',ArrowDown:'ArrowDown',ArrowLeft:'ArrowLeft',ArrowRight:'ArrowRight'};
function codeFor(event){
  return event.code&&event.code!=='Unidentified'?event.code:keyCodes[event.key]||keyCodes[event.key?.toLowerCase()];
}

/** All input methods share the same camera and collision-aware walk. */
export function createControls({ root, canvas, camera, move, spit, trail, onInteract, onJump, onPause, onInputMode, initialInputMode, isPaused = () => false, holdingView = () => false }) {
  const doc = root.ownerDocument, win = doc.defaultView, keys = new Set(), off = [];
  const pad = createExpoGamepad(() => win.navigator.getGamepads?.());
  const primaryMode = () => win.matchMedia?.('(pointer:coarse)').matches ? 'touch' : 'keyboard';
  let inputMode = initialInputMode ?? primaryMode();
  let lastMoveKey = null, lastPressAt = 0, sprintKey = null;
  function used(mode) {
    if (inputMode === mode) return;
    inputMode = mode;
    root.toggleAttribute?.('data-expo-touch',mode==='touch');
    if(mode!=='touch')touch.close();
    onInputMode?.(mode);
  }
  let disposed = false, drag = null, stickId = null, stickX = 0, stickY = 0, locked = false;
  let releasing = false;
  const oldTouchAction = canvas.style.touchAction;
  canvas.style.touchAction = 'none';
  camera.rotation.order = 'YXZ';
  camera.rotation.z = 0;
  root.toggleAttribute?.('data-expo-touch',inputMode==='touch');
  function listen(target, event, fn, options) {
    target.addEventListener(event, fn, options);
    off.push(() => target.removeEventListener(event, fn, options));
  }
  const available = () => !disposed && !isPaused() && !doc.hidden;
  const active = () => available() && !trail?.opened;
  const touch=createTouchActions(root,{active,canMove:()=>active()&&!holdingView(),interact:onInteract,jump:onJump,spit});
  const {stick,knob,spitting}=touch;
  function focusGame(){root.focus({preventScroll:true});}
  const capture = (el, id) => { try { el.setPointerCapture(id); } catch {} };
  const release = (el, id) => { try { if (id != null && el.hasPointerCapture(id)) el.releasePointerCapture(id); } catch {} };
  function clearPointers() {
    const oldDrag = drag, oldStick = stickId;
    drag = null; stickId = null; stickX = stickY = 0;
    knob.style.transform = '';
    release(canvas, oldDrag?.id); release(stick, oldStick);
  }
  function clearKeys() {
    keys.clear();
    lastMoveKey = sprintKey = null;
  }
  function reset() {
    clearKeys(); clearPointers(); pad.reset();spitting?.cancel();touch.close();
    if (doc.pointerLockElement === canvas) {
      releasing = true;
      doc.exitPointerLock?.();
    }
  }
  function turn(yaw, pitch) {
    if (!active()||holdingView()) return;
    camera.rotation.y -= yaw;
    camera.rotation.x = Math.max(-1.45, Math.min(1.45, camera.rotation.x - pitch));
    camera.rotation.z = 0;
  }
  function look(dx,dy) { turn(dx*LOOK_SPEED,dy*LOOK_SPEED); }
  function point(event) {
    const r = canvas.getBoundingClientRect();
    return { x: (event.clientX - r.left) / r.width * 2 - 1, y: 1 - (event.clientY - r.top) / r.height * 2 };
  }
  function requestLock() {
    if (!active() || !canvas.requestPointerLock) return;
    try { canvas.requestPointerLock()?.catch?.(() => {}); } catch {}
  }
  listen(doc, 'pointerlockchange', () => {
    const wasLocked = locked;
    locked = doc.pointerLockElement === canvas;
    if (!locked && wasLocked) {
      clearKeys(); clearPointers();
      if (wasLocked && !releasing && active()) onPause?.();
    }
    releasing = false;
  });
  listen(doc, 'mousemove', event => {
    if (locked && (event.movementX || event.movementY)) {
      used('mouse'); look(event.movementX || 0,event.movementY || 0);
    }
  });
  function keyDown(event){
    if (!available() || event.ctrlKey || event.altKey || event.metaKey || event.target?.closest?.(EDITOR)) return;
    const code=codeFor(event);
    if(trail && (code==='KeyT'||(trail.opened&&event.key==='Escape'))){
      event.preventDefault();event.stopImmediatePropagation();
      if(!event.repeat){used('keyboard');trail.toggle();}
      return;
    }
    if(trail?.opened)return;
    if(holdingView()&&code!=='KeyE'&&code!=='Enter')return;
    if(code==='KeyQ'&&!event.target?.closest?.(UI)){
      used('keyboard');event.preventDefault();event.stopPropagation();if(!event.repeat)spitting?.begin('key');return;
    }
    if (MOVE_KEYS.has(code)) {
      used('keyboard');
      if(doc.activeElement!==root||!doc.hasFocus())focusGame();
      event.preventDefault();event.stopPropagation();
      if (!event.repeat && !keys.has(code)) {
        const now = win.performance.now();
        if (code === lastMoveKey && now - lastPressAt <= DOUBLE_TAP_MS) {
          sprintKey = code;
          lastMoveKey = null;
        } else {
          lastMoveKey = code;
          lastPressAt = now;
        }
        keys.add(code);
      }
      return;
    }
    if(event.target?.closest?.(UI)||!['KeyE','Enter','Space'].includes(code))return;
    used('keyboard');
    event.preventDefault();event.stopPropagation();
    if(!event.repeat){if(code==='Space')onJump?.();else onInteract();}
  }
  function keyUp(event) {
    const code = codeFor(event);
    keys.delete(code);
    if (code === sprintKey) sprintKey = null;
    if (code === 'KeyQ') spitting?.end('key');
  }
  listen(win,'keydown',keyDown,true);listen(win,'keyup',keyUp,true);
  // A launcher can retain focus on its document after its play button is removed.
  // Only accept its background keystrokes while the embedded visit is active.
  try{
    const host=win.frameElement?.ownerDocument;
    if(host){
      listen(host,'keydown',event=>{
        if(event.target===host.body||event.target===host.documentElement||event.target===win.frameElement)keyDown(event);
      },true);
      listen(host,'keyup',keyUp,true);
    }
  }catch{}
  listen(win, 'blur', reset);
  listen(win, 'gamepaddisconnected', event => {pad.disconnect(event.gamepad?.index);spitting?.cancel();});
  listen(doc, 'visibilitychange', () => { if (doc.hidden) reset(); });
  listen(canvas, 'pointerdown', event => {
    if (!active() || event.button !== 0 || drag) return;
    used(event.pointerType === 'touch' ? 'touch' : 'mouse');
    focusGame();
    event.preventDefault();
    if (!spitting?.touchHeld) touch.close();
    if (locked) { onInteract(); return; }
    drag = { id: event.pointerId, x: event.clientX, y: event.clientY, distance: 0, type: event.pointerType };
    capture(canvas, event.pointerId);
  });
  listen(canvas, 'pointermove', event => {
    if (locked || !drag || drag.id !== event.pointerId) return;
    const dx = event.clientX - drag.x, dy = event.clientY - drag.y;
    drag.distance += Math.hypot(dx, dy); drag.x = event.clientX; drag.y = event.clientY;
    look(dx, dy);
  });
  listen(canvas, 'pointerup', event => {
    if (!drag || drag.id !== event.pointerId) return;
    const ended = drag; drag = null; release(canvas, event.pointerId);
    if (!active() || ended.distance >= 6) return;
    onInteract(point(event));
    if (ended.type === 'mouse') requestLock();
  });
  listen(canvas, 'pointercancel', clearPointers);
  listen(canvas, 'lostpointercapture', event => { if (drag?.id === event.pointerId) drag = null; });
  listen(canvas, 'contextmenu', event => event.preventDefault());
  function moveStick(event) {
    if (stickId !== event.pointerId || !active()) return;
    const r = stick.getBoundingClientRect(), dx = event.clientX - r.left - r.width / 2, dy = event.clientY - r.top - r.height / 2;
    const travel=r.width/2-knob.offsetWidth/2, length=Math.hypot(dx,dy);
    const scale=length>travel?travel/length:1;
    stickX=dx*scale/travel;stickY=-dy*scale/travel;
    knob.style.transform = `translate(${dx * scale}px,${dy * scale}px)`;
  }
  listen(stick, 'pointerdown', event => {
    if (!active() || stickId != null) return;
    used('touch');
    event.preventDefault(); event.stopPropagation();
    stickId = event.pointerId; capture(stick, stickId); moveStick(event);
  });
  listen(stick, 'pointermove', moveStick);
  function endStick(event) {
    if (event.pointerId !== stickId) return;
    const id = stickId; stickId = null; stickX = stickY = 0; knob.style.transform = ''; release(stick, id);
  }
  listen(stick, 'pointerup', endStick);
  listen(stick, 'pointercancel', clearPointers);
  listen(stick, 'lostpointercapture', endStick);

  return {
    get inputMode() { return inputMode; },
    setTouchAction:touch.setAction,
    stopMoving(){clearKeys();clearPointers();spitting?.cancel();touch.close();},
    step(dt) {
      if (!available()) { reset(); return; }
      if (!Number.isFinite(dt) || dt <= 0) return;
      const seconds = Math.min(dt,.1);
      const focus = doc.activeElement;
      const padFocus = doc.hasFocus() && (trail?.opened || focus === root || focus === doc.body || focus === doc.documentElement ||
        (root.contains(focus) && !focus?.closest?.(UI+','+EDITOR)));
      if (!padFocus) pad.reset();
      const input = padFocus ? pad.poll() : null;
      if (padFocus && !input && inputMode === 'gamepad') used(primaryMode());
      if (input?.activity) used('gamepad');
      if(trail?.opened){
        if(input?.start)onPause?.();
        else if(input)trail.handlePad(input);
        return;
      }
      if(input?.trail&&trail){trail.toggle();return;}
      if (input?.back || input?.start) { onPause?.(); return; }
      if(holdingView()){if(input?.interact)onInteract();return;}
      if (input) {
        const sensitivity = Math.hypot(input.lookX,input.lookY);
        if (sensitivity) turn(input.lookX*sensitivity*PAD_YAW_SPEED*seconds,input.lookY*sensitivity*PAD_PITCH_SPEED*seconds);
        if (input.jump) onJump?.();
        if (input.interact) onInteract();
        if(input.spit)spitting?.begin('pad');
        if(input.spitReleased)spitting?.end('pad');
        if (!active()) return;
      }
      const pressed = (...codes) => codes.some(code => keys.has(code)) ? 1 : 0;
      let x = stickX + (input?.moveX || 0) + pressed('KeyD', 'ArrowRight') - pressed('KeyA', 'ArrowLeft');
      let z = stickY - (input?.moveY || 0) + pressed('KeyW', 'ArrowUp') - pressed('KeyS', 'ArrowDown');
      const length = Math.hypot(x, z);
      if (!length) return;
      if (length > 1) { x /= length; z /= length; }
      const sprint = sprintKey !== null || (input?.sprintHeld && (input.moveX || input.moveY));
      const distance = seconds * (sprint ? SPRINT_SPEED : WALK_SPEED), yaw = camera.rotation.y;
      const dx = (x * Math.cos(yaw) - z * Math.sin(yaw)) * distance;
      const dz = (-z * Math.cos(yaw) - x * Math.sin(yaw)) * distance;
      move(dx, dz);
    },
    reset,
    dispose() {
      if (disposed) return;
      disposed = true; reset();
      for (const remove of off) remove();
      touch.dispose();root.removeAttribute?.('data-expo-touch');
      canvas.style.touchAction = oldTouchAction;
    },
  };
}
