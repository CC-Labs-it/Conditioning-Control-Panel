/* render/firefoxWebgpu.js - Firefox WebGPU workarounds (runtime patches, never the vendored build).
 * Measured on Firefox 158.0.1 (RTX 5080, 2026-10-27); both are Firefox gaps, not glassroad bugs:
 *
 * 1. SHADERS. Firefox compiles WGSL with naga. r186's WGSLNodeBuilder writes float constants as bare literals
 *    ("0.55"), which WGSL types as AbstractFloat. When every argument of a builtin is such a literal, e.g. the soft
 *    prop material's `mix(float(0.55), float(1), ao)` with ao = float(1) (render/materials/colossus.js), the call
 *    is a constant expression with an abstract result. Tint (Chrome, Edge) folds it; naga has no constant
 *    evaluator for mix, keeps the abstract value in a runtime expression and rejects the module ("Abstract types
 *    may only appear in constant expressions"). Every pipeline built from such a graph failed: the colossi,
 *    watches, cards, bubbles and the ShadowMaterial catchers never drew (the missing / black lit props of polish 4).
 *    Fix: in Firefox a float constant is written `f32( 0.55 )`, a concrete constant of the same value.
 *
 * 2. VIDEO. Firefox's copyExternalImageToTexture takes only ImageBitmap, HTMLImageElement, HTMLCanvasElement and
 *    OffscreenCanvas: an HTMLVideoElement (or VideoFrame) throws a TypeError, which r186's _copyImageToTexture
 *    swallows, so every clip face stayed black. Readback routes cost ~9 ms a clip (createImageBitmap + copy, or
 *    drawImage + copy). Firefox does import a video as a GPUExternalTexture, so the frame is drawn GPU to GPU into
 *    the texture three already owns: one full-target triangle sampling the external texture, flipped like
 *    copyExternalImageToTexture's flipY, and decoded to linear first when the target is an -srgb format (the
 *    render target re-encodes it, so the stored bytes equal the video's).
 *
 * Other browsers keep the stock generator and upload path (their shader text and pipeline caches do not change).
 * Keep aligned with NodeBuilder.generateConst and WebGPUTextureUtils._copyImageToTexture when upgrading three;
 * tests/firefox-webgpu.test.mjs pins both. */
const patched = new WeakSet();

export function isFirefox(nav = globalThis.navigator) {
  return !!nav && String(nav.userAgent || '').includes('Firefox/');
}

/** Wrap a WGSL float literal as a concrete f32 constant; anything else passes through. */
export function concreteFloat(code) {
  return typeof code === 'string' && /^[-+]?(\d+\.?\d*|\.\d+)(e[-+]?\d+)?$/i.test(code.trim()) ? `f32( ${code.trim()} )` : code;
}

/** Patch WGSLNodeBuilder so float constants are concrete f32. Returns true when it patched. */
export function concreteFloatConstants(WGSLNodeBuilder) {
  const proto = WGSLNodeBuilder && WGSLNodeBuilder.prototype;
  if (!proto || typeof proto.generateConst !== 'function' || patched.has(proto)) return false;
  patched.add(proto);
  const generateConst = proto.generateConst;
  proto.generateConst = function(type, value = null) {
    const code = generateConst.call(this, type, value);
    return type === 'float' ? concreteFloat(code) : code;
  };
  return true;
}

/** The blit shader: a full-target triangle sampling an external texture. */
export function videoBlitWgsl({ flipY = false, decode = false } = {}) {
  const v = flipY ? '0.5 + p.y * 0.5' : '0.5 - p.y * 0.5';
  const lin = decode
    ? '  let lo = c.rgb / 12.92;\n  let hi = pow((c.rgb + vec3f(0.055)) / 1.055, vec3f(2.4));\n  return vec4f(select(hi, lo, c.rgb <= vec3f(0.04045)), c.a);\n'
    : '  return c;\n';
  return [
    '@group(0) @binding(0) var src: texture_external;',
    '@group(0) @binding(1) var smp: sampler;',
    'struct VO { @builtin(position) pos: vec4f, @location(0) uv: vec2f };',
    '@vertex fn vs(@builtin(vertex_index) i: u32) -> VO {',
    '  var corners = array<vec2f, 3>(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));',
    '  let p = corners[i];',
    '  var o: VO;',
    '  o.pos = vec4f(p, 0.0, 1.0);',
    `  o.uv = vec2f(p.x * 0.5 + 0.5, ${v});`,
    '  return o;',
    '}',
    '@fragment fn fs(in: VO) -> @location(0) vec4f {',
    '  let c = textureSampleBaseClampToEdge(src, smp, in.uv);',
    lin + '}',
  ].join('\n');
}

