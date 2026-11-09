// The game owns movement, scaling, audio routing and the lifetime of this object.
import { mountVolumeSlider } from './volume-slider.js';
const assets = new URL('../../assets/lofi/', import.meta.url);

export function mountLofi(ctx) {
  const host = ctx.dock({ title: 'Lo-fi Radio', width: 292, reserveHeight: 274 });
  const doc = host.ownerDocument;
  if (!doc.querySelector('link[data-lofi-css]')) {
    const css = doc.createElement('link');
    css.rel = 'stylesheet';
    css.href = new URL('player.css', assets).href;
    css.dataset.lofiCss = '';
    doc.head.append(css);
  }
  let player = null, removed = false;
  const files = ['tracks.js', 'art.js', 'waveforms.js', 'player.js'];
  Promise.all(files.map(name => import(new URL(name, assets).href))).then(() => {
    if (removed) return;
    player = globalThis.FuzzFM.mount(host, {
      niche: ctx.pick('f5'),
      base: assets.href,
      autoplay: ctx.store.get('playing') !== false,
      onIntent: playing => ctx.store.set('playing', playing),
      openStream: url => ctx.audio.stream(url, { bus: 'podcast', autoplay: false }),
    });
    /* Owner 2026-11-08: a quiet volume line under the controls, the same level as the HUD mixer's Lo-fi Radio row. */
    const pill = host.querySelector('.fm-pill');
    if (pill) mountVolumeSlider(ctx, pill, { label: 'Radio volume', ink: 'var(--fm-ink)', ring: '#fffaf0' });
  }).catch(() => {
    if (!removed) ctx.toast('Lo-fi Radio could not load.');
  });
  ctx.frame((_dt, now) => {
    if (host.dock?.visible ?? host.getClientRects().length > 0) player?.tick(now, ctx.gentle || ctx.reduced);
  });
  return { off() { removed = true; player?.destroy(); } };
}
