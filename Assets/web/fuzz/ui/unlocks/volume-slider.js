/* ui/unlocks/volume-slider.js - a quiet volume line for a dock object (owner 2026-11-08: Lo-fi Radio and the podcasts).
 * It is the same per-source level as the HUD mixer row for that unlock (ctx.item.id is the stream's source id, see
 * ui/context.js soundOptions), so the two stay in step both ways and the mixer's own save keeps it. A native range input:
 * keyboard, touch and screen readers come for free, and the dock never starts a casing move from an input. */

const CSS = `
.vs-row{display:flex;align-items:center;gap:6px;height:14px;margin:2px 0 0;opacity:.55;transition:opacity .2s;}
.vs-row:hover,.vs-row:focus-within,.vs-row.vs-touch{opacity:1;}
.vs-row svg{flex:none;width:11px;height:11px;color:var(--vs-ink,currentColor);}
.vs-row input{flex:1 1 auto;min-width:0;height:24px;margin:-5px 0;padding:0;background:transparent;cursor:pointer;touch-action:none;
  -webkit-appearance:none;appearance:none;--vs-fill:100%;}
.vs-row input:focus-visible{outline:2px solid var(--vs-ink,currentColor);outline-offset:-4px;border-radius:6px;}
.vs-row input::-webkit-slider-runnable-track{height:2px;border-radius:2px;
  background:linear-gradient(90deg,var(--vs-ink,currentColor) var(--vs-fill),color-mix(in srgb,var(--vs-ink,currentColor) 25%,transparent) var(--vs-fill));}
.vs-row input::-moz-range-track{height:2px;border-radius:2px;background:color-mix(in srgb,var(--vs-ink,currentColor) 25%,transparent);}
.vs-row input::-moz-range-progress{height:2px;border-radius:2px;background:var(--vs-ink,currentColor);}
.vs-row input::-webkit-slider-thumb{-webkit-appearance:none;width:10px;height:10px;margin-top:-4px;border-radius:50%;border:0;
  background:var(--vs-ink,currentColor);box-shadow:0 0 0 2px var(--vs-ring,#fff8);}
.vs-row input::-moz-range-thumb{width:10px;height:10px;border-radius:50%;border:0;background:var(--vs-ink,currentColor);box-shadow:0 0 0 2px var(--vs-ring,#fff8);}
.vs-row.vs-muted svg path.vs-wave{opacity:.15;}
`;

/** Speaker plus a wave that thins with the level. */
const ICON = '<svg viewBox="0 0 12 12" aria-hidden="true" focusable="false"><path d="M1 4.2h2.2L6 1.8v8.4L3.2 7.8H1z" fill="currentColor"/>'
  + '<path class="vs-wave" d="M8 3.6a3.4 3.4 0 0 1 0 4.8" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>';

/** The level a source plays at in the mixer: its saved volume, or full when nothing was saved. */
export function sourceVolume(mixer, id) {
  const live = mixer?.sources?.().find((s) => s.id === id);
  if (live) return { volume: live.volume, muted: live.muted };
  const saved = mixer?.snapshot?.()[id];
  return { volume: saved?.volume ?? 1, muted: saved?.muted === true };
}

/**
 * Appends the slider to `parent`. `label` is the accessible name ("Radio volume"). Moving it off zero unmutes the
 * source, so the line never looks set while the sound stays off.
 */
export function mountVolumeSlider(ctx, parent, { label, ink = '', ring = '' } = {}) {
  const mixer = ctx.audio?.mixer, id = ctx.item.id, doc = parent.ownerDocument;
  if (!mixer) return null;
  if (!doc.getElementById('vs-css')) {
    const st = doc.createElement('style');
    st.id = 'vs-css';
    st.textContent = CSS;
    doc.head.appendChild(st);
  }
  const row = doc.createElement('div');
  row.className = 'vs-row';
  if (ink) row.style.setProperty('--vs-ink', ink);
  if (ring) row.style.setProperty('--vs-ring', ring);
  row.innerHTML = ICON;
  const input = doc.createElement('input');
  input.type = 'range'; input.min = '0'; input.max = '100'; input.step = '1';
  input.setAttribute('aria-label', label);
  input.title = label;
  row.append(input);
  parent.append(row);

  function paint() {
    const { volume, muted } = sourceVolume(mixer, id);
    const v = Math.round(volume * 100);
    if (doc.activeElement !== input || input.valueAsNumber !== v) input.value = String(v);
    input.style.setProperty('--vs-fill', v + '%');
    input.setAttribute('aria-valuenow', String(v));
    input.setAttribute('aria-valuetext', muted ? 'Muted' : v + '%');
    row.classList.toggle('vs-muted', muted || v === 0);
  }
  input.addEventListener('input', () => {
    const v = input.valueAsNumber / 100;
    mixer.setVolume(id, v);
    if (v > 0 && sourceVolume(mixer, id).muted) mixer.setMuted(id, false);
    paint();
  });
  /* Touch shows the line at full strength while the finger is on it. */
  input.addEventListener('pointerdown', (e) => { e.stopPropagation(); if (e.pointerType === 'touch') row.classList.add('vs-touch'); });
  const lift = () => row.classList.remove('vs-touch');
  input.addEventListener('pointerup', lift);
  input.addEventListener('pointercancel', lift);
  const off = mixer.subscribe(paint);
  ctx.cleanup?.(() => { off?.(); row.remove(); });
  paint();
  return { row, input, paint };
}
