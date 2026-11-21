// The Panel fate card (ui/unlocks/fate/llama.js), as a small code-drawn preview. Never names the ending.
import { DEMOS } from './kit.js';

const INK = '#2a2031';
const calm = () => matchMedia('(prefers-reduced-motion: reduce)').matches || document.body.classList.contains('gentle');
const emoji = 'font:22px/1 "Segoe UI Emoji","Apple Color Emoji","Noto Color Emoji",sans-serif;';

DEMOS.ft_panel = k => {
  k.page('pink');
  const b = k.btn({ y: 88, skin: 'pink' });
  const panel = [52, 92, 132].map((x, i) => k.add(x, i === 1 ? 14 : 18, emoji + (i === 1 ? 'font-size:26px;' : ''), '\u{1F999}'));
  k.add(36, 40, 'width:136px;height:22px;background:#b5824f;border:2px solid ' + INK + ';border-radius:2px;');
  k.add(86, 44, 'width:28px;height:12px;background:#efcf86;border:2px solid ' + INK + ';transform:rotate(-6deg);');
  k.every(1400, () => k.press(b));
  if (!calm()) k.anim(panel[1], [{ transform: 'translateY(0)' }, { transform: 'translateY(2px) scaleY(.96)' }, { transform: 'translateY(0)' }], 700, { iterations: Infinity });
};
