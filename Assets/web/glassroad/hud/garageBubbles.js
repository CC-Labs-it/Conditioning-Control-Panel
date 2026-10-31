/* Bubbles that float up the garage and pop under a finger. Nothing to win: a toy to play with while choosing a kart.
 * DOM only, so there is no shader to warm and no draw call; the layer sits behind the menu and in front of the scene.
 * Time comes from the garage frame, so a hidden page or a closed garage freezes them.
 * Gentle: half the pace, half as many, and a pop is a plain 320 ms fade. OS reduced motion and Motion off: none. */
import { popRungNext, semisToRate } from '../audio/sfxmap.js';
import { ladderSemis } from '../audio/mix.js';

export const GARAGE_BUBBLES = Object.freeze({ max: 7, maxPhone: 4, firstS: .4, everyS: [1, 2.4], riseMps: [46, 92],
  size: [52, 108], sizePhone: [48, 84], swayPx: [8, 26], popS: .24, softPopS: .32, streakS: 1.8, rungs: 8, drops: 6 });

const between = (rng, [a, b]) => a + (b - a) * rng();

/** The next pop's pitch: pops inside the streak window climb the same pentatonic ladder as the race's word pops. */
export function popPitch(streak, t) {
  return semisToRate(ladderSemis(popRungNext(streak, t, GARAGE_BUBBLES.streakS, GARAGE_BUBBLES.rungs)));
}

/** pop({ pitch }) plays the sound. leftEdge() = the first free pixel right of the menu; floor() = where they rise from
 * (a phone's menu fills the bottom of the screen, so they come out from behind its top edge). */
export function createGarageBubbles({ root, pop = () => {}, leftEdge = () => 0, floor = null, rng = Math.random, doc = globalThis.document }) {
  const layer = doc.createElement('div');
  layer.className = 'g-bubbles'; layer.setAttribute('aria-hidden', 'true');
  root.prepend(layer);
  const live = new Set(), streak = { rung: 0, t: -Infinity };
  let time = 0, next = GARAGE_BUBBLES.firstS, soft = false, disposed = false;

  function drop(b) { b.el.removeEventListener('pointerdown', b.press); b.el.remove(); live.delete(b); }

  function burst(b) {
    for (let i = 0; i < GARAGE_BUBBLES.drops; i++) {
      const d = doc.createElement('i'), a = (i + rng() * .7) / GARAGE_BUBBLES.drops * Math.PI * 2, far = b.size * (.55 + rng() * .35);
      d.style.setProperty('--dx', (Math.cos(a) * far).toFixed(1) + 'px');
      d.style.setProperty('--dy', (Math.sin(a) * far).toFixed(1) + 'px');
      b.el.append(d);
    }
  }

  function burstOf(b, e) {
    if (b.popAt >= 0 || (e && e.button > 0)) return;
    if (e && e.cancelable) e.preventDefault();
    b.popAt = time; b.popS = soft ? GARAGE_BUBBLES.softPopS : GARAGE_BUBBLES.popS;
    b.el.classList.add(soft ? 'g-bubble-soft' : 'g-bubble-pop');
    if (!soft) burst(b);
    pop({ pitch: popPitch(streak, time) });
  }

  function spawn(width, height) {
    const phone = width < 700, size = Math.round(between(rng, phone ? GARAGE_BUBBLES.sizePhone : GARAGE_BUBBLES.size));
    const left = Math.min(Math.max(0, leftEdge()), Math.max(0, width - size));
    const el = doc.createElement('div');
    el.className = 'g-bubble';
    el.style.width = el.style.height = size + 'px';
    const b = { el, size, x: left + (width - left - size) * rng(), y: Math.min(height, floor ? floor() : height), phase: rng() * Math.PI * 2,
      rise: between(rng, GARAGE_BUBBLES.riseMps) * Math.max(.7, height / 900), sway: between(rng, GARAGE_BUBBLES.swayPx), popAt: -1, popS: 0, press: null };
    b.press = e => burstOf(b, e);
    el.addEventListener('pointerdown', b.press);
    layer.append(el); live.add(b);
    return b;
  }

  function clear() { for (const b of [...live]) drop(b); }

  return {
    layer,
    get count() { return live.size; },
    /** spawn: new bubbles may start (the carousel is on screen). off: OS reduced motion or Motion off. */
    update(dt, { spawn: open = true, gentle = false, off = false } = {}) {
      if (disposed) return;
      if (off) { clear(); return; }
      soft = gentle; time += dt;
      const width = root.clientWidth || 0, height = root.clientHeight || 0, pace = gentle ? .5 : 1;
      if (open && width > 0 && time >= next && live.size < Math.floor((width < 700 ? GARAGE_BUBBLES.maxPhone : GARAGE_BUBBLES.max) * pace)) {
        spawn(width, height);
        next = time + between(rng, GARAGE_BUBBLES.everyS) / pace;
      }
      for (const b of [...live]) {
        if (b.popAt >= 0) { if (time - b.popAt >= b.popS) drop(b); continue; }
        b.y -= b.rise * pace * dt;
        if (b.y < -b.size) { drop(b); continue; }
        const x = b.x + Math.sin(time * .9 + b.phase) * b.sway;
        b.el.style.transform = `translate3d(${x.toFixed(1)}px,${b.y.toFixed(1)}px,0)`;
      }
    },
    dispose() { if (disposed) return; disposed = true; clear(); layer.remove(); },
  };
}
