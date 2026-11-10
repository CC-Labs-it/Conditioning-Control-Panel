const spiralPoints = Array.from({ length: 150 }, (_, i) => {
  const t = i / 149, angle = t * Math.PI * 4.8, radius = 9 + t * 530;
  return `${i ? 'L' : 'M'}${(400 + Math.cos(angle) * radius).toFixed(1)} ${(400 + Math.sin(angle) * radius).toFixed(1)}`;
}).join(' ');

// Also embedded in the preview HTML so the handoff covers module loading itself.
export const ARRIVAL_MARKUP = `<div class="td-arrival" role="status" aria-label="Entering the museum" style="position:absolute;inset:0;z-index:40;overflow:hidden;background:#14131c;color:#f0e9d8;display:grid;place-items:center;pointer-events:auto">
<svg class="td-arrival-spiral" viewBox="0 0 800 800" aria-hidden="true" focusable="false"><g fill="none" stroke-width="34" stroke-linecap="round" opacity=".65">
${['#7ec8c9', '#be719e', '#dbc38e'].map((color, i) => `<path d="${spiralPoints}" stroke="${color}" transform="rotate(${i * 120} 400 400)"/>`).join('')}
</g></svg><div class="td-arrival-shade"></div><div class="td-arrival-title"><span>GOONING MUSEUM</span><strong>A Natural History<br>of the Tab</strong><i aria-hidden="true"></i></div></div>`;

export const ARRIVAL_STYLE = `.td-arrival .td-arrival-spiral{position:absolute;left:50%;top:50%;width:145vmax;height:145vmax;max-width:none;transform:translate(-50%,-50%);animation:td-arrival-turn 24s linear infinite;will-change:transform}
.td-arrival .td-arrival-shade{position:absolute;inset:0;background:radial-gradient(ellipse at center,#14131c00 15%,#14131c55 55%,#14131cf2 100%),radial-gradient(circle at 50% 50%,#14131cee 0 13%,#14131c55 34%,transparent 54%)}
.td-arrival .td-arrival-title{position:relative;text-align:center;padding:24px;color:#f0e9d8;text-shadow:0 2px 18px #14131c;font-family:Georgia,'Times New Roman',serif}
.td-arrival-title span{display:block;font:600 10px/1.5 Arial,sans-serif;letter-spacing:4px;color:#b3d9d3;margin-bottom:18px}
.td-arrival-title strong{display:block;font-size:clamp(26px,4vw,48px);line-height:1.12;font-weight:normal;font-style:italic}
.td-arrival-title i{display:block;width:42px;height:1px;background:#d5bd8b;margin:23px auto 0}
.td-arrival[data-gentle=true] .td-arrival-spiral{animation-duration:72s}
.td-arrival[data-reduced=true] .td-arrival-spiral{animation:none}
.td-arrival[data-paused=true] .td-arrival-spiral{animation-play-state:paused}
@keyframes td-arrival-turn{to{transform:translate(-50%,-50%) rotate(360deg)}}
@media(prefers-reduced-motion:reduce){.td-arrival .td-arrival-spiral{animation:none}}`;

/** A finite handoff over the existing ending layer. It owns no render loop. */
export function createMuseumArrival(ctx, parent) {
  const doc = parent.ownerDocument, win = doc.defaultView;
  let node = parent.querySelector(':scope > .td-arrival');
  if (!node) { const template = doc.createElement('template'); template.innerHTML = ARRIVAL_MARKUP; node = template.content.firstElementChild; parent.append(node); }
  let styles = parent.querySelector(':scope > style[data-museum-arrival]');
  if (!styles) { styles = doc.createElement('style'); styles.dataset.museumArrival = ''; styles.textContent = ARRIVAL_STYLE; parent.append(styles); }
  node.dataset.gentle = String(Boolean(ctx.gentle)); node.dataset.reduced = String(Boolean(ctx.reduced));
  const born = win.performance.now(), waits = new Set(), cleanups = [];
  let disposed = false, animation = null, revealing = null, held = false;
  function wait(ms) {
    return new Promise(resolve => {
      if (disposed) { resolve(); return; }
      const job = { timer: null, finish() { win.clearTimeout(job.timer); waits.delete(job); resolve(); } };
      waits.add(job); job.timer = win.setTimeout(job.finish, ms);
    });
  }
  function sync() {
    const paused = held || ctx.paused || doc.hidden;
    node.dataset.paused = String(Boolean(paused));
    if (animation) { if (paused) animation.pause(); else animation.play(); }
  }
  function dispose() {
    if (disposed) return;
    disposed = true;
    animation?.cancel(); animation = null;
    for (const job of [...waits]) job.finish();
    for (const off of cleanups) off?.();
    doc.removeEventListener('visibilitychange', sync);
    node.remove(); styles.remove();
  }
  async function reveal(sceneRoot) {
    if (revealing) return revealing;
    revealing = (async () => {
      // Give async media mounts a chance to attach, then wait only for nearby faces.
      await wait(80);
      const deadline = win.performance.now() + 1400;
      while (!disposed && win.performance.now() < deadline) {
        const visible = element => !element.closest('.td-plane')?.hidden;
        const images = [...sceneRoot.querySelectorAll('img')].filter(visible);
        const waitingImages = images.some(img => !img.complete);
        const waitingMedia = ctx.pictures?.source !== 'none' && [...sceneRoot.querySelectorAll('.td-run-media')].some(layer => visible(layer) && layer.hidden);
        if (!waitingImages && !waitingMedia) break;
        await wait(50);
      }
      await wait(Math.max(0, 700 - (win.performance.now() - born)));
      while (!disposed && (held || ctx.paused || doc.hidden)) await wait(50);
      if (disposed) return;
      // Assets and one static 3D view are already underneath this short dissolve.
      animation = node.animate([{ opacity: 1 }, { opacity: 0 }], { duration: ctx.reduced ? 140 : 520, easing: 'ease-in-out', fill: 'forwards' });
      sync();
      try { await animation.finished; } catch { /* Panic cancels the finite dissolve. */ }
      dispose();
    })();
    return revealing;
  }
  cleanups.push(ctx.on?.('pause', () => { held = true; sync(); }), ctx.on?.('resume', () => { held = false; sync(); }));
  cleanups.push(ctx.on?.('suspend', event => { if (event?.on && event.reason === 'panic') dispose(); else sync(); }));
  doc.addEventListener('visibilitychange', sync);
  ctx.cleanup?.(dispose); sync();
  return Object.freeze({ reveal, dispose, debug: () => ({ disposed, revealing: Boolean(revealing), waits: waits.size }) });
}
