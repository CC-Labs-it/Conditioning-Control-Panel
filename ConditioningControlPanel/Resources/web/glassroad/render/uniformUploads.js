/* WebGL2: an unchanged small InstancedMesh stops re-sending its matrices on every draw.
 * r186 keeps the matrices of an InstancedMesh that fits the uniform-buffer limit (count * 64 bytes, 64 KB on
 * WebGL2) in a NodeUniformBuffer, and Buffer.update() always answers true, so every draw of every pass re-sent
 * the whole array with gl.bufferData even when nothing moved (Edge GOONWORKS profile, 2026-10-26: ~0.2 ms a frame
 * on bufferData alone, and Firefox's WebGL calls cost about twice Edge's). The instanceMatrix attribute already
 * counts its edits (needsUpdate bumps version), so a binding re-sends only when that version, or the GL buffer
 * behind the binding, differs from what it last sent. The values on the GPU are the same either way.
 * Everything else (uniform groups, bone buffers, uniform arrays) takes three's own path untouched.
 * Keep aligned with the vendored renderer (Bindings.updateForRender -> backend.updateBinding) when upgrading. */
const patched = new WeakSet();

export function skipUnchangedInstanceUploads(three) {
  const backend = three && three.backend, bindings = three && three._bindings;
  if (!backend || backend.isWebGLBackend !== true || !bindings || patched.has(three)) return false;
  if (typeof backend.updateBinding !== 'function' || typeof bindings.updateForRender !== 'function') return false;
  patched.add(three);
  let current = null;
  const sent = new WeakMap();                  // binding -> { gpu, version } it last uploaded
  const updateForRender = bindings.updateForRender, updateBinding = backend.updateBinding;
  bindings.updateForRender = function(renderObject) {
    current = renderObject;
    try { return updateForRender.call(this, renderObject); } finally { current = null; }
  };
  backend.updateBinding = function(binding) {
    const object = current && current.object, matrices = object && object.isInstancedMesh ? object.instanceMatrix : null;
    if (!matrices || binding.isNodeUniformBuffer !== true || binding.buffer !== matrices.array) return updateBinding.call(this, binding);
    const ranges = binding.updateRanges;
    if (ranges && ranges.length) return updateBinding.call(this, binding);
    const gpu = this.get(binding).bufferGPU, last = sent.get(binding);
    if (last && last.gpu === gpu && last.version === matrices.version) return;
    updateBinding.call(this, binding);
    if (last) { last.gpu = gpu; last.version = matrices.version; } else sent.set(binding, { gpu, version: matrices.version });
  };
  return true;
}
