/* sparkles-slip.js - SPARKLES BY PLACE (B1 W5): the paper slip that shows what a finished game paid.
 * ONE FILE, COPIED byte for byte into every tree that shows it (SparklesSlipParityTests pins the copies).
 * The host claims, localises every line and posts { type: 'sparkles-receipt', credited, lines, stamp, sp,
 * gentle }; a tree wires bridge.on('sparkles-receipt', showSlip). No imports, no fetch, nothing at import.
 * IN and OUT for every piece: the slip slides up, the PAID stamp lands, Sparkles fly to the wallet chip and
 * it bumps; the slip leaves after 6 s. Zero pay: lines only. Gentle: the amount counts up only (fades, no
 * flight, no slam, no shake); a tree whose own motion switch is calmer passes showSlip(m, { calm: true })
 * for the same. Reduced motion: static. Nothing takes the pointer: clicks reach the end card under it. */

const HOLD_MS = 6000;
const SPARK = String.fromCharCode(0x2726); // the four-point spark, kept ASCII in source
const CSS = `
.sps-slip{position:fixed;left:50%;bottom:7vh;z-index:2147483000;width:min(300px,86vw);box-sizing:border-box;
 transform:translate(-50%,0);padding:14px 18px 16px;color:#2a2420;pointer-events:none;
 background:#f4ecd8 linear-gradient(180deg,rgba(255,255,255,.35),rgba(120,90,50,.08));
 font:14px/1.5 ui-monospace,Consolas,"Courier New",monospace;filter:drop-shadow(0 6px 10px rgba(30,20,10,.35))}
.sps-slip::after{content:"";position:absolute;left:0;right:0;bottom:-6px;height:6px;
 background:radial-gradient(circle at 6px 0,#f4ecd8 6px,transparent 6.5px) 0 0/12px 6px repeat-x}
.sps-amt{font-size:30px;font-weight:700;letter-spacing:.02em;margin-bottom:4px}
.sps-amt i{font-style:normal;font-size:20px;color:#9a6a12;margin-right:6px}
.sps-lines{margin:0;padding:0;list-style:none;text-transform:uppercase;font-size:12px;letter-spacing:.06em}
.sps-lines li+li{border-top:1px dashed rgba(42,36,32,.3);margin-top:3px;padding-top:3px}
.sps-stamp{position:absolute;right:12px;top:12px;padding:3px 8px;border:3px double #a3232b;border-radius:4px;
 color:#a3232b;font:900 18px/1 "Arial Black",Impact,sans-serif;letter-spacing:.14em;text-transform:uppercase;
 transform:rotate(-14deg);opacity:.85;mix-blend-mode:multiply}
.sps-chip{position:fixed;top:14px;right:16px;z-index:2147483000;pointer-events:none;padding:5px 12px;
 background:#f4ecd8;color:#2a2420;border:2px solid #2a2420;border-radius:999px;
 font:700 15px/1.2 ui-monospace,Consolas,"Courier New",monospace;box-shadow:0 3px 0 #2a2420}
.sps-chip i,.sps-fly{font-style:normal;color:#9a6a12}
.sps-fly{position:fixed;left:0;top:0;z-index:2147483001;pointer-events:none;font-size:18px}`;

let live = null;

/** What one receipt plays. Pure (tests): motion is 'full', 'gentle' or 'static'. */
export function slipPlan(m, reduced, calm) {
  const amount = Math.max(0, Math.round(Number(m && m.credited) || 0));
  const motion = reduced ? 'static' : ((m && m.gentle) || calm) ? 'gentle' : 'full';
  const lines = Array.isArray(m && m.lines) ? m.lines.filter((l) => typeof l === 'string' && l) : [];
  return { amount, lines, motion, stamp: amount > 0, chip: amount > 0 && Number.isFinite(m.sp),
    flight: amount > 0 && motion === 'full', sparks: Math.min(8, Math.max(3, Math.round(amount / 10))) };
}

function el(tag, cls, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text != null) e.textContent = text;
  return e;
}
function play(node, frames, opts) {
  try { return node.animate(frames, Object.assign({ fill: 'both' }, opts)); } catch (e) { return null; }
}
function countUp(node, from, to, ms, prefix) {
  if (ms <= 0 || from === to) { node.textContent = prefix + to; return; }
  const t0 = performance.now();
  const step = (t) => {
    const k = Math.min(1, Math.max(0, (t - t0) / ms)); // a frame stamp can predate t0
    node.textContent = prefix + Math.round(from + (to - from) * (1 - Math.pow(1 - k, 3)));
    if (k < 1) requestAnimationFrame(step);
  };
  requestAnimationFrame(step);
}

/** Take the slip (and chip) away. */
export function hideSlip() {
  const cur = live;
  if (!cur) return;
  live = null;
  clearTimeout(cur.timer);
  const out = (node, frames, ms, delay) => {
    if (cur.plan.motion === 'static') { node.remove(); return; }
    const a = play(node, frames, { duration: ms, delay: delay || 0, easing: 'ease-in' });
    if (a) a.onfinish = () => node.remove(); else node.remove();
  };
  out(cur.slip, cur.plan.motion === 'gentle' ? [{ opacity: 1 }, { opacity: 0 }]
    : [{ transform: 'translate(-50%,0)', opacity: 1 }, { transform: 'translate(-50%,40px)', opacity: 0 }], 320);
  if (cur.chip) out(cur.chip, cur.plan.motion === 'gentle' ? [{ opacity: 1 }, { opacity: 0 }]
    : [{ transform: 'translateY(0)' }, { transform: 'translateY(-160%)' }], 260, 220);
}

