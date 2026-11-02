/* ui/unlocks/podcast-player.js - one player for every `*_pod` unlock (themes-a and themes-b import it) and for ASMR.
 *
 *   return mountPodcast(ctx, { theme: 'bimbo', show: 'True Bimbo' });
 *   return mountPodcast(ctx, { show: 'Closer', label: 'ASMR', base: 'assets/audio/asmr/trance/' });
 *
 * Reads <base>manifest.json, default assets/audio/podcast/<theme>/ (shape: handoff-1102/GAME-PODCAST-HANDOFF.md, "What to
 * hand back"). Anything missing is silence: no manifest, no file, a failed play. The player still shows, at the screen's
 * edge, with the show's name, "Off air" and its level bars standing still.
 * All audio goes through ctx.audio.stream: the kit owns the element, so mute, pause, suspend, panic and the end stop it
 * in the same call, with nothing here listening or polling.
 */

const DUCK = 0.36;            // an episode under a clip, as a share of the bus
const CLIP_EVERY_MS = 210000; // an ad between stretches
const REACT_GAP_MS = 60000;   // a reaction at most this often
const IDLE_MS = 45000;        // no taps for this long is worth a remark
const CELLS = 14;
const BARS = 16;
const OFF_AIR = 'Off air';
const ON_AIR = 'On air';

const pick = (ctx, list) => list[Math.floor(ctx.rng() * list.length) % list.length];

export function mountPodcast(ctx, { theme, show, label = 'Podcast', base = `assets/audio/podcast/${theme}/`, accent = 'var(--niche)' }) {
  const ui = drawPlayer(ctx, { show, label, accent });
  let manifest = null, ep = null, clip = null, wanted = true, gone = false;
  let lastClip = 0, lastReact = 0, lastTap = 0, saveAt = 0;

  const air = () => ui.level(!!ep && wanted);

  /* ---- episodes, in rotation, resuming where the save left off ---- */
  function loadEpisode(i, at = 0) {
    const list = manifest?.episodes || [];
    if (!list.length) return;
    const n = ((i % list.length) + list.length) % list.length, e = list[n];
    ctx.store.set('ep', n);
    ep?.stop();
    const s = ctx.audio.stream(base + e.file, { bus: 'podcast', autoplay: wanted });
    if (!s.el) return;
    ep = s;
    if (clip) s.setGain(DUCK);
    if (at > 0) s.el.addEventListener('loadedmetadata', () => { try { s.el.currentTime = at; } catch { /* not seekable yet */ } }, { once: true });
    s.el.addEventListener('ended', () => { if (gone || ep !== s) return; ctx.store.set('at', 0); loadEpisode(n + 1); });
    s.el.addEventListener('error', () => { if (ep === s) { ep = null; ui.title(''); ui.onAir(false); air(); } });
    ui.title(e.title || '');
    ui.onAir(true);
    ui.progress(0);
    air();
  }

  /* ---- clips: the opener, ads, reactions. One at a time, over a ducked episode. ---- */
  function playClip(kinds) {
    if (clip || !wanted || !manifest?.clips) return false;
    const pool = kinds.flatMap((k) => manifest.clips[k] || []);
    if (!pool.length) return false;
    const c = pick(ctx, pool);
    const s = ctx.audio.stream(base + c.file, { bus: 'podcast' });
    if (!s.el) return false;
    clip = s;
    ep?.setGain(DUCK);
    const end = () => { if (clip !== s) return; clip = null; s.stop(); ep?.setGain(1); };
    s.el.addEventListener('ended', end);
    s.el.addEventListener('error', end);
    if (c.line) ctx.say(c.line);
    return true;
  }
  const react = (kinds) => {
    const t = ctx.state.clock;
    if (t - lastReact < REACT_GAP_MS || !wanted || !manifest) return;
    if (playClip(kinds)) lastReact = t;
  };

  ctx.on('buy', () => react(['react_buy']));
  ctx.on('tap', (e) => { if (!e?.auto) { lastTap = ctx.state.clock; if (ctx.rng() < 0.01) react(['react_tap']); } });

  /* ---- start ---- */
  fetch(`${base}manifest.json`).then((r) => (r.ok ? r.json() : null)).then((m) => {
    if (gone || !m) return;
    manifest = m;
    if (m.show) ui.show(m.show);
    const first = !ctx.store.get('heard');
    ctx.store.set('heard', 1);
    loadEpisode(ctx.store.get('ep') || 0, ctx.store.get('at') || 0);
    if (first) playClip(['opener']);
  }).catch(() => {});

  ui.toggle.addEventListener('click', () => {
    wanted = !wanted;
    ui.paused(!wanted);
    if (wanted) { ep?.play(); clip?.play(); } else { ep?.pause(); clip?.pause(); }
    air();
  });

  ctx.frame((dt, now) => {
    if (!manifest || !ep?.el) return;
    const dur = ep.el.duration;
    if (dur > 0) ui.progress(ep.el.currentTime / dur);
    if (now > saveAt) {
      saveAt = now + 10000;
      ctx.store.set('at', Math.floor(ep.el.currentTime || 0));
      if (!lastClip) lastClip = now;
      if (now - lastClip > CLIP_EVERY_MS) { lastClip = now; playClip(['ad_ccp', 'ad_joke']); }
      if (lastTap && ctx.state.clock - lastTap > IDLE_MS) { lastTap = 0; react(['react_idle']); }
    }
  });

  /* The kit kills the streams with the unlock. */
  return { off() { gone = true; } };
}

