/* ui/unlocks/podcast-player.js - the small player behind every theme podcast. Reads
 * assets/audio/podcast/<theme>/manifest.json (shape: handoff-1102/GAME-PODCAST-HANDOFF.md). Anything missing is silence and the
 * card stays. Episodes go through ctx.audio.stream, so mute, pause, suspend, panic and the end stop them in the kit's own
 * call; this file never holds an audio element and never polls. Clips are short and play over a ducked episode. */

const DUCK = 0.25;
const AD_EVERY_MS = 210000;
const REACT_GAP_MS = 60000;
const IDLE_MS = 45000;

export function mountPodcast(ctx, { theme, show }) {
  const roomy = ctx.bounds().w >= 1100;
  const box = roomy
    ? ctx.el('div', 'box-sizing:border-box;padding:8px 10px;background:var(--panel);color:var(--ink);border:1px solid var(--line);border-radius:4px;font:13px/1.25 var(--font);', null, ctx.layer('side'))
    : ctx.el('div', 'display:flex;gap:6px;align-items:center;justify-content:center;width:min(300px,100%);font:13px var(--font);white-space:nowrap;', null, 'meters');
  const name = ctx.el('b', 'flex:none;', `▶ ${show}`, box);
  const sub = ctx.el(roomy ? 'div' : 'span', 'overflow:hidden;text-overflow:ellipsis;color:var(--ink-soft);', '', box);
  const track = ctx.el('div', `height:4px;background:var(--fill);border-radius:2px;overflow:hidden;${roomy ? 'margin-top:6px;' : 'flex:0 1 60px;'}`, null, box);
  const fill = ctx.el('div', 'height:100%;width:100%;background:var(--stamp);transform-origin:0 50%;transform:scaleX(0);', null, track);

  const base = `assets/audio/podcast/${theme}/`;
  let manifest = null, ep = null, at = ctx.store.get('ep') || 0, gone = false;
  let lastReact = -REACT_GAP_MS, lastTap = ctx.state.clock, lastAd = ctx.state.clock;

  function next() {
    const list = manifest?.episodes || [];
    if (!list.length || gone) return;
    const e = list[at % list.length];
    at += 1;
    ctx.store.set('ep', at);
    sub.textContent = e.title ? ctx.brand.text(e.title) : '';
    ep?.stop();
    fill.style.transform = 'scaleX(0)';
    ep = ctx.audio.stream(base + e.file, { bus: 'podcast' });
    const el = ep.el;
    if (!el) return;
    el.addEventListener('ended', next);
    el.addEventListener('error', () => { sub.textContent = ''; });
    el.addEventListener('timeupdate', () => { if (el.duration) fill.style.transform = `scaleX(${Math.min(1, el.currentTime / el.duration)})`; });
  }

  /** A short clip over a ducked episode. A missing or broken one is silence and the duck still lifts. */
  function clip(kinds) {
    const pool = kinds.flatMap((k) => manifest?.clips?.[k] || []).filter((c) => c && c.file);
    if (!pool.length || gone) return false;
    const c = pool[Math.floor(ctx.rng() * pool.length)];
    ep?.setGain(DUCK);
    ctx.audio.play(base + c.file, { bus: 'podcast' });
    ctx.after(Math.max(1, Number(c.seconds) || 8) * 1000, () => ep?.setGain(1));
    return true;
  }

  const react = (kinds) => {
    const clock = ctx.state.clock;
    if (clock - lastReact < REACT_GAP_MS) return;
    if (clip(kinds)) lastReact = clock;
  };
  ctx.on('buy', () => react(['react_buy']));
  ctx.on('tap', (e) => { if (!e.auto) lastTap = ctx.state.clock; });
  ctx.every(5000, () => {
    const clock = ctx.state.clock;
    if (clock - lastTap > IDLE_MS) { lastTap = clock; react(['react_idle']); }
    if (clock - lastAd > AD_EVERY_MS) { lastAd = clock; clip(['ad_ccp', 'ad_joke']); }
  });

  fetch(`${base}manifest.json`).then((r) => (r.ok ? r.json() : null)).then((m) => {
    if (!m || gone) return;
    manifest = m;
    if (m.show) name.textContent = `▶ ${ctx.brand.text(m.show)}`;
    next();
    clip(['opener']);
  }).catch(() => {});

  return { off() { gone = true; } };
}
