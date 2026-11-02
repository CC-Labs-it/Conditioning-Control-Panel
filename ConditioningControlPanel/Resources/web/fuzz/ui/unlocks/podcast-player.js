/* ui/unlocks/podcast-player.js - the small player behind every theme podcast. Reads
 * assets/audio/podcast/<theme>/manifest.json (shape: handoff-1102/GAME-PODCAST-HANDOFF.md). Anything missing is silence and the
 * card stays. Episodes stream through a media element and are never decoded whole. */

/** The media element cannot join the game's audio buses, so it follows them by hand. The check runs on a real timer on
 *  purpose: the game clock stops on pause, suspend and the end, which is exactly when the sound has to stop. */
const WATCH_MS = 250;

export function mountPodcast(ctx, { theme, show }) {
  const doc = ctx.button.el.ownerDocument;
  const roomy = ctx.bounds().w >= 1100;
  const box = roomy
    ? ctx.el('div', 'box-sizing:border-box;padding:8px 10px;background:var(--panel);color:var(--ink);border:1px solid var(--line);border-radius:4px;font:13px/1.25 var(--font);', null, ctx.layer('side'))
    : ctx.el('div', 'display:flex;gap:6px;align-items:center;justify-content:center;width:min(300px,100%);font:13px var(--font);white-space:nowrap;', null, 'meters');
  const name = ctx.el('b', 'flex:none;', `▶ ${show}`, box);
  const sub = ctx.el(roomy ? 'div' : 'span', 'overflow:hidden;text-overflow:ellipsis;color:var(--ink-soft);', '', box);
  const track = ctx.el('div', `height:4px;background:var(--fill);border-radius:2px;overflow:hidden;${roomy ? 'margin-top:6px;' : 'flex:0 1 60px;'}`, null, box);
  const fill = ctx.el('div', 'height:100%;width:100%;background:var(--stamp);transform-origin:0 50%;transform:scaleX(0);', null, track);

  const audio = doc.createElement('audio');
  audio.preload = 'none';
  audio.volume = 0.5;
  let episodes = [], at = ctx.store.get('ep') || 0, wanted = false, seen = performance.now();
  const base = `assets/audio/podcast/${theme}/`;

  function next() {
    if (!episodes.length) return;
    const ep = episodes[at % episodes.length];
    at += 1;
    ctx.store.set('ep', at);
    sub.textContent = ep.title ? ctx.brand.text(ep.title) : '';
    audio.src = base + ep.file;
    wanted = true;
    audio.play?.().catch(() => {});
  }
  audio.addEventListener('ended', next);
  audio.addEventListener('error', () => { wanted = false; });
  audio.addEventListener('timeupdate', () => {
    if (audio.duration) fill.style.transform = `scaleX(${Math.min(1, audio.currentTime / audio.duration)})`;
  });

  ctx.frame(() => { seen = performance.now(); });
  const watch = setInterval(() => {
    const running = performance.now() - seen < WATCH_MS * 2;
    const muted = !!doc.getElementById('mute')?.checked;
    audio.muted = muted;
    if (!running && !audio.paused) audio.pause();
    else if (running && wanted && audio.paused) audio.play?.().catch(() => {});
  }, WATCH_MS);

  fetch(`${base}manifest.json`).then((r) => (r.ok ? r.json() : null)).then((m) => {
    if (!m || !box.isConnected) return;
    if (m.show) name.textContent = `▶ ${ctx.brand.text(m.show)}`;
    episodes = Array.isArray(m.episodes) ? m.episodes.filter((e) => e && e.file) : [];
    next();
  }).catch(() => {});

  return {
    off() {
      clearInterval(watch);
      wanted = false;
      audio.pause();
      audio.removeAttribute('src');
      audio.load?.();
    },
  };
}