export function isVideoSource(x) {
  return (typeof HTMLVideoElement !== 'undefined' && x instanceof HTMLVideoElement)
    || (typeof VideoFrame !== 'undefined' && x instanceof VideoFrame);
}

/** A blitter on a device: draw(source, gpuTexture, format, flipY, layer) copies one video frame GPU to GPU. */
export function createVideoBlit(device) {
  const pipelines = new Map();
  const sampler = device.createSampler({ magFilter: 'linear', minFilter: 'linear' });
  const pipelineFor = (format, flipY) => {
    const decode = /-srgb$/.test(format), key = format + (flipY ? ':f' : ':n');
    let p = pipelines.get(key);
    if (!p) {
      const module = device.createShaderModule({ label: 'glassroad-video-blit', code: videoBlitWgsl({ flipY, decode }) });
      p = device.createRenderPipeline({
        label: 'glassroad-video-blit', layout: 'auto',
        vertex: { module, entryPoint: 'vs' },
        fragment: { module, entryPoint: 'fs', targets: [{ format }] },
        primitive: { topology: 'triangle-list' },
      });
      pipelines.set(key, p);
    }
    return p;
  };
  return {
    draw(source, texture, format, flipY = false, layer = 0) {
      const pipeline = pipelineFor(format, !!flipY);
      const external = device.importExternalTexture({ source });
      const bindGroup = device.createBindGroup({ layout: pipeline.getBindGroupLayout(0), entries: [
        { binding: 0, resource: external }, { binding: 1, resource: sampler }] });
      const view = texture.createView({ dimension: '2d', baseMipLevel: 0, mipLevelCount: 1, baseArrayLayer: layer, arrayLayerCount: 1 });
      const enc = device.createCommandEncoder({ label: 'glassroad-video-blit' });
      const pass = enc.beginRenderPass({ colorAttachments: [{ view, loadOp: 'clear', storeOp: 'store', clearValue: { r: 0, g: 0, b: 0, a: 1 } }] });
      pass.setPipeline(pipeline);
      pass.setBindGroup(0, bindGroup);
      pass.draw(3);
      pass.end();
      device.queue.submit([enc.finish()]);
    },
  };
}

/** Patch a WebGPU backend's texture utils so video frames upload through the external-texture blit. */
export function uploadVideoThroughExternalTexture(three) {
  const backend = three && three.backend, utils = backend && backend.textureUtils, device = backend && backend.device;
  if (!backend || backend.isWebGPUBackend !== true || !utils || !device || patched.has(three)) return false;
  if (typeof utils._copyImageToTexture !== 'function' || typeof device.importExternalTexture !== 'function') return false;
  patched.add(three);
  const copy = utils._copyImageToTexture;
  let blit = null;
  utils._copyImageToTexture = function(image, textureGPU, descriptorGPU, originZ, flipY, premultiplyAlpha, mipLevel = 0) {
    if (mipLevel === 0 && isVideoSource(image) && textureGPU && descriptorGPU) {
      const ready = typeof VideoFrame !== 'undefined' && image instanceof VideoFrame ? true : image.readyState >= 2 && image.videoWidth > 0;
      if (!ready) return undefined;
      try {
        blit = blit || createVideoBlit(device);
        blit.draw(image, textureGPU, descriptorGPU.format, flipY, originZ || 0);
        return undefined;
      } catch (e) { /* fall through to three's own copy */ }
    }
    return copy.call(this, image, textureGPU, descriptorGPU, originZ, flipY, premultiplyAlpha, mipLevel);
  };
  return true;
}