/** Show one receipt. A newer receipt replaces the one on screen. opts.calm: the tree's motion is calmer. */
export function showSlip(m, opts) {
  if (!m || m.type !== 'sparkles-receipt' || typeof document === 'undefined') return;
  const reduced = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  const plan = slipPlan(m, reduced, !!(opts && opts.calm));
  if (!plan.lines.length && !plan.amount) return;
  if (live) { clearTimeout(live.timer); live.slip.remove(); if (live.chip) live.chip.remove(); live = null; }
  if (!document.getElementById('sps-css')) {
    const st = el('style'); st.id = 'sps-css'; st.textContent = CSS; document.head.appendChild(st);
  }
  const full = plan.motion === 'full', gentle = plan.motion === 'gentle';
  const slip = el('div', 'sps-slip');
  slip.setAttribute('role', 'status');
  let amt = null, num = null;
  if (plan.amount) {
    amt = el('div', 'sps-amt'); amt.appendChild(el('i', null, SPARK)); num = el('b', null, '+0');
    amt.appendChild(num); slip.appendChild(amt);
  }
  const ul = el('ul', 'sps-lines');
  plan.lines.forEach((l) => ul.appendChild(el('li', null, l)));
  slip.appendChild(ul);
  document.body.appendChild(slip);
  let chip = null, chipNum = null;
  if (plan.chip) {
    chip = el('div', 'sps-chip'); chip.appendChild(el('i', null, SPARK + ' '));
    chipNum = el('b', null, String(m.sp - plan.amount)); chip.appendChild(chipNum);
    document.body.appendChild(chip);
  }
  live = { slip, chip, plan, timer: setTimeout(hideSlip, HOLD_MS) };
  if (plan.motion === 'static') {
    if (num) num.textContent = '+' + plan.amount;
    if (chipNum) chipNum.textContent = String(m.sp);
  } else {
    play(slip, gentle ? [{ opacity: 0 }, { opacity: 1 }]
      : [{ transform: 'translate(-50%,120%)', opacity: 0 }, { transform: 'translate(-50%,0)', opacity: 1 }],
    { duration: gentle ? 450 : 380, easing: 'cubic-bezier(.2,1.25,.4,1)' });
    if (chip) play(chip, gentle ? [{ opacity: 0 }, { opacity: 1 }]
      : [{ transform: 'translateY(-160%)' }, { transform: 'translateY(0)' }], { duration: 320, delay: 120, easing: 'ease-out' });
    if (num) countUp(num, 0, plan.amount, gentle ? 900 : 700, '+');
    if (gentle && chipNum) setTimeout(() => countUp(chipNum, m.sp - plan.amount, m.sp, 900, ''), 450);
  }
  if (!plan.stamp) return;
  const stamp = el('div', 'sps-stamp', typeof m.stamp === 'string' && m.stamp ? m.stamp : 'Paid');
  slip.appendChild(stamp);
  if (plan.motion === 'static') return;
  if (gentle) { play(stamp, [{ opacity: 0 }, { opacity: 0.85 }], { duration: 450, delay: 500 }); return; }
  play(stamp, [{ opacity: 0, transform: 'rotate(-14deg) scale(2.4)' },
    { opacity: 0.95, transform: 'rotate(-14deg) scale(0.94)', offset: 0.75 },
    { opacity: 0.85, transform: 'rotate(-14deg) scale(1)' }], { duration: 240, delay: 520, easing: 'ease-in' });
  play(slip, [{ transform: 'translate(-50%,0)' }, { transform: 'translate(-50%,3px) rotate(0.6deg)' },
    { transform: 'translate(-50%,-1px) rotate(-0.3deg)' }, { transform: 'translate(-50%,0)' }],
  { duration: 170, delay: 700, fill: 'none' });
  if (plan.flight && chip) setTimeout(() => { if (live && live.chip === chip) fly(amt, chip, chipNum, m.sp, plan); }, 820);
}

function fly(from, chip, chipNum, sp, plan) {
  const a = from.getBoundingClientRect(), b = chip.getBoundingClientRect();
  const x0 = a.left + 14, y0 = a.top + a.height / 2, x1 = b.left + 16, y1 = b.top + b.height / 2;
  let landed = 0;
  for (let i = 0; i < plan.sparks; i++) {
    const s = el('div', 'sps-fly', SPARK);
    document.body.appendChild(s);
    const lift = 60 + ((i * 37) % 70), frames = [];
    for (let k = 0; k <= 6; k++) {
      const t = k / 6, x = x0 + (x1 - x0) * t, y = y0 + (y1 - y0) * t - lift * 4 * t * (1 - t);
      frames.push({ transform: `translate(${x}px,${y}px) scale(${1 - 0.4 * t})`, opacity: k === 6 ? 0.4 : 1 });
    }
    const anim = play(s, frames, { duration: 520 + i * 40, delay: i * 70, easing: 'ease-in' });
    const land = () => {
      s.remove();
      landed++;
      chipNum.textContent = String(Math.round(sp - plan.amount + (plan.amount * landed) / plan.sparks));
      if (landed === plan.sparks) {
        play(chip, [{ transform: 'scale(1)' }, { transform: 'scale(1.22)' }, { transform: 'scale(1)' }],
          { duration: 260, easing: 'ease-out', fill: 'none' });
      }
    };
    if (anim) anim.onfinish = land; else land();
  }
}
