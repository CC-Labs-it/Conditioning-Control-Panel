/* Demos for the theme fate cards (ui/unlocks/fate/themes.js): Little Bubble (ft_bubble), Scan Line (ft_scan) and Name Tag
 * (ft_tag). Silent, as every demo is. Never spoils an ending. */
import { DEMOS } from './kit.js';

const GREEN = '#3dff8e';
const MONO = 'Consolas,"Courier New",monospace';
const HAND = '"Segoe Print","Comic Sans MS","Chalkboard SE",cursive';

/** A row of small shop tiles; returns the elements. */
function tiles(k, n, y, css) {
  const out = [];
  for (let i = 0; i < n; i++) out.push(k.add(28 + i * 38, y, `width:32px;height:30px;border-radius:7px;box-sizing:border-box;${css}`));
  return out;
}

/* Little Bubble: every buy, the bubble in the corner gets a little bigger. */
DEMOS.ft_bubble = (k) => {
  k.page('pink');
  const c = k.counter({ v: 60000, y: 8, css: 'color:#b0306f;' });
  const b = k.btn({ w: 76, h: 26, y: 38, skin: 'pink', label: 'pretty' });
  const shop = tiles(k, 5, 76, 'background:linear-gradient(#ffe08a,#f6c446);border:2px solid #c99a20;');
  const bubble = k.add(14, 0, 'width:28px;height:28px;bottom:14px;top:auto;transition:width .6s cubic-bezier(.3,1.6,.5,1),height .6s cubic-bezier(.3,1.6,.5,1);');
  bubble.innerHTML = '<i style="position:absolute;inset:14%;border-radius:50%;background:radial-gradient(circle at 38% 34%,rgba(255,226,242,.55),rgba(255,122,192,.42) 58%,rgba(232,70,160,.62));"></i>'
    + '<i style="position:absolute;inset:0;background:url(assets/img/bubble.png) center/100% 100% no-repeat;"></i>';
  k.anim(bubble, [{ translate: '0 0' }, { translate: '0 -3px' }, { translate: '0 0' }], 3200, { iterations: Infinity });
  const f = k.finger(200, 130);
  let size = 28, n = 0;
  k.every(1500, () => {
    const t = shop[n % shop.length];
    f.tapEl(t, () => {
      k.anim(t, [{ transform: 'scale(1)' }, { transform: 'scale(.86)' }, { transform: 'scale(1)' }], 260);
      c.add(-4000);
      size = n % 6 === 5 ? 28 : Math.min(48, size + 4);
      bubble.style.width = bubble.style.height = `${size}px`;
      n += 1;
    });
    k.press(b);
  });
};

/** The drone page, a counter, a button and a row of tiles: the scan's targets. */
function dronePage(k) {
  k.page('#070b09');
  const c = k.counter({ v: 23387, y: 8, css: `color:${GREEN};font-family:${MONO};font-size:15px;` });
  const b = k.btn({ w: 70, h: 24, y: 36, label: 'ACCEPT', css: `background:linear-gradient(135deg,#c8ffe0,${GREEN});color:#062;font:700 10px ${MONO};border:0;` });
  const shop = tiles(k, 5, 80, `background:#0f2a1d;border:1px solid #1c6b42;`);
  return { c, b, shop };
}

