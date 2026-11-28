/* ui/sell-all.js - the joke ad "Sell your {cur} for more {cur}" (j_swap, owner 2026-11-09). Pressing it shows the counter
 * at 0 for SELL_HOLD_MS, then counts back up to the real number. DISPLAY ONLY: the wallet never changes, so buying works
 * as always (the shop reads the real wallet). Pause, Wake Up and panic put the real counter back at once. The show runs
 * on the game clock (ctx.frame), so a paused page does not wait it out. */
import { whole } from '../sim/format.js';

export const SELL_ALL_AD = 'j_swap';
export const SELL_HOLD_MS = 10000;
export const SELL_RISE_MS = 1200;

/** What the counter shows elapsedMs into the show for a real wallet of `real`; null = the show is over. */
export function sellAllShown(real, elapsedMs) {
  if (!(elapsedMs < SELL_HOLD_MS + SELL_RISE_MS)) return null;
  if (elapsedMs < SELL_HOLD_MS) return 0;
  const k = (elapsedMs - SELL_HOLD_MS) / SELL_RISE_MS;
  const eased = 1 - (1 - k) ** 3;
  return Math.max(0, Math.floor(real * eased));
}

const CSS = '#counter[data-sold] > :is(#count, #fz-roll) { display: none !important; } #fz-sold { display: inline-block; }';

/** Run the show once; a press while one runs does nothing. Answers true when a show started. */
export function sellAllJoke(ctx) {
  const counter = ctx.parts.counter;
  if (!counter || counter.hasAttribute('data-sold')) return false;
  const doc = counter.ownerDocument;
  if (!doc.getElementById('fz-sold-css')) {
    const st = doc.createElement('style');
    st.id = 'fz-sold-css';
    st.textContent = CSS;
    doc.head.appendChild(st);
  }
  const shown = doc.createElement('span');
  shown.id = 'fz-sold';
  shown.textContent = '0';
  counter.insertBefore(shown, counter.querySelector('#fz-roll, #count'));
  counter.setAttribute('data-sold', '');
  let elapsed = 0, live = true, said = false;
  const r = counter.getBoundingClientRect();
  ctx.float(r.left + r.width / 2, r.top, 'Sold. Every last one.', 'font-size:15px;');
  const offs = [];
  const end = () => {
    if (!live) return;
    live = false;
    for (const off of offs) off?.();
    shown.remove();
    counter.removeAttribute('data-sold');
  };
  offs.push(ctx.frame((sec) => {
    if (!live) return;
    elapsed += sec * 1000;
    const n = sellAllShown(ctx.state.fuzz, elapsed);
    if (n === null) { end(); return; }
    if (n > 0 && !said) {
      said = true;
      const b = counter.getBoundingClientRect();
      ctx.float(b.left + b.width / 2, b.top, 'Bought it all back.', 'font-size:15px;');
    }
    shown.textContent = whole(n);
  }));
  offs.push(ctx.on('pause', end), ctx.on('suspend', ({ on } = {}) => { if (on) end(); }));
  ctx.cleanup?.(end);
  return true;
}
