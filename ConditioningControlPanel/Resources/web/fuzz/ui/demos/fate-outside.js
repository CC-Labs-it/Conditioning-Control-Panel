/* Demos for the Go Outside fate cards (ui/unlocks/fate/outside.js): A Window, Fresh Air, Touch Grass, Door Ajar. Hand
 * written (dev/gen-items.mjs knows nothing of these rows), drawn with the unlocks' own art and rules, silent as every demo
 * is: a bird shows as rings. Loaded by ui/fork.js beside the generated demo files. */
import { DEMOS } from './kit.js';
import { skyAt, mix } from '../unlocks/fate/outside/sky.js';
import { windowFrameUrl, windowBackUrl, flyerUrl, INK } from '../unlocks/fate/outside/art.js';
import { makeBlades, brush, step } from '../unlocks/fate/outside/grass-model.js';
import { doorWidth } from '../unlocks/fate/outside/rules.js';

const img = (k, x, y, w, h, src) => k.add(x, y, `width:${w}px;height:${h}px;background:url("${src}") center/100% 100% no-repeat;`);
const tiles = (k, n, y = 104) => Array.from({ length: n }, (_, i) => k.add(18 + i * 36, y, 'width:30px;height:30px;border-radius:5px;background:#f6cf62;border:1.5px solid #b98a1c;box-shadow:0 2px 0 rgba(0,0,0,.2);'));

/* A Window: the sky runs a day in a few seconds (the real one keeps your clock). */
DEMOS.ft_window = (k) => {
  k.page('#2b2440');
  const c = k.counter({ v: 52000, y: 12, css: 'color:#efeaff;' });
  const b = k.btn({ w: 70, h: 26, x: 150, y: 62, skin: 'pink' });
  const s = 0.62, W = 172 * s, H = 152 * s, X = 18, Y = 36;
  img(k, X, Y, W, H, windowBackUrl());
  const pane = k.add(X + 21 * s, Y + 19 * s, `width:${130 * s}px;height:${98 * s}px;overflow:hidden;border-radius:2px;`);
  const hill = k.add(0, 0, `left:0;bottom:0;top:auto;width:100%;height:42%;border-radius:60% 70% 0 0 / 40% 50% 0 0;`, null, pane);
  const sun = k.add(0, 0, 'width:12px;height:12px;border-radius:50%;background:#ffd34d;box-shadow:0 0 0 3px rgba(255,233,160,.5);', null, pane);
  img(k, X, Y, W, H, windowFrameUrl());
  let hour = 9;
  const paint = () => {
    const sky = skyAt(hour);
    pane.style.background = `linear-gradient(${sky.top}, ${mix(sky.top, sky.bottom, 0.55)} 55%, ${sky.bottom})`;
    hill.style.background = sky.hill;
    const body = sky.sun.up ? sky.sun : sky.moon;
    sun.style.background = sky.sun.up ? '#ffd34d' : '#f4f0d8';
    sun.style.boxShadow = sky.sun.up ? '0 0 0 3px rgba(255,233,160,.5)' : 'none';
    sun.style.opacity = body.up ? '1' : '0';
    sun.style.left = `${body.x * 130 * s - 6}px`;
    sun.style.top = `${body.y * 98 * s * 0.62 - 6}px`;
  };
  paint();
  k.every(120, () => { hour = (hour + 0.25) % 24; paint(); }, true);
  k.every(1000, () => { c.add(130); k.float(X + W / 2, Y + H * 0.45, '+130', 'color:#efeaff;'); }, true);
  const f = k.finger(200, 130);
  k.every(2200, () => f.tapEl(b, () => { k.press(b); c.add(40); }), true);
};

/* Fresh Air: a gust carries leaves across, the tiles lean and settle, a bird somewhere. */
DEMOS.ft_air = (k) => {
  k.page('#2b2440');
  k.counter({ v: 310000, y: 12, css: 'color:#efeaff;' });
  k.btn({ w: 70, h: 26, x: 85, y: 52, skin: 'pink' });
  const row = tiles(k, 6);
  const gust = () => {
    const dir = Math.random() < 0.5 ? 1 : -1;
    for (let i = 0; i < 4; i++) {
      const e = img(k, 0, 0, 14, 13, flyerUrl(i === 3 ? 2 : i % 2));
      const y0 = 20 + Math.random() * 80, x0 = dir > 0 ? -20 : 250, x1 = dir > 0 ? 250 : -20;
      k.anim(e, [
        { transform: `translate(${x0}px,${y0}px) rotate(0deg)` },
        { transform: `translate(${(x0 + x1) / 2}px,${y0 + 14}px) rotate(${200 * dir}deg)` },
        { transform: `translate(${x1}px,${y0 - 4}px) rotate(${420 * dir}deg)` },
      ], 2400, { delay: i * 220, fill: 'none' });
      k.after(2400 + i * 220, () => e.remove());
    }
    row.forEach((t, i) => k.anim(t, [{ rotate: '0deg', translate: '0 0' }, { rotate: `${5 * dir}deg`, translate: `${3 * dir}px 0`, offset: 0.3 }, { rotate: `${-2 * dir}deg`, translate: '0 0', offset: 0.6 }, { rotate: '0deg', translate: '0 0' }], 1300, { delay: (dir > 0 ? i : 5 - i) * 140, fill: 'none' }));
    k.after(900, () => k.sound(dir > 0 ? 220 : 20, 26, '#bfe3a0').forEach((w) => k.after(2400, () => w.remove())));
  };
  k.every(3600, gust);
};

