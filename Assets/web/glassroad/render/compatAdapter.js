/* render/compatAdapter.js - WebGPU on OpenGL ES (Chrome 146+, featureLevel 'compatibility'). Pure.
 * Android Chrome hands some GPUs only this adapter: Imagination below Android 16 has Vulkan refused and OpenGL ES
 * allowed (chromium gpu/config/webgpu_blocklist_impl.cc). Its device starts on the compatibility defaults, where
 * maxTextureDimension2D is 4096. The 8192 texel course tables then fail validation, the bind groups built on them
 * turn invalid, and every submit is refused: a black world, no throw, a game that runs on behind it.
 */

/** No core-features-and-limits on the adapter = a compatibility-only adapter (OpenGL ES underneath). */
export const isCompatOnly = (adapter) => !!adapter?.features && !adapter.features.has('core-features-and-limits');

const RAISED = ['maxTextureDimension1D', 'maxTextureDimension2D', 'maxTextureDimension3D', 'maxTextureArrayLayers',
  'maxColorAttachments', 'maxColorAttachmentBytesPerSample', 'maxUniformBufferBindingSize', 'maxInterStageShaderVariables',
  'maxVertexAttributes', 'maxVertexBuffers', 'maxSampledTexturesPerShaderStage', 'maxSamplersPerShaderStage', 'maxUniformBuffersPerShaderStage'];

/** What the device must ask for by name: everything the adapter has for the limits the game's core defaults assume. */
export function compatLimits(adapter) {
  const out = {};
  for (const name of RAISED) if (typeof adapter?.limits?.[name] === 'number') out[name] = adapter.limits[name];
  return out;
}

/** Uncaptured validation errors before a compat device counts as drawing nothing (about one second of refused frames). */
export const COMPAT_DEAD_ERRORS = 60;
