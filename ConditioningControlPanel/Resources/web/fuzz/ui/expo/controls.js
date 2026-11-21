const WALK_SPEED = 4;
const LOOK_SPEED = 0.0025;
const MOVE_KEYS = new Set(['KeyW', 'KeyA', 'KeyS', 'KeyD', 'ArrowUp', 'ArrowLeft', 'ArrowDown', 'ArrowRight']);
const UI = 'button,a,input,textarea,select,[contenteditable=true],[role=button]';
const EDITOR = 'input,textarea,select,[contenteditable=true],[role=slider],[role=menu],[role=listbox],[role=tablist]';
const keyCodes={w:'KeyW',a:'KeyA',s:'KeyS',d:'KeyD',e:'KeyE',' ':'Space',Spacebar:'Space',
  Enter:'Enter',ArrowUp:'ArrowUp',ArrowDown:'ArrowDown',ArrowLeft:'ArrowLeft',ArrowRight:'ArrowRight'};
function codeFor(event){
  return event.code&&event.code!=='Unidentified'?event.code:keyCodes[event.key]||keyCodes[event.key?.toLowerCase()];
}

/** The same walk on mouse, keyboard and touch. The camera never bobs or rolls. */
export function createControls({ root, canvas, camera, move, onInteract, onJump, onPause, isPaused = () => false }) {
  const doc = root.ownerDocument, win = doc.defaultView, keys = new Set(), off = [];
  let disposed = false, drag = null, stickId = null, stickX = 0, stickY = 0, locked = false;
  let releasing = false;
  const oldTouchAction = canvas.style.touchAction;
  canvas.style.touchAction = 'none';
  camera.rotation.order = 'YXZ';
  camera.rotation.z = 0;
  const style = doc.createElement('style');
  style.textContent = `
    .expo-touch { display:none; position:absolute; z-index:5; touch-action:none; user-select:none; -webkit-user-select:none; }
    .expo-stick { left:max(18px,env(safe-area-inset-left)); bottom:max(24px,env(safe-area-inset-bottom)); width:112px; height:112px;
      border:2px solid #fff9; border-radius:50%; background:#143d3470; box-shadow:0 2px 10px #0003; }
    .expo-stick-knob { position:absolute; left:35px; top:35px; width:38px; height:38px; border:2px solid #fff;
      border-radius:50%; background:#d7eadbd9; pointer-events:none; }
    .expo-interact { right:max(18px,env(safe-area-inset-right)); bottom:max(38px,env(safe-area-inset-bottom)); min-width:92px;
      min-height:60px; padding:12px; border:2px solid white; border-radius:30px; background:#174b3be6; color:white; font:700 16px system-ui; }
    .expo-has-touch .expo-touch { display:block; }
    @media(pointer:coarse) { .expo-touch { display:block; } }
  `;
  const stick = doc.createElement('div');
  stick.className = 'expo-touch expo-stick';
  stick.setAttribute('aria-label', 'Drag to walk');
  const knob = doc.createElement('div'); knob.className = 'expo-stick-knob'; stick.append(knob);
  const interact = doc.createElement('button');
  interact.className = 'expo-touch expo-interact'; interact.type = 'button'; interact.textContent = 'Interact';
  root.append(style, stick, interact);
  function listen(target, event, fn, options) {
    target.addEventListener(event, fn, options);
    off.push(() => target.removeEventListener(event, fn, options));
  }
  const active = () => !disposed && !isPaused() && !doc.hidden;
  function focusGame(){root.focus({preventScroll:true});}
  const capture = (el, id) => { try { el.setPointerCapture(id); } catch {} };
  const release = (el, id) => { try { if (id != null && el.hasPointerCapture(id)) el.releasePointerCapture(id); } catch {} };
  function clearPointers() {
    const oldDrag = drag, oldStick = stickId;
    drag = null; stickId = null; stickX = stickY = 0;
    knob.style.transform = '';
    release(canvas, oldDrag?.id); release(stick, oldStick);
  }
  function reset() {
    keys.clear(); clearPointers();
    if (doc.pointerLockElement === canvas) {
      releasing = true;
      doc.exitPointerLock?.();
    }
  }
  function look(dx, dy) {
    if (!active()) return;
    camera.rotation.y -= dx * LOOK_SPEED;
    camera.rotation.x = Math.max(-1.45, Math.min(1.45, camera.rotation.x - dy * LOOK_SPEED));
    camera.rotation.z = 0;
  }
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
      keys.clear(); clearPointers();
      if (wasLocked && !releasing && active()) onPause?.();
    }
    releasing = false;
  });
  listen(doc, 'mousemove', event => {
    if (locked) look(event.movementX || 0, event.movementY || 0);
  });
  function keyDown(event){
    if (!active() || event.ctrlKey || event.altKey || event.metaKey || event.target?.closest?.(EDITOR)) return;
    const code=codeFor(event);
    if (MOVE_KEYS.has(code)) {
      if(doc.activeElement!==root||!doc.hasFocus())focusGame();
      event.preventDefault();event.stopPropagation();keys.add(code);return;
    }
    if(event.target?.closest?.(UI)||!['KeyE','Enter','Space'].includes(code))return;
    event.preventDefault();event.stopPropagation();
    if(!event.repeat){if(code==='Space')onJump?.();else onInteract();}
  }
  const keyUp=event=>keys.delete(codeFor(event));
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
  listen(doc, 'visibilitychange', () => { if (doc.hidden) reset(); });
  listen(canvas, 'pointerdown', event => {
    if (!active() || event.button !== 0 || drag) return;
    focusGame();
    event.preventDefault();
    if (event.pointerType === 'touch') root.classList.add('expo-has-touch');
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
    const length = Math.hypot(dx, dy), scale = length > 38 ? 38 / length : 1;
    stickX = dx * scale / 38; stickY = -dy * scale / 38;
    knob.style.transform = `translate(${dx * scale}px,${dy * scale}px)`;
  }
  listen(stick, 'pointerdown', event => {
    if (!active() || stickId != null) return;
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
  let tap = null, handledTouch = -Infinity;
  listen(interact, 'pointerdown', event => {
    if (event.pointerType !== 'touch' || !active()) return;
    tap = { id:event.pointerId,x:event.clientX,y:event.clientY };
    capture(interact,event.pointerId);
  });
  listen(interact, 'pointerup', event => {
    if (!tap || tap.id !== event.pointerId) return;
    const valid = Math.hypot(event.clientX-tap.x,event.clientY-tap.y) < 14;
    tap = null; release(interact,event.pointerId); handledTouch = win.performance.now();
    event.preventDefault();
    if (valid && active()) onInteract();
    interact.blur();
  });
  listen(interact,'pointercancel',()=>{tap=null;});
  listen(interact, 'click', event => {
    event.preventDefault(); event.stopPropagation();
    if (event.detail && win.performance.now()-handledTouch<700) return;
    if (active()) onInteract();
    interact.blur();
  });
  return {
    step(dt) {
      if (!active()) { reset(); return; }
      const pressed = (...codes) => codes.some(code => keys.has(code)) ? 1 : 0;
      let x = stickX + pressed('KeyD', 'ArrowRight') - pressed('KeyA', 'ArrowLeft');
      let z = stickY + pressed('KeyW', 'ArrowUp') - pressed('KeyS', 'ArrowDown');
      const length = Math.hypot(x, z);
      if (!length || !Number.isFinite(dt) || dt <= 0) return;
      if (length > 1) { x /= length; z /= length; }
      const distance = Math.min(dt, 0.1) * WALK_SPEED, yaw = camera.rotation.y;
      const dx = (x * Math.cos(yaw) - z * Math.sin(yaw)) * distance;
      const dz = (-z * Math.cos(yaw) - x * Math.sin(yaw)) * distance;
      move(dx, dz);
    },
    reset,
    dispose() {
      if (disposed) return;
      disposed = true; reset();
      for (const remove of off) remove();
      style.remove(); stick.remove(); interact.remove(); root.classList.remove('expo-has-touch');
      canvas.style.touchAction = oldTouchAction;
    },
  };
}
