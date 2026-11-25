/* ui/unlocks/podcast-player.js - one player for every `*_pod` unlock (themes-a and themes-b import it).
 *
 *   return mountPodcast(ctx, { theme: 'bimbo', show: 'True Bimbo' });
 *
 * Reads <base>manifest.json, default assets/audio/podcast/<theme>/ (shape: handoff-1102/GAME-PODCAST-HANDOFF.md, "What to
 * hand back"), and the other seven shows beside it. Owner 2026-11-08: the station plays EVERY show, starting with the
 * player's own and walking on to the nearest niche (RING), wrapping round; back and next in the pill move through that
 * list. Anything missing is silence: no manifest, no file, a failed play. A show with no manifest drops out of the list.
 * The player still shows, at the screen's edge, with the show's name, "Off air" and a still line.
 *
 * The card (owner 2026-11-08, pick B3 of handoff-1108/tc-podcast-card): a boy and a girl in silhouette at a mic, backlit.
 * A wave over the floor of the picture runs from the mic outwards on the loudness of what is playing; three arcs light
 * over the head of whoever speaks; the red lamp glows while on air. Card and pill take a light tint of the playing show's
 * niche. Loudness and turns come from <show>/waves.json (clicker-podcast tools/bake_waves.py); without it the line still
 * moves softly while on air and no arcs show. Gentle and reduced motion hold a still line and steady arcs.
 *
 * All audio goes through ctx.audio.stream: the kit owns the element, so mute, pause, suspend, panic and the end stop it
 * in the same call, with nothing here listening or polling.
 */

import { mountVolumeSlider } from './volume-slider.js';
import { THEME_PALETTES, mixHex } from '../palette.js';
import { canCatchEverywhere, catchEverywhere, wordsForEverywhere } from './prestige/trig.js';  // prestige hook: Trigger Words Everywhere (trig_7)
import { nextGapMs } from '../../sim/prestige-words.js';

const DUCK = 0.36;            // an episode under a clip, as a share of the bus
const CLIP_EVERY_MS = 210000; // an ad between stretches
const REACT_GAP_MS = 60000;   // a reaction at most this often
const IDLE_MS = 45000;        // no taps for this long is worth a remark
const RESTART_S = 5;          // back past this far into an episode restarts it instead
const OFF_AIR = 'Off air';
/** Trigger Words Everywhere: how long the player's word rides the caption tape, game ms. */
export const CAPTION_WORD_MS = 14000;
const ON_AIR = 'On air';

/** Nearest niche next, owner 2026-11-08: sissy, bimbo, goon, chastity, drone, then the rest, round again. */
export const RING = Object.freeze(['sissy', 'bimbo', 'goon', 'chastity', 'drone', 'trance', 'infection', 'censored']);
const SHOWS = {
  trance: 'Going Under with...', bimbo: 'True Bimbo', drone: 'Unit Hour', sissy: 'Manners Hour', goon: 'Still Going',
  chastity: 'The Keyholder Report', censored: '[REDACTED] FM', infection: 'Symptoms',
};
const hueOf = t => (THEME_PALETTES[t === 'chastity' ? 'locked' : t] || THEME_PALETTES.bimbo).a;

/** The order a station plays: its own show, then onwards round the ring. */
export function stationOrder(theme) {
  const at = RING.indexOf(theme);
  return at < 0 ? [theme, ...RING] : [...RING.slice(at), ...RING.slice(0, at)];
}

const pick = (ctx, list) => list[Math.floor(ctx.rng() * list.length) % list.length];

