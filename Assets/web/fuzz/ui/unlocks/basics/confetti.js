/* The Confetti unlock throws a handful from the main button on every purchase. */
import { CONFIG } from '../../../data/config.js';
import { LITE } from '../../perf.js';
import { btnCenter, burst, fresh, spawn } from './fx.js';

const colours = ['var(--accent)', 'var(--accent-2)', 'var(--accent-soft)', 'var(--accent-deep)'];
const liveLimit = LITE ? Math.ceil(CONFIG.MAX_PARTICLES / 2) : CONFIG.MAX_PARTICLES;
const between = (lo, hi) => lo + Math.random() * (hi - lo);

function paperFlight(origin, bounds, lane) {
  const sideRoom = lane < 0 ? origin.x : bounds.w - origin.x;
  const reach = lane * Math.min(bounds.w * .48, Math.max(0, sideRoom - 14), 520);
  const lift = Math.min(Math.max(0, origin.y - 12), between(85, 155) * (1 - .28 * Math.abs(lane)));
  const drop = Math.max(0, bounds.h + 12 - origin.y + lift);
  const riseMs = between(780, 1060);
  const duration = riseMs + 550 + drop / between(.15, .21);
  const crest = riseMs / duration;
  const sway = between(11, 32);
  const phase = between(0, 2 * Math.PI);
  const waves = between(1.3, 2.3) * 2 * Math.PI;
  const tilt = between(-55, 55);
  const turn = between(100, 260) * (Math.random() < .5 ? -1 : 1);
  const frames = [];

  // Sample the arc once. The browser tweens between poses while the game clock owns pause and cleanup.
  for (let step = 0; step <= 32; step++) {
    const t = step / 32;
    const rise = Math.min(1, t / crest);
    const fall = Math.max(0, (t - crest) / (1 - crest));
    const flutter = Math.sin(phase + waves * fall);
    const x = reach * (1 - (1 - t) ** 3) + sway * flutter * fall * (2 - fall);
    const y = -lift * rise * (2 - rise) + drop * fall * fall * (2 - fall);
    const bank = tilt + turn * t + 26 * flutter * fall;
    const roll = tilt + turn * 2 * t;
    frames.push({
      transform: `translate(${x.toFixed(1)}px,${y.toFixed(1)}px) rotate(${bank.toFixed(1)}deg) rotateY(${roll.toFixed(1)}deg)`,
      opacity: Math.min(1, t / .065, (1 - t) / .18),
    });
  }
  return { frames, duration };
}

export function mountConfetti(ctx) {
  let live = 0;
  ctx.css('.gentle .buy-confetti { visibility:hidden; }');
  function shower(wanted) {
    const origin = btnCenter(ctx);
    if (ctx.gentle) {
      burst(ctx, origin.x, origin.y, { color: colours[0], reach: 90 });
      return;
    }
    const bounds = ctx.bounds();
    const count = Math.min(liveLimit - live, LITE ? Math.ceil(wanted / 2) : wanted);
    for (let i = 0; i < count; i++) {
      // Give each strip its own slice of the fan, including on narrow screens and small purchases.
      const lane = 2 * (i + Math.random()) / count - 1;
      const { frames, duration } = paperFlight(origin, bounds, lane);
      const width = between(5, 8), height = between(8, 14);
      const strip = spawn(ctx, `left:${origin.x - width / 2}px;top:${origin.y - height / 2}px;width:${width}px;height:${height}px;`
        + `background:${colours[i % colours.length]};border-radius:1px;box-shadow:inset 0 1px #fff6,inset 0 -1px #0002;`);
      strip.className = 'buy-confetti';
      strip.setAttribute('aria-hidden', 'true');
      const anim = ctx.anim(strip, frames, { duration, delay: between(0, 360), fill: 'backwards' });
      live++;
      anim.onfinish = anim.oncancel = () => { live--; strip.remove(); };
    }
  }
  ctx.on('buy', ({ cost }) => shower(Math.min(28, 10 + Math.round(Math.log10(Math.max(1, cost)) * 3))));
  if (fresh(ctx)) shower(30);
}
