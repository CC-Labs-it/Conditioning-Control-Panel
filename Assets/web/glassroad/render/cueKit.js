import { Matrix4, Vector3, CanvasTexture, SRGBColorSpace, MeshBasicNodeMaterial } from 'three/webgpu';

// Shared bits for the song cue pieces: road-frame placement, eased envelopes, pooled materials and labels.
export const unit = x => Math.max(0, Math.min(1, Number.isFinite(x) ? x : 0));
export const ease = x => { const u = unit(x); return u * u * (3 - 2 * u); };
export const easeOut = x => 1 - (1 - unit(x)) ** 3;
export const easeIn = x => unit(x) ** 2;
/** Overshooting pop (0..1..~1.1..1) for things that spring open. */
export const pop = x => { const u = unit(x); return u >= 1 ? 1 : 1 + 2.7 * (u - 1) ** 3 + 1.7 * (u - 1) ** 2; };
export function rng(seed) { let s = (seed >>> 0) || 1; return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296); }
export const hashId = text => { let n = 2166136261; for (const c of String(text)) n = Math.imul(n ^ c.charCodeAt(0), 16777619); return n >>> 0; };

const m4 = new Matrix4(), bx = new Vector3(), ny = new Vector3(), tz = new Vector3();
/** Seat an object on a road frame: x across (b), y up (n), -z ahead, so a face at +z looks back at the kart. */
export function placeOnFrame(object, f) {
  object.position.fromArray(f.p);
  m4.makeBasis(bx.fromArray(f.b), ny.fromArray(f.n), tz.fromArray(f.t).negate());
  object.quaternion.setFromRotationMatrix(m4);
  return object;
}
/** Beat phase on the song clock (beats since the chart's downbeat), or null with no tempo. */
export function beatPhase(song, songS) {
  if (!(song?.bpm > 0) || !Number.isFinite(songS)) return null;
  return (songS - (song.downbeatS || 0)) * song.bpm / 60;
}
/** A kick that peaks on each beat and decays before the next (0..1). */
export function beatKick(song, songS, sharp = 6) {
  const ph = beatPhase(song, songS);
  return ph === null ? 0 : Math.exp(-(ph - Math.floor(ph)) * sharp);
}

/** Transparent unlit materials that fade together. */
export function fadeSet() {
  const mats = [];
  return {
    mats,
    make(options) { const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, fog: false, ...options }); mats.push(m); return m; },
    opacity(a) { for (const m of mats) m.opacity = a; },
    dispose() { for (const m of mats) m.dispose(); mats.length = 0; },
  };
}

/** A canvas label texture; paint(fn) redraws only when its key changes. */
export function labelTexture(make, w = 256, h = 256) {
  const canvas = make(); canvas.width = w; canvas.height = h;
  const ctx = canvas.getContext('2d'), texture = new CanvasTexture(canvas);
  texture.colorSpace = SRGBColorSpace;
  let key = null;
  return { texture, ctx, w, h,
    paint(nextKey, draw) { if (nextKey === key) return false; key = nextKey; ctx.clearRect?.(0, 0, w, h); draw(ctx, w, h); texture.needsUpdate = true; return true; },
    dispose() { texture.dispose(); canvas.width = canvas.height = 1; } };
}