export function mountPodcast(ctx, { theme, show, base = `assets/audio/podcast/${theme}/` }) {
  const ui = drawPlayer(ctx, { show, hue: hueOf(theme) });
  const rotates = base.endsWith(`/${theme}/`);
  const dirOf = t => (t === theme || !rotates ? base : base.slice(0, -theme.length - 1) + t + '/');
  const order = rotates ? stationOrder(theme) : [theme];
  const shows = new Map(), waves = new Map();
  let playlist = [], cur = 0, ep, clip, pendingNext = null, gone = false, misses = 0;
  let wanted = ctx.store.get('paused') !== true;
  let lastAd = ctx.state.clock, lastReact = ctx.state.clock, lastTap = lastReact, saveAt = 0;
  const showOf = () => playlist[cur]?.theme ?? theme;

  function update() {
    ui.onAir(!!(ep || clip));
    ui.paused(!wanted);
  }

  function savePosition() {
    if (ep?.el.readyState >= 1) ctx.store.set('at', Math.floor(ep.el.currentTime));
  }

  function wavesFor(t) {
    if (waves.has(t)) return;
    waves.set(t, null);
    fetch(dirOf(t) + 'waves.json').then(r => r.ok ? r.json() : null).then(w => {
      if (!gone && w?.files) waves.set(t, w);
    }).catch(() => {});
  }

  function loadEpisode(index, at = 0) {
    if (!playlist.length) { update(); return; }
    const k = ((index % playlist.length) + playlist.length) % playlist.length, slot = playlist[k];
    cur = k;
    ctx.store.set('ep', k);
    ui.now(shows.get(slot.theme)?.show || SHOWS[slot.theme] || show, slot.e.title || '', hueOf(slot.theme));
    wavesFor(slot.theme);
    const stream = ctx.audio.stream(dirOf(slot.theme) + slot.e.file, { bus: 'podcast', autoplay: wanted });
    if (!stream.el) return;
    stream.file = slot.e.file;
    stream.show = slot.theme;
    ep = stream;
    stream.el.addEventListener('loadedmetadata', () => {
      if (ep !== stream) return;
      misses = 0;
      if (at > 0) {
        try { stream.el.currentTime = Math.min(at, stream.el.duration || at); } catch { /* Non-seekable media starts at the beginning. */ }
      }
    }, { once: true });
    stream.el.addEventListener('ended', () => {
      if (gone || ep !== stream) return;
      ctx.emit?.('sticker', { key: 'episode' });   // Sticker Book: Regular Listener
      ep = null;
      stream.stop();
      pendingNext = (k + 1) % playlist.length;
      ctx.store.set('ep', pendingNext);
      ctx.store.set('at', 0);
      if (!clip) closeEpisode();
    });
    stream.el.addEventListener('error', () => {
      if (ep !== stream) return;
      ep = null;
      stream.stop();
      /* A file missing from one show moves on to the next; a whole list of misses stops. */
      if (++misses < playlist.length) loadEpisode(k + 1);
      else { ui.now(show, '', hueOf(theme)); update(); }
    });
    update();
  }

  function playClip(kinds, after, from = showOf()) {
    const m = shows.get(from);
    if (clip || !wanted || !m?.clips) return false;
    const pool = kinds.flatMap(k => m.clips[k] || []);
    if (!pool.length) return false;
    const c = pick(ctx, pool);
    const stream = ctx.audio.stream(dirOf(from) + c.file, { bus: 'podcast' });
    if (!stream.el) return false;
    stream.file = c.file;
    stream.show = from;
    wavesFor(from);
    clip = stream;
    ep?.setGain(DUCK);
    update();
    const finish = () => {
      if (gone || clip !== stream) return;
      clip = null;
      stream.stop();
      ep?.setGain(1);
      if (after) after();
      else if (pendingNext !== null) closeEpisode();
      update();
    };
    stream.el.addEventListener('ended', finish);
    stream.el.addEventListener('error', finish);
    if (c.line) ctx.say(c.line);
    return true;
  }

  function closeEpisode() {
    const next = pendingNext;
    pendingNext = null;
    if (!playClip(['closer'], () => loadEpisode(next))) loadEpisode(next);
  }

  /** Back (-1) or next (+1). Back restarts an episode a few seconds in, like any player. */
  function jump(delta) {
    if (!playlist.length) return;
    const from = pendingNext !== null ? pendingNext - 1 : cur;
    const target = delta < 0 && ep?.el.currentTime > RESTART_S ? cur : from + delta;
    pendingNext = null;
    if (clip) { const c = clip; clip = null; c.stop(); }
    if (ep) { const e = ep; ep = null; e.stop(); }
    misses = 0;
    ctx.store.set('at', 0);
    loadEpisode(target);
  }

  function react(kind) {
    const now = ctx.state.clock;
    if (shows.size && now - lastReact >= REACT_GAP_MS && playClip([kind])) lastReact = now;
  }
  ctx.on('buy', () => react('react_buy'));
  ctx.on('tap', e => {
    if (!e?.auto) {
      lastTap = ctx.state.clock;
      if (ctx.rng() < .01) react('react_tap');
    }
  });

  const load = t => fetch(dirOf(t) + 'manifest.json').then(r => r.ok ? r.json() : null).catch(() => null);
  Promise.all(order.map(load)).then(found => {
    if (gone) return;
    found.forEach((m, i) => { if (m) shows.set(order[i], m); });
    if (!shows.size) return;
    playlist = order.flatMap(t => (shows.get(t)?.episodes || []).map(e => ({ theme: t, e })));
    const own = shows.get(theme);
    if (own?.show) ui.now(own.show, '', hueOf(theme));
    const start = () => loadEpisode(ctx.store.get('ep') || 0, ctx.store.get('at') || 0);
    if (ctx.store.get('heard') || !playClip(['opener'], start, theme)) start();
    ctx.store.set('heard', 1);
  });

  ui.toggle.addEventListener('click', () => {
    wanted = !wanted;
    ctx.store.set('paused', !wanted);
    if (wanted) { ep?.play(); clip?.play(); }
    else { ep?.pause(); clip?.pause(); savePosition(); }
    update();
  });
  ui.prev.addEventListener('click', () => jump(-1));
  ui.next.addEventListener('click', () => jump(1));

  /* Trigger Words Everywhere (trig_7): now and then, while a show is on air, one of the player's words rides the caption
   * tape for CAPTION_WORD_MS. A tap catches it for the same pay as the floating word (prestige/trig.js); with the lane
   * not mounted it is display only. */
  const mine = wordsForEverywhere(ctx);
  if (mine.length) {
    let gap = nextGapMs(ctx.rng()), shownFor = 0;
    const hide = () => { shownFor = 0; ui.word(null); };
    ctx.every(1000, () => {
      if (shownFor > 0) { shownFor -= 1000; if (shownFor <= 0) hide(); return; }
      if (!(ep && !clip && wanted)) return;
      gap -= 1000;
      if (gap > 0) return;
      gap = nextGapMs(ctx.rng());
      shownFor = CAPTION_WORD_MS;
      const word = mine[Math.floor(ctx.rng() * mine.length) % mine.length];
      ui.word(word, canCatchEverywhere() ? (at) => { if (catchEverywhere(word, at)) hide(); } : null);
    });
  }

  ctx.frame((_dt, now) => {
    ui.tick(now, clip || ep, wanted, waves);
    if (!ep) return;
    const clock = ctx.state.clock;
    if (clock < saveAt) return;
    saveAt = clock + 10000;
    savePosition();
    if (!wanted || clip) return;
    if (theme && showOf() === theme && !ctx.store.get('welcomed') && clock - lastReact >= REACT_GAP_MS && playClip(['react_theme'])) {
      ctx.store.set('welcomed', 1);
      lastReact = clock;
    } else if (clock - lastAd >= CLIP_EVERY_MS) {
      const kinds = ctx.ads.jokesAreOff ? ['ad_ccp'] : ['ad_ccp', 'ad_joke'];
      if (playClip(kinds)) lastAd = clock;
    } else if (lastTap !== null && clock - lastTap >= IDLE_MS && clock - lastReact >= REACT_GAP_MS) {
      react('react_idle');
      lastTap = null;
    }
  });

  return { off() { gone = true; savePosition(); } };
}

