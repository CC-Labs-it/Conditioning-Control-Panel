/* ui/unlocks/podcast-player.js - one small player for every `*_pod` unlock (themes-a and themes-b import it).
 *
 *   return mountPodcast(ctx, { theme: 'bimbo', show: 'True Bimbo' });
 *
 * Reads assets/audio/podcast/<theme>/manifest.json (shape: handoff-1102/GAME-PODCAST-HANDOFF.md, "What to hand back").
 * Anything missing is silence: no manifest, no file, a failed play. The player with the show's name still shows.
 * Episodes stream through a media element, never decoded whole. ctx.audio cannot stream, so this file owns two <audio>
 * elements and answers to the same stops by hand: the `pause` event, the Mute box, and a dead man's switch on the game
 * clock (ctx.frame stops on pause, suspend and the end, so audio stops within a quarter second of any of them).
 */

const VOLUME = 0.5;           // same as the kit's podcast bus
const DUCK = 0.18;            // an episode under a clip
const CLIP_EVERY_MS = 210000; // an ad between stretches
const REACT_GAP_MS = 60000;   // a reaction at most this often
const IDLE_MS = 45000;        // no taps for this long is worth a remark
const CELLS = 10;

const pick = (ctx, list) => list[Math.floor(ctx.rng() * list.length) % list.length];

export function mountPodcast(ctx, { theme, show, accent = 'var(--stamp)' }) {
  const base = `assets/audio/podcast/${theme}/`;
  const ui = drawPlayer(ctx, show, accent);
  const ep = new Audio(), clip = new Audio();
  ep.preload = 'none'; clip.preload = 'auto';
  let manifest = null, playing = false, wanted = true, gone = false, clipOn = false;
  let beat = performance.now(), lastClip = 0, lastReact = 0, lastTap = 0, saveAt = 0;

  const muted = () => !!ctx.world.ownerDocument.getElementById('mute')?.checked;
  const volumes = () => { const v = muted() ? 0 : VOLUME; ep.volume = clipOn ? v * DUCK : v; clip.volume = v; };

  const stopNow = () => { ep.pause(); clip.pause(); playing = false; };
  const goOn = () => {
    if (gone || !wanted || !manifest) return;
    playing = true;
    volumes();
    if (clipOn) clip.play().catch(() => {});
    if (ep.src) ep.play().catch(() => {});
  };

  /* ---- episodes, in rotation, resuming where the save left off ---- */
  function loadEpisode(i, at = 0) {
    const list = manifest?.episodes || [];
    if (!list.length) return;
    const n = ((i % list.length) + list.length) % list.length, e = list[n];
    ctx.store.set('ep', n);
    ep.src = base + e.file;
    if (at > 0) ep.addEventListener('loadedmetadata', () => { try { ep.currentTime = at; } catch { /* not seekable yet */ } }, { once: true });
    ui.title(e.title || show);
    goOn();
  }
  ep.addEventListener('ended', () => { ctx.store.set('at', 0); loadEpisode((ctx.store.get('ep') || 0) + 1); });
  ep.addEventListener('error', () => { ep.removeAttribute('src'); ui.title(show); });

  /* ---- clips: the opener, ads, reactions. One at a time, over a ducked episode. ---- */
  function playClip(kinds) {
    if (clipOn || !manifest?.clips) return false;
    const pool = kinds.flatMap((k) => manifest.clips[k] || []);
    if (!pool.length) return false;
    const c = pick(ctx, pool);
    clipOn = true;
    clip.src = base + c.file;
    if (c.line) ctx.say(c.line);
    volumes();
    if (playing) clip.play().catch(() => clipEnd());
    return true;
  }
  function clipEnd() { clipOn = false; clip.removeAttribute('src'); volumes(); }
  clip.addEventListener('ended', clipEnd);
  clip.addEventListener('error', clipEnd);
  const react = (kinds) => {
    const t = ctx.state.clock;
    if (t - lastReact < REACT_GAP_MS || !playing) return;
    if (playClip(kinds)) lastReact = t;
  };

  ctx.on('buy', () => react(['react_buy']));
  ctx.on('tap', (e) => { if (!e?.auto) { lastTap = ctx.state.clock; if (ctx.rng() < 0.01) react(['react_tap']); } });
  ctx.on('pause', stopNow);
  ctx.on('resume', goOn);

  /* ---- start ---- */
  fetch(`${base}manifest.json`).then((r) => (r.ok ? r.json() : null)).then((m) => {
    if (gone || !m) return;
    manifest = m;
    if (m.show) ui.show(m.show);
    ui.onAir((m.episodes || []).length > 0);
    const first = !ctx.store.get('heard');
    ctx.store.set('heard', 1);
    loadEpisode(ctx.store.get('ep') || 0, ctx.store.get('at') || 0);
    if (first) playClip(['opener']);
  }).catch(() => {});

  ui.toggle.addEventListener('click', () => {
    wanted = !wanted;
    ui.paused(!wanted);
    if (wanted) goOn(); else stopNow();
  });

  ctx.frame((dt, now) => {
    beat = performance.now();
    if (!wanted || !manifest) return;
    if (!playing) goOn();
    const dur = ep.duration;
    if (dur > 0) ui.progress(ep.currentTime / dur);
    if (now > saveAt) {
      saveAt = now + 10000;
      ctx.store.set('at', Math.floor(ep.currentTime || 0));
      volumes();
      if (!lastClip) lastClip = now;
      if (now - lastClip > CLIP_EVERY_MS) { lastClip = now; playClip(['ad_ccp', 'ad_joke']); }
      if (lastTap && ctx.state.clock - lastTap > IDLE_MS) { lastTap = 0; react(['react_idle']); }
    }
  });

  /* A real timer on purpose: it is what notices the game clock has stopped. */
  const guard = setInterval(() => {
    if (performance.now() - beat > 250 && playing) stopNow();
    else if (playing) volumes();
  }, 120);
  ctx.world.ownerDocument.getElementById('mute')?.addEventListener('change', volumes);

  return {
    off() {
      gone = true;
      clearInterval(guard);
      ctx.world.ownerDocument.getElementById('mute')?.removeEventListener('change', volumes);
      stopNow();
      ep.removeAttribute('src'); clip.removeAttribute('src');
      ep.load?.(); clip.load?.();
    },
  };
}

