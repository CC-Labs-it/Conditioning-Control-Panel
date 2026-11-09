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

/* ---- Bimbodoll: after the pop (./bimbodoll.js) the page is black. 0.8 s, then the pink card rises out of it with a
 * slow Ken Burns drift and a few bubbles floating up: "GOOD GIRL" typed letter by letter, the last word slammed in with a
 * flash, a chime and a ring of hearts; then "SEE YOU SOON :3" drops in from above and the ":3" winks; then the link.
 * Gentle and reduced motion: words fade in, no slam, no flash, no bounce, quiet ticks. Synth sound only. ---- */
const HEART = '<svg viewBox="0 0 24 22" width="100%" height="100%" aria-hidden="true"><path d="M12 21C5 15.5 1 12 1 7.3 1 3.8 3.7 1 7 1c2 0 3.8 1 5 2.6C13.2 2 15 1 17 1c3.3 0 6 2.8 6 6.3C23 12 19 15.5 12 21z" fill="#fff"/></svg>';

/** Ticks and the chime for the card, through one sfx input; quiet = the Gentle level. */
function cardSound(ctx, quiet) {
  const io = ctx.audio.input?.({ bus: 'sfx', gain: 1 });
  if (!io) return { tick() {}, chime() {}, stop() {} };
  const ac = io.ac, nb = ac.createBuffer(1, Math.ceil(ac.sampleRate * 0.03), ac.sampleRate), d = nb.getChannelData(0);
  for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
  const lvl = quiet ? 0.5 : 1;
  return {
    tick() {
      const now = ac.currentTime, s = ac.createBufferSource(), bp = ac.createBiquadFilter(), g = ac.createGain();
      s.buffer = nb; bp.type = 'bandpass'; bp.frequency.value = 2000; bp.Q.value = 3;
      g.gain.setValueAtTime(0.22 * lvl, now); g.gain.exponentialRampToValueAtTime(0.0001, now + 0.025);
      s.connect(bp); bp.connect(g); g.connect(io.node); s.start(now); s.stop(now + 0.03);
    },
    chime() {
      const now = ac.currentTime;
      for (const [f, v] of [[880, 0.32], [1320, 0.22]]) {
        const o = ac.createOscillator(), g = ac.createGain();
        o.type = 'sine'; o.frequency.value = f;
        g.gain.setValueAtTime(0.0001, now); g.gain.linearRampToValueAtTime(v * lvl, now + 0.01); g.gain.exponentialRampToValueAtTime(0.0001, now + 0.7);
        o.connect(g); g.connect(io.node); o.start(now); o.stop(now + 0.75);
      }
    },
    stop() { io.stop(); },
  };
}

