/* ui/demos/wave2-edges.js - hand-written previews for the rows that came after the design source. Same kit as the generated files. */
import { DEMOS } from './kit.js';
import './extras.js';   // its preview of the question card is replaced here, so it has to load first

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
