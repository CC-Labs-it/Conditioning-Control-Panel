/* ui/demos/wave2-edges.js - hand-written previews for the rows that came after the design source. Same kit as the generated files. */
import { DEMOS } from './kit.js';
import './extras.js';   // its preview of the question card is replaced here, so it has to load first

const BAR = 'background:#7b5cff;transform-origin:50% 100%;';

/* ASMR: the page in the middle, and a player at the edge with its bars standing still. Off air for now. */
DEMOS.asmr = (k) => {
  k.page('grey');
  k.add(30, 40, 'width:90px;height:34px;background:#d4d0c8;border:2px outset #fff;display:grid;place-items:center;font:13px "Times New Roman",serif;', 'Click');
  k.add(30, 90, 'width:90px;height:44px;background:#e4e1da;border:1px solid #8a867d;');
  const box = k.add(144, 28, 'width:88px;height:94px;background:#e4e1da;border:1px solid #8a867d;border-top:2px solid #7b5cff;');
  k.add(5, 4, 'font:600 9px sans-serif;color:#444;', '🎧 ASMR', box);
  k.add(5, 18, 'width:18px;height:18px;border-radius:50%;background:#7b5cff;', null, box);
  k.add(27, 17, 'font:700 10px sans-serif;', 'Closer', box);
  k.add(27, 29, 'font:8px sans-serif;color:#444;border:1px solid #8a867d;padding:0 2px;', 'Off air', box);
  const bars = [];
  for (let i = 0; i < 10; i++) bars.push(k.add(6 + i * 8, 48, `width:6px;height:30px;${BAR}transform:scaleY(.08);`, null, box));
  void bars;
  for (let i = 0; i < 10; i++) k.add(6 + i * 8, 84, 'width:7px;height:3px;background:#7b5cff;opacity:.25;', null, box);
};

/* Pop-Up Question: same spot every time, four answers, one of them pays. */
DEMOS.quiz = (k) => {
  k.page('grey');
  const card = k.add(14, 12, 'width:212px;height:126px;background:#fff;border:1px solid #333;border-radius:4px;box-shadow:2px 3px 0 rgba(0,0,0,.28);overflow:hidden;');
  k.add(0, 0, 'width:100%;height:15px;padding-left:5px;background:#7b5cff;color:#fff;font:600 9px/15px sans-serif;', 'Quick question', card);
  const said = k.add(0, 21, 'width:100%;text-align:center;font:600 13px sans-serif;', 'Could you stop right now?', card);
  const labels = ['Easily', 'Why would I', 'Soon', 'Give me a minute'];
  const cells = labels.map((t, i) => k.add(6 + (i % 2) * 102, 46 + (i >> 1) * 38, 'width:98px;height:32px;display:grid;place-items:center;background:#d4d0c8;border:2px outset #fff;font:600 10px sans-serif;color:#111;', t, card));
  let step = 0;
  k.every(1300, () => {
    step = (step + 1) % 3;
    if (step === 1) { cells[1].style.background = '#1f9d55'; cells[1].style.color = '#fff'; said.textContent = 'That is what we wanted.'; k.float?.(120, 60, '+ a lot'); }
    if (step === 2) { cells[1].style.background = '#d4d0c8'; cells[1].style.color = '#111'; said.textContent = 'Could you stop right now?'; }
  });
};

export default DEMOS;