const PLAYER_CSS = `
.pc-top{display:flex;align-items:center;gap:8px;}
.pc-mic{flex:none;position:relative;width:30px;height:30px;border-radius:50%;}
.pc-mic i{position:absolute;left:11px;top:6px;width:8px;height:13px;border-radius:4px;background:var(--page);}
.pc-mic b{position:absolute;left:14px;top:19px;width:2px;height:5px;background:var(--page);}
.pc-words{flex:1 1 auto;min-width:0;line-height:1.15;}
.pc-show{font-weight:700;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.pc-ep{font-size:12px;color:var(--ink-soft);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.pc-state{display:inline-block;margin-top:1px;padding:0 4px;font-size:11px;line-height:15px;color:var(--ink-soft);border:1px solid var(--line);}
.pc-state.on{color:var(--niche-ink);background:var(--niche);border-color:var(--niche);}
.pc-toggle{flex:none;width:36px;height:36px;padding:0;background:var(--btn);color:inherit;border:1px solid var(--line);font-size:13px;touch-action:manipulation;}
.pc-toggle:disabled{opacity:.4;}
.pc-bars{display:flex;align-items:flex-end;gap:2px;height:26px;margin-top:6px;}
.pc-bars i{flex:1 1 0;height:100%;transform-origin:50% 100%;opacity:.4;}
.pc-bars.live i{opacity:1;animation:pc-level 900ms ease-in-out infinite alternate;}
@keyframes pc-level{from{transform:scaleY(.15);}to{transform:scaleY(.9);}}
body.gentle .pc-bars.live i{animation:none;}
.pc-cells{display:flex;gap:1px;margin-top:4px;}
.pc-cells i{flex:1 1 0;height:4px;opacity:.22;}
`;

/** Mic, show, episode, on or off air, a stop of its own, level bars and a play bar cut into cells. */
function drawPlayer(ctx, { show, label, accent }) {
  ctx.css(PLAYER_CSS);
  /* First in the right rail, in order of purchase, and never folded: a player is the one slot you came to the edge for. */
  const box = ctx.dock({ edge: 'right', title: label, keep: true, order: -500 + ctx.state.order.indexOf(ctx.item.id) });
  const top = ctx.el('div', '', null, box);
  top.className = 'pc-top';
  const mic = ctx.el('span', `background:${accent};`, null, top);
  mic.className = 'pc-mic';
  ctx.el('i', '', null, mic);
  ctx.el('b', '', null, mic);
  const words = ctx.el('div', '', null, top);
  words.className = 'pc-words';
  const name = ctx.el('div', '', show, words);
  name.className = 'pc-show';
  const episode = ctx.el('div', 'display:none;', '', words);
  episode.className = 'pc-ep';
  /* Before a manifest lands the show is simply not on air. */
  const status = ctx.el('span', '', OFF_AIR, words);
  status.className = 'pc-state';
  const toggle = ctx.el('button', '', '▮▮', top);
  toggle.type = 'button';
  toggle.className = 'pc-toggle';
  toggle.disabled = true;
  toggle.setAttribute('aria-label', `Pause ${show}`);
  const barsEl = ctx.el('div', '', null, box);
  barsEl.className = 'pc-bars';
  /* Standing still at uneven heights until there is something to hear: a level meter with the sound off. */
  for (let i = 0; i < BARS; i++) ctx.el('i', `background:${accent};transform:scaleY(${(0.14 + ((i * 7) % 11) / 16).toFixed(2)});animation-delay:${-((i * 317) % 900)}ms;`, null, barsEl);
  const cellsEl = ctx.el('div', '', null, box);
  cellsEl.className = 'pc-cells';
  const cells = [];
  for (let i = 0; i < CELLS; i++) cells.push(ctx.el('i', `background:${accent};`, null, cellsEl));
  let shown = -1;
  return {
    toggle,
    show(s) { name.textContent = s; },
    title(s) { episode.textContent = s; episode.style.display = s ? '' : 'none'; },
    onAir(v) { status.textContent = v ? ON_AIR : OFF_AIR; status.classList.toggle('on', v); toggle.disabled = !v; },
    level(v) { barsEl.classList.toggle('live', v); },
    paused(v) { toggle.textContent = v ? '▶' : '▮▮'; },
    progress(p) {
      const n = Math.floor(Math.min(1, p) * CELLS);
      if (n === shown) return;
      shown = n;
      cells.forEach((b, i) => { b.style.opacity = i <= n ? '.9' : '.22'; });
    },
  };
}
