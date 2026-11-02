/* The four endings' shows. Each runs between the fade and the credits, draws above the end fade, and ends on its own or on
 * the player's press. Copy comes from data/content/endings.js. */

import { COPY } from './copy.js';

const wait = (ctx, ms) => new Promise((res) => ctx.after(ms, res));

/** A full-page stage on the ending layer, above the end fade. */
function stage(ctx, css) {
  return ctx.el('div', `position:absolute;inset:0;overflow:hidden;pointer-events:auto;${css}`, null, 'ending');
}

/** The honest link and the way out. Resolves when the player goes on, or after `ms`. */
function outro(ctx, host, end, css, ms = 26000) {
  const row = ctx.el('div', `position:absolute;left:16px;right:16px;bottom:10%;display:flex;flex-direction:column;align-items:center;gap:10px;text-align:center;${css}`, null, host);
  ctx.el('div', 'font-size:18px;max-width:520px;', end.closing, row);
  if (end.link) {
    const a = ctx.el('button', 'min-height:44px;padding:6px 16px;background:transparent;color:inherit;border:1px solid currentColor;font-size:15px;cursor:pointer;', end.link.label, row);
    a.type = 'button';
    a.addEventListener('click', () => ctx.openLink(end.link.url));
  }
  const go = ctx.el('button', 'min-height:44px;padding:6px 16px;background:transparent;color:inherit;border:0;text-decoration:underline;font-size:15px;cursor:pointer;', COPY.go, row);
  go.type = 'button';
  if (!ctx.gentle) ctx.anim(row, [{ opacity: 0 }, { opacity: 1 }], { duration: 900 });
  return new Promise((res) => {
    go.addEventListener('click', () => res());
    ctx.after(ms, res);
  });
}

/* ---- Count Up: one, two, two and a half, and it never gets to three. ---- */
async function trance(ctx, end) {
  const s = stage(ctx, 'background:radial-gradient(circle at 50% 110%,#463a86,#15123a 70%);color:#dfe4ff;font-family:"Barlow","Segoe UI",system-ui,sans-serif;');
  const word = ctx.el('div', 'position:absolute;left:16px;right:16px;top:32%;text-align:center;font-size:clamp(30px,7vw,60px);', null, s);
  const track = ctx.el('div', 'position:absolute;left:50%;top:48%;width:min(70vw,420px);height:6px;margin-left:calc(min(70vw,420px)/-2);border-radius:3px;background:rgba(255,255,255,.14);overflow:hidden;', null, s);
  const bar = ctx.el('div', 'width:100%;height:100%;background:#b79cff;transform-origin:0 50%;transform:scaleX(0);', null, track);
  let i = 0, fill = 0;
  ctx.frame((dt) => { const cap = 0.3 + 0.62 * (1 - 1 / (1 + i * 0.4)); fill += (cap - fill) * dt * 0.5; bar.style.transform = `scaleX(${fill.toFixed(3)})`; });
  const show = () => {
    word.textContent = end.count[i % end.count.length];
    ctx.anim(word, [{ opacity: 0 }, { opacity: 1, offset: 0.25 }, { opacity: 1, offset: 0.8 }, { opacity: 0.2 }], { duration: 2400, fill: 'forwards' });
    ctx.audio.play('assets/audio/sfx/count_tick.mp3', { bus: 'sfx', gain: 0.4 });
    i += 1;
  };
  show();
  ctx.every(2400, show);
  await wait(ctx, 7000);
  await outro(ctx, s, end, '', 30000);
}

/* ---- Empty Smile: pink, a blank face, hearts. ---- */
async function bimbo(ctx, end) {
  ctx.css('@font-face{font-family:"Fredoka";src:url("assets/fonts/fredoka-latin.woff2") format("woff2");font-weight:300 700;font-display:swap;}');  // the theme's own sheet went with it
  const s = stage(ctx, 'background:#ff9fd0;color:#8a0f5c;font-family:"Fredoka","Segoe UI",system-ui,sans-serif;');
  const face = ctx.el('div', 'position:absolute;left:50%;top:18%;width:min(60vw,300px);height:min(52vw,260px);transform:translateX(-50%);', null, s);
  ['16%', '72%'].forEach((x) => ctx.el('div', `position:absolute;top:12%;left:${x};width:12%;aspect-ratio:1;border-radius:50%;background:#fff;`, null, face));
  ctx.el('div', 'position:absolute;left:8%;right:8%;top:38%;height:46%;box-sizing:border-box;border:clamp(10px,3vw,18px) solid #fff;border-top:0;border-radius:0 0 50% 50%/0 0 100% 100%;', null, face);
  if (!ctx.gentle) ctx.anim(face, [{ transform: 'translateX(-50%) scale(1)' }, { transform: 'translateX(-50%) scale(1.04)' }, { transform: 'translateX(-50%) scale(1)' }], { duration: 3600, iterations: Infinity });
  const heart = () => {
    const { w, h } = ctx.bounds();
    const e = ctx.el('div', `position:absolute;left:${ctx.rng() * (w - 30)}px;top:${ctx.rng() * h * 0.5}px;color:#fff;font-size:${18 + ctx.rng() * 18}px;`, '♡', s);
    ctx.anim(e, [{ opacity: 0 }, { opacity: 1, offset: 0.3 }, { opacity: 0 }], { duration: 2400 }).finished?.then(() => e.remove(), () => e.remove());
  };
  ctx.every(900, heart);
  await wait(ctx, 3500);
  await outro(ctx, s, end, 'color:#8a0f5c;');
}

