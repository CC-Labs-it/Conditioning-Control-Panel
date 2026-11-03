/* Earned button feedback. One spring per button, bounded sparks, no game RNG or extra payouts. */
import { btnCenter, spawn } from './fx.js';

const springs = new WeakMap();
const sparks = new WeakMap();
const watched = new WeakSet();

function watchMotion(ctx) {
  if (watched.has(ctx)) return;
  watched.add(ctx);
  const stop = () => {
    if (!ctx.gentle) return;
    const el = ctx.button.el;
    springs.get(el)?.animation.cancel();
    for (const a of sparks.get(el)?.animations || []) a.cancel();
  };
  ctx.on('resume', stop);
  ctx.frame(stop);
}

export function buttonImpulse(ctx, amount = 1, hero = false) {
  watchMotion(ctx);
  const el = ctx.button.el, previous = springs.get(el), now = ctx.state.clock;
  if (ctx.gentle) { previous?.animation.cancel(); return; }
  if (!hero && previous?.heroUntil > now) return;
  const start = previous ? el.ownerDocument.defaultView.getComputedStyle(el).scale : '1 1';
  previous?.animation.cancel();
  const a = Math.min(1.8, amount);
  const animation = ctx.anim(el, [
    { scale: start === 'none' ? '1 1' : start },
    { scale: `${1 + .09 * a} ${1 - .13 * a}`, offset: .13 },
    { scale: `${1 - .025 * a} ${1 + .045 * a}`, offset: .52 },
    { scale: '1 1' },
  ], { duration: hero ? 360 : 230, easing: 'cubic-bezier(.2,.8,.3,1)' });
  const state = { animation, heroUntil: hero ? now + 160 : 0 };
  springs.set(el, state);
  const clear = () => { if (springs.get(el) === state) springs.delete(el); };
  animation.finished?.then(clear, clear);
}

/** Contact sparks use their own sequence so decoration never changes the next lucky roll. */
export function contactSparks(ctx, event = {}, hero = false) {
  watchMotion(ctx);
  if (ctx.gentle || (event.auto && !hero)) return;
  const el = ctx.button.el, now = ctx.state.clock;
  let state = sparks.get(el);
  if (!state) { state = { live: 0, last: -Infinity, serial: 0, animations: new Set() }; sparks.set(el, state); }
  if (!hero && now - state.last < 100) return;
  state.last = now;
  const c = Number.isFinite(event.x) && Number.isFinite(event.y) ? event : btnCenter(ctx);
  const count = Math.min(hero ? (event.auto ? 7 : 18) : 4, 32 - state.live);
  const phase = ++state.serial * 2.39996;
  for (let i = 0; i < count; i++) {
    const angle = phase + i * Math.PI * 2 / count;
    const reach = (hero ? 62 : 19) + ((i * 13 + state.serial * 7) % (hero ? 52 : 17));
    const size = hero ? 4 + i % 4 : 3 + i % 2;
    const d = spawn(ctx, `width:${size}px;height:${size}px;background:var(${i % 3 ? '--accent' : '--accent-2'});border-radius:${i % 2 ? '50%' : '1px'};`, null);
    d.dataset.tapSpark = hero ? 'lucky' : 'tap';
    state.live++;
    const a = ctx.anim(d, [
      { transform: `translate(${c.x}px,${c.y}px) scale(.5)`, opacity: 0 },
      { transform: `translate(${c.x + Math.cos(angle) * reach * .3}px,${c.y + Math.sin(angle) * reach * .3}px) scale(1)`, opacity: .9, offset: .18 },
      { transform: `translate(${c.x + Math.cos(angle) * reach}px,${c.y + Math.sin(angle) * reach + (hero ? 18 : 7)}px) rotate(${i * 45}deg) scale(.2)`, opacity: 0 },
    ], { duration: hero ? 620 : 360, easing: 'ease-out' });
    state.animations.add(a);
    let done = false;
    a.onfinish = a.oncancel = () => { if (done) return; done = true; d.remove(); state.live--; state.animations.delete(a); };
  }
}

/** The sound unlock owns this phrase. Silence or a break starts a new little sentence. */
export function createTapPhrase() {
  const notes = [3, 5, 6, 8, 6, 5, 3, 1];
  let last = -Infinity, lastBody = -Infinity, step = 0, taps = 0;
  return {
    tap(now, auto = false) {
      if (auto || now - lastBody < 100) return null;
      lastBody = now;
      if (now - last > 1100) { step = 0; taps = 0; }
      last = now;
      const note = taps++ % 4 === 0 ? notes[step++ % notes.length] : null;
      return { note, gain: .18 };
    },
    reset() { last = lastBody = -Infinity; step = taps = 0; },
  };
}
