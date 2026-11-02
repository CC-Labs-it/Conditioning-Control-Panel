/* ui/unlocks/podcast-player.js - one small player for every `*_pod` unlock (themes-a and themes-b import it).
 *
 *   return mountPodcast(ctx, { theme: 'bimbo', show: 'True Bimbo' });
 *
 * Reads assets/audio/podcast/<theme>/manifest.json (shape: handoff-1102/GAME-PODCAST-HANDOFF.md, "What to hand back").
 * Anything missing is silence: no manifest, no file, a failed play. The player with the show's name still shows.
 * All audio goes through ctx.audio.stream: the kit owns the element, so mute, pause, suspend, panic and the end stop it
 * in the same call, with nothing here listening or polling.
 */

const DUCK = 0.36;            // an episode under a clip, as a share of the bus
const CLIP_EVERY_MS = 210000; // an ad between stretches
const REACT_GAP_MS = 60000;   // a reaction at most this often
const IDLE_MS = 45000;        // no taps for this long is worth a remark
const CELLS = 10;
const OFF_AIR = 'off air';

const pick = (ctx, list) => list[Math.floor(ctx.rng() * list.length) % list.length];

export function mountPodcast(ctx, { theme, show, accent = 'var(--stamp)' }) {
  const base = `assets/audio/podcast/${theme}/`;
  const ui = drawPlayer(ctx, show, accent);
  let manifest = null, ep = null, clip = null, wanted = true, gone = false;
  let lastClip = 0, lastReact = 0, lastTap = 0, saveAt = 0;

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
    s.el.addEventListener('error', () => { if (ep === s) { ep = null; ui.title(show); } });
    ui.title(e.title || show);
    ui.progress(0);
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
    ui.onAir((m.episodes || []).length > 0);
    const first = !ctx.store.get('heard');
    ctx.store.set('heard', 1);
    loadEpisode(ctx.store.get('ep') || 0, ctx.store.get('at') || 0);
    if (first) playClip(['opener']);
  }).catch(() => {});

  ui.toggle.addEventListener('click', () => {
    wanted = !wanted;
    ui.paused(!wanted);
    if (wanted) { ep?.play(); clip?.play(); } else { ep?.pause(); clip?.pause(); }
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
  const status = ctx.el('span', 'flex:none;font-size:11px;', OFF_AIR, box);
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
