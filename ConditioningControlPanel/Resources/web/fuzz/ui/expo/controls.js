import { createExpoGamepad } from './gamepad.js';
import { createTouchActions } from './touch-actions.js';
import { createMouseCapture } from './mouse-capture.js';

const WALK_SPEED = 4;
const SPRINT_SPEED = 6;
const DOUBLE_TAP_MS = 300;
const CAPTURE_LOOK = 0.0022, DRAG_LOOK = 0.003;
const KEY_YAW_SPEED = 1.8, KEY_PITCH_SPEED = 1.2;
const TAP_SLOP = 14;
const PAD_YAW_SPEED = 2.25;
const PAD_PITCH_SPEED = 1.70;
const MOVE_KEYS = new Set(['KeyW', 'KeyA', 'KeyS', 'KeyD', 'ArrowUp', 'ArrowLeft', 'ArrowDown', 'ArrowRight']);
const UI = 'button,a,input,textarea,select,[contenteditable=true],[role=button]';
const EDITOR = 'input,textarea,select,[contenteditable=true],[role=slider],[role=menu],[role=listbox],[role=tablist]';
const keyCodes={w:'KeyW',a:'KeyA',s:'KeyS',d:'KeyD',e:'KeyE',q:'KeyQ',t:'KeyT',' ':'Space',Spacebar:'Space',
  Enter:'Enter',Shift:'ShiftLeft',ArrowUp:'ArrowUp',ArrowDown:'ArrowDown',ArrowLeft:'ArrowLeft',ArrowRight:'ArrowRight'};
function codeFor(event){
  return event.code&&event.code!=='Unidentified'?event.code:keyCodes[event.key]||keyCodes[event.key?.toLowerCase()];
}