/* ---- Unit Complete: the terminal goes off a line at a time. ---- */
async function drone(ctx, end) {
  const s = stage(ctx, 'background:#070b09;color:#3dff8e;font-family:Consolas,"Courier New",monospace;');
  const box = ctx.el('div', 'position:absolute;left:50%;top:18%;transform:translateX(-50%);width:min(84vw,420px);font-size:clamp(17px,3.6vw,24px);line-height:1.6;', null, s);
  const lines = end.lines.map((t) => ctx.el('div', 'transition:opacity .5s;opacity:0;', `> ${t}`, box));
  for (let i = 0; i < lines.length; i++) { lines[i].style.opacity = 1; ctx.audio.play('assets/audio/sfx/tally_tick.mp3', { bus: 'sfx', gain: 0.5 }); await wait(ctx, 750); }
  for (let i = 0; i < lines.length - 1; i++) { lines[i].style.opacity = 0.08; ctx.audio.play('assets/audio/sfx/thud.mp3', { bus: 'sfx', gain: 0.25 }); await wait(ctx, 520); }
  await wait(ctx, 800);
  await outro(ctx, s, end, 'color:#3dff8e;');
}

/* ---- The Debut: curtains, one light, one slow bow. ---- */
async function sissy(ctx, end) {
  const s = stage(ctx, 'background:#17080f;color:#fbeef0;font-family:Georgia,"Times New Roman",serif;');
  ctx.el('div', 'position:absolute;left:50%;top:0;width:min(70vw,420px);height:64%;transform:translateX(-50%);background:linear-gradient(rgba(255,240,200,.05),rgba(255,240,200,.45));clip-path:polygon(44% 0,56% 0,100% 100%,0 100%);', null, s);
  ctx.el('div', 'position:absolute;left:50%;top:60%;width:min(58vw,340px);height:7%;transform:translateX(-50%);border-radius:50%;background:rgba(255,240,200,.5);', null, s);
  const who = ctx.el('div', 'position:absolute;left:50%;top:42%;width:150px;height:110px;margin-left:-75px;transform-origin:50% 100%;', null, s);
  ctx.el('div', 'position:absolute;left:0;right:0;top:42px;height:58px;display:grid;place-items:center;background:#d4d0c8;border:2px outset #fff;color:#000;font:20px "Times New Roman",serif;', 'Click', who);
  ctx.el('div', 'position:absolute;left:0;right:0;top:-12px;text-align:center;font-size:50px;', '🎀', who);
  const cur = 'position:absolute;top:0;bottom:0;width:50%;background:repeating-linear-gradient(90deg,#8d1830 0 14px,#6d0f24 14px 28px);';
  const l = ctx.el('div', `${cur}left:0;`, null, s), r = ctx.el('div', `${cur}right:0;`, null, s);
  await wait(ctx, 500);
  ctx.audio.play('assets/audio/sfx/reveal_chime.mp3', { bus: 'sfx', gain: 0.6 });
  const open = { duration: ctx.gentle ? 400 : 1500, easing: 'ease-in-out', fill: 'forwards' };
  if (ctx.gentle) { ctx.anim(l, [{ opacity: 1 }, { opacity: 0 }], open); ctx.anim(r, [{ opacity: 1 }, { opacity: 0 }], open); } else {
    ctx.anim(l, [{ transform: 'translateX(0)' }, { transform: 'translateX(-82%)' }], open);
    ctx.anim(r, [{ transform: 'translateX(0)' }, { transform: 'translateX(82%)' }], open);
  }
  await wait(ctx, 1800);
  if (!ctx.gentle) ctx.anim(who, [{ transform: 'rotate(0)' }, { transform: 'rotate(26deg)', offset: 0.45 }, { transform: 'rotate(26deg)', offset: 0.65 }, { transform: 'rotate(0)' }], { duration: 3400, easing: 'ease-in-out' });
  await wait(ctx, 3600);
  await outro(ctx, s, end, 'color:#fbeef0;bottom:6%;');
}

const SHOWS = { trance, bimbo, drone, sissy };
export function endingShow(ctx, end, theme) { return SHOWS[theme](ctx, end); }
