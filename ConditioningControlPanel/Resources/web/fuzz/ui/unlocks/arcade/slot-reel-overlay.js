// Scrolller media the reel texture cannot take (no CORS on the website, see slot-reel-media.js)
// is shown as DOM elements over the reel windows, the Glassroad "pictures behind windows"
// pattern turned inside out. Each window sits on the projected cell of a reel that landed on
// the media symbol, is clipped to it, never takes a pointer (the casing drag and the lever keep
// working) and is hidden while the reels spin. Gentle and reduced motion get a still.
// Only display: nothing here reads pixels back, so a tainted canvas is fine.

const CSS = `
.mini-slot-reel-pics{position:absolute;inset:0;pointer-events:none;overflow:hidden}
.mini-slot-reel-pic{position:absolute;overflow:hidden;border-radius:3px;opacity:0;transition:opacity .18s ease-out;background:#140f1c}
.mini-slot-reel-pic[data-on="true"]{opacity:1}
.mini-slot-reel-pics[data-calm="true"] .mini-slot-reel-pic{transition-duration:.36s}
.mini-slot-reel-pic>img,.mini-slot-reel-pic>canvas{display:block;width:100%;height:100%;object-fit:cover}
.mini-slot-reel-pics>video{position:absolute;left:0;top:0;width:2px;height:2px;opacity:0}
`;
const PLACE_MS = 500, CLIP_FRAME_MS = 66;

function cover(g, src, sw, sh, w, h) {
  const s = Math.max(w / sw, h / sh), dw = sw * s, dh = sh * s;
  g.clearRect(0, 0, w, h);
  g.drawImage(src, (w - dw) / 2, (h - dh) / 2, dw, dh);
}

export function createReelOverlay(ctx, root, before) {
  ctx.css(CSS);
  const doc = root.ownerDocument;
  const layer = doc.createElement('div');
  layer.className = 'mini-slot-reel-pics';
  layer.setAttribute('aria-hidden', 'true');
  root.insertBefore(layer, before);
  const wins = [0, 1, 2].map(i => {
    const w = doc.createElement('div');
    w.className = 'mini-slot-reel-pic';
    w.dataset.reel = String(i);
    layer.appendChild(w);
    return {el:w, face:null, on:false, w:0, h:0, drawnAt:-Infinity};
  });
  let art = null, calm = null, placedAt = -Infinity, playing = false;

  function buildFaces() {
    for (const win of wins) { win.face?.remove(); win.face = null; win.drawnAt = -Infinity; }
    if (!art) return;
    for (const win of wins) {
      const live = art.kind === 'picture' || (art.kind === 'gif' && !calm);
      win.face = live ? art.element.cloneNode(false) : doc.createElement('canvas');
      win.el.appendChild(win.face);
    }
  }
  function drawFace(win, now) {
    if (!art || !(win.face instanceof HTMLCanvasElement) || !win.w || !win.h) return;
    const still = art.kind !== 'clip' || calm;
    if (still && win.drawnAt > -Infinity) return;
    if (!still && now - win.drawnAt < CLIP_FRAME_MS) return;
    const dpr = Math.min(2, doc.defaultView?.devicePixelRatio || 1);
    const cw = Math.max(1, Math.round(win.w * dpr)), ch = Math.max(1, Math.round(win.h * dpr));
    if (win.face.width !== cw || win.face.height !== ch) { win.face.width = cw; win.face.height = ch; }
    const g = win.face.getContext('2d');
    if (!g) return;
    try { cover(g, art.element, art.width, art.height, cw, ch); win.drawnAt = now; }
    catch { /* a frame that is not ready yet draws next time */ }
  }
  function place(scene) {
    const rr = root.getBoundingClientRect();
    if (!rr.width || !rr.height) return;
    const k = root.offsetWidth / rr.width || 1;
    // The landed cell fills the glass top to bottom (four symbols round a drum), so the window
    // takes the glass's height; portrait() alone gives the flat cell, which reads too short.
    const top = scene?.project?.('reel_window', true), mid = scene?.project?.('reel_window');
    const glass = top && mid && mid.y > top.y ? {y:top.y, h:2 * (mid.y - top.y)} : null;
    for (const [i, win] of wins.entries()) {
      if (!win.on) continue;
      const p = scene?.portrait(i);
      if (!p || !(p.w > 0) || !(p.h > 0)) { win.on = false; win.el.dataset.on = 'false'; continue; }
      const y = glass ? glass.y : p.y - p.h / 2, ph = glass ? glass.h : p.h;
      const w = p.w * k, h = ph * k;
      if (Math.abs(w - win.w) > .5 || Math.abs(h - win.h) > .5) win.drawnAt = -Infinity;
      win.w = w; win.h = h;
      Object.assign(win.el.style, {left:`${(p.x - p.w / 2 - rr.left) * k}px`, top:`${(y - rr.top) * k}px`, width:`${w}px`, height:`${h}px`});
    }
  }
  function setPlaying(next) {
    if (art?.kind !== 'clip' || next === playing) return;
    playing = next;
    if (next) art.element.play()?.catch?.(() => {}); else art.element.pause();
  }

  return {
    /** A {dom:true} reel art from loadDomMedia, or null. The caller owns and disposes it. */
    setArt(next) {
      if (art?.kind === 'clip') { setPlaying(false); art.element.remove(); }
      art = next?.dom ? next : null;
      playing = false;
      if (art?.kind === 'clip') layer.appendChild(art.element);
      calm = null;
      for (const win of wins) { win.on = false; win.el.dataset.on = 'false'; }
      buildFaces();
      root.dataset.reelOverlay = art ? art.kind : '';
    },
    get active() { return !!art; },
    /** Each frame. `reels` are the landed symbol indices, `mediaSymbol` the index that shows media. */
    update({scene, reels, mediaSymbol, settled, show, still}) {
      if (!art) return;
      const now = performance.now();
      if (calm !== !!still) { calm = !!still; layer.dataset.calm = String(calm); buildFaces(); }
      let changed = false;
      for (const [i, win] of wins.entries()) {
        const on = !!(show && settled && scene && reels?.[i] === mediaSymbol);
        if (on !== win.on) { win.on = on; changed = true; if (!on) win.el.dataset.on = 'false'; }
      }
      if (changed || now - placedAt > PLACE_MS) { placedAt = now; place(scene); }
      for (const win of wins) {
        if (!win.on) continue;
        drawFace(win, now);
        win.el.dataset.on = 'true';
      }
      setPlaying(!calm && wins.some(win => win.on));
    },
    /** The current element for a 2D still (display only), or null. */
    get element() { return art?.element || null; },
    dispose() { this.setArt(null); layer.remove(); },
  };
}
