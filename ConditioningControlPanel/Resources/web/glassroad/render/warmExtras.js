/* Materials that modules build only once a race runs, rebuilt here once so the warm links their programs behind
 * the cover (render/renderer.js warmAll and warmInset take them on every course).
 * - The rival name tag (render/karts.js tagSprite): made on the first pose that knows the name, so its program
 *   linked in the first race frame (Firefox WebGL2: 35-97 ms, GOONWORKS and Threshold, 2026-10-27). Same
 *   material as tagSprite: an sRGB canvas map, transparent, no depth write. Keep the two in step.
 * Built lazily (a document is needed) and kept for the page: a few KB, shared by every course. The argument is the
 * three/webgpu module namespace (renderer.js hands it over; tests stub that module without these classes). */
let late = null;

export function lateWarmMaterials(three = {}) {
  if (late) return late;
  const { CanvasTexture, SRGBColorSpace, SpriteNodeMaterial } = three;
  if (typeof document === 'undefined' || !CanvasTexture || !SpriteNodeMaterial) return [];
  const cv = document.createElement('canvas'); cv.width = 256; cv.height = 64;
  const t = new CanvasTexture(cv); t.colorSpace = SRGBColorSpace;
  const tag = new SpriteNodeMaterial({ map: t, transparent: true, depthWrite: false });
  tag.name = 'warm-name-tag';
  late = [tag];
  return late;
}
