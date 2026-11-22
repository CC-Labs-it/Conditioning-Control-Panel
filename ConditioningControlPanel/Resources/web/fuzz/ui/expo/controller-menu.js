import { createExpoGamepad } from './gamepad.js';

export function expoGuideText(mode) {
  if (mode === 'gamepad') return 'Left stick to walk. Hold RB / R1 to sprint. Right stick to look. A / Cross to jump. X / Square to interact. Hold RT / R2 to spit. Menu / Options to pause.';
  if (mode === 'touch') return 'Drag to look. Use the stick to walk. Tap Jump. Hold Spit to aim, then release.';
  return 'WASD or arrows to walk. Double-tap and hold a direction to sprint. Drag or click to look. Space to jump. E to interact. Hold Q to spit. Esc to pause.';
}

export function expoInteractionText(label, mode) {
  return label && mode === 'gamepad' ? 'X / Square · ' + label : label;
}

const MENU_STYLE = `
[data-expo-pad-focus]{outline:3px solid #ffe19d!important;outline-offset:2px;box-shadow:0 0 0 5px #173e34!important}
.expo-pad-hint{display:block;color:#fff;background:#153b32;padding:.25em .65em;border-radius:3px;margin:.6em auto 0;width:fit-content;max-width:95%;text-align:center;font:600 14px/1.4 system-ui;pointer-events:none}
.expo-worm>.expo-pad-hint{position:absolute;left:50%;bottom:max(20px,env(safe-area-inset-bottom));transform:translateX(-50%);width:max-content}
`;
const CHOICES = 'button:not(:disabled),input[type=checkbox]:not(:disabled),input[type=range]:not(:disabled),a[href]';
const NATIVE_INPUT = '[data-credit],[data-pick=files],[data-pick=signin],#pic-folder,#save-in,a[href]';

function choices(root) {
  return [...root.querySelectorAll(CHOICES)].filter(el => !el.closest('[hidden],[inert],[aria-hidden=true]') &&
    el.tabIndex >= 0 && el.getClientRects().length && el.ownerDocument.defaultView.getComputedStyle(el).visibility === 'visible');
}

/** Each menu operates its existing native controls. A fresh reader requires release after a change of screen. */
function bindMenus(doc, { surface, describe, alive = () => true }) {
  const win = doc.defaultView;
  const reader = createExpoGamepad(() => win.navigator.getGamepads?.());
  const style = doc.createElement('style');
  style.textContent = MENU_STYLE;
  doc.head.append(style);
  const hint = doc.createElement('small');
  hint.className = 'expo-pad-hint';
  const previousFocus = new WeakMap();
  let panel = null, menu = null, marked = null, axis = '', mode = null, frame = 0, dead = false;

  function clearPaint(preserveLayout = false) {
    marked?.removeAttribute('data-expo-pad-focus');
    marked = null;
    if (preserveLayout) hint.style.visibility = 'hidden';
    else { hint.remove(); hint.style.visibility = ''; }
  }
  function setMode(next, preserveLayout = false) {
    mode = next;
    if (mode !== 'gamepad') clearPaint(preserveLayout);
  }
  function pointer(e) { setMode(e.pointerType === 'touch' ? 'touch' : 'mouse', true); }
  function pointerEnded() {
    if (hint.isConnected && mode !== 'gamepad') win.setTimeout(() => { if (!dead && mode !== 'gamepad') clearPaint(); }, 0);
  }
  function keyboard() { setMode('keyboard'); }
  function focus(el, move = false) {
    if (!el) return;
    if (marked !== el) {
      marked?.removeAttribute('data-expo-pad-focus');
      marked = el;
      el.setAttribute('data-expo-pad-focus', '');
      previousFocus.set(panel, el);
    }
    if (move && doc.activeElement !== el) el.focus({preventScroll:true});
    if (move) el.scrollIntoView?.({block:'nearest', inline:'nearest'});
  }
  function selectPanel(next) {
    if (panel === next) return;
    clearPaint();
    panel = next;
    menu = next ? describe(next) : null;
    reader.reset();
    axis = '';
  }
  function tick() {
    if (dead || !alive()) { dispose(); return; }
    frame = win.requestAnimationFrame(tick);
    selectPanel(doc.hidden || !doc.hasFocus() ? null : surface());
    if (!panel) return;
    const input = reader.poll();
    if (!input) { clearPaint(); axis = ''; return; }
    if (input.activity) setMode('gamepad');
    if (mode !== 'gamepad') return;
    if (!hint.isConnected) { hint.textContent = menu.hint; panel.append(hint); }
    hint.style.visibility = '';
    if (panel.contains(doc.activeElement) && doc.activeElement.matches(CHOICES)) focus(doc.activeElement);
    else if (!panel.contains(doc.activeElement)) {
      const previous = previousFocus.get(panel);
      const list = choices(panel);
      focus(list.includes(previous) ? previous : list[0], true);
    }
    const x = input.moveX, y = input.moveY;
    const strength = Math.max(Math.abs(x),Math.abs(y));
    const stick = Math.abs(y) >= Math.abs(x) ? (y > 0 ? 'down' : 'up') : (x > 0 ? 'right' : 'left');
    let direction = ['up','down','left','right'].find(key => input[key]);
    if (strength < .3) axis = '';
    else if (strength >= .55 && axis !== stick) { axis = stick; direction ||= stick; }
    if (input.back || input.start) { menu.back?.(); return; }
    if (input.interact && menu.mixer) { menu.mixer(); return; }
    if (!input.jump && !direction) return;
    const list = choices(panel);
    if (!list.length) return;
    const index = list.indexOf(doc.activeElement);
    let el = index < 0 ? list[0] : list[index];
    if (direction) {
      hint.textContent = menu.hint;
      const forward = direction === 'down' || direction === 'right';
      if (el.type === 'range' && (direction === 'left' || direction === 'right')) {
        const old = el.value;
        if (forward) el.stepUp(5); else el.stepDown(5);
        if (el.value !== old) el.dispatchEvent(new win.Event('input',{bubbles:true}));
      } else if (index >= 0) el = list[(index + (forward ? 1 : -1) + list.length) % list.length];
      focus(el, true);
    } else {
      focus(el, true);
      if (el.matches(NATIVE_INPUT)) hint.textContent = 'Use a keyboard or pointer for this action. B / Circle to go back.';
      else el.click();
    }
  }
  function pageHidden(event) {
    if (event.persisted) selectPanel(null); else dispose();
  }
  function disconnected(event) { reader.disconnect(event.gamepad.index); axis = ''; }
  function dispose() {
    if (dead) return;
    dead = true;
    win.cancelAnimationFrame(frame);
    win.removeEventListener('pagehide', pageHidden);
    win.removeEventListener('gamepaddisconnected', disconnected);
    doc.removeEventListener('pointerdown', pointer, true);
    win.removeEventListener('pointerup', pointerEnded, true);
    win.removeEventListener('pointercancel', pointerEnded, true);
    doc.removeEventListener('keydown', keyboard, true);
    clearPaint();
    style.remove();
  }
  doc.addEventListener('pointerdown', pointer, true);
  win.addEventListener('pointerup', pointerEnded, true);
  win.addEventListener('pointercancel', pointerEnded, true);
  doc.addEventListener('keydown', keyboard, true);
  win.addEventListener('pagehide', pageHidden);
  win.addEventListener('gamepaddisconnected', disconnected);
  tick();
  return { dispose, setMode, get mode() { return mode; } };
}

