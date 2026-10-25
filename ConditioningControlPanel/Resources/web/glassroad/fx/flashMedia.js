// One view-blocking card, borrowing the existing approved media decoder.
// No fetches, retained private frames, CSS filters or independent animation clock.
export function flashLayout(aspect, params = {}) {
  const cover = Math.min(.9, Math.max(.1, params.cover || .7));
  let h = Math.min(.9, Math.sqrt(cover * aspect * .75)), w = Math.min(.94, h * 4 / 3 / aspect);
  if (params.corner) { w *= params.scale || .35; h *= params.scale || .35; }
  return { x: params.corner ? .035 : .5 - w / 2, y: params.corner ? .065 : .5 - h / 2, w, h };
}

export function createFlashMedia({ parent = document.body, source = () => null, make = () => document.createElement('canvas'),
  fallback = typeof Image === 'function' ? new Image() : null, fallbackSource = () => null } = {}) {
  const canvas = make(), ctx = canvas.getContext('2d', { alpha: false });
  canvas.width = 768; canvas.height = 576;
  canvas.setAttribute('aria-hidden', 'true');
  Object.assign(canvas.style, { position: 'fixed', zIndex: '2', pointerEvents: 'none', borderRadius: '3%', display: 'none' });
  parent.appendChild(canvas);
  if (fallback) fallback.src = new URL('../art/fx/spiral.png', import.meta.url).href;
  let clock = 0, lastSeed = -1, lastSource = null, disposed = false, flashParams = null;
  const clear = () => { ctx.fillStyle = '#25152e'; ctx.fillRect(0, 0, canvas.width, canvas.height); };
  function update(screen, dt, aspect = 16 / 9) {
    if (disposed) return;
    const amount = screen?.kind === 'flash' ? screen.k : screen?.prevKind === 'flash' ? screen.prevK : 0;
    if (screen?.kind === 'flash') flashParams = { ...screen.params };
    if (!(amount > .001)) {
      if (canvas.style.display !== 'none') clear();
      canvas.style.display = 'none'; lastSource = null; return;
    }
    const p = flashParams || screen.params || {}, seed = p.seed || 0;
    const media = source(seed), picture = media || fallbackSource(seed) || (fallback?.complete && fallback.naturalWidth ? fallback : null);
    const r = flashLayout(aspect, p);
    Object.assign(canvas.style, { display: 'block', left: r.x * 100 + 'vw', top: r.y * 100 + 'vh',
      width: r.w * 100 + 'vw', height: r.h * 100 + 'vh', opacity: String(Math.min(1, amount)) });
    clock += Math.max(0, dt || 0);
    // Source-off and expiry clear immediately, including while paused.
    if (picture === lastSource && seed === lastSeed && (clock < 1 / 15 || !(dt > 0))) return;
    clock = 0; lastSeed = seed; lastSource = picture; clear();
    if (picture) {
      const w = picture.videoWidth || picture.naturalWidth || picture.width, h = picture.videoHeight || picture.naturalHeight || picture.height;
      if (w > 0 && h > 0) {
        const k = Math.max(canvas.width / w, canvas.height / h);
        try { ctx.drawImage(picture, (canvas.width - w * k) / 2, (canvas.height - h * k) / 2, w * k, h * k); }
        catch { clear(); }
      }
    }
    if (media) {
      ctx.fillStyle = 'rgba(12,6,20,.78)'; ctx.fillRect(0, 540, 768, 36);
      ctx.fillStyle = '#fff'; ctx.font = '20px system-ui'; ctx.fillText('via Scrolller', 16, 565);
    }
  }
  return { canvas, update, dispose() { disposed = true; clear(); lastSource = null; canvas.remove(); } };
}
