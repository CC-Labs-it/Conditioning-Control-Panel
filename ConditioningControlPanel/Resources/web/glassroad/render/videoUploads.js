/* WebGL2: a regular Texture whose image is an HTMLVideoElement uploads the decoded frame straight to the GPU.
 * render/media.js used to copy every playing clip into a 2D canvas first, because r186's WebGL texture upload
 * (WebGLTextureUtils.updateTexture -> module-private getImage) passes images, canvases and bitmaps through but
 * answers `.data` for anything else, so a video became undefined. Firefox read each decoded frame back to the
 * CPU for that drawImage: ~3 ms per copy, ~12 ms a frame with four clips (GOONWORKS, Firefox 158, 2026-10-26).
 * Here the video rides in a tiny holder whose `.data` is the element, so three's own path calls
 * texSubImage2D(<video>), which browsers serve GPU to GPU. Only plain 2D textures without mipmap arrays take the
 * holder; everything else, and every non-video image, goes through three untouched. A regular Texture (not a
 * VideoTexture) keeps the warmed sRGB sampler graph: the WebGL VideoTexture path changes the shader and the
 * storage. WebGPU: Chromium copyExternalImageToTexture accepts a video; Firefox does not (render/firefoxWebgpu.js).
 * Keep aligned with the vendored renderer (WebGLTextureUtils.updateTexture, getImage) when upgrading;
 * tests/video-uploads.test.mjs pins the fallback this relies on. Never edit the vendored build for this. */
const patched = new WeakSet();

export function isVideoElement(x) {
  return typeof HTMLVideoElement !== 'undefined' && x instanceof HTMLVideoElement;
}

/** A plain 2D texture whose upload is a single texSubImage2D of its image. */
export function plainImageTexture(t) {
  return !!t && t.isTexture === true && !t.isVideoTexture && !t.isDataTexture && !t.isCompressedTexture
    && !t.isCompressedArrayTexture && !t.isCubeTexture && !t.isArrayTexture && !t.isDataArrayTexture
    && !t.isData3DTexture && !t.isHTMLTexture && !t.isRenderTargetTexture && !t.isDepthTexture
    && !t.isFramebufferTexture && !t.isStorageTexture && !(t.mipmaps && t.mipmaps.length);
}

export function uploadVideoElementsDirectly(three) {
  const backend = three && three.backend, utils = backend && backend.textureUtils;
  if (!backend || backend.isWebGLBackend !== true || !utils || patched.has(three)) return false;
  if (typeof utils.updateTexture !== 'function') return false;
  patched.add(three);
  const updateTexture = utils.updateTexture;
  utils.updateTexture = function(texture, options) {
    const image = options && options.image;
    if (!isVideoElement(image) || !plainImageTexture(texture)) return updateTexture.call(this, texture, options);
    return updateTexture.call(this, texture, Object.assign({}, options, { image: { data: image, width: options.width, height: options.height } }));
  };
  return true;
}
