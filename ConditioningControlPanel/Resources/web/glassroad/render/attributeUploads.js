/* WebGL2: a vertex / instance attribute is re-sent only for the bytes that changed since its last upload.
 *
 * Why (Firefox 60 fps, 2026-10-27, handoff-1027/GLASSROAD-FF60.md): Firefox serializes every WebGL call, upload
 * bytes included, into 100 KB command blocks and forces a SYNCHRONOUS present once a frame flushed more than ten of
 * them (dom/canvas/ClientWebGLContext.cpp, tooManyFlushes = 10); the page then waits for the GPU process to replay
 * the whole frame and 60 becomes 40. GOONWORKS high sent ~1.4 MB a frame, 1.1 MB of it whole-array attribute uploads:
 * three r186 re-sends a DynamicDrawUsage attribute on EVERY draw of every pass (Attributes.update:
 * `version < attr.version || usage === DynamicDrawUsage`), and a writer's addUpdateRange only covers the first of
 * those (the backend clears the ranges), so the shadow pass and the hit cam re-sent the skids (172 KB), the
 * particle sprites, the road words and the sparkles whole, two or three times a frame.
 *
 * This wrapper around the WebGL backend's attributeUtils.updateAttribute keeps a copy of the words it last sent
 * per buffer and, whenever three wants an upload, compares and sends ONE bufferSubData per changed span (within the
 * writer's update ranges when it gave some, else over the whole array). Identical bytes send nothing. The GPU holds
 * exactly the bytes it held before (bitwise compare, Uint32 view), so nothing on screen changes; Chrome/Edge gain
 * the same cut. A new array or GL buffer (a resized attribute, a rebuilt geometry) takes three's own upload once.
 * Keep aligned with the vendored WebGLAttributeUtils.updateAttribute (bufferType / bufferGPU / version on
 * backend.get(data), clearUpdateRanges after an upload) when upgrading three. */

const patched = new WeakSet();

/** A Uint32 view over the same bytes, or null when the array cannot be viewed whole (odd length / offset). */
function wordsOf(array) {
  if (!array || !ArrayBuffer.isView(array) || array.byteLength % 4 !== 0 || array.byteOffset % 4 !== 0) return null;
  return new Uint32Array(array.buffer, array.byteOffset, array.byteLength >> 2);
}

/** The changed word span inside [from, to] (inclusive), or null when the words equal the copy there. */
function changedSpan(words, copy, from, to) {
  let lo = from;
  while (lo <= to && words[lo] === copy[lo]) lo++;
  if (lo > to) return null;
  let hi = to;
  while (hi > lo && words[hi] === copy[hi]) hi--;
  return [lo, hi];
}

/**
 * Install on a WebGPURenderer running the WebGL2 backend. Returns false on WebGPU or when already installed.
 * @param {object} three  the renderer (three.backend.attributeUtils.updateAttribute is wrapped)
 */
export function uploadChangedAttributeBytes(three) {
  const backend = three && three.backend;
  if (!backend || backend.isWebGLBackend !== true || patched.has(three)) return false;
  const utils = backend.attributeUtils;
  if (!utils || typeof utils.updateAttribute !== 'function' || !backend.gl) return false;
  patched.add(three);
  const gl = backend.gl;
  // data owner (BufferAttribute or InterleavedBuffer) -> { array, gpu, copy }
  const sent = new WeakMap();
  const stock = utils.updateAttribute;
  utils.updateAttribute = function(attribute) {
    const data = attribute.isInterleavedBufferAttribute ? attribute.data : attribute;
    const array = data.array, words = wordsOf(array);
    if (words === null) return stock.call(this, attribute);
    const entry = backend.get(data);
    let last = sent.get(data), gpu = entry.bufferGPU;
    if (!last || !gpu || last.array !== array || last.gpu !== gpu || last.copy.length !== words.length) {
      // first sight, a new array or a new GL buffer: three's own upload (full or ranged), then remember the bytes
      stock.call(this, attribute);
      gpu = entry.bufferGPU;
      if (!gpu) return;
      if (!last) sent.set(data, { array, gpu, copy: new Uint32Array(words) });
      else { last.array = array; last.gpu = gpu; last.copy = new Uint32Array(words); }
      return;
    }
    const copy = last.copy, bpe = array.BYTES_PER_ELEMENT, ranges = data.updateRanges, n = words.length;
    let bound = false;
    const send = (from, to) => {
      const span = changedSpan(words, copy, from, to);
      if (span === null) return;
      const [lo, hi] = span;
      if (!bound) { gl.bindBuffer(entry.bufferType, gpu); bound = true; }
      // element units for bufferSubData's srcOffset / length; whole words always cover whole elements for bpe <= 4
      const perWord = 4 / bpe;
      gl.bufferSubData(entry.bufferType, lo * 4, array, lo * perWord, (hi - lo + 1) * perWord);
      copy.set(words.subarray(lo, hi + 1), lo);
    };
    if (ranges && ranges.length) {
      for (let i = 0; i < ranges.length; i++) {
        const r = ranges[i];
        const from = Math.floor(r.start * bpe / 4), to = Math.min(n - 1, Math.ceil((r.start + r.count) * bpe / 4) - 1);
        if (to >= from) send(from, to);
      }
      data.clearUpdateRanges();
    } else if (n > 0) send(0, n - 1);
    if (bound) gl.bindBuffer(entry.bufferType, null);
    entry.version = data.version;
  };
  return true;
}
