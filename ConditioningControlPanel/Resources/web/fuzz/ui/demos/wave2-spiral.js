/* ui/demos/wave2-spiral.js - hand-written previews for the rows that came after the design source. Same kit as the generated files.
 * These draw the real thing: the Loom's field and the same patterns the game morphs through, only quicker. */
import { DEMOS } from './kit.js';
import { paintLoom, spiralBus } from '../../fx/spiral-bus.js';
import { createMorphClock, driftPair, groundOf, morphFrame, DRIFT_PERIOD_S } from '../../fx/spiral-patterns.js';

function stageCanvas(k) {
  const c = document.createElement('canvas');
  c.width = k.W; c.height = k.H;
  c.style.cssText = `position:absolute;left:0;top:0;width:${k.W}px;height:${k.H}px;`;
  k.el.appendChild(c);
  return c.getContext('2d', { alpha: false });
}

const pairNow = () => spiralBus.target.slice();

/* The plain spiral walks through its colours, at about eight times the game's pace. Two swatches show where it has got to. */
DEMOS.sp_colors = (k) => {
  k.page('dark');
  const g = stageCanvas(k);
  const dots = [0, 1].map((i) => k.add(k.W - 40 + i * 15, k.H - 19, 'width:12px;height:12px;border-radius:50%;border:1.5px solid #fff;'));
  let sec = 0, phase = 0, spent = 1;
  k.frame((dt) => {
    sec += dt * 8;
    phase += dt * 0.1;
    spent += dt;
    if (spent < 1 / 20) return;
    spent = 0;
    const p = driftPair(pairNow(), sec % DRIFT_PERIOD_S);
    paintLoom(g, morphFrame(0, null, 0, p, groundOf(p[0])), phase, k.W, k.H);
    dots[0].style.background = p[0];
    dots[1].style.background = p[1];
  });
};

/* The real morph, a shape every couple of seconds, the whole deck in a row. */
DEMOS.sp_shapes = (k) => {
  k.page('dark');
  const g = stageCanvas(k);
  const clock = createMorphClock({ morphS: 1.5, holdS: 0.7 });
  clock.setOn(true);
  let phase = 0, spent = 1;
  k.frame((dt) => {
    clock.step(dt);
    phase += dt * 0.12;
    spent += dt;
    if (spent < 1 / 24) return;
    spent = 0;
    const p = pairNow();
    paintLoom(g, morphFrame(clock.from, clock.to, clock.t, p, groundOf(p[0])), phase, k.W, k.H);
  });
};

export default DEMOS;