async function bimbo(ctx, end) {
  ctx.css('@font-face{font-family:"Fredoka";src:url("assets/fonts/fredoka-latin.woff2") format("woff2");font-weight:300 700;font-display:swap;}');  // the theme's own sheet went with it
  const s = stage(ctx, 'background:#000;color:#ffa8d5;font-family:"Fredoka","Segoe UI",system-ui,sans-serif;');
  const soft = !!(ctx.gentle || ctx.reduced), still = !!ctx.reduced;
  const [first = 'GOOD GIRL BAMBI', second = 'SEE YOU SOON :3'] = end.flash || [];
  const sound = cardSound(ctx, soft);
  await wait(ctx, 800);

  const card = ctx.el('div', 'position:absolute;inset:0;overflow:hidden;opacity:0;background:radial-gradient(circle at 50% 44%,#ffd0e8 0,#ffa8d5 58%,#f591c6 100%);color:#fff;font-weight:700;', null, s);
  const flash = ctx.el('div', 'position:absolute;inset:0;background:#fff;opacity:0;pointer-events:none;z-index:3;', null, card);
  for (let i = 0; i < 3; i++) {   // faint bubbles drifting up at their own speeds
    const sz = 60 + Math.random() * 110, b = ctx.el('div', `position:absolute;left:${(8 + i * 30 + Math.random() * 16).toFixed(1)}%;top:100%;width:${sz.toFixed(0)}px;height:${sz.toFixed(0)}px;background:url("assets/img/bubble.png") center/100% 100% no-repeat;opacity:.28;mix-blend-mode:screen;pointer-events:none;`, null, card);
    if (!still) ctx.anim(b, [{ transform: 'translate(0,0)' }, { transform: `translate(${(Math.random() * 60 - 30).toFixed(0)}px,-${(100 + sz / 4).toFixed(0)}vh)` }], { duration: (soft ? 1.6 : 1) * (9000 + i * 3500 + Math.random() * 2000), delay: -Math.random() * 4000, iterations: Infinity });
  }
  const drift = ctx.el('div', 'position:absolute;inset:0;display:grid;place-items:center;padding:16px;text-align:center;line-height:1.05;z-index:2;', null, card);
  ctx.anim(card, still ? [{ opacity: 0 }, { opacity: 1 }] : [{ opacity: 0, transform: 'translateY(4%)' }, { opacity: 1, transform: 'translateY(0)' }], { duration: soft ? 900 : 700, easing: 'cubic-bezier(.2,.8,.3,1)', fill: 'forwards' });
  if (!still) ctx.anim(drift, [{ transform: 'scale(1)' }, { transform: 'scale(1.06)' }], { duration: 9000, easing: 'linear', fill: 'forwards' });
  await wait(ctx, soft ? 700 : 500);

  /* "GOOD GIRL", typed */
  const box = ctx.el('div', 'display:flex;flex-direction:column;align-items:center;gap:.1em;', null, drift);
  const words = first.trim().split(/\s+/), last = words.length > 1 ? words.pop() : '', lead = words.join(' ');
  const row = ctx.el('div', 'font-size:clamp(34px,9.5vw,108px);letter-spacing:.03em;white-space:nowrap;', null, box);
  if (soft) {
    for (const w of lead.split(' ')) {
      const sp = ctx.el('span', 'display:inline-block;opacity:0;margin:0 .14em;', w, row);
      ctx.anim(sp, [{ opacity: 0 }, { opacity: 1 }], { duration: 450, fill: 'forwards' });
      sound.tick();
      await wait(ctx, 380);
    }
  } else {
    for (const ch of lead) {
      const sp = ctx.el('span', 'display:inline-block;opacity:0;', ch === ' ' ? ' ' : ch, row);
      if (ch !== ' ') {
        ctx.anim(sp, [{ opacity: 0, transform: 'scale(1.5)', filter: 'blur(6px)' }, { opacity: 1, transform: 'scale(1)', filter: 'blur(0)' }], { duration: 120, easing: 'cubic-bezier(.34,1.56,.64,1)', fill: 'forwards' });
        sound.tick();
      } else sp.style.opacity = '1';
      await wait(ctx, 70);
    }
  }

  /* "BAMBI", slammed */
  if (last) {
    await wait(ctx, soft ? 300 : 220);
    const big = ctx.el('div', 'position:relative;font-size:clamp(58px,17vw,200px);letter-spacing:.02em;opacity:0;text-shadow:0 .04em 0 #f07bb8;', last, box);
    sound.chime();
    if (soft) ctx.anim(big, [{ opacity: 0 }, { opacity: 1 }], { duration: 700, fill: 'forwards' });
    else {
      ctx.anim(big, [{ opacity: 0, transform: 'scale(1.8)' }, { opacity: 1, transform: 'scale(.9)', offset: 0.4 }, { transform: 'scale(1.07)', offset: 0.62 }, { transform: 'scale(.97)', offset: 0.8 }, { transform: 'scale(1.01)', offset: 0.92 }, { opacity: 1, transform: 'scale(1)' }], { duration: 420, easing: 'ease-out', fill: 'forwards' });
      ctx.anim(flash, [{ opacity: 0 }, { opacity: 0.25, offset: 0.25 }, { opacity: 0 }], { duration: 150 });
      const n = 12 + Math.floor(Math.random() * 7);
      for (let i = 0; i < n; i++) {   // a ring of hearts bursting from the word
        const a = (i / n) * Math.PI * 2 + Math.random() * 0.3, dist = 110 + Math.random() * 110, sz = 14 + Math.random() * 14;
        const h = ctx.el('div', `position:absolute;left:50%;top:50%;width:${sz.toFixed(0)}px;height:${sz.toFixed(0)}px;margin:-${(sz / 2).toFixed(0)}px 0 0 -${(sz / 2).toFixed(0)}px;pointer-events:none;`, null, big);
        h.innerHTML = HEART;
        const dx = Math.cos(a) * dist, dy = Math.sin(a) * dist * 0.75;
        ctx.anim(h, [{ transform: 'translate(0,0) scale(.4)', opacity: 1 }, { transform: `translate(${(dx * 0.8).toFixed(0)}px,${(dy * 0.8).toFixed(0)}px) scale(1.1)`, opacity: 1, offset: 0.55 }, { transform: `translate(${dx.toFixed(0)}px,${(dy + 16).toFixed(0)}px) scale(.8)`, opacity: 0 }], { duration: 900 + Math.random() * 300, delay: 60, easing: 'cubic-bezier(.2,.8,.3,1)', fill: 'forwards' });
      }
    }
  }
  await wait(ctx, 1800);

  /* cross to "SEE YOU SOON :3", dropping in from above */
  ctx.anim(box, [{ opacity: 1 }, { opacity: 0 }], { duration: 300, fill: 'forwards' });
  await wait(ctx, 320);
  box.remove();
  const row2 = ctx.el('div', 'font-size:clamp(30px,8.5vw,96px);letter-spacing:.03em;white-space:nowrap;', null, drift);
  const face = second.match(/\s*(:3)$/), text = face ? second.slice(0, face.index) : second;
  let wink = null;
  if (soft) {
    for (const w of [...text.split(' '), ...(face ? [':3'] : [])]) {
      const sp = ctx.el('span', 'display:inline-block;opacity:0;margin:0 .12em;', w, row2);
      ctx.anim(sp, [{ opacity: 0 }, { opacity: 1 }], { duration: 400, fill: 'forwards' });
      await wait(ctx, 260);
    }
  } else {
    const units = [...text].map((ch) => (ch === ' ' ? ' ' : ch));
    if (face) units.push(' ', ':3');
    for (const u of units) {
      const sp = ctx.el('span', 'display:inline-block;opacity:0;', u, row2);
      if (u === ':3') wink = sp;
      if (u !== ' ') ctx.anim(sp, [{ opacity: 0, transform: 'translateY(-1.2em)' }, { opacity: 1, transform: 'translateY(.12em)', offset: 0.6 }, { transform: 'translateY(-.05em)', offset: 0.82 }, { opacity: 1, transform: 'translateY(0)' }], { duration: 380, easing: 'ease-in', fill: 'forwards' });
      else sp.style.opacity = '1';
      await wait(ctx, 40);
    }
    if (wink) {
      await wait(ctx, 500 + 380);
      wink.style.transformOrigin = '50% 60%';
      ctx.anim(wink, [{ transform: 'scaleY(1)' }, { transform: 'scaleY(.1)', offset: 0.45 }, { transform: 'scaleY(1)' }], { duration: 220, easing: 'ease-in-out' });
    }
  }
  await wait(ctx, 2000);
  ctx.anim(card, [{ opacity: 1 }, { opacity: 0 }], { duration: soft ? 700 : 500, fill: 'forwards' });
  await wait(ctx, soft ? 700 : 500);
  card.remove();
  sound.stop();
  await outro(ctx, s, end, 'color:#ffa8d5;');
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
