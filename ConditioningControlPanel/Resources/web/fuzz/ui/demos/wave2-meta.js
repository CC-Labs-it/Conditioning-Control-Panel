/* ui/demos/wave2-meta.js - hand-written previews for the rows that came after the design source. Same kit as the generated files.
 * Levels gets its new preview here, so basics loads first: the row's generated demo still shows the old bar. */
import { DEMOS } from './kit.js';
import './basics.js';

const FONT = '"Barlow", "Segoe UI", system-ui, sans-serif';

DEMOS.levels = (k) => {
  let lvl = 3, bank = 0;
  const label = k.add(0, 16, `width:240px;text-align:center;font:600 17px ${FONT};`, 'Level 3');
  k.add(30, 48, 'width:180px;height:18px;background:#fff;border:1px solid #555;border-radius:9px;overflow:hidden;');
  const fill = k.add(31, 49, 'width:0;height:18px;background:linear-gradient(90deg,#ff4fa8,#ffb0d8);border-radius:9px;');
  const col = k.btn({ w: 96, h: 28, x: 72, y: 84, skin: 'pink', label: 'Collect 1', css: 'opacity:0;transition:opacity .2s,transform .1s;' });
  const f = k.finger();
  const fillUp = () => {
    fill.style.transition = 'none';
    fill.style.width = '0';
    k.after(40, () => { fill.style.transition = 'width .85s linear'; fill.style.width = '178px'; });
  };
  const loop = () => {
    bank = 0;
    col.style.opacity = 0;
    fillUp();
    for (let i = 1; i <= 3; i++) {
      k.after(i * 950, () => {
        bank = i;
        col.innerHTML = `Collect ${bank}`;
        col.style.opacity = 1;
        k.press(col);
        if (i < 3) fillUp();
      });
    }
    k.after(3500, () => f.tapEl(col, () => {
      k.press(col);
      lvl += bank;
      label.textContent = `Level ${lvl}`;
      k.float(150, 74, '+9,000', 'color:#a3232b;font-size:22px');
      k.float(92, 80, '+1', 'color:#ff4fa8;font-size:14px');
      k.float(188, 86, '+1', 'color:#ff4fa8;font-size:14px');
      col.style.opacity = 0;
    }));
  };
  loop();
  k.every(4800, loop);
};

DEMOS.guide = (k) => {
  k.page('#f4f2ec');
  const rows = [[20, 66], [70, 66], [120, 66], [170, 30], [170, 102], [214, 30]];
  const box = (x, y, on) => k.add(x, y, `width:22px;height:22px;box-sizing:border-box;border-radius:3px;border:2px ${on ? 'solid #ff4fa8' : 'dashed #8a867d'};background:${on ? '#ff4fa8' : '#fff'};transition:background .2s;`);
  const line = (x, y, w, tilt) => k.add(x, y, `width:${w}px;height:2px;background:#8a867d;transform-origin:0 50%;transform:rotate(${tilt}deg);`);
  line(42, 77, 28, 0); line(92, 77, 28, 0); line(142, 72, 36, -38); line(142, 82, 36, 38); line(192, 41, 22, 0);
  const nodes = rows.map(([x, y], i) => box(x, y, i < 3));
  nodes[4].style.opacity = 0.25;
  const tip = k.add(120, 8, `width:112px;padding:3px 5px;background:#fff;border:1px solid #333;font:600 10px/1.1 ${FONT};opacity:0;transition:opacity .2s;`, 'Next on this route');
  const f = k.finger();
  const loop = () => {
    tip.style.opacity = 0;
    nodes[3].style.background = '#fff';
    f.tapEl(nodes[3], () => { tip.style.opacity = 1; nodes[3].style.background = '#ffd0e8'; });
  };
  loop();
  k.every(2600, loop);
};

DEMOS.panel = (k) => {
  const stray = k.add(18, 18, 'width:34px;height:14px;background:#d4d0c8;border:1px solid #666;transition:opacity .25s;');
  const spiral = k.add(14, 58, 'width:66px;height:66px;border-radius:50%;background:repeating-conic-gradient(#ff4fa8 0 30deg,#ffd0e8 30deg 60deg);transition:opacity .25s;');
  const card = k.add(112, 10, 'width:118px;height:130px;background:#fff;border:1px solid #333;border-top:3px solid #ff4fa8;');
  const row = (y, label) => {
    k.add(6, y + 2, `font:600 11px ${FONT};`, label, card);
    k.add(78, y, 'width:30px;height:16px;background:#ffd0e8;border:1px solid #666;border-radius:8px;', null, card);
    return k.add(92, y + 2, 'width:12px;height:12px;background:#333;border-radius:50%;transition:left .15s;', null, card);
  };
  const a = row(14, 'Button'), b = row(46, 'Spiral'), c = row(78, 'Words');
  c.style.left = '92px';
  const f = k.finger();
  const set = (knob, on) => { knob.style.left = on ? '92px' : '80px'; };
  const tapKnob = (knob, then) => f.tap(card.offsetLeft + 92, card.offsetTop + knob.offsetTop + 6, then);
  const loop = () => {
    tapKnob(a, () => { set(a, false); stray.style.opacity = 0; });
    k.after(800, () => tapKnob(b, () => { set(b, false); spiral.style.opacity = 0; }));
    k.after(1700, () => tapKnob(b, () => { set(b, true); spiral.style.opacity = 1; }));
    k.after(2500, () => tapKnob(a, () => { set(a, true); stray.style.opacity = 1; }));
  };
  loop();
  k.every(3600, loop);
};

export default DEMOS;