/** All input methods share the same camera and collision-aware walk. */
export function createControls({ root, canvas, camera, move, spit, trail, cameraOptions = {}, onInteract, onJump, onPause, onInputMode, initialInputMode, isPaused = () => false, holdingView = () => false }) {
  const doc = root.ownerDocument, win = doc.defaultView, keys = new Set(), off = [];
  const pad = createExpoGamepad(() => win.navigator.getGamepads?.());
  const primaryMode = () => win.matchMedia?.('(pointer:coarse)').matches ? 'touch' : 'keyboard';
  let inputMode = initialInputMode ?? primaryMode();
  let lastMoveKey = null, lastPressAt = 0, sprintKey = null;
  function used(mode) {
    if (inputMode === mode) return;
    inputMode = mode;
    if(mode==='touch'||mode==='gamepad'){
      hover=null;mouse.release();
      if(drag?.type==='mouse')clearWorldDrag();
    }
    root.toggleAttribute?.('data-expo-touch',mode==='touch');
    if(mode!=='touch')touch.close();
    onInputMode?.(mode);
  }
  let disposed = false, drag = null, tap = null, hover = null, stickId = null, stickX = 0, stickY = 0;
  let unlockPauseAt=-Infinity,escapeReleased=true,handledEscapeDown=false;
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
  const mouse=createMouseCapture(canvas,doc,{enabled:()=>active()&&!cameraOptions.dragLook,
    onEscape:()=>{unlockPauseAt=win.performance.now();escapeReleased=false;onPause?.();},
    onRelease:userReleased=>{if(userReleased){clearKeys();clearPointers();}}});
  const freePointer=()=>!mouse.locked&&(!!cameraOptions.dragLook||mouse.unavailable)&&
    (inputMode==='mouse'||(inputMode==='keyboard'&&!!hover));
  const aimPoint=()=>freePointer()&&!drag?.moved?hover:null;
  function focusGame(){root.focus({preventScroll:true});}
  const capture = (el, id) => { try { el.setPointerCapture(id); } catch {} };
  const release = (el, id) => { try { if (id != null && el.hasPointerCapture(id)) el.releasePointerCapture(id); } catch {} };
  function clearWorldDrag(){
    const old=drag;drag=tap=null;release(canvas,old?.id);
  }
  function clearPointers() {
    clearWorldDrag();hover=null;
    const oldStick=stickId;stickId=null;stickX=stickY=0;
    knob.style.transform='';release(stick,oldStick);
  }
  function clearKeys() {
    keys.clear();
    lastMoveKey = sprintKey = null;
  }
  function reset() {
    clearKeys(); clearPointers(); pad.reset();spitting?.cancel();touch.close();
    mouse.release();
  }
  function turn(yaw, pitch) {
    if (!active()||holdingView()) return;
    camera.rotation.y -= yaw;
    camera.rotation.x = Math.max(-1.45, Math.min(1.45, camera.rotation.x - pitch));
    camera.rotation.z = 0;
  }
  function look(yaw,pitch) { const sign=cameraOptions.invertLook?-1:1;turn(yaw*sign,pitch*sign); }
  function point(event) {
    const r = canvas.getBoundingClientRect();
    return { x: (event.clientX - r.left) / r.width * 2 - 1, y: 1 - (event.clientY - r.top) / r.height * 2 };
  }
  listen(doc, 'mousemove', event => {
    if (mouse.locked && (event.movementX || event.movementY)) {
      used('mouse');look((event.movementX||0)*CAPTURE_LOOK,(event.movementY||0)*CAPTURE_LOOK);
    }
  });
  function keyDown(event){
    const code=codeFor(event),escape=code==='Escape'||event.key==='Escape';
    if(escape){
      // The browser may unlock before delivering this Escape. Its late keydown must not resume the pause it opened.
      const sinceUnlock=win.performance.now()-unlockPauseAt;
      const hostReplay=!event.isTrusted&&event.keyCode===27&&event.which===27;
      const late=isPaused()&&(hostReplay?sinceUnlock<500:!escapeReleased&&sinceUnlock<150);
      if(late||(handledEscapeDown&&event.repeat)||(active()&&mouse.locked)){
        event.preventDefault();event.stopImmediatePropagation();handledEscapeDown=true;
        if(late)unlockPauseAt=-Infinity;
        else if(mouse.locked&&!event.repeat){mouse.release();onPause?.();}
        return;
      }
    }
    if(!available()||event.ctrlKey||event.altKey||event.metaKey||event.target?.closest?.(EDITOR))return;
    if(trail && (code==='KeyT'||(trail.opened&&escape))){
      event.preventDefault();event.stopImmediatePropagation();
      if(!event.repeat){used('keyboard');trail.toggle();}
      return;
    }
    if(trail?.opened)return;
    if(holdingView()&&code!=='KeyE'&&code!=='Enter')return;
    if(code==='KeyQ'&&!event.target?.closest?.(UI)){
      used('keyboard');event.preventDefault();event.stopPropagation();if(!event.repeat)spitting?.begin('key');return;
    }
    const shift=code==='ShiftLeft'||code==='ShiftRight';
    if (shift||MOVE_KEYS.has(code)) {
      used('keyboard');
      if(doc.activeElement!==root||!doc.hasFocus())focusGame();
      event.preventDefault();event.stopPropagation();
      if (!event.repeat && !keys.has(code)) {
        keys.add(code);
        if(shift||(cameraOptions.arrowMode==='turn'&&code.startsWith('Arrow')))return;
        const now = win.performance.now();
        if (code === lastMoveKey && now - lastPressAt <= DOUBLE_TAP_MS) {
          sprintKey = code;
          lastMoveKey = null;
        } else {
          lastMoveKey = code;
          lastPressAt = now;
        }
      }
      return;
    }
    if(event.target?.closest?.(UI)||!['KeyE','Enter','Space'].includes(code))return;
    const pointer=aimPoint();
    // E may operate the item under a free mouse while the visitor keeps the same aim.
    if(code==='Space'||!pointer)used('keyboard');
    event.preventDefault();event.stopPropagation();
    if(!event.repeat){if(code==='Space')onJump?.();else onInteract(pointer);}
  }
  function keyUp(event) {
    const code = codeFor(event);
    keys.delete(code);
    if (code === sprintKey) sprintKey = null;
    if(code==='Escape'||event.key==='Escape'){escapeReleased=true;handledEscapeDown=false;}
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
    if (!active() || event.button !== 0) return;
    const isMouse=event.pointerType==='mouse';
    used(event.pointerType==='touch'?'touch':'mouse');
    if(drag&&(drag.id===event.pointerId||canvas.hasPointerCapture?.(drag.id)===false))clearWorldDrag();
    if(drag)return;
    focusGame();event.preventDefault();
    if(!spitting?.touchHeld)touch.close();
    if(isMouse&&mouse.locked){onInteract();return;}
    if(!isMouse)mouse.release();
    if(isMouse&&!holdingView()&&mouse.take()){tap=null;return;}
    drag=tap={id:event.pointerId,type:event.pointerType,x:event.clientX,y:event.clientY,startX:event.clientX,startY:event.clientY,moved:false};
    capture(canvas,event.pointerId);
    if(isMouse)hover=point(event);
  });
  listen(canvas, 'pointermove', event => {
    if(mouse.locked)return;
    if(event.pointerType==='mouse'){
      if(event.movementX||event.movementY)used('mouse');
      hover=point(event);
    }
    if(!drag||drag.id!==event.pointerId)return;
    drag.moved ||= Math.hypot(event.clientX-drag.startX,event.clientY-drag.startY)>TAP_SLOP;
    if(!drag.moved)return;
    look((event.clientX-drag.x)*DRAG_LOOK,(event.clientY-drag.y)*DRAG_LOOK);
    drag.x=event.clientX;drag.y=event.clientY;
  });
  listen(canvas,'pointerleave',()=>{hover=null;});
  listen(canvas, 'pointerup', event => {
    if(!tap||tap.id!==event.pointerId)return;
    const ended=tap;drag=tap=null;release(canvas,event.pointerId);
    if(!active()||ended.moved||Math.hypot(event.clientX-ended.startX,event.clientY-ended.startY)>TAP_SLOP)return;
    onInteract(point(event));
  });
  listen(canvas,'pointercancel',clearPointers);
  // A browser can lose implicit capture before delivering touchend. Keep its tap until pointerup.
  listen(canvas,'lostpointercapture',event=>{if(drag?.id===event.pointerId)drag=null;});
  listen(canvas, 'contextmenu', event => event.preventDefault());
  function moveStick(event) {
    if (stickId !== event.pointerId || !active()) return;
    const r = stick.getBoundingClientRect(), dx = event.clientX - r.left - r.width / 2, dy = event.clientY - r.top - r.height / 2;
    const travel=(r.width-knob.offsetWidth)/2,length=Math.hypot(dx,dy);
    const amount=Math.max(0,Math.min(1,(length/travel-.12)/.88));
    stickX=length?dx/length*amount:0;stickY=length?-dy/length*amount:0;
    knob.style.transform=`translate(${stickX*travel}px,${-stickY*travel}px)`;
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
    get captured(){return mouse.locked;},
    get freePointer(){return freePointer();},
    get aimPoint(){return aimPoint();},
    applyCameraOptions(options=cameraOptions){
      cameraOptions=options;reset();mouse.retry();
    },
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
        if (sensitivity) look(input.lookX*sensitivity*PAD_YAW_SPEED*seconds,input.lookY*sensitivity*PAD_PITCH_SPEED*seconds);
        if (input.jump) onJump?.();
        if (input.interact) onInteract();
        if(input.spit)spitting?.begin('pad');
        if(input.spitReleased)spitting?.end('pad');
        if (!active()) return;
      }
      const pressed = (...codes) => codes.some(code => keys.has(code)) ? 1 : 0;
      const arrowsLook=cameraOptions.arrowMode==='turn';
      if(arrowsLook){
        const yaw=pressed('ArrowRight')-pressed('ArrowLeft'),pitch=pressed('ArrowDown')-pressed('ArrowUp');
        if(yaw||pitch)turn(yaw*KEY_YAW_SPEED*seconds,pitch*KEY_PITCH_SPEED*seconds);
      }
      const right=arrowsLook?pressed('KeyD'):pressed('KeyD','ArrowRight');
      const left=arrowsLook?pressed('KeyA'):pressed('KeyA','ArrowLeft');
      const forward=arrowsLook?pressed('KeyW'):pressed('KeyW','ArrowUp');
      const back=arrowsLook?pressed('KeyS'):pressed('KeyS','ArrowDown');
      let x=stickX+(input?.moveX||0)+right-left;
      let z=stickY-(input?.moveY||0)+forward-back;
      const length = Math.hypot(x, z);
      if (!length) return;
      if (length > 1) { x /= length; z /= length; }
      const sprint = sprintKey!==null||keys.has('ShiftLeft')||keys.has('ShiftRight')||(input?.sprintHeld&&(input.moveX||input.moveY));
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
      mouse.dispose();touch.dispose();root.removeAttribute?.('data-expo-touch');
      canvas.style.touchAction = oldTouchAction;
    },
  };
}
