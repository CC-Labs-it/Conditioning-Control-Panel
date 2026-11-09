/* Demos for the three late Sissy rows (owner 2026-11-08): Order Tracking, Posture Check, Thank-You Notes.
 * Registered by importing this module beside the other demo modules in ui/fork.js. */
import { DEMOS } from './kit.js';

const INK = '#7a2f4b', ROSE = '#c96a8c', BLUSH = '#f2c5d2', SERIF = 'Georgia,serif';

DEMOS.si_track = (k) => {
  k.page('pink');
  const steps = ['Packed', 'Shipped', 'Out for delivery', 'At your door', 'Please try it on'];
  const card = k.add(18, 28, `width:204px;height:84px;box-sizing:border-box;padding:8px 10px;background:#fffaf6;border:1px solid #c9a3b2;border-radius:8px;box-shadow:0 2px 0 #d9a7bb;color:#3b2430;font:11px/1.2 Arial,sans-serif;`);
  k.add(10, 9, 'width:24px;height:20px;border-radius:3px;background:linear-gradient(#d9a46c,#c18a52);border:1px solid #8a5a2c;', '', card);
  k.add(19, 9, `width:6px;height:22px;background:${BLUSH};`, '', card);
  const order = k.add(44, 8, 'font:700 8px/1 Consolas,monospace;letter-spacing:.5px;color:#8e6578;', 'ORDER #1104 · TRACKING', card);
  const status = k.add(44, 20, 'font:700 14px/1.1 Arial,sans-serif;white-space:nowrap;', steps[0], card);
  k.add(14, 50, 'width:174px;height:2px;background:#ead3dc;', '', card);
  const dots = steps.map((_, i) => k.add(10 + i * 43, 45, `width:10px;height:10px;box-sizing:border-box;border-radius:50%;border:2px solid #e2c3cf;background:#fff;`, '', card));
  k.add(10, 64, 'font:600 10px/1 Arial,sans-serif;color:#7a5566;', '1x frilly uniform · size: yours', card);
  const f = k.finger(205, 132);
  let step = 0, n = 1104;
  const paint = () => {
    status.textContent = steps[step];
    status.style.color = step === 4 ? '#b0386a' : '#3b2430';
    status.style.fontStyle = step === 4 ? 'italic' : 'normal';
    order.textContent = 'ORDER #' + n + ' · TRACKING';
    dots.forEach((d, i) => { d.style.background = i <= step ? ROSE : '#fff'; d.style.borderColor = i <= step ? ROSE : '#e2c3cf'; });
  };
  paint();
  k.every(900, () => f.tapEl(card, () => {
    k.press(card);
    if (step < 4) step += 1; else { step = 0; n += 1; }
    paint();
    k.float(196, 22, '+46', `color:${ROSE};font-size:13px`);
  }));
};

DEMOS.si_posture = (k) => {
  k.page('pink');
  k.add(36, 16, `width:168px;height:104px;box-sizing:border-box;background:#fff;border:1px solid ${ROSE};border-radius:8px;box-shadow:3px 3px 0 ${BLUSH};`);
  k.add(0, 26, `width:${k.W}px;text-align:center;font:700 9px/1 Consolas,monospace;letter-spacing:1px;color:#b5587b;`, 'POSTURE CHECK');
  k.add(0, 42, `width:${k.W}px;text-align:center;font:italic 15px/1.2 ${SERIF};color:${INK};`, 'Sit up straight.');
  k.add(52, 70, `width:136px;height:8px;border-radius:4px;background:${BLUSH};`);
  const bar = k.add(52, 70, `width:0;height:8px;border-radius:4px;background:${ROSE};`);
  const hint = k.add(0, 86, `width:${k.W}px;text-align:center;font:600 11px/1 ${SERIF};color:#9a6a80;`, 'Hold still');
  const f = k.finger(214, 130);
  let t = 0, fidgeted = false;
  k.every(100, () => {
    t += 0.1;
    const cycle = t % 7;
    if (cycle < 0.15) { fidgeted = false; hint.textContent = 'Hold still'; hint.style.color = '#9a6a80'; }
    if (cycle > 1 && cycle < 1.3 && !fidgeted) { fidgeted = true; f.to(196, 118); }
    const held = cycle < 1.2 ? cycle : Math.max(0, cycle - 1.3);
    const v = Math.min(1, held / 3);
    bar.style.width = (v * 136) + 'px';
    if (v >= 1 && hint.textContent === 'Hold still') { hint.textContent = 'Very still. Very good.'; hint.style.color = '#b5860f'; k.float(150, 60, '+10s', `color:${ROSE};font-size:13px`); }
  });
};

DEMOS.si_notes = (k) => {
  k.page('pink');
  const notes = ['Thank you for doing as you were told. It suits you.', 'A note to say how good you are being. Keep the bow on.', 'Thank you. We will be thinking of you in lace.'];
  const tile = k.add(16, 40, `width:62px;height:62px;box-sizing:border-box;border-radius:8px;background:#fff;border:2px solid ${ROSE};display:grid;place-items:center;font:700 11px Arial;color:${INK};`, 'Buy');
  const f = k.finger(46, 120);
  let i = 0;
  k.every(3600, () => f.tapEl(tile, () => {
    k.press(tile);
    const note = k.add(92, 18, `width:138px;box-sizing:border-box;padding:7px 9px;background:#fffdf7;border:1px solid #e3c2cf;border-radius:3px;box-shadow:2px 3px 0 ${BLUSH};color:#5a2440;font:italic 11px/17px "Segoe Print","Bradley Hand","Comic Sans MS",cursive;rotate:-2deg;`, notes[i++ % notes.length]);
    k.add(0, 0, 'display:block;text-align:right;font-size:10px;color:#b5587b;margin-top:2px;position:static;', 'With love, the House', note);
    k.anim(note, [{ opacity: 0, transform: 'translate(20px,-6px)' }, { opacity: 1, transform: 'none', offset: 0.12 }, { opacity: 1, offset: 0.85 }, { opacity: 0, transform: 'translateY(-8px)' }], 3200);
    k.after(3200, () => note.remove());
  }));
};