/** Mic, name, a play bar cut into cells, and a stop of its own. Small enough to live under the counter on a phone. */
function drawPlayer(ctx, show, accent) {
  const box = ctx.el('div', 'display:flex;align-items:center;gap:6px;height:24px;margin-top:2px;font-size:13px;color:var(--ink-soft);white-space:nowrap;max-width:100%;', null, 'meters');
  const mic = ctx.el('span', `flex:none;width:18px;height:18px;border-radius:50%;background:${accent};position:relative;`, null, box);
  ctx.el('span', 'position:absolute;left:6px;top:3px;width:6px;height:9px;border-radius:3px;background:var(--page);', null, mic);
  ctx.el('span', 'position:absolute;left:8px;top:12px;width:2px;height:3px;background:var(--page);', null, mic);
  const name = ctx.el('span', 'min-width:0;overflow:hidden;text-overflow:ellipsis;max-width:200px;', show, box);
  const cells = ctx.el('span', 'flex:none;display:inline-flex;gap:1px;', null, box);
  const bars = [];
  for (let i = 0; i < CELLS; i++) bars.push(ctx.el('span', `width:7px;height:6px;background:${accent};opacity:.25;`, null, cells));
  const toggle = ctx.el('button', 'flex:none;min-width:30px;height:24px;padding:0 4px;background:transparent;border:0;color:inherit;font-size:13px;pointer-events:auto;cursor:pointer;', '▮▮', box);
  toggle.type = 'button';
  toggle.setAttribute('aria-label', `Pause ${show}`);
  /* Before a manifest lands the show is simply not on air. */
  const status = ctx.el('span', 'flex:none;font-size:11px;', 'off air', box);
  let shown = -1;
  return {
    toggle,
    show(s) { name.textContent = s; },
    title(s) { name.textContent = s; },
    onAir(v) { status.hidden = v; },
    paused(v) { toggle.textContent = v ? '▶' : '▮▮'; },
    progress(p) {
      const n = Math.floor(Math.min(1, p) * CELLS);
      if (n === shown) return;
      shown = n;
      bars.forEach((b, i) => { b.style.opacity = i <= n ? '.9' : '.25'; });
    },
  };
}
