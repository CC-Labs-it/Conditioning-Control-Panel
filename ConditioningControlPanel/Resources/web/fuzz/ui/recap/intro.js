/* ui/recap/intro.js - the end card arrives piece by piece (owner 2026-11-09: "the whole card should come in piece by piece
 * falling on the background, with visible thuds, little dust particles").
 *
 * Sections land in reading order: the left column top to bottom then the poster on a wide screen; on one column the
 * visual order (title, Next run, road, endings, poster), and a section below the fold waits until it scrolls into view
 * (owner 2026-11-09: "wait for ppl to scroll before landing the animation for the deeper parts on mobile").
 * Every hit is a few small channels at once: a squash, a thud, dust, a knock of the card in real pixels. Sounds are the
 * game's own files through the audio kit (its cue budget drops what would pile up). Gentle and reduced motion skip the
 * whole show: the card is simply there. Cosmetic randomness is Math.random; the run's RNG is untouched. */

const SFX = 'assets/audio/sfx/', POP = 'assets/audio/pop/', PLUCK = (n) => `assets/audio/pluck/wood_${String(n).padStart(2, '0')}.wav`;
export const SOUNDS = Object.freeze({
  thud: SFX + 'thud.mp3', snap: SFX + 'snap.mp3', chip: SFX + 'chip_pop.mp3', click: SFX + 'ui_click.mp3', tally: SFX + 'tally_tick.mp3',
  tick: SFX + 'count_tick.mp3', gold: SFX + 'golden_pop.mp3', chime: SFX + 'reveal_chime.mp3', equip: SFX + 'ui_equip.mp3', denied: SFX + 'ui_denied.mp3',
  pop: POP + 'Pop.mp3', pop2: POP + 'Pop2.mp3', pop3: POP + 'Pop3.mp3',
  w03: PLUCK(3), w05: PLUCK(5), w07: PLUCK(7), w09: PLUCK(9), w10: PLUCK(10), w12: PLUCK(12), w14: PLUCK(14), w15: PLUCK(15),
});
export const SCALE = ['w03', 'w05', 'w07', 'w09', 'w10', 'w12', 'w14', 'w15'];
const THUD = 'cubic-bezier(.2,1.5,.4,1)';
const rand = (a, b) => a + Math.random() * (b - a);
const ARROWS = ['M2 9 C 7 6, 12 11, 18 8 M13 3 L19 8 L13 13', 'M2 8 C 8 11, 12 5, 18 9 M12 4 L19 9 L13 14', 'M2 10 C 6 7, 13 9, 18 8 M13 3 L19 8 L12 12'];
export const arrowPath = (i) => ARROWS[i % ARROWS.length];

/**
 * root: the .rr card. scroller: the element that scrolls it (the backdrop). fx: the particle layer inside root.
 * sfx(name, gain, rate): plays one SOUNDS entry now. stars: () => an svg string of a crayon star (cosmetic).
 * poster: { start() } or null. onSettled(): the card has finished arriving.
 */