const ART = new URL('../../assets/podcast/studio.webp', import.meta.url).href;
/* Where things sit in studio.webp, as shares of its width and height. */
const HEADS = [[.308, .38, 1], [.708, .40, -1]]; // head centre and which way the face points: the boy, the girl
const LAMP = [.5, .21];
const FLOOR = .9;                                  // the wave's middle line
const ENV_CODES = (() => {
  const t = new Uint8Array(128), a = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_';
  for (let i = 0; i < a.length; i++) t[a.charCodeAt(i)] = i;
  return t;
})();

const PLAYER_CSS = `
.pc-card{position:relative;container-type:inline-size;display:flex;flex-direction:column;align-items:center;width:100%;
  user-select:none;color:#302a32;--pc-hue:#ff4fa8;--pc-play:color-mix(in srgb,var(--pc-hue) 30%,#302a32);}
.pc-pill{display:flex;flex-direction:column;align-items:stretch;width:92%;box-sizing:border-box;
  margin-bottom:clamp(5px,1.8cqw,10px);padding:.3em .45em .2em .75em;border-radius:99px;
  border:1px solid color-mix(in srgb,var(--pc-hue) 30%,#4b414db3);background:color-mix(in srgb,var(--pc-hue) 14%,#fffaf0);
  box-shadow:0 1px 3px #32253026;font:clamp(9px,4.1cqw,17px)/1 "Segoe UI",Arial,sans-serif;
  transition:background-color 1.2s,border-color 1.2s;}
.pc-row{display:flex;align-items:center;gap:.25em;}
.pc-air{flex:none;width:.5em;height:.5em;margin-right:.25em;border-radius:50%;background:#b8aeb0;transition:background-color .4s,box-shadow .4s;}
.pc-card.on .pc-air{background:#e2445c;box-shadow:0 0 0 .18em #e2445c33;}
.pc-display{flex:1;min-width:0;overflow:hidden;font-size:.9em;line-height:1.3;}
.pc-tape{display:flex;width:max-content;}
.pc-tape span{padding-right:2.4em;white-space:nowrap;}
.pc-tape b{font-weight:700;}
.pc-tape i{font-style:normal;opacity:.7;}
.pc-pill .pc-tape .pc-word{display:inline;width:auto;height:auto;min-width:0;min-height:0;margin:0;padding:0 .4em;border:1.5px solid #2b2233;
  border-radius:6px 4px 7px 3px;background:#fffdf5;color:color-mix(in srgb,var(--pc-hue) 45%,#1d1328);font-weight:700;font-size:1em;line-height:1.25;font-family:inherit;
  box-shadow:1px 2px 0 rgba(30,20,40,.25);cursor:pointer;}
.pc-pill .pc-tape b.pc-word{cursor:default;}
.pc-card.pc-still .pc-tape{display:block;width:auto;transform:none!important;}
.pc-card.pc-still .pc-tape span{display:block;padding:0;overflow:hidden;text-overflow:ellipsis;}
.pc-card.pc-still .pc-tape span+span{display:none;}
.pc-pill button{display:grid;place-items:center;flex:none;width:1.9em;height:1.9em;min-width:0;min-height:0;margin:0;padding:0;
  border:0;border-radius:50%;background:transparent;color:#39303a;box-shadow:none;cursor:pointer;touch-action:manipulation;font:inherit;}
.pc-pill button:hover{background:color-mix(in srgb,var(--pc-hue) 18%,#ece4da);}
.pc-pill button:focus-visible{outline:2px solid var(--pc-play);outline-offset:1px;}
.pc-pill .pc-play{background:var(--pc-play);color:#fffaf0;transition:background-color 1.2s;}
.pc-play i{width:0;height:0;border-top:.45em solid transparent;border-bottom:.45em solid transparent;border-left:.68em solid currentColor;margin-left:.16em;}
.pc-play.pc-playing i{width:.6em;height:.86em;box-sizing:border-box;border:0;border-left:.21em solid currentColor;border-right:.21em solid currentColor;margin:0;}
.pc-step i{position:relative;width:.78em;height:.78em;box-sizing:border-box;border-right:.15em solid currentColor;}
.pc-step i::before{content:"";position:absolute;top:0;left:0;border-left:.58em solid currentColor;border-top:.39em solid transparent;border-bottom:.39em solid transparent;}
.pc-back i{transform:rotate(180deg);}
.pc-stage{position:relative;width:100%;aspect-ratio:4 / 3;max-width:100%;border-radius:10px;overflow:hidden;}
.pc-stage img{display:block;width:100%;height:100%;pointer-events:none;-webkit-user-drag:none;}
.pc-tint{position:absolute;inset:0;background:var(--pc-hue);mix-blend-mode:color;opacity:.3;pointer-events:none;transition:background-color 1.2s;}
.pc-stage canvas{position:absolute;inset:0;width:100%;height:100%;pointer-events:none;}
`;

