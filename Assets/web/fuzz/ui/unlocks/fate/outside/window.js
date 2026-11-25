/* ui/unlocks/fate/outside/window.js - A Window (ft_window, Go Outside seed). A small crayon window on the page, a movable
 * dock object. Its sky follows the player's real local time: dawn, day, dusk, night with a few stars, a sun or a moon on
 * its arc, a hill and one tree, clouds drifting by. It pays its row's own per-second rate (the sim does that); the window
 * only looks. Gentle and reduced motion: the clouds and stars hold still, and the sky still changes with the hour. */

import { skyAt, hourOf, mix } from './sky.js';
import { WIN_W, WIN_H, PANE, windowFrameUrl, windowBackUrl, cloud, hill, bodies, star } from './art.js';
import { LITE } from '../../../perf.js';

const PAD = 6;   // the art's margin: the pictures are 12 px larger than the window
const STARS = [[0.12, 0.16], [0.3, 0.08], [0.47, 0.22], [0.66, 0.12], [0.83, 0.26], [0.2, 0.36], [0.74, 0.4], [0.9, 0.08], [0.55, 0.42]];

export function mountWindow(ctx) {
  const still = () => ctx.gentle || ctx.reduced;
  /* min false: it asks to open when there is room, like the theme screens (a saved hidden state still wins) */
  const host = ctx.dock({ title: ctx.item.name, icon: ctx.item.emoji, width: WIN_W + PAD * 2 + 4, edge: 'left', min: false });
  const doc = host.ownerDocument;
  const W = WIN_W + PAD * 2, H = WIN_H + PAD * 2;
  const casing = ctx.el('div', `position:relative;width:${W}px;height:${H}px;margin:0 auto;pointer-events:auto;filter:drop-shadow(3px 4px 0 rgba(30,20,40,.18));`, null, host);
  casing.dataset.dockSurface = '';
  casing.setAttribute('role', 'img');
  const back = doc.createElement('img');
  back.alt = ''; back.src = windowBackUrl(); back.draggable = false;
  back.style.cssText = `position:absolute;left:0;top:0;width:${W}px;height:${H}px;pointer-events:none;`;
  const pane = doc.createElement('div');
  pane.style.cssText = `position:absolute;left:${PAD + PANE.x}px;top:${PAD + PANE.y}px;width:${PANE.w}px;height:${PANE.h}px;overflow:hidden;border-radius:2px;transition:background 2s linear;`;
  const frame = doc.createElement('img');
  frame.alt = ''; frame.src = windowFrameUrl(); frame.draggable = false;
  frame.style.cssText = `position:absolute;left:0;top:0;width:${W}px;height:${H}px;pointer-events:none;`;
  casing.append(back, pane, frame);

  const loops = [];
  /* stars, then the sun and the moon, then clouds, then the hill in front */
  const starLayer = doc.createElement('div');
  starLayer.style.cssText = 'position:absolute;inset:0;transition:opacity 2s linear;';
  STARS.forEach(([x, y], i) => {
    const s = star(doc, i % 3 === 0 ? 7 : 5);
    s.style.left = `${Math.round(x * PANE.w)}px`; s.style.top = `${Math.round(y * PANE.h)}px`;
    starLayer.appendChild(s);
    if (!still()) loops.push(ctx.anim(s, [{ opacity: 1 }, { opacity: 0.35 }, { opacity: 1 }], { duration: 2600 + i * 430, iterations: Infinity, delay: -i * 700, easing: 'ease-in-out' }));
  });
  const { sun, moon } = bodies(doc);
  for (const b of [sun, moon]) b.style.transition = 'opacity 1.5s linear';
  const clouds = (LITE ? [0.9, 0.62] : [0.9, 0.62, 0.75]).map((k) => cloud(doc, k));
  const ground = hill(doc, PANE.w, Math.round(PANE.h * 0.42));
  pane.append(starLayer, sun, moon, ...clouds.map((c) => c.el), ground.el);
  clouds.forEach((c, i) => {
    const y = 6 + i * 15 + Math.random() * 6;
    const x = Math.random() * (PANE.w - 30);
    c.el.style.top = `${Math.round(y)}px`;
    c.el.style.left = `${Math.round(x)}px`;
    if (still()) return;
    /* one slow lap each, from off the left to off the right, starting somewhere along it */
    const ms = 52000 + i * 17000 + Math.random() * 12000;
    c.el.style.left = '0px';
    loops.push(ctx.anim(c.el, [{ transform: 'translateX(-64px)' }, { transform: `translateX(${PANE.w + 8}px)` }], { duration: ms, iterations: Infinity, delay: -Math.random() * ms, easing: 'linear' }));
  });
  /* Gentle switched on in the pause card: the sky holds still from the resume on (clouds back to their resting spots). */
  const settle = () => {
    if (!still() || !loops.length) return;
    for (const a of loops.splice(0)) { try { a.cancel(); } catch { /* gone */ } }
    clouds.forEach((c, i) => { c.el.style.left = `${Math.round(10 + i * 38)}px`; });
  };

  const place = (el, body, size) => {
    el.style.opacity = body.up ? '1' : '0';
    if (!body.up) return;
    el.style.left = `${Math.round(body.x * PANE.w - size / 2)}px`;
    el.style.top = `${Math.round(body.y * PANE.h * 0.62 - size / 2)}px`;
  };
  let phase = '';
  const paint = () => {
    const sky = skyAt(hourOf(new Date()));
    pane.style.background = `linear-gradient(${sky.top}, ${mix(sky.top, sky.bottom, 0.55)} 55%, ${sky.bottom})`;
    starLayer.style.opacity = String(sky.stars);
    place(sun, sky.sun, 28);
    place(moon, sky.moon, 24);
    for (const c of clouds) c.setColour(sky.cloud);
    ground.setColour(sky.hill, mix(sky.hill, '#10180c', 0.25));
    if (sky.phase !== phase) { phase = sky.phase; casing.dataset.sky = phase; casing.setAttribute('aria-label', `A window. It is ${phase === 'day' ? 'daytime' : phase} outside.`); }
  };
  paint();
  /* the real clock, read once a minute (the page's own game clock stops on a pause; the sky outside does not wait, but it
   * only gets repainted while the game runs) */
  ctx.every(60000, paint);
  ctx.on('resume', () => { settle(); paint(); });
  return {};
}
