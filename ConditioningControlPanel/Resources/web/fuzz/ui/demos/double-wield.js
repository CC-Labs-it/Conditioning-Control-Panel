/* Demo for Double Wielding (dualwield): a little mouse beside the button. Left and right halves light in turn and each
 * press lands on the button; now and then both light together and two numbers rise. Silent, as every demo is. */
import { DEMOS } from './kit.js';

const LEFT = '#ff7fb8', RIGHT = '#6fd6ff';

DEMOS.dualwield = (k) => {
  const c = k.counter({ v: 60000, y: 14 });
  const b = k.btn({ w: 104, h: 44, x: 46, y: 64, skin: 'pink' });
  const mouse = k.add(178, 52, 'width:38px;height:56px;box-sizing:border-box;border:3px solid #28232b;border-radius:19px 19px 22px 22px;background:#fff0cf;overflow:hidden;');
  const half = (left) => {
    const e = mouse.ownerDocument.createElement('i');
    e.style.cssText = `position:absolute;top:0;${left ? 'left:0' : 'right:0'};width:50%;height:22px;background:transparent;transition:background .08s;`;
    mouse.appendChild(e);
    return e;
  };
  const lh = half(true), rh = half(false);
  k.add(195, 52, 'width:3px;height:22px;background:#28232b;');
  k.add(178, 74, 'width:38px;height:3px;background:#28232b;');
  const hit = (left) => {
    const h = left ? lh : rh;
    h.style.background = left ? LEFT : RIGHT;
    k.after(140, () => { h.style.background = 'transparent'; });
    k.press(b);
    c.add(4);
    k.float(left ? 70 : 126, 52, '+4', `color:${left ? '#b03a7a' : '#1f7fa8'};font-size:16px;font-weight:700;`);
  };
  const beats = [[true], [false], [true], [false], [true, false], [true], [false], [true, false]];
  let i = 0;
  k.every(520, () => {
    const beat = beats[i++ % beats.length];
    for (const left of beat) hit(left);
  });
};
