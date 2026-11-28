/* ui/demos/fate-button.js - demos for the Living Button fate cards (ft_depth, ft_cursor, ft_pressed, ft_glass). Hand
 * written, loaded by ui/fork.js after the generated demos. Each shows the card's effect on a little page and never what it
 * leads to. Silent, as every demo is. */
import { DEMOS } from './kit.js';
import { cursorSvg, TIP, leanFor, curve, smooth } from '../unlocks/fate/button/model.js';

const calmNow = () => matchMedia('(prefers-reduced-motion: reduce)').matches || document.body.classList.contains('gentle');

/* A Little Depth: a pointer circles the page and the button, the counter and two small windows lean at their own depths. */
DEMOS.ft_depth = (k) => {
  k.page('pink');
  const c = k.counter({ v: 412000, y: 22 });
  const b = k.btn({ w: 76, h: 30, x: 82, y: 66, skin: 'pink' });
  const w1 = k.add(14, 74, 'width:46px;height:36px;border:2px solid #111;border-radius:4px;background:#fff8fb;box-shadow:2px 2px 0 #0003;');
  const w2 = k.add(182, 22, 'width:44px;height:38px;border:2px solid #111;border-radius:4px;background:#fff1c9;box-shadow:2px 2px 0 #0003;');
  const dot = k.add(0, 0, 'width:7px;height:7px;margin:-3px;border-radius:50%;background:#2a1830;opacity:.55;');
  const objs = [[b, 1], [c.el, 0.5], [w1, 0.7], [w2, 0.85]];
  const calm = calmNow();
  k.frame((_dt, t) => {
    const nx = calm ? 0.6 : Math.cos(t * 0.9), ny = calm ? -0.4 : Math.sin(t * 1.3) * 0.8;
    dot.style.left = (120 + nx * 92) + 'px';
    dot.style.top = (75 + ny * 58) + 'px';
    for (const [el, d] of objs) {
      const l = leanFor(nx, ny, d);
      el.style.translate = (l.x * 1.6) + 'px ' + (l.y * 1.6) + 'px';
      el.style.rotate = (l.r * 1.6) + 'deg';
    }
  });
};

/* Second Cursor: another cursor drifts in, presses the button, the counter goes up, and it leaves. */
DEMOS.ft_cursor = (k) => {
  k.page('pink');
  const c = k.counter({ v: 412000, y: 26 });
  const b = k.btn({ w: 76, h: 30, x: 82, y: 70, skin: 'pink' });
  const cur = k.add(0, 0, 'width:22px;height:22px;transform-origin:' + TIP + 'px ' + TIP + 'px;', cursorSvg({ size: 22, edge: '#9a2a74' }));
  const target = { x: 132, y: 86 };
  const calm = calmNow();
  const at = (p, s) => { cur.style.transform = 'translate(' + (p.x - TIP) + 'px,' + (p.y - TIP) + 'px) scale(' + (s || 1) + ')'; };
  let t = 0;
  const loop = 4.2;
  k.frame((dt) => {
    t = (t + dt) % loop;
    const from = { x: 250, y: 20 }, out = { x: -20, y: 160 }, bend = { x: 200, y: 110 }, bend2 = { x: 70, y: 80 };
    if (calm) { at(target); cur.style.opacity = t < 0.4 ? String(t / 0.4) : t > 3.6 ? String((loop - t) / 0.6) : '1'; return; }
    cur.style.opacity = '1';
    if (t < 1.5) at(curve(from, bend, target, smooth(t / 1.5)));
    else if (t < 2.8) at(target, (t > 1.62 && t < 1.74) || (t > 2.2 && t < 2.32) ? 0.86 : 1);
    else at(curve(target, bend2, out, Math.pow(smooth((t - 2.8) / 1.4), 2)));
  });
  const pressAt = (ms) => k.every(4200, () => k.after(ms, () => { k.press(b); c.add(31); k.float(150, 70, '+31'); }));
  pressAt(1620); pressAt(2200);
};

/* Pressed: a finger taps, and the whole little page dips and darkens at its rim for a moment. */
DEMOS.ft_pressed = (k) => {
  k.page('pink');
  const page = k.add(0, 0, 'width:240px;height:150px;');
  const c = k.counter({ v: 412000, y: 26 });
  page.append(c.el);
  const b = k.btn({ w: 76, h: 30, x: 82, y: 70, skin: 'pink' });
  page.append(b);
  const rim = k.add(0, 0, 'width:240px;height:150px;opacity:0;box-shadow:inset 0 0 34px 8px rgba(24,8,34,.55);pointer-events:none;');
  const f = k.finger(196, 128);
  const calm = calmNow();
  k.every(1300, () => f.tapEl(b, () => {
    k.press(b); c.add(12);
    if (calm) { k.anim(rim, [{ opacity: 0 }, { opacity: 0.4 }, { opacity: 0 }], 500); return; }
    k.anim(rim, [{ opacity: 0 }, { opacity: 0.8, offset: 0.22 }, { opacity: 0 }], 300, { easing: 'ease-out' });
    k.anim(page, [{ translate: '0 0' }, { translate: '0 2.5px', offset: 0.28 }, { translate: '0 0' }], 220, { easing: 'ease-out' });
  }));
};

/* Glass: faint curved highlights sit over the page edges, and a slow sheen crosses now and then. */
DEMOS.ft_glass = (k) => {
  k.page('dark');
  k.counter({ v: 412000, y: 26 });
  k.btn({ w: 76, h: 30, x: 82, y: 70, skin: 'pink' });
  const pane = k.add(0, 0, 'width:240px;height:150px;overflow:hidden;border-radius:12px;box-shadow:inset 0 0 0 1px rgba(255,255,255,.22),inset 0 0 30px rgba(0,0,0,.25);');
  k.add(-30, 6, 'width:200px;height:140px;border-radius:50%;border-top:2px solid rgba(255,255,255,.5);transform:rotate(-8deg);-webkit-mask-image:linear-gradient(90deg,transparent 4%,#000 30%,#000 52%,transparent 80%);mask-image:linear-gradient(90deg,transparent 4%,#000 30%,#000 52%,transparent 80%);', null, pane);
  k.add(76, 8, 'width:190px;height:136px;border-radius:50%;border-bottom:2px solid rgba(255,255,255,.32);transform:rotate(-8deg);-webkit-mask-image:linear-gradient(270deg,transparent 6%,#000 30%,transparent 64%);mask-image:linear-gradient(270deg,transparent 6%,#000 30%,transparent 64%);', null, pane);
  const sheen = k.add(0, -40, 'width:46px;height:230px;opacity:0;background:linear-gradient(90deg,transparent,rgba(255,255,255,.16) 50%,transparent);', null, pane);
  const calm = calmNow();
  k.every(3600, () => {
    if (calm) k.anim(sheen, [{ opacity: 0, transform: 'translateX(60px) skewX(-18deg)' }, { opacity: 0.9, transform: 'translateX(60px) skewX(-18deg)' }, { opacity: 0, transform: 'translateX(60px) skewX(-18deg)' }], 2400);
    else k.anim(sheen, [{ opacity: 0, transform: 'translateX(-60px) skewX(-18deg)' }, { opacity: 1, offset: 0.2 }, { opacity: 1, offset: 0.8 }, { opacity: 0, transform: 'translateX(280px) skewX(-18deg)' }], 2200);
  });
};
