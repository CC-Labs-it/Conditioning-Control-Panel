import { herdRoster, llamaPortrait } from '../../../../data/content/llamas.js';
import { warmEntrance,clearEntrance } from '../../../expo/entrance.js';
import { createPortal } from '../../../expo/portal.js';

export const PORTAL_DARK = '#071819';
const ENTRY_CSS = `
.expo-worm {position:absolute;inset:0;pointer-events:auto;overflow:hidden;background:radial-gradient(ellipse at var(--wx) var(--wy),#20282238 10%,#253a326e 52%,#26352bc4);backdrop-filter:blur(2px);color:#fff;font:600 18px/1.3 system-ui;text-align:center}
.expo-worm .worm-question {position:absolute;left:var(--wx);top:var(--wy);transform:translate(-50%,-100%);margin:-44px 0 0;max-width:90%;width:max-content;text-shadow:0 1px 7px #101911}
.expo-worm #btn {position:absolute!important;left:var(--wx)!important;top:var(--wy)!important;transform:translate(-50%,-50%)!important;margin:0!important;min-width:90px;max-width:55%;pointer-events:auto}
.expo-orbit {position:absolute;width:clamp(58px,10vmin,120px);aspect-ratio:1;left:var(--wx);top:var(--wy);border-radius:50%;object-fit:cover;opacity:.45;box-shadow:0 0 18px #f6fae7a1;pointer-events:none}
.expo-crossing {position:absolute;inset:0;z-index:6;pointer-events:none}
.expo-eyelids {position:absolute;inset:0;opacity:0;background:radial-gradient(ellipse 79% var(--opening,156%) at 50% 50%,transparent 76%,#6e9c8d32 83%,#112d2ae8 93%,#071819 100%)}
.expo-eyelids::after {content:'';position:absolute;inset:50% auto auto 50%;width:min(23vw,240px);height:2px;transform:translate(-50%,-50%) scaleX(var(--seam,0));background:linear-gradient(90deg,transparent,#739f9459,#bbcab9a1,#739f9459,transparent);filter:blur(.6px)}
.expo-ticket {position:absolute;inset:0;z-index:4;display:grid;place-content:center;justify-items:center;gap:.75em;background:#071819;color:#c4d8cb;font:600 16px/1.4 system-ui;pointer-events:auto;text-align:center;padding:1em}
.expo-ticket[data-loading] {z-index:7;align-content:start;padding-top:calc(50vh + 2rem);pointer-events:none;opacity:0;background:transparent}
.expo-ticket[data-loading] b {display:none}
.expo-ticket[data-loading] span {font-size:14px;font-weight:500;letter-spacing:.02em}
body:has(.expo-root,.expo-ticket) #dock-strip {visibility:hidden;pointer-events:none}
.expo-ticket b {font-size:clamp(22px,5vmin,40px);letter-spacing:.07em}
.expo-ticket button {font:inherit;min-height:44px;padding:.35em 1em;border:1px solid #496858;color:#174937;background:#faf9ee}
`;

/** The purchase hands its own button to the visitor before the page fades. */
function wormhole(ctx, portal) {
  const parent = ctx.layer('ending'), doc = parent.ownerDocument;
  const button = ctx.button.el, original = button.parentNode, after = button.nextSibling;
  const rect = button.getBoundingClientRect(), frame = parent.getBoundingClientRect();
  const wx = Math.max(frame.width * .22, Math.min(frame.width * .78, rect.left + rect.width/2 - frame.left));
  const wy = Math.max(frame.height * .26, Math.min(frame.height * .72, rect.top + rect.height/2 - frame.top));
  ctx.css(ENTRY_CSS);
  const root = doc.createElement('div'); root.className = 'expo-worm';
  root.style.setProperty('--wx', wx + 'px'); root.style.setProperty('--wy', wy + 'px'); parent.append(root);
  const herd = herdRoster(ctx.life?.llamas ?? ctx.state?.herd);
  const portraits = herd.filter((_, i) => i % Math.max(1, Math.floor(herd.length / 6)) === 0).slice(0, 6).map(llama => {
    const photo = doc.createElement('img'); photo.className = 'expo-orbit'; photo.src = llamaPortrait(llama.id); photo.alt = '';
    root.append(photo); return photo;
  });
  const question = doc.createElement('p'); question.className = 'worm-question'; question.textContent = 'Spit in the Llama wormhole?'; root.append(question);
  const oldNodes = [...button.childNodes], oldName = button.getAttribute('aria-label');
  root.append(button); button.replaceChildren(doc.createTextNode('YES')); button.setAttribute('aria-label', question.textContent + ' YES');
  button.focus({ preventScroll: true });
  warmEntrance();
  void import('../../../expo/scene.js').catch(()=>{});
  return new Promise(resolve => {
    let gone = false, crossed = -1, elapsed = 0, previous = ctx.activeMs, offFrame, offHold;
    const previousOpacity = button.style.opacity;
    const stop = (admitted = false) => {
      if (gone) return; gone = true; offFrame?.(); offHold?.();
      button.style.opacity = previousOpacity;
      button.removeEventListener('click', enter, true); button.replaceChildren(...oldNodes);
      if (oldName == null) button.removeAttribute('aria-label'); else button.setAttribute('aria-label', oldName);
      original.insertBefore(button, after?.parentNode === original ? after : null); root.remove(); resolve(admitted);
    };
    const enter = event => {
      event.preventDefault(); event.stopImmediatePropagation();
      if (gone || crossed >= 0 || ctx.paused || doc.hidden) return;
      crossed = elapsed;
      portal.begin();
      warmEntrance();
      ctx.audio.play('assets/expo/audio/spit.ogg', { bus: 'sfx', gain: .55 });
    };
    const paint = () => {
      const now = ctx.activeMs, dt = Math.max(0, (now - previous)/1000); previous = now;
      if (doc.hidden || ctx.paused) return;
      elapsed += dt;
      const calm = ctx.gentle || ctx.reduced;
      const transition = crossed < 0 ? 0 : Math.min(1,(elapsed-crossed)/(calm ? .38 : .54));
      const radius = Math.min(frame.width * .29, frame.height * .32) * (calm ? 1 : 1-transition);
      portraits.forEach((photo, i) => {
        const angle = i * Math.PI * 2 / portraits.length + (calm ? 0 : elapsed * .17);
        photo.style.transform = 'translate(-50%,-50%) translate('+ Math.cos(angle)*radius +'px,'+ Math.sin(angle)*radius*.75 +'px)';
        photo.style.opacity = String(.47 * Math.min(1, elapsed/1.2) * (1-transition));
      });
      if (crossed >= 0) {
        question.style.opacity = button.style.opacity = String(1-transition);
        portal.cover(transition);
        if (transition >= 1) stop(true);
      }
    };
    button.addEventListener('click', enter, true); offFrame = ctx.frame(paint);
    offHold = ctx.on('suspend', e => { if (e?.on && e.reason === 'panic') { clearEntrance();stop(); } });
    ctx.cleanup(()=>{if(crossed<0)clearEntrance();stop();}); paint();
  });
}