export function bindExpoPreviewButton(button) {
  const root = button.parentElement;
  return bindMenus(button.ownerDocument, {surface:() => root, alive:() => button.isConnected,
    describe:() => ({hint:'A / Cross to play'})});
}

export function bindExpoMenus(ctx, parent) {
  const doc = parent.ownerDocument, pause = doc.getElementById('pause');
  const mixer = doc.getElementById('audio-mixer'), mixerToggle = doc.getElementById('audio-toggle');
  let suspended = false;
  const off = ctx.on('suspend', e => { suspended = !!e?.on; });
  const openPause = () => { if (!ctx.paused) doc.getElementById('wake')?.click(); };
  const menus = bindMenus(doc, {
    surface() {
      if (suspended) return null;
      if (pause && !pause.hidden) {
        if (pause.inert) return doc.querySelector('.tc.above [role=dialog]');
        if (mixer && !mixer.hidden) return mixer;
        const book = doc.querySelector('#sticker-book:not([hidden]) [role=dialog]');
        if (book) {
          const detail = book.querySelector('.sb-card:not([hidden]) .close');
          return detail?.getClientRects().length ? detail.closest('.sb-card') : book;
        }
        return pause.querySelector('[role=dialog]');
      }
      if (ctx.paused) return null;
      return parent.querySelector('.expo-farewell,.expo-worm,.expo-ticket:not([data-loading])');
    },
    describe(panel) {
      if (panel.matches('.sb-card')) return {hint:'B / Circle to return to the page', back:() => panel.querySelector('.close')?.click()};
      if (panel.closest('#sticker-book')) return {hint:'D-pad to move · A / Cross to select · B / Circle to close the book', back:() => panel.querySelector('.sb-x')?.click()};
      if (panel === mixer) return {hint:'Up / down to select · Left / right for volume · B / Circle to go back', back:() => mixerToggle.click()};
      if (panel.closest('#pause')) return {hint:'D-pad to move · A / Cross to select · X / Square for sound · B / Circle to resume',
        mixer:() => mixerToggle?.click(), back:() => doc.getElementById('resume')?.click()};
      if (panel.closest('.tc.above')) return {hint:'D-pad to move · A / Cross to select · B / Circle to go back', back:() => panel.querySelector('#pic-back')?.click()};
      if (panel.matches('.expo-worm')) return {hint:'A / Cross for YES · Menu / Options to pause', back:openPause};
      if (panel.matches('.expo-farewell')) return {hint:'A / Cross to return to recap · Menu / Options to pause', back:openPause};
      return {hint:'D-pad to move · A / Cross to select', back:openPause};
    }
  });
  if (doc.documentElement.dataset.expoPadAdmission) {
    delete doc.documentElement.dataset.expoPadAdmission;
    menus.setMode('gamepad');
  }
  return {get mode() { return menus.mode; }, setMode:menus.setMode, dispose() { off(); menus.dispose(); }};
}
