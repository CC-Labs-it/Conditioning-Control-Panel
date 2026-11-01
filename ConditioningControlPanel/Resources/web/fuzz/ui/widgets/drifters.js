/* Drifting Bubble (3, x1.5, up to 12): the DVD logo. Each one drifts, bounces
 * off the viewport edges and pays sim.bounce(); a corner hit pays x10 once
 * Corner Pop is owned. Capped at 12 live bubbles (BUILD plan section 6). */
import * as E from '../../sim/economy.js';

const SIZE = 72, SPEED = 90, CORNER_PX = 6;

export const drifters = {
  mounted: false,
  list: [],
  mount(app, r) {
    const want = r?.count ?? 1;
    while (this.list.length < Math.min(12, want)) this.add(app);
    this.mounted = true;
  },
  add(app) {
    const img = app.doc.createElement('img');
    img.src = 'assets/img/bubble.png'; img.alt = ''; img.className = 'drifter';
    img.style.cssText = `position:absolute;width:${SIZE}px;height:${SIZE}px;left:0;top:0;will-change:transform;opacity:0;transition:opacity 400ms ease;pointer-events:none`;
    app.doc.getElementById('widgets').appendChild(img);
    requestAnimationFrame(() => { img.style.opacity = '0.85'; });
    const a = Math.random() * Math.PI * 2;
    this.list.push({ img, x: Math.random() * (innerWidth - SIZE), y: Math.random() * (innerHeight - SIZE), vx: Math.cos(a) * SPEED, vy: Math.sin(a) * SPEED, last: 0 });
  },
  tick(now, app) {
    const fast = now < app.state.effects.deeperUntil ? 2 : 1;
    for (const d of this.list) {
      const dt = d.last ? Math.min(0.1, (now - d.last) / 1000) : 0;
      d.last = now;
      d.x += d.vx * dt * fast; d.y += d.vy * dt * fast;
      let hitX = false, hitY = false;
      if (d.x <= 0) { d.x = 0; d.vx = Math.abs(d.vx); hitX = true; }
      else if (d.x >= innerWidth - SIZE) { d.x = innerWidth - SIZE; d.vx = -Math.abs(d.vx); hitX = true; }
      if (d.y <= 0) { d.y = 0; d.vy = Math.abs(d.vy); hitY = true; }
      else if (d.y >= innerHeight - SIZE) { d.y = innerHeight - SIZE; d.vy = -Math.abs(d.vy); hitY = true; }
      if (hitX || hitY) {
        const corner = hitX && hitY || (hitX && (d.y <= CORNER_PX || d.y >= innerHeight - SIZE - CORNER_PX)) || (hitY && (d.x <= CORNER_PX || d.x >= innerWidth - SIZE - CORNER_PX));
        E.bounce(app.state, corner);
        if (E.has(app.state, 'bouncepop')) app.audio?.play('assets/audio/pop/Pop2.mp3', { gain: 0.35 });
        if (corner && E.has(app.state, 'corner')) app.audio?.play('assets/audio/sfx/golden_pop.mp3', { gain: 0.6 });
      }
      d.img.style.transform = `translate(${d.x}px, ${d.y}px)`;
    }
  },
};