export async function expoEnding(ctx, releaseWorld) {
  const parent = ctx.layer('ending'), doc = parent.ownerDocument, win = doc.defaultView;
  ctx.css(ENTRY_CSS);
  let portal = createPortal(ctx,parent);
  ctx.cleanup(()=>portal.dispose());
  if (!await wormhole(ctx,portal)) { portal.dispose(); return; }
  releaseWorld();
  const cover = doc.createElement('div'); cover.className = 'expo-ticket'; parent.append(cover);
  const name = doc.createElement('b'); name.textContent = 'LLAMA EXPO 2037';
  const status = doc.createElement('span'); status.setAttribute('role','status');
  status.textContent = 'Opening the Expo...'; cover.append(name, status);
  let scene = null, dead = false, finish, retry;
  const done = new Promise(resolve => { finish = resolve; });
  function dispose() {
    if (dead) return;
    dead = true; scene?.dispose(); portal.dispose(); clearEntrance();cover.remove(); ctx.emit('ending-media', false);
    if (win.__expo === scene) delete win.__expo;
    retry?.(); finish();
  }
  ctx.emit('ending-media', true); ctx.cleanup(dispose);
  ctx.on('suspend', e => { if (e?.on && e.reason === 'panic') dispose(); });
  try {
    while (!dead) {
      try {
        cover.dataset.loading = '';
        const loadingAt = ctx.activeMs;
        const updateStatus = ctx.frame(() => {
          if (dead || !cover.isConnected || !cover.hasAttribute('data-loading')) { updateStatus(); return; }
          cover.style.opacity = String(Math.min(1,Math.max(0,(ctx.activeMs-loadingAt-1450)/300)));
        });
        const module = await Promise.race([import('../../../expo/scene.js'), done]);
        if (!module || dead) break;
        scene = module.createExpo(ctx, parent, { onExit: dispose, arrival: true });
        await Promise.race([scene.ready, done]);
        if (dead) break;
        if (['localhost','127.0.0.1','[::1]'].includes(win.location.hostname)) win.__expo = scene;
        cover.remove();
        let off;
        const reveal = () => {
          if (ctx.paused || doc.hidden || dead) return;
          if (!started) { started = true; start = ctx.activeMs; portal.arrive(); scene.beginArrival(); }
          const seconds = (ctx.activeMs-start)/1000;
          portal.reveal(seconds);
          if (seconds >= .7) off?.();
        };
        let started = false, start = 0;
        off = ctx.frame(reveal); ctx.cleanup(off); reveal();
        await done;
      } catch (error) {
        if (dead) break;
        console.error(error); scene?.dispose(); scene = null; portal.dispose();
        delete cover.dataset.loading; cover.style.opacity = '';
        status.textContent = 'The Expo could not open.';
        const again = doc.createElement('button'), leave = doc.createElement('button');
        again.type = leave.type = 'button'; again.textContent = 'Try again'; leave.textContent = 'Continue to recap';
        cover.append(again, leave); again.focus(); leave.onclick = dispose;
        await new Promise(resolve => { retry = resolve; again.onclick = resolve; });
        again.remove(); leave.remove(); retry = null; status.textContent = 'Opening the Expo...';
        if (!dead) { portal = createPortal(ctx,parent); portal.begin(); portal.cover(1); }
      }
    }
  } finally { dispose(); }
}
