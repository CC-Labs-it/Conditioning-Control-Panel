import { addWavePress, chosenWave, drawWave, waveFields } from './wave-ribbon.js';
import { IMPULSE_LIFE, isKineticWave } from './kinetic-waves.js';

/** Listen owns the original wave. Players on another route can equip a visual
 * ribbon through the button wardrobe without starting the Low Hum audio. */
export function mountGameWave(ctx, fromWardrobe = false) {
  const button = ctx.button.el;
  const anchor = button.closest('#btn-zone') || button;
  let canvas, g, width, height = 284, density, lastTop, lastStyle, lastQuiet;
  let time = 0, elapsed = 0;
  const hits = [];
  function visibleStyle() {
    const bag = ctx.state.store?.bubble;
    const preview = button.ownerDocument.documentElement.dataset.waveStudy;
    if (fromWardrobe && (ctx.has('a_hum') || (!bag?.waveStyle && !preview))) return null;
    if (isKineticWave(preview)) return preview;
    return ctx.has('bubble') && ctx.switches.isOn('bubble') ? chosenWave(bag) : 'classic';
  }
  ctx.on('tap', ({auto, x}) => {
    if (auto || ctx.gentle || ctx.reduced || !canvas || canvas.hidden) return;
    const bb = canvas.getBoundingClientRect();
    const b = button.getBoundingClientRect();
    addWavePress(hits, time, ((x ?? b.left + b.width / 2) - bb.left) / Math.max(1, bb.width));
  });
  return dt => {
    elapsed += dt;
    if (elapsed < 1 / 30) return;
    const step = elapsed;
    elapsed = 0;
    const style = visibleStyle();
    if (style === null) {
      if (canvas && !canvas.hidden) { canvas.hidden = true; hits.length = 0; }
      return;
    }
    if (!canvas) {
      canvas = ctx.el('canvas', 'position:absolute;left:0;width:100%;height:284px;z-index:2;pointer-events:none;', null, 'back');
      canvas.setAttribute('aria-hidden','true');
      canvas.dataset.humWave = '';
      g = canvas.getContext('2d');
    }
    const wasHidden = canvas.hidden;
    canvas.hidden = false;
    const w = Math.max(1, canvas.clientWidth), dpr = Math.min(2, button.ownerDocument.defaultView.devicePixelRatio || 1, 2000/w);
    const resized = width !== w || density !== dpr;
    if (resized) {
      width = w; density = dpr;
      canvas.width = Math.round(w * dpr); canvas.height = Math.round(height * dpr);
      g.setTransform(canvas.width / w, 0, 0, canvas.height / height, 0, 0);
    }
    // Individual button skins squash and hop on a press. Anchor the ribbon
    // to the layout slot so that transient motion never moves the whole wave.
    const rect = anchor.getBoundingClientRect(), top = Math.round(rect.top + rect.height / 2 - height / 2);
    if (top !== lastTop) { lastTop = top; canvas.style.top = top + 'px'; }
    const quiet = ctx.gentle || ctx.reduced;
    const phones = ctx.has('a_phones') && ctx.switches.isOn('a_phones') && ctx.state.store.a_phones?.on !== undefined;
    const wide = phones ? (ctx.state.store.a_phones.on ? 1.6 : 1.1) : 1;
    const split = ctx.has('a_bin') && ctx.switches.isOn('a_bin');
    const palette = ctx.palette();
    const colors = palette.themes.length ? [palette.a,palette.b] : split ? ['#ff69b4','#2f8fd0'] : ['#7b5cff'];
    const textureReady = style !== 'classic' && !isKineticWave(style) && waveFields(canvas.ownerDocument).naturalWidth > 0;
    const paintKey = [style,wide,split,textureReady,...colors].join('|');
    if (quiet) {
      hits.length = 0;
      if (lastQuiet && !resized && !wasHidden && lastStyle === paintKey) return;
    } else time += step;
    while (hits.length && time - hits[0].time >= IMPULSE_LIFE) hits.shift();
    canvas.dataset.waveStyle = style;
    drawWave(g,width,height,{style,time,hits,wide,split,colors});
    lastStyle = paintKey; lastQuiet = quiet;
  };
}
