// The Gooning Museum fate cards (ui/unlocks/fate/museum.js), as small code-drawn previews. Never names the ending.
import { DEMOS } from './kit.js';

const INK = '#2a2031';
const calm = () => matchMedia('(prefers-reduced-motion: reduce)').matches || document.body.classList.contains('gentle');
const brass = 'background:linear-gradient(#f3dc94,#cfa245 52%,#a87b25);border:2px solid ' + INK + ';border-radius:3px;color:#2a1a04;font:600 8px/1.15 Georgia,serif;text-align:center;';

DEMOS.ft_ticket = k => {
  k.page('pink');
  const b = k.btn({ y: 84, skin: 'pink' });
  const t = k.add(22, 14, 'width:112px;height:54px;background:#f4e7c6;border:2px solid ' + INK + ';border-radius:3px;box-shadow:3px 3px 0 rgba(42,32,49,.2);transform:rotate(-4deg);',
    '<div style="position:absolute;left:6px;top:4px;right:28px;text-align:center;font:700 6px sans-serif;letter-spacing:2px">MUSEUM</div>' +
    '<div style="position:absolute;left:4px;top:17px;right:26px;text-align:center;font:800 12px Georgia,serif;color:#b8323f">ADMIT ONE</div>' +
    '<div style="position:absolute;left:6px;bottom:6px;right:28px;text-align:center;font:600 6px sans-serif">10 NOV 2026</div>' +
    '<div style="position:absolute;right:22px;top:4px;bottom:4px;border-left:2px dashed ' + INK + '"></div>' +
    '<div style="position:absolute;right:3px;top:16px;font:800 7px monospace;color:#b8323f;writing-mode:vertical-rl">004211</div>' +
    '<div style="position:absolute;left:46px;top:-6px;width:10px;height:10px;border-radius:50%;background:#d8414f;border:2px solid ' + INK + '"></div>');
  k.every(1300, () => k.press(b));
  if (!calm()) k.anim(t, [{ transform: 'rotate(-4deg)' }, { transform: 'rotate(-2deg)' }, { transform: 'rotate(-4deg)' }], 3000, { iterations: Infinity });
};

DEMOS.ft_placard = k => {
  k.page('pink');
  const c = k.counter({ y: 18, v: 4100 });
  const b = k.btn({ y: 70, skin: 'pink' });
  const plq = k.add(10, 74, 'width:70px;padding:2px 3px;' + brass, 'Button. Pressed 4,100 times.');
  k.add(172, 46, 'width:62px;padding:2px 3px;' + brass, 'Counter. Still counting.');
  let n = 4100;
  k.every(900, () => { k.press(b); n += 1; c.set(n); plq.textContent = 'Button. Pressed ' + n.toLocaleString('en-US') + ' times.'; });
};

DEMOS.ft_rope = k => {
  k.page('pink');
  const b = k.btn({ y: 56, skin: 'pink' });
  const post = 'width:5px;height:28px;background:linear-gradient(90deg,#8a6420,#f0d37e,#b48630);border:1.5px solid ' + INK + ';border-radius:2px;';
  k.add(80, 66, post); k.add(153, 66, post);
  const rope = k.add(80, 66, 'width:82px;height:30px;transform-origin:50% 0;',
    '<svg viewBox="0 0 100 100" preserveAspectRatio="none" width="100%" height="100%" style="overflow:visible"><path d="M3 4 Q50 190 97 4" fill="none" stroke="' + INK + '" stroke-width="6" vector-effect="non-scaling-stroke"/><path d="M3 4 Q50 190 97 4" fill="none" stroke="#9c1832" stroke-width="3.5" vector-effect="non-scaling-stroke"/></svg>');
  const f = k.finger(200, 130);
  k.every(1600, () => f.tapEl(b, () => {
    k.press(b);
    if (!calm()) k.anim(rope, [{ transform: 'scaleY(1)' }, { transform: 'scaleY(1.12)' }, { transform: 'scaleY(.97)' }, { transform: 'scaleY(1)' }], 900);
  }));
};

DEMOS.ft_gift = k => {
  k.page('pink');
  k.add(120, 6, 'width:2px;height:12px;margin-left:-30px;background:' + INK + ';transform:rotate(14deg);');
  k.add(120, 6, 'width:2px;height:12px;margin-left:28px;background:' + INK + ';transform:rotate(-14deg);');
  const sign = k.add(78, 17, 'width:84px;padding:3px 0;background:#f7ecd2;color:#a42b3a;border:2px solid ' + INK + ';border-radius:3px;text-align:center;font:800 10px Georgia,serif;letter-spacing:2px;transform-origin:50% -10px;box-shadow:2px 2px 0 rgba(42,32,49,.2);', 'GIFT SHOP');
  const tiles = [0, 1, 2, 3].map((i) => k.add(34 + i * 44, 52, 'width:36px;height:36px;background:#fff6fb;border:1px solid #c58aa6;'));
  tiles.forEach((t, i) => k.add(34 + i * 44, 92, 'width:36px;text-align:center;font:600 8px sans-serif;', String([30, 75, 120, 400][i])));
  k.every(2200, () => {
    const t = tiles[Math.floor(Math.random() * tiles.length)];
    k.anim(t, [{ transform: 'scale(1)' }, { transform: 'scale(.9)' }, { transform: 'scale(1)' }], 260);
    if (!calm()) k.anim(sign, [{ transform: 'rotate(0)' }, { transform: 'rotate(5deg)' }, { transform: 'rotate(-3deg)' }, { transform: 'rotate(0)' }], 1400);
  });
};