/** Pill (on-air dot, show and episode, back, play, next, volume) over the studio picture and its wave. */
function drawPlayer(ctx, { show, hue }) {
  ctx.css(PLAYER_CSS);
  /* The audio keeps its existing playback behavior when its compact player is hidden. */
  const host = ctx.dock({ edge: 'right', title: 'Podcast', width: 280, reserveHeight: 260, order: -500 + ctx.state.order.indexOf(ctx.item.id) });
  const card = ctx.el('div', '', null, host);
  card.className = 'pc-card';
  card.dataset.dockSurface = '';
  let hueNow = hue;
  const setHue = h => { hueNow = h; card.style.setProperty?.('--pc-hue', h); };
  setHue(hue);
  const pill = ctx.el('div', '', null, card);
  pill.className = 'pc-pill';
  const row = ctx.el('div', '', null, pill);
  row.className = 'pc-row';
  ctx.el('span', '', null, row).className = 'pc-air';
  const display = ctx.el('div', '', null, row);
  display.className = 'pc-display';
  const tape = ctx.el('div', '', null, display);
  tape.className = 'pc-tape';
  const button = (cls, label) => {
    const b = ctx.el('button', '', null, row);
    b.type = 'button';
    b.className = cls;
    b.setAttribute('aria-label', label);
    ctx.el('i', '', null, b);
    return b;
  };
  const prev = button('pc-step pc-back', 'Previous episode');
  const toggle = button('pc-play', `Play ${show}`);
  const next = button('pc-step', 'Next episode');
  /* Owner 2026-11-08: a quiet volume line under the controls, the same level as the HUD mixer row for this show. */
  mountVolumeSlider(ctx, pill, { label: 'Podcast volume', ink: 'var(--pc-play)', ring: '#fffaf0' });
  const stage = ctx.el('div', '', null, card);
  stage.className = 'pc-stage';
  const img = ctx.el('img', '', null, stage);
  img.alt = 'Two podcast hosts at a microphone';
  img.draggable = false;
  img.src = ART;
  ctx.el('div', '', null, stage).className = 'pc-tint';
  const cv = ctx.el('canvas', '', null, stage);
  const g = cv.getContext?.('2d');

  let name = show, title = '', onAir = false, level = 0, tapeX = 0, lastNow = 0, capWord = null, onCatch = null;
  const lit = [0, 0];
  function label() {
    tape.replaceChildren?.();
    for (let i = 0; i < 2; i++) {
      const span = ctx.el('span', '', null, tape);
      ctx.el('b', '', name, span);
      ctx.el('i', '', ` · ${onAir ? title || ON_AIR : OFF_AIR}`, span);
      if (capWord) capLabel(span);
    }
    tapeX = 0;
  }
  /* The player's word on the tape (Everywhere): a small paper chip, a button when it can be caught. */
  function capLabel(span) {
    ctx.el('i', '', ' · ', span);
    const chip = ctx.el(onCatch ? 'button' : 'b', '', capWord, span);
    chip.className = 'pc-word';
    if (onCatch) {
      chip.type = 'button';
      chip.setAttribute('aria-label', `Catch the word ${capWord}`);
      chip.addEventListener('click', (e) => {
        e.stopPropagation();
        if (ctx.paused) return;
        const r = chip.getBoundingClientRect();
        onCatch({ x: r.left + r.width / 2, y: r.top + r.height / 2 });
      });
    }
    ctx.anim?.(chip, [{ opacity: 0 }, { opacity: 1 }], { duration: ctx.gentle || ctx.reduced ? 900 : 300, fill: 'both', easing: 'ease-out' });
  }
  label();

  /* Loudness at second t of the playing file, 0..1; null with no baked strip. */
  function loudness(w, file, t) {
    const env = w?.files?.[file]?.env;
    if (!env) return null;
    const i = Math.floor(t * w.fps);
    return i < 0 || i >= env.length ? 0 : ENV_CODES[env.charCodeAt(i)] / 63;
  }
  function speaker(w, file, t) {
    const turns = w?.files?.[file]?.turns;
    if (!turns) return -1;
    let lo = 0, hi = turns.length - 1;
    while (lo <= hi) {
      const m = (lo + hi) >> 1, s = turns[m];
      if (t < s[0]) hi = m - 1; else if (t >= s[1]) lo = m + 1; else return s[2];
    }
    return -1;
  }

  function tick(now, src, want, waves) {
    if (!g || host.dock?.visible === false) return;
    const w = cv.clientWidth, h = cv.clientHeight;
    if (!w || !h) return;
    const dt = Math.min(.1, (now - (lastNow || now)) / 1000);
    lastNow = now;
    const still = !!(ctx.gentle || ctx.reduced);
    card.classList.toggle('pc-still', still);
    if (!still) {
      const half = tape.scrollWidth / 2;
      if (half > display.clientWidth) { tapeX = (tapeX + dt * 24) % half; tape.style.transform = `translateX(${-tapeX}px)`; }
      else tape.style.transform = '';
    }
    const dpr = Math.min(2, globalThis.devicePixelRatio || 1);
    if (cv.width !== Math.round(w * dpr) || cv.height !== Math.round(h * dpr)) { cv.width = Math.round(w * dpr); cv.height = Math.round(h * dpr); }
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    g.clearRect(0, 0, w, h);

    const live = !!(want && src && !src.el.paused);
    const wv = src ? waves.get(src.show) : null, file = src?.file, t = src?.el.currentTime || 0;
    const baked = loudness(wv, file, t);
    const soft = i => .28 + .16 * Math.sin(now / 260 + i * .7) * Math.sin(now / 610 + i * .23); // no strip: a soft murmur
    const sample = (tt, i) => (!live || still ? .05 : baked === null ? soft(i) : loudness(wv, file, tt));
    level += ((live && !still ? sample(t, 0) : 0) - level) * .3;
    const spk = live ? speaker(wv, file, t) : -1;
    const glowCol = mixHex('#ff8fc0', hueNow, .4);

    /* the wave: one glowing line each way from the mic, older sound further out, in the speaker's shade */
    const mid = w / 2, base = h * FLOOR, maxH = h * .095, n = 56;
    g.lineWidth = Math.max(1.6, h * .009);
    g.lineJoin = 'round';
    g.shadowColor = glowCol;
    g.shadowBlur = 8;
    g.strokeStyle = spk === 1 ? mixHex('#f6a6c4', hueNow, .45) : mixHex('#ffe3ee', hueNow, .25);
    for (const side of [-1, 1]) {
      g.beginPath();
      for (let i = 0; i <= n; i++) {
        const v = sample(t - i / 30 * 1.6, i);
        const x = mid + side * (i / n) * w * .47;
        const y = base - Math.sin(i * .9 + (still ? 0 : now / 140)) * v * maxH * (1 - i / n * .5);
        if (i) g.lineTo(x, y); else g.moveTo(x, y);
      }
      g.stroke();
    }
    g.shadowBlur = 0;

    /* the red lamp: lit on air, a dark bulb off air */
    const lx = LAMP[0] * w, ly = LAMP[1] * h, r = h * .06;
    if (onAir && want) {
      const a = .3 + level * .5;
      const glow = g.createRadialGradient(lx, ly, 0, lx, ly, r * 2.4);
      glow.addColorStop(0, `rgba(255,70,90,${a.toFixed(3)})`);
      glow.addColorStop(1, 'rgba(255,70,90,0)');
      g.fillStyle = glow;
      g.fillRect(lx - r * 3, ly - r * 3, r * 6, r * 6);
    } else {
      g.fillStyle = 'rgba(40,20,30,.55)';
      g.beginPath(); g.arc(lx, ly, r * .55, 0, Math.PI * 2); g.fill();
    }

    /* who is talking: three arcs at 45 degrees over that head, up and away from the mic (facing it they hit the lamp) */
    HEADS.forEach(([cx, cy, dir], who) => {
      lit[who] += ((who === spk ? 1 : 0) - lit[who]) * (still ? .06 : .12);
      if (lit[who] < .02) return;
      const ang = dir > 0 ? -Math.PI * 3 / 4 : -Math.PI / 4;
      const ox = cx * w + Math.cos(ang) * h * .15, oy = cy * h + Math.sin(ang) * h * .15;
      const lvl = still ? .6 : Math.min(1, level * 1.5), step = h * .042, spread = Math.PI * .22;
      g.lineCap = 'round';
      g.lineWidth = Math.max(2, h * .012);
      g.strokeStyle = '#ffe3ee';
      g.shadowColor = glowCol;
      for (let k = 1; k <= 3; k++) {
        const reach = Math.max(0, Math.min(1, lvl * 3 - (k - 1)));   // inner arc first, outer ones as the voice rises
        g.globalAlpha = lit[who] * (.35 + .65 * reach) * (1 - (k - 1) * .18);
        g.shadowBlur = 8 * reach;
        g.beginPath();
        g.arc(ox, oy, k * step + reach * step * .15, ang - spread, ang + spread);
        g.stroke();
      }
      g.shadowBlur = 0;
      g.globalAlpha = 1;
    });
  }

  return {
    toggle, prev, next, tick,
    now(s, t, h) { name = s || name; title = t; setHue(h); label(); },
    word(w, catchFn = null) { capWord = w || null; onCatch = capWord ? catchFn : null; label(); },
    onAir(v) {
      if (v === onAir) return;
      onAir = v;
      card.classList.toggle('on', v);
      label();
    },
    paused(v) {
      toggle.classList.toggle('pc-playing', !v);
      toggle.setAttribute('aria-label', `${v ? 'Play' : 'Pause'} ${name}`);
    },
  };
}
