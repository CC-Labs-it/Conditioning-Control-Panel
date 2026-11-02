/* WebGL2: uniform buffers are re-sent only when the bytes the GPU holds would change.
 * r186 keeps the matrices of an InstancedMesh that fits the uniform-buffer limit (count * 64 bytes, 64 KB on
 * WebGL2), skinned meshes' bone matrices and uniform arrays in a NodeUniformBuffer, and Buffer.update() always
 * answers true, so every draw of every pass re-sent the whole array with gl.bufferData even when nothing moved
 * (Edge GOONWORKS profile, 2026-10-26: ~0.2 ms a frame on bufferData alone, and Firefox's WebGL calls cost about
 * twice Edge's). A binding now re-sends only when its GL buffer or its contents differ from what it last sent:
 *  - an InstancedMesh's matrices: the instanceMatrix version (needsUpdate) is the cheap first check;
 *  - then, for every full-buffer upload (instance matrices whose version moved, bones shared by several draws of
 *    one skeleton, uniform arrays), the bytes themselves: a writer that bumps needsUpdate every frame with an
 *    unchanged pose, or several draws of one kart's skeleton, no longer pay a bufferData each (Edge census,
 *    GOONWORKS 2026-10-27: ~70 identical bufferData calls and ~270 KB a frame).
 * The comparison is bitwise (a Uint32 view), so the GPU holds exactly the same bytes either way.
 * Uniform groups with update ranges (the per-object and shared groups) take three's own upload path; their value
 * check runs through render/uniformGroups.js, and NodeFrame.updateNode through render/nodeUpdates.js (both
 * installed from here, WebGL2 only).
 * Keep aligned with the vendored renderer (Bindings.updateForRender -> backend.updateBinding) when upgrading. */
import { fastUniformGroupUpdates, cacheGroupVersions } from './uniformGroups.js';
import { installNodeUpdates } from './nodeUpdates.js';

const patched = new WeakSet();

/** A Uint32 view over the same bytes (bitwise compare: NaN and -0 count as the bytes they are), or null. */
function wordsOf(array) {
  if (!array || !ArrayBuffer.isView(array) || array.byteLength % 4 !== 0 || array.byteOffset % 4 !== 0) return null;
  return new Uint32Array(array.buffer, array.byteOffset, array.byteLength >> 2);
}

export function skipUnchangedInstanceUploads(three) {
  const backend = three && three.backend, bindings = three && three._bindings;
  if (!backend || backend.isWebGLBackend !== true || !bindings || patched.has(three)) return false;
  if (typeof backend.updateBinding !== 'function' || typeof bindings.updateForRender !== 'function') return false;
  patched.add(three);
  fastUniformGroupUpdates(three);            // the uniform groups' value check without per-uniform dispatch
  cacheGroupVersions(three);                 // shared groups' version check without a ChainMap lookup per draw
  installNodeUpdates(three);                    // shared node refresh without a WeakMap lookup per draw (nodeUpdates.js)
  let current = null;
  // binding -> { gpu, version, array, words, copy }: what it last uploaded (copy holds the sent words)
  const sent = new WeakMap();
  const updateForRender = bindings.updateForRender, updateBinding = backend.updateBinding;
  bindings.updateForRender = function(renderObject) {
    current = renderObject;
    try { return updateForRender.call(this, renderObject); } finally { current = null; }
  };
  backend.updateBinding = function(binding) {
    if (binding.isNodeUniformBuffer !== true) return updateBinding.call(this, binding);
    const ranges = binding.updateRanges;
    if (ranges && ranges.length) return updateBinding.call(this, binding);
    const array = binding.buffer, gpu = this.get(binding).bufferGPU;
    const object = current && current.object;
    const matrices = object && object.isInstancedMesh && object.instanceMatrix.array === array ? object.instanceMatrix : null;
    let last = sent.get(binding);
    if (last && last.gpu === gpu && last.array === array) {
      if (matrices && last.version === matrices.version) return;
      const words = last.words;
      if (words !== null) {
        const copy = last.copy, n = words.length;
        if (copy.length === n) {
          let lo = 0;
          while (lo < n && words[lo] === copy[lo]) lo++;
          if (lo === n) { if (matrices) last.version = matrices.version; return; }
          const gl = this.gl;
          if (gl && typeof gl.bufferSubData === 'function' && gpu) {
            // Only the changed span goes, into the buffer the whole upload sized (Firefox's 100 KB command blocks,
            // 2026-10-27: a 15 KB screen-frame instance buffer whose two halo slots moved now sends 128 bytes).
            let hi = n - 1;
            while (hi > lo && words[hi] === copy[hi]) hi--;
            const perWord = 4 / array.BYTES_PER_ELEMENT;
            gl.bindBuffer(gl.UNIFORM_BUFFER, gpu);
            gl.bufferSubData(gl.UNIFORM_BUFFER, lo * 4, array, lo * perWord, (hi - lo + 1) * perWord);
            gl.bindBuffer(gl.UNIFORM_BUFFER, null);
            copy.set(words.subarray(lo, hi + 1), lo);
            if (matrices) last.version = matrices.version;
            return;
          }
        }
      }
    }
    updateBinding.call(this, binding);
    if (!last || last.array !== array) {
      const words = wordsOf(array);
      last = { gpu, version: -1, array, words, copy: words ? new Uint32Array(words.length) : null };
      sent.set(binding, last);
    }
    last.gpu = gpu;
    last.version = matrices ? matrices.version : -1;
    if (last.words !== null) last.copy.set(last.words);
  };
  return true;
}
