/* A real Racing Thoughts capture with a separate bonus button. The video never
 * owns money or a game clock, and no remote media or account is involved. */
import { restoreRace, advanceRace, takeRaceBonus, raceCanPlay } from './race-model.js';
import { sfx } from '../basics/fx.js';

export const RACE_CLIP = 'assets/race/threshold-loop.mp4';
export const RACE_POSTER = 'assets/race/threshold-poster.jpg';

export function mountRace(ctx) {
  ctx.css(`
    [data-arcade=mini_race]{width:100%;max-width:300px;margin:auto;display:grid;gap:3px;color:var(--ink);font:13px Arial,sans-serif}
    .mini-race-film{position:relative;aspect-ratio:16/9;width:100%;overflow:hidden;border:2px solid var(--niche);background:#15151d}
    .mini-race-film video,.mini-race-film img{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;pointer-events:none}
    .mini-race-collect{min-height:44px;width:100%;padding:6px 10px;border:2px solid var(--line);background:var(--niche-soft);color:var(--ink);font:700 13px Arial,sans-serif;touch-action:manipulation;cursor:pointer}
    .mini-race-collect:disabled{cursor:default;opacity:.7}
    .mini-race-collect:not(:disabled){border-color:var(--niche-text);box-shadow:inset 0 -3px 0 var(--niche)}
    .mini-race-caption{font-size:11px;color:var(--ink-soft);text-align:center}
    .mini-race-receipt{text-align:center;font-variant-numeric:tabular-nums;font-weight:700}
    .mini-race-receipt:empty{display:none}
  `);
  const dock = ctx.dock({ title: 'Racing Thoughts' });
  const root = ctx.el('div', '', null, dock);
  root.dataset.arcade = 'mini_race';
  const film = ctx.el('div', '', null, root);
  film.className = 'mini-race-film';
  const video = ctx.el('video', '', null, film);
  video.muted = true;
  video.defaultMuted = true;
  video.setAttribute('muted', '');
  video.playsInline = true;
  video.setAttribute('playsinline', '');
  video.disablePictureInPicture = true;
  video.loop = true;
  video.preload = 'metadata';
  video.poster = RACE_POSTER;
  video.src = RACE_CLIP;
  const poster = ctx.el('img', '', null, film);
  poster.src = RACE_POSTER;
  poster.alt = 'Racing Thoughts on the Threshold course';
  const caption = ctx.el('small', '', 'Recorded gameplay', root);
  caption.className = 'mini-race-caption';
  const collect = ctx.el('button', '', '', root);
  collect.type = 'button';
  collect.className = 'mini-race-collect';
  const receipt = ctx.el('div', '', '', root);
  receipt.className = 'mini-race-receipt';
  receipt.setAttribute('aria-live', 'polite');

  const doc = root.ownerDocument;
  const model = restoreRace(ctx.store.get('race'));
  let dead = false, ended = false, failed = false, visible = false, playing = false;
  let lastClock = ctx.state.clock, lastSecond = -1, wasGentle = ctx.gentle;
  const persist = () => ctx.store.set('race', { ...model });
  const canPlay = () => raceCanPlay({ paused: ctx.paused || ended || !!ctx.state.ended, hidden: doc.hidden, gentle: ctx.gentle, visible, dead, failed });
  function sync() {
    poster.hidden = !ctx.gentle && !failed;
    video.hidden = ctx.gentle || failed;
    if (!canPlay()) { playing = false; video.pause(); return; }
    if (playing) return;
    playing = true;
    video.play()?.catch?.(() => { playing = false; });
  }
  function measure() {
    const r = film.getBoundingClientRect();
    visible = r.width > 0 && r.height > 0 && r.bottom > 0 && r.right > 0 && r.top < doc.defaultView.innerHeight && r.left < doc.defaultView.innerWidth;
    sync();
  }
  video.addEventListener('play', () => { if (!canPlay()) { playing = false; video.pause(); } });
  video.addEventListener('volumechange', () => { if (!video.muted) video.muted = true; });
  video.addEventListener('error', () => { failed = true; caption.textContent = 'Footage unavailable'; sync(); });
  const observer = doc.defaultView.IntersectionObserver ? new doc.defaultView.IntersectionObserver((entries) => {
    visible = entries.some((e) => e.isIntersecting && e.intersectionRect.width > 0 && e.intersectionRect.height > 0);
    sync();
  }) : null;
  observer?.observe(film);
  doc.addEventListener('visibilitychange', sync);
  ctx.on('pause', sync);
  ctx.on('resume', () => { lastClock = ctx.state.clock; measure(); });
  ctx.on('suspend', sync);
  ctx.on('ending', () => { ended = true; sync(); });
  ctx.on('buy', sync); // An ending purchase stops footage before the ending fade.
  ctx.every(500, measure); // Covers display:none trays and collapsed dock bodies too.
  ctx.frame(() => {
    const now = ctx.state.clock;
    if (!doc.hidden) advanceRace(model, Math.max(0, now - lastClock));
    lastClock = now;
    if (wasGentle !== ctx.gentle) { wasGentle = ctx.gentle; sync(); }
    const seconds = Math.ceil(model.leftMs / 1000);
    if (seconds === lastSecond) return;
    lastSecond = seconds;
    collect.disabled = seconds > 0;
    collect.textContent = seconds ? `Bonus in ${seconds}s` : 'Collect bonus';
    persist();
  });
  collect.disabled = model.leftMs > 0;
  collect.textContent = model.leftMs ? `Bonus in ${Math.ceil(model.leftMs / 1000)}s` : 'Collect bonus';
  collect.addEventListener('click', () => {
    if (dead || ended || ctx.paused || !takeRaceBonus(model)) return;
    persist(); // Consume readiness before crediting: a second click cannot pay again.
    collect.disabled = true;
    collect.textContent = 'Bonus in 35s';
    const r = collect.getBoundingClientRect(), rates = ctx.rates();
    const got = ctx.pay(Math.max(20, rates.fps * 12 + rates.click * 20), 'mini_race', { x: r.left + r.width / 2, y: r.top });
    receipt.textContent = `+${ctx.fmt(got)} ${ctx.brand.currencyPlural}`;
    sfx(ctx, 'pluck/wood_07.wav', .16);
    if (!ctx.reduced) ctx.anim(receipt, [{ opacity: .35 }, { opacity: 1 }], { duration: ctx.gentle ? 1400 : 350 });
  });
  ctx.after(0, measure);
  ctx.cleanup(() => {
    dead = true;
    observer?.disconnect();
    doc.removeEventListener('visibilitychange', sync);
    video.pause();
    video.removeAttribute('src');
    video.load();
  });
  return { root };
}
