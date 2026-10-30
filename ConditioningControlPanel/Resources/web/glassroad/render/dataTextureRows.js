/* WebGL2: a DataTexture whose writer marks the rows it changed re-sends only those rows.
 *
 * r186 uploads a DataTexture whole on every needsUpdate (one texSubImage2D of the full array). The water height
 * atlas (render/waterField.js, 64 x ~1000 RGBA8 = 255 KB) repacks every frame while any run is awake, and Firefox
 * serializes typed-array uploads into its 100 KB WebGL command blocks: that one texture flushed three blocks a
 * frame on Threshold (2026-10-27, handoff-1027/GLASSROAD-FF60.md). The field already repacks per run, so it knows
 * which rows it touched:
 *   markRows(texture, from, to)   // inclusive row span (texture rows, y down in the array), unions with earlier marks
 *   clearRows(texture)            // forget the marks: the next upload goes whole
 * and the next upload sends that span with one texSubImage2D from the same array (rows are contiguous, so no
 * row-length unpack state is needed). The first upload, a resized image, a texture three rebuilt, WebGPU, a texture
 * without marks or a non-2D target take three's path untouched. Mirrors render/canvasRegions.js; keep aligned with
 * the vendored WebGLTextureUtils.updateTexture / setTextureParameters when upgrading three. Never edit the vendored
 * build for this. */
const patched = new WeakSet();
const KEY = '__rows';

export function markRows(texture, from, to) {
  if (!texture || !(to >= from)) return;
  const ud = texture.userData || (texture.userData = {});
  const r = ud[KEY];
  if (!r) ud[KEY] = { from, to };
  else { if (from < r.from) r.from = from; if (to > r.to) r.to = to; }
}

export function clearRows(texture) {
  if (texture && texture.userData) delete texture.userData[KEY];
}

/** The row span an upload would send, or null when the whole array must go (exported for tests). */
export function rowPlan(texture, image, data) {
  const r = texture && texture.userData && texture.userData[KEY];
  if (!r || !image || !data || !ArrayBuffer.isView(image.data)) return null;
  if (data.textureGPU === undefined || data.rowsGPU !== data.textureGPU) return null;
  if (data.rowsW !== image.width || data.rowsH !== image.height) return null;
  const w = image.width, h = image.height, bytes = image.data.byteLength;
  if (!(w > 0 && h > 0) || bytes % (w * h) !== 0) return null;
  const from = Math.max(0, Math.floor(r.from)), to = Math.min(h - 1, Math.floor(r.to));
  if (to < from) return null;
  const texelBytes = bytes / (w * h);
  if ((from * w * texelBytes) % image.data.BYTES_PER_ELEMENT !== 0) return null;
  return { from, to, texelBytes };
}

export function uploadDataTextureRows(three) {
  const backend = three && three.backend, utils = backend && backend.textureUtils;
  if (!backend || backend.isWebGLBackend !== true || !utils || patched.has(three)) return false;
  if (typeof utils.updateTexture !== 'function' || typeof utils.setTextureParameters !== 'function') return false;
  patched.add(three);
  const updateTexture = utils.updateTexture;
  utils.updateTexture = function(texture, options) {
    const ud = texture && texture.userData;
    if (!ud || !ud[KEY]) return updateTexture.call(this, texture, options);
    const image = texture.image, data = this.backend.get(texture), gl = this.gl;
    const plan = rowPlan(texture, image, data);
    delete ud[KEY];                                  // consumed: a full upload carries the marked rows too
    if (!plan || !gl || data.glTextureType !== gl.TEXTURE_2D) {
      const out = updateTexture.call(this, texture, options);
      if (data.textureGPU !== undefined && image) { data.rowsGPU = data.textureGPU; data.rowsW = image.width; data.rowsH = image.height; }
      return out;
    }
    const { textureGPU, glTextureType, glFormat, glType } = data, array = image.data;
    const rows = plan.to - plan.from + 1, y = texture.flipY ? image.height - plan.from - rows : plan.from;
    this.backend.state.bindTexture(glTextureType, textureGPU);
    this.setTextureParameters(glTextureType, texture);   // the same unpack flips and alignment as a full upload
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, y, image.width, rows, glFormat, glType, array, (plan.from * image.width * plan.texelBytes) / array.BYTES_PER_ELEMENT);
  };
  return true;
}
