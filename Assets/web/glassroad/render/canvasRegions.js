/* WebGL2: a canvas atlas that redraws a few cells re-uploads only those cells.
 * r186 uploads a CanvasTexture whole on every needsUpdate (one texSubImage2D of the full canvas). The living item
 * icons (render/items/living.js) redraw the cells worn on screen at the icon rate inside a 1280 x 1280 atlas, so
 * Firefox snapshotted and sent 6.5 MB for each tick (GOONWORKS high, 2026-10-27: ~1 ms a frame in texSubImage2D).
 * An owner that knows which cells changed hands them over before it bumps the texture:
 *   markRegion(texture, x, y, source)  // source = a canvas holding exactly that cell's new pixels, x/y = its
 *                                      // top-left in the atlas canvas (canvas pixels, y down)
 * and the next upload sends each region with texSubImage2D(x, y', source) into the texture three already holds.
 * Three's own mipmap pass still follows (Textures._update calls generateMipmaps after updateTexture), so sampling
 * is unchanged. The very first upload, a resized canvas, a texture three rebuilt, WebGPU, or a texture without
 * regions take three's path untouched; the atlas canvas itself must still carry every cell (it is what a full
 * upload sends). Keep aligned with the vendored renderer (WebGLTextureUtils.updateTexture/setTextureParameters).
 * Never edit the vendored build for this. */
const patched = new WeakSet();
const KEY = '__regions';

/** Queue one changed cell for the texture's next upload (needsUpdate is the caller's, as before). */
export function markRegion(texture, x, y, source) {
  if (!texture || !source) return;
  const ud = texture.userData || (texture.userData = {});
  const list = ud[KEY] || (ud[KEY] = []);
  for (const r of list) if (r.x === x && r.y === y && r.source === source) return;
  list.push({ x, y, source });
}

/** The regions an upload would send, or null when the whole canvas must go (exported for tests). */
export function regionPlan(texture, image, data) {
  const list = texture && texture.userData && texture.userData[KEY];
  if (!list || !list.length || !image || !data) return null;
  if (data.textureGPU === undefined || data.regionGPU !== data.textureGPU) return null;
  if (data.regionW !== image.width || data.regionH !== image.height) return null;
  for (const r of list) {
    const w = r.source.width, h = r.source.height;
    if (!(w > 0 && h > 0) || r.x < 0 || r.y < 0 || r.x + w > image.width || r.y + h > image.height) return null;
  }
  return list;
}

export function uploadCanvasRegions(three) {
  const backend = three && three.backend, utils = backend && backend.textureUtils;
  if (!backend || backend.isWebGLBackend !== true || !utils || patched.has(three)) return false;
  if (typeof utils.updateTexture !== 'function' || typeof utils.setTextureParameters !== 'function') return false;
  patched.add(three);
  const updateTexture = utils.updateTexture;
  utils.updateTexture = function(texture, options) {
    const ud = texture && texture.userData, list = ud && ud[KEY];
    if (!list) return updateTexture.call(this, texture, options);
    const image = texture.image, data = this.backend.get(texture), plan = regionPlan(texture, image, data);
    if (!plan) {
      list.length = 0;                             // the whole canvas goes, every queued cell with it
      const out = updateTexture.call(this, texture, options);
      if (data.textureGPU !== undefined && image) { data.regionGPU = data.textureGPU; data.regionW = image.width; data.regionH = image.height; }
      return out;
    }
    const gl = this.gl, { textureGPU, glTextureType, glFormat, glType } = data;
    this.backend.state.bindTexture(glTextureType, textureGPU);
    this.setTextureParameters(glTextureType, texture);   // the same unpack flips and premultiply as a full upload
    for (const r of plan) {
      const h = r.source.height, y = texture.flipY ? image.height - r.y - h : r.y;
      gl.texSubImage2D(gl.TEXTURE_2D, 0, r.x, y, glFormat, glType, r.source);
    }
    list.length = 0;
  };
  return true;
}