export function createIntro({ doc, root, scroller, fx, sfx, stars, poster, onSettled }) {
  const win = doc.defaultView;
  let timers = [], anims = [], io = null, running = false, done = false;
  const nums = [];
  const later = (ms, fn) => { const t = win.setTimeout(fn, Math.max(0, ms)); timers.push(t); return t; };
  const say = (name, at, gain, rate = 1) => later(at, () => sfx(name, gain, rate));
  function A(el, frames, o) { if (!el) return null; const a = el.animate(frames, { fill: 'backwards', ...o }); anims.push(a); return a; }
  const fadeIn = (el, t, dur = 120) => A(el, [{ opacity: 0 }, { opacity: 1 }], { delay: t, duration: dur });

  /* ---- particles and knocks: measured in real pixels of the card's own box ---- */
  function local(r) { const s = root.getBoundingClientRect(); return { x: r.left - s.left, y: r.top - s.top, w: r.width, h: r.height }; }
  function dust(rect, n = 9, big = 1) {
    for (let i = 0; i < n; i++) {
      const p = doc.createElement('i');
      p.className = 'rr-puff' + (i % 3 === 0 ? ' g' : '');
      const s = rand(7, 15) * big, edge = i % 2 ? rect.x + rect.w - rand(0, 26) : rect.x + rand(0, 26);
      const x = i < n * .7 ? edge : rect.x + rand(.2, .8) * rect.w, side = x < rect.x + rect.w / 2 ? -1 : 1;
      p.style.cssText = `--s:${s}px;left:${x}px;top:${rect.y + rect.h - 2}px;`;
      fx.append(p);
      p.animate([{ transform: 'translate(0,0) scale(.35)', opacity: 1 }, { transform: `translate(${side * rand(16, 46) * big}px,${-rand(6, 26) * big}px) scale(1.15)`, opacity: .9, offset: .55 },
        { transform: `translate(${side * rand(30, 70) * big}px,${-rand(10, 34) * big}px) scale(.6)`, opacity: 0 }], { duration: rand(480, 760), easing: 'cubic-bezier(.1,.7,.3,1)' }).onfinish = () => p.remove();
    }
  }
  function zips(rect) {
    for (const [cx, dir] of [[rect.x, -1], [rect.x + rect.w, 1]]) {
      for (let k = 0; k < 3; k++) {
        const z = doc.createElement('i'); z.className = 'rr-zip';
        const ang = dir < 0 ? 200 + k * 25 : -20 - k * 25;
        z.style.cssText = `left:${cx + dir * 4}px;top:${rect.y + rect.h - 4}px;width:${rand(12, 20)}px;`;
        fx.append(z);
        z.animate([{ transform: `rotate(${ang}deg) translateX(0) scaleX(.2)`, opacity: 1 }, { transform: `rotate(${ang}deg) translateX(14px) scaleX(1)`, opacity: 1, offset: .5 },
          { transform: `rotate(${ang}deg) translateX(26px) scaleX(.3)`, opacity: 0 }], { duration: 300, easing: 'ease-out' }).onfinish = () => z.remove();
      }
    }
  }
  function knock(px) {
    root.animate([{ transform: 'translate(0,0)' }, { transform: `translate(${px * .3}px,${px}px)` }, { transform: `translate(${-px * .2}px,${-px * .45}px)` },
      { transform: `translate(0,${px * .2}px)` }, { transform: 'translate(0,0)' }], { duration: 280, easing: 'ease-out', composite: 'add' });
  }
  function burst(rect, n = 10, spread = 110) {
    const cx = rect.x + rect.w / 2, cy = rect.y + rect.h / 2;
    for (let i = 0; i < n; i++) {
      const s = doc.createElement('i'); s.className = 'rr-spark'; s.innerHTML = stars(i);
      s.style.left = cx + 'px'; s.style.top = cy + 'px';
      fx.append(s);
      const a = (i / n) * Math.PI * 2 + rand(-.3, .3), d = rand(.55, 1) * spread, sc = rand(.5, 1.05);
      s.animate([{ transform: 'translate(0,0) scale(.2) rotate(0deg)', opacity: 1 }, { transform: `translate(${Math.cos(a) * d}px,${Math.sin(a) * d - 20}px) scale(${sc}) rotate(${rand(-160, 160)}deg)`, opacity: 1, offset: .6 },
        { transform: `translate(${Math.cos(a) * d * 1.1}px,${Math.sin(a) * d + 30}px) scale(${sc * .6}) rotate(${rand(-220, 220)}deg)`, opacity: 0 }], { duration: rand(650, 950), easing: 'cubic-bezier(.15,.8,.35,1)' }).onfinish = () => s.remove();
    }
  }

  /** Fall from above (or rise from below), land, squash, settle. */
  function drop(el, t, { from = 'translateY(-140px) rotate(-4deg)', dur = 460, sound = ['thud', .32], dustN = 9, big = 1, kn = 3, lines = false } = {}) {
    if (!el) return t;
    fadeIn(el, t, dur * .25);
    A(el, [{ transform: from, easing: 'cubic-bezier(.5,0,.9,.4)' }, { transform: 'translateY(4px) scale(1.02,.95)', offset: .66, easing: 'ease-out' },
      { transform: 'translateY(-3px) scale(.99,1.02)', offset: .84, easing: 'ease-in-out' }, { transform: 'none' }], { delay: t, duration: dur, composite: 'add' });
    const land = t + dur * .66;
    if (sound) say(sound[0], land, sound[1], sound[2] || 1);
    later(land, () => { const r = local(el.getBoundingClientRect()); dust(r, dustN, big); if (lines) zips(r); if (kn) knock(kn); });
    return land;
  }
  function popIn(el, t, { from = 'scale(.3) rotate(-8deg)', dur = 340, sound = null } = {}) {
    if (!el) return;
    fadeIn(el, t, 90);
    A(el, [{ transform: from }, { transform: 'scale(1.12)', offset: .6 }, { transform: 'none' }], { delay: t, duration: dur, easing: 'ease-out', composite: 'add' });
    if (sound) say(sound[0], t + dur * .45, sound[1], sound[2] || 1);
  }
  function countUp(node, to, t, dur, fmt = String, gain = .5) {
    if (!node || !(to > 0)) return;
    nums.push([node, node.nodeValue]);
    node.nodeValue = fmt(0);
    const steps = Math.min(to, 12);
    for (let k = 1; k <= steps; k++) {
      const at = t + (dur * k) / steps, v = Math.round(to * (1 - Math.pow(1 - k / steps, 2)));
      later(at, () => { node.nodeValue = fmt(v); });
      if (k % 2 === 0) say('tick', at, gain, 1 + k * .03);
    }
  }
  const textNode = (el) => el && [...el.childNodes].find((n) => n.nodeType === 3 && n.nodeValue.trim());
  const q = (s, el = root) => el.querySelector(s), qa = (s, el = root) => [...el.querySelectorAll(s)];
  const shown = (el) => !!el && !el.hidden && el.getClientRects().length > 0;

  /* ---- one beat per section. Each returns when the next one may start (ms after its own start). ---- */
  const BEATS = {
    splash(el) {
      drop(el, 0, { from: 'translateY(-160px) rotate(-6deg)', dur: 480, sound: ['thud', .38], lines: true, kn: 3 });
      popIn(q('.rr-kind', el), 360, { sound: ['chip', .2] });
      const h = q('.rr-title', el);
      fadeIn(h, 440, 60);
      A(h, [{ transform: 'scale(2.2) rotate(-6deg)' }, { transform: 'scale(.94) rotate(1deg)', offset: .55 }, { transform: 'none' }], { delay: 440, duration: 380, easing: 'cubic-bezier(.5,0,.4,1)', composite: 'add' });
      say('snap', 650, .42);
      later(650, () => knock(2));
      qa('.rr-line .rr-off', el).forEach((o, i) => popIn(o, 820 + i * 110, { from: 'scale(2.4) rotate(-14deg)', dur: 260, sound: ['click', .3, 1 + i * .12] }));
      const hl = q('.rr-line .kw, .rr-line .rr-hl', el);
      if (hl) { A(hl, [{ backgroundSize: '0% 100%' }, { backgroundSize: '100% 100%' }], { delay: 1300, duration: 380, easing: 'ease-out' }); say('chip', 1340, .14, 1.2); }
      qa('.rr-stamp', el).forEach((s, i) => { popIn(s, 1040 + i * 170, { from: 'scale(1.9) rotate(-18deg)', dur: 300 }); say('thud', 1190 + i * 170, .18, 1.5); });
      return 1240;
    },
    road(el) {
      drop(el, 0, { sound: ['thud', .3, .95] });
      qa('.rr-step', el).forEach((s, i) => {
        popIn(q('.rr-fr', s), 400 + i * 120, { from: 'translateY(16px) scale(.4) rotate(-10deg)', dur: 320, sound: [SCALE[1 + i], .22] });
        fadeIn(q('.rr-word', s), 500 + i * 120, 160);
      });
      qa('.rr-arrow', el).forEach((a, i) => { fadeIn(a, 490 + i * 120, 60); A(a, [{ transform: 'scaleX(0)', transformOrigin: 'left' }, { transform: 'scaleX(1)', transformOrigin: 'left' }], { delay: 490 + i * 120, duration: 180, easing: 'ease-out' }); });
      const peel = q('.rr-peel', el);
      if (shown(peel)) {
        fadeIn(peel, 1100, 100); fadeIn(q('.rr-peeltag', el), 1200, 200);
        A(peel, [{ transform: 'rotate(0)' }, { transform: 'rotate(-14deg)', offset: .3 }, { transform: 'rotate(8deg)', offset: .6 }, { transform: 'none' }], { delay: 1120, duration: 520, composite: 'add' });
        say('chip', 1150, .12, .8);
      }
      return 850;
    },
    coll(el) {
      drop(el, 0, { sound: ['thud', .3, 1.05] });
      countUp(textNode(q('.rr-ends-big', el)), Number(el.dataset.got) || 0, 400, 500);
      qa('.rr-endcard', el).forEach((c, i) => {
        fadeIn(c, 370 + i * 45, 60);
        A(c, [{ transform: 'translateX(-60px) translateY(-10px) rotate(-24deg)' }, { transform: 'none' }], { delay: 370 + i * 45, duration: 280, easing: THUD, composite: 'add' });
        if (i % 2 === 0) say('tally', 490 + i * 45, .4, 1 + i * .02);
      });
      qa('.rr-endcard.got', el).forEach((c, i) => { A(c, [{ transform: 'perspective(300px) rotateY(180deg)' }, { transform: 'perspective(300px) rotateY(0deg)' }], { delay: 1050 + i * 140, duration: 360, easing: 'ease-out', composite: 'add' }); say('pop3', 1150 + i * 140, .18, 1 + i * .1); });
      const bub = q('.rr-bubble', el);
      if (shown(bub)) popIn(bub, 1500, { from: 'scale(.5) translateY(-10px)', dur: 320, sound: ['chip', .14] });
      const seen = q('.rr-seen', el);
      if (shown(seen)) {
        countUp(textNode(q('.rr-big', seen)), Number(seen.dataset.n) || 0, 650, 900);
        A(q('.rr-meter i', seen), [{ transform: 'scaleX(0)' }, { transform: 'scaleX(1.06)', offset: .8 }, { transform: 'scaleX(1)' }], { delay: 650, duration: 1000, easing: 'ease-out' });
        fadeIn(q('.rr-tiny', seen), 1550, 300);
      }
      return 1600;
    },
    next(el) {
      drop(el, 0, { from: 'translateY(220px) rotate(3deg)', dur: 460, sound: ['thud', .5, .85], dustN: 14, big: 1.3, kn: 5, lines: true });
      const renew = q('.rr-renew', el);
      if (shown(renew)) {
        const star = q('.rr-star', renew);
        popIn(renew, 400, { from: 'scale(.6) rotate(-6deg)', dur: 360 });
        fadeIn(star, 430, 60);
        A(star, [{ transform: 'rotate(-320deg) scale(0)' }, { transform: 'rotate(20deg) scale(1.2)', offset: .7 }, { transform: 'none' }], { delay: 430, duration: 560, easing: 'cubic-bezier(.2,.8,.3,1)', composite: 'add' });
        say('gold', 750, .3);
        later(750, () => burst(local(star.getBoundingClientRect()), 8, 80));
        const n = textNode(q('.rr-plus', renew)), to = Number(renew.dataset.dust) || 0;
        countUp(n, to, 500, 450, (v) => '+' + v);
      }
      qa('.rr-side > .rr-tool, .rr-restart > *', el).forEach((x, i) => { if (shown(x)) popIn(x, 700 + i * 110, { from: 'translateY(14px) scale(.6)', dur: 320, sound: i < 4 ? ['chip', .16, 1 + i * .08] : null }); });
      const tag = q('.rr-freetag', el);
      if (shown(tag)) popIn(tag, 1200, { from: 'rotate(-70deg) scale(0)', dur: 420, sound: ['pop', .14] });
      return 1350;
    },
    poster(el) {
      const tilt = q('.rr-tilt', el), shadow = q('.rr-pshadow', el);
      drop(tilt, 0, { from: 'translateY(-260px) rotate(10deg)', dur: 520, sound: ['pop2', .34], dustN: 12, big: 1.2, kn: 3, lines: true });
      say('thud', 343, .26, .9);
      fadeIn(shadow, 300, 120);
      poster?.start(343);
      qa('.rr-sticker', el).forEach((s, i) => popIn(s, 1100 + i * 180, { from: 'scale(1.8) rotate(-30deg)', dur: 300, sound: ['pop', .2, 1 + i * .15] }));
      const credit = q('.rr-credit', el);
      if (credit) [...credit.children].forEach((c, i) => popIn(c, 1500 + i * 130, { from: 'translateY(10px) scale(.5)', dur: 300, sound: i === 1 ? ['click', .2, .9] : null }));
      return Number(el.dataset.settle) || 2000;
    },
  };

  /* ---- the order and the scroll gate ---- */
  function order() {
    const secs = qa('[data-rr]').filter(shown);
    const col = q('.rr-col');
    const oneColumn = !col || win.getComputedStyle(col).display === 'contents';
    if (!oneColumn) return secs.sort((a, b) => (a.dataset.rr === 'poster') - (b.dataset.rr === 'poster'));
    return secs.sort((a, b) => a.getBoundingClientRect().top - b.getBoundingClientRect().top);
  }
  const inView = (el) => {
    const r = el.getBoundingClientRect(), v = scroller.getBoundingClientRect();
    return r.top < Math.min(v.bottom, win.innerHeight) - Math.min(120, r.height * .3);
  };

  function hideAll(list) { for (const s of list) s.style.opacity = '0'; }
  let next = 0, queue = [];
  function pump() {
    while (queue.length && running) {
      const sec = queue[0];
      if (!inView(sec)) { watch(sec); return; }
      queue.shift();
      const now = performance.now();
      if (next > now) { const at = next; next = at + 99999; later(at - now, () => { if (!running) return; startSection(sec); pump(); }); return; }
      startSection(sec);
    }
    if (running && !queue.length) finish();
  }
  function startSection(sec) {
    sec.style.opacity = '';
    next = performance.now() + BEATS[sec.dataset.rr](sec);
  }
  function watch(sec) {
    io?.disconnect();
    io = new win.IntersectionObserver((entries) => {
      if (!entries.some((e) => e.isIntersecting)) return;
      io.disconnect(); io = null;
      if (next < performance.now()) next = performance.now() + 120;
      pump();
    }, { root: scroller, threshold: 0, rootMargin: '0px 0px -18% 0px' });
    io.observe(sec);
  }
  function finish() {
    later(Math.max(0, next - performance.now()) + 200, () => stop(true));
  }

  function stop(settle = true) {
    timers.forEach((t) => win.clearTimeout(t)); timers = [];
    anims.forEach((a) => { try { a.finish(); } catch { a.cancel(); } }); anims = [];
    io?.disconnect(); io = null;
    for (const [n, v] of nums) n.nodeValue = v;
    nums.length = 0;
    fx.replaceChildren();
    qa('[data-rr]').forEach((s) => { s.style.opacity = ''; });
    queue = [];
    if (!running) return;
    running = false;
    if (settle && !done) { done = true; poster?.start(0); onSettled?.(); }
  }

  return {
    get running() { return running; },
    start() {
      const list = order();
      running = true;
      hideAll(list);
      queue = list;
      next = performance.now();
      // the sheet itself lands first
      drop(root, 0, { from: 'translateY(-50px) scale(1.03)', dur: 380, sound: ['thud', .2, .75], dustN: 12, big: 1.3, kn: 0 });
      next = performance.now() + 260;
      pump();
    },
    stop,
    burst, knock, dust, local,
  };
}