/* Touch Grass: a finger brushes the grass; each blade pays once, then rests. */
DEMOS.ft_grass = (k) => {
  k.page('#2b2440');
  const c = k.counter({ v: 640000, y: 12, css: 'color:#efeaff;' });
  k.btn({ w: 70, h: 26, x: 85, y: 50, skin: 'pink' });
  const H = 36, cv = k.el.ownerDocument.createElement('canvas');
  cv.width = 480; cv.height = H * 2;
  cv.style.cssText = `position:absolute;left:0;top:${150 - H}px;width:240px;height:${H}px;`;
  k.el.appendChild(cv);
  const g = cv.getContext('2d');
  const blades = makeBlades(240, H - 4);
  let t = 0;
  const f = k.finger(10, 150 - 12);
  let fx = 10;
  k.frame((dt) => {
    t += dt * 1000;
    step(blades, dt, 0.07 * Math.sin(t / 1000 * 0.55));
    g.setTransform(2, 0, 0, 2, 0, 0);
    g.clearRect(0, 0, 240, H);
    g.lineCap = 'round';
    for (const b of blades) {
      const tx = b.x + Math.sin(b.a) * b.h, ty = H - Math.cos(b.a) * b.h;
      g.beginPath(); g.moveTo(b.x, H); g.quadraticCurveTo(b.x + Math.sin(b.a * 0.45) * b.h * 0.5, H - b.h * 0.58, tx, ty);
      g.strokeStyle = INK; g.globalAlpha = 0.7; g.lineWidth = 3.6; g.stroke();
      g.globalAlpha = 1; g.strokeStyle = ['#5d9a3e', '#79b24f', '#4a8634'][b.hue]; g.lineWidth = 2.2; g.stroke();
    }
    g.fillStyle = '#3f6e2a'; g.fillRect(0, H - 3, 240, 3);
  });
  k.every(3200, () => {
    fx = 14; f.to(fx, 150 - 12);
    let n = 0;
    const sweep = () => {
      fx += 12;
      f.to(fx, 150 - 12);
      const paid = brush(blades, fx, 10, 12, t, 2600).length;
      if (paid) { n += paid; c.add(paid * 70); }
      if (fx < 226) k.after(40, sweep);
      else if (n) k.float(200, 104, `+${(n * 70).toLocaleString('en-US')}`, 'color:#bfe3a0;');
    };
    k.after(380, sweep);
  });
};

/* Door Ajar: white light at the edge, a little wider with each buy. */
DEMOS.ft_door = (k) => {
  k.page('#2b2440');
  const c = k.counter({ v: 1150000, y: 12, css: 'color:#efeaff;' });
  k.btn({ w: 70, h: 26, x: 80, y: 50, skin: 'pink' });
  const glow = k.add(0, 0, 'top:0;right:0;left:auto;height:150px;width:0;background:linear-gradient(to left, rgba(255,253,240,.6), rgba(255,253,240,0));transition:width 1s ease-out;');
  const core = k.add(0, 0, 'top:0;right:0;left:auto;height:150px;width:0;background:#fffdf4;box-shadow:0 0 10px 2px rgba(255,252,236,.6);transition:width 1s ease-out;');
  let later = 0;
  const open = () => { const w = doorWidth(later) * 0.6; core.style.width = `${w}px`; glow.style.width = `${w * 3 + 8}px`; };
  k.after(300, open);
  const f = k.finger(120, 135);
  let row = [];
  const restart = () => {
    row.forEach((t) => t.remove());
    later = 0; open();
    row = tiles(k, 5);
    row.forEach((t, i) => k.after(900 + i * 1100, () => f.tapEl(t, () => { t.remove(); c.add(-50000); later += 3; open(); })));
  };
  restart();
  k.every(7000, restart, true);
};
