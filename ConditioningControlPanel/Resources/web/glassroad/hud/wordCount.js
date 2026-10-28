/* hud/wordCount.js - the word coin count, bottom left above the place numeral (playtest fix 2026-10-28, lane words).
 *
 * sim/wordCoins.js counts the lit road words the player drove over; boot.js sends `word-coins`
 * { count, cap, delta, show } on the bus. A number and a small word glyph on an ink chip: a point swells it IN and
 * back OUT, a hit (delta < 0) dips it, the cap warms it gold. motion 'less' or Gentle keep a soft brightness swell
 * with no scale, motion 'off' plays nothing; the chip fades in and out (longer under Gentle). Mounted by hud.js.
 */

export const COIN_HUD = Object.freeze({ gainMs: 320, lossMs: 420, fadeMs: 200, gentleFadeMs: 600 });

const GAIN_FULL = [{ transform: 'scale(1)', filter: 'brightness(1)' }, { transform: 'scale(1.18)', filter: 'brightness(1.5)', offset: 0.3 },
  { transform: 'scale(1)', filter: 'brightness(1)' }];
const GAIN_SOFT = [{ filter: 'brightness(1)' }, { filter: 'brightness(1.35)', offset: 0.4 }, { filter: 'brightness(1)' }];
const LOSS = [{ opacity: 1 }, { opacity: 0.45, offset: 0.35 }, { opacity: 1 }];

/** Keyframes for a change (delta > 0 gain, < 0 loss) at a motion level, or null when nothing plays. */
export function coinFrames(delta, motion = 'full', gentle = false) {
  if (motion === 'off' || !delta) return null;
  if (delta < 0) return LOSS;
  return motion === 'less' || gentle ? GAIN_SOFT : GAIN_FULL;
}

const CSS = '.hud-coins{position:absolute;left:max(16px,env(safe-area-inset-left));bottom:calc(max(10px,env(safe-area-inset-bottom)) + 112px);' +
  'display:flex;align-items:center;gap:6px;padding:4px 10px 4px 7px;border-radius:999px;background:var(--paper-ink,#1c1722);' +
  'border:2px solid var(--paper,#f5f0e8);color:var(--paper,#f5f0e8);pointer-events:none;opacity:0;transition:opacity 200ms ease-out;}' +
  '.hud-coins.on{opacity:1}.hud-coins svg{width:18px;height:18px;fill:none;stroke:var(--pink,#ff5fa8);stroke-width:2.4;stroke-linecap:round}' +
  '.hud-coins b{font:800 18px/1 var(--mono,monospace);font-variant-numeric:tabular-nums;min-width:1.2em;text-align:right}' +
  '.hud-coins.max b{color:var(--gold,#ffd36b)}.hud-coins.max svg{stroke:var(--gold,#ffd36b)}' +
  '@media (max-width:640px){.hud-coins{bottom:calc(max(10px,env(safe-area-inset-bottom)) + 8px)}}';

/**
 * @param host the HUD race layer; bus the event bus; motion() -> 'full' | 'less' | 'off'; gentle() -> boolean
 * @returns dispose()
 */
export function createCoinCount(host, bus, { doc = globalThis.document, motion = () => 'full', gentle = () => false, H = COIN_HUD } = {}) {
  // a bare test DOM (no getElementById / appendChild) mounts nothing
  if (!host || !doc || typeof doc.getElementById !== 'function' || typeof host.appendChild !== 'function') return () => {};
  if (!doc.getElementById('hud-coins-css')) {
    const st = doc.createElement('style'); st.id = 'hud-coins-css'; st.textContent = CSS;
    (doc.head || host).appendChild(st);
  }
  const el = doc.createElement('div');
  el.className = 'hud-coins';
  el.setAttribute('aria-label', 'Words');
  el.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M4 12h11M4 17h14"/></svg><b>0</b>';
  host.appendChild(el);
  const num = el.querySelector('b');
  let last = -1, on = false;
  const off = bus.on('word-coins', ({ count = 0, cap = 10, delta = 0, show = true } = {}) => {
    if (show !== on) {
      on = !!show; el.classList.toggle('on', on);
      el.style.transitionDuration = (gentle() ? H.gentleFadeMs : H.fadeMs) + 'ms';
    }
    if (count !== last) { num.textContent = String(count); last = count; }
    el.classList.toggle('max', count >= cap);
    const frames = on ? coinFrames(delta, motion(), gentle()) : null;
    if (frames && typeof el.animate === 'function') el.animate(frames, { duration: delta < 0 ? H.lossMs : H.gainMs, easing: 'cubic-bezier(.2,.8,.3,1)' });
  });
  return () => { if (typeof off === 'function') off(); el.remove?.(); };
}