/** One green sweep down the stage. targets: [{el, y}], skip: {el, x0, x1, y0, y1} or null. */
function sweep(k, line, segL, segR, targets, skip, mark) {
  let y = -4, done = new Set(), skipped = false;
  line.style.display = '';
  const stop = { on: true };
  k.frame((dt) => {
    if (!stop.on) return;
    y += dt * 52;
    line.style.top = `${y}px`;
    if (skip && y >= skip.y0 - 2 && y <= skip.y1 + 2) {
      segL.style.width = `${Math.max(0, skip.x0 - 4)}px`;
      segR.style.left = `${skip.x1 + 4}px`; segR.style.width = `${Math.max(0, k.W - skip.x1 - 4)}px`; segR.style.display = '';
      if (!skipped) {
        skipped = true;
        mark.style.left = `${skip.x0 - 2}px`; mark.style.top = `${skip.y0 - 12}px`;
        k.anim(mark, [{ opacity: 0 }, { opacity: 1, offset: 0.2 }, { opacity: 1, offset: 0.7 }, { opacity: 0 }], 1200);
      }
    } else { segL.style.width = `${k.W}px`; segR.style.display = 'none'; }
    for (const t of targets) {
      if (done.has(t) || y < t.y) continue;
      done.add(t);
      k.anim(t.el, [{ boxShadow: `0 0 0 0 ${GREEN}` }, { boxShadow: `0 0 0 2px ${GREEN}, 0 0 8px ${GREEN}` }, { boxShadow: `0 0 0 0 ${GREEN}` }], 600, { fill: 'none' });
      k.float(t.el.offsetLeft + t.el.offsetWidth - 6, t.el.offsetTop - 6, '+90', `color:${GREEN};font:700 9px ${MONO};`);
    }
    if (y > k.H + 4) { stop.on = false; line.style.display = 'none'; }
  });
}

function scanParts(k) {
  const line = k.add(0, -4, 'width:100%;height:0;');
  const segL = k.add(0, -1, `height:2px;width:${k.W}px;background:${GREEN};box-shadow:0 0 5px 1px rgba(61,255,142,.7);`, null, line);
  const segR = k.add(0, -1, `height:2px;display:none;background:${GREEN};box-shadow:0 0 5px 1px rgba(61,255,142,.7);`, null, line);
  const mark = k.add(0, 0, `padding:0 3px;border:1px solid ${GREEN};background:#070b09;color:${GREEN};font:700 7px/1.3 ${MONO};opacity:0;transform:rotate(-6deg);`, 'SKIP');
  return { line, segL, segR, mark };
}

/* Scan Line: once a minute the line sweeps down; everything it crosses lights green and pays. */
DEMOS.ft_scan = (k) => {
  const { c, b, shop } = dronePage(k);
  const { line, segL, segR, mark } = scanParts(k);
  const targets = [{ el: c.el, y: 18 }, { el: b, y: 48 }, ...shop.map((el) => ({ el, y: 95 }))];
  k.every(3600, () => { c.add(450); sweep(k, line, segL, segR, targets, null, mark); });
};

/* Name Tag: your name on a pinned tag. The line always leaves a gap round it. */
DEMOS.ft_tag = (k) => {
  const { c, b, shop } = dronePage(k);
  const tag = k.add(14, 30, `width:58px;border:1.5px solid #2b2328;border-radius:4px;background:#fffdf6;overflow:hidden;text-align:center;transform:rotate(-3deg);box-shadow:1px 2px 0 rgba(0,0,0,.35);`);
  tag.innerHTML = '<div style="background:#d2263a;color:#fff;font:800 7px/1.1 system-ui,sans-serif;letter-spacing:.1em;padding:2px 0 1px;">HELLO<div style="font:600 5px/1.2 system-ui,sans-serif;letter-spacing:0;">my name is</div></div>'
    + `<div style="font:700 9px/1.2 ${HAND};color:#1d1a22;padding:2px 0 3px;">UNIT 4017</div>`;
  k.add(41, 26, 'width:7px;height:7px;border-radius:50%;background:radial-gradient(circle at 35% 30%,#ff8d8d,#c3122a 60%,#6d0715);');
  const { line, segL, segR, mark } = scanParts(k);
  const targets = [{ el: c.el, y: 18 }, { el: b, y: 48 }, ...shop.map((el) => ({ el, y: 95 }))];
  k.every(3600, () => { c.add(450); sweep(k, line, segL, segR, targets, { x0: 12, x1: 74, y0: 26, y1: 62 }, mark); });
};
