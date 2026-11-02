/* dev/uploadCensus.js - DEV ONLY (?census=1): who sends how many bytes to the GPU per frame on WebGL2.
 * Firefox serializes every WebGL call, upload bytes included, into 100 KB command blocks and forces a
 * SYNCHRONOUS present once more than 10 blocks were flushed in a frame (dom/canvas/ClientWebGLContext.cpp,
 * tooManyFlushes = 10): the page then waits for the GPU process and fps halves (handoff-1027/GLASSROAD-FF60.md).
 * This hook wraps the live context's bufferData / bufferSubData / texSubImage2D / texImage2D and labels every
 * byte with the render object three is working on (geometry attributes via Geometries.updateAttributes,
 * bindings via Bindings.updateForRender). window.__census.report() answers the top rows per frame. */

function nameOf(o) {
  if (!o) return '?';
  const parts = [];
  for (let p = o, i = 0; p && i < 4; p = p.parent, i++) parts.push(p.name || p.type || '?');
  return parts.join('<');
}

function attrKey(ro, attr) {
  const o = ro && ro.object, g = ro && ro.geometry;
  if (o && o.instanceMatrix === attr) return 'instanceMatrix';
  if (o && o.instanceColor === attr) return 'instanceColor';
  if (g) {
    if (g.index === attr) return 'index';
    for (const k in g.attributes) { const a = g.attributes[k]; if (a === attr || (a && a.data === attr)) return k; }
  }
  return attr && attr.name ? attr.name : 'attr';
}

export function installUploadCensus(three) {
  const backend = three.backend, gl = backend && backend.gl;
  if (!gl || typeof gl.bufferSubData !== 'function') return null;
  const rows = new Map();
  let label = 'other', frames = 0, lastT = -1, totalFrame = 0, blockFrame = 0;
  const perFrame = [], perFrameBlock = [], perFrameDt = [], heavy = [], fullCanvas = [];
  let frameRows = new Map();                      // this frame's in-channel bytes per row key          // block = bytes that ride Firefox's command channel (typed arrays), not canvas/video/img surfaces
  const row = (k) => { let r = rows.get(k); if (!r) rows.set(k, r = { bytes: 0, calls: 0, full: 0, max: 0 }); return r; };
  const add = (kind, bytes, full, inBlock = true) => {
    const r = row(kind + ' | ' + label);
    r.bytes += bytes; r.calls++; if (full) r.full++; if (bytes > r.max) r.max = bytes;
    totalFrame += bytes; if (inBlock) { blockFrame += bytes; const k = kind + ' | ' + label; frameRows.set(k, (frameRows.get(k) || 0) + bytes); }
  };
  const sizeOf = (src, so, len) => {
    if (typeof src === 'number') return src;
    if (!src) return 0;
    if (src.BYTES_PER_ELEMENT) return len ? len * src.BYTES_PER_ELEMENT : src.byteLength - (so || 0) * src.BYTES_PER_ELEMENT;
    return src.byteLength || 0;
  };
  const tname = (t) => t === gl.UNIFORM_BUFFER ? 'ubo' : t === gl.ARRAY_BUFFER ? 'vbo' : t === gl.ELEMENT_ARRAY_BUFFER ? 'ibo' : 'buf';
  const bsd = gl.bufferSubData, bd = gl.bufferData, tsi = gl.texSubImage2D, ti = gl.texImage2D;
  gl.bufferSubData = function(target, off, src, so, len) { add('bufferSubData:' + tname(target), sizeOf(src, so, len), false); return bsd.apply(this, arguments); };
  gl.bufferData = function(target, d, usage, so, len) { add('bufferData:' + tname(target), sizeOf(d, so, len), true); return bd.apply(this, arguments); };
  const CH = { 6408: 4, 6407: 3, 6403: 1, 33319: 2, 36244: 1, 33320: 2, 36248: 3, 36249: 4, 6406: 1, 6409: 1, 6410: 2, 6402: 1 };
  const TB = { 5121: 1, 5120: 1, 5126: 4, 5131: 2, 5123: 2, 5122: 2, 5125: 4, 5124: 4, 36193: 2, 32819: 2, 32820: 2, 34042: 4 };
  const texBytes = (args, sub) => {
    const src = Array.prototype.find.call(args, (x) => x && typeof x === 'object');
    if (!src) return [0, 'none'];
    if (src.byteLength !== undefined) {
      // a typed-array sub-image sends width x height texels, not the whole array (dataTextureRows.js row spans)
      if (sub && args.length >= 9 && typeof args[4] === 'number' && typeof args[5] === 'number') {
        const ch = CH[args[6]] || 4, tb = TB[args[7]] || 1;
        return [Math.min(src.byteLength, args[4] * args[5] * ch * tb), 'array'];
      }
      return [src.byteLength, 'array'];
    }
    const w = src.width || src.videoWidth || 0, h = src.height || src.videoHeight || 0;
    return [w * h * 4, src.nodeName || src.constructor.name];
  };
  gl.texSubImage2D = function() { const [n, k] = texBytes(arguments, true); add('texSubImage2D:' + k, n, false, k === 'array'); return tsi.apply(this, arguments); };
  gl.texImage2D = function() { const [n, k] = texBytes(arguments); add('texImage2D:' + k, n, true, k === 'array'); return ti.apply(this, arguments); };

  // why a canvas texture went up whole: the region plan's inputs at that moment (render/canvasRegions.js)
  const tu = backend.textureUtils;
  if (tu && typeof tu.updateTexture === 'function') {
    const ut = tu.updateTexture;
    tu.updateTexture = function(texture, options) {
      const img = texture && texture.image;
      if (img && img.data && ArrayBuffer.isView(img.data) && img.data.byteLength >= 100000 && fullCanvas.filter((f) => f.rows !== undefined).length < 20) {
        const ud = texture.userData || {}, d = this.backend.get(texture);
        fullCanvas.push({ frame: frames, name: texture.name || label, px: img.width + 'x' + img.height, rows: ud.__rows ? ud.__rows.from + '-' + ud.__rows.to : null, gpu: d.textureGPU !== undefined, sameGpu: d.rowsGPU === d.textureGPU, sameSize: d.rowsW === img.width && d.rowsH === img.height, type: d.glTextureType, tex2d: this.gl && this.gl.TEXTURE_2D });
      }
      if (img && img.nodeName === 'CANVAS' && img.width * img.height * 4 >= 1000000 && fullCanvas.length < 60) {
        const ud = texture.userData || {}, list = ud.__regions, d = this.backend.get(texture);
        fullCanvas.push({ frame: frames, name: texture.name || label, px: img.width + 'x' + img.height, regions: list ? list.length : -1, gpu: d.textureGPU !== undefined, sameGpu: d.regionGPU === d.textureGPU, sameSize: d.regionW === img.width && d.regionH === img.height });
      }
      return ut.call(this, texture, options);
    };
  }
  // labels: the geometry attribute pass and the bindings pass of each render object
  const geos = three._geometries, bindings = three._bindings;
  if (geos && typeof geos.updateAttributes === 'function') {
    const ua = geos.updateAttributes;
    geos.updateAttributes = function(ro) {
      const prev = label;
      label = 'geo ' + nameOf(ro.object) + ' [' + ro.getAttributes().map((a) => { const b = a.isInterleavedBufferAttribute ? a.data : a; return attrKey(ro, a) + ':' + b.count + 'x' + b.itemSize + (b.usage === 35048 ? ':DYN' : ''); }).join(',') + ']';
      try { return ua.call(this, ro); } finally { label = prev; }
    };
  }
  if (bindings && typeof bindings.updateForRender === 'function') {
    const uf = bindings.updateForRender;
    bindings.updateForRender = function(ro) {
      const prev = label;
      label = 'bind ' + nameOf(ro.object) + (ro.object && ro.object.isSkinnedMesh ? ' skinned' : '') + (ro.object && ro.object.isInstancedMesh ? ' inst' + ro.object.count : '');
      try { return uf.call(this, ro); } finally { label = prev; }
    };
  }
  // frames: one per animation frame timestamp
  const raf = globalThis.requestAnimationFrame.bind(globalThis);
  globalThis.requestAnimationFrame = (cb) => raf((t) => { if (t !== lastT) { perFrameDt.push(lastT < 0 ? 0 : t - lastT); lastT = t; frames++; perFrame.push(totalFrame); perFrameBlock.push(blockFrame);
      if (blockFrame > 300000) { if (heavy.length >= 40) heavy.shift(); heavy.push({ frame: frames, block: blockFrame, rows: [...frameRows.entries()].sort((a, b) => b[1] - a[1]).slice(0, 8).map(([k, b]) => b + ' ' + k.slice(0, 90)) }); }
      totalFrame = 0; blockFrame = 0; frameRows = new Map(); } return cb(t); });

  const census = globalThis.__census = {
    reset() { rows.clear(); frames = 0; perFrame.length = 0; perFrameBlock.length = 0; perFrameDt.length = 0; heavy.length = 0; totalFrame = 0; blockFrame = 0; },
    report(top = 40) {
      const n = Math.max(1, frames);
      const sorted = [...perFrame].sort((a, b) => a - b), block = [...perFrameBlock].sort((a, b) => a - b);
      const q = (arr, f) => arr[Math.min(arr.length - 1, Math.floor(arr.length * f))] || 0;
      const over1m = perFrameBlock.filter((b) => b > 1000000).length;
      // frame time by in-channel bytes, over the last 700 frames (the race): does the channel drive the slow frames?
      const tail = Math.max(0, frames - 700), bins = [[0, 150000], [150000, 300000], [300000, 600000], [600000, 1e9]];
      const byBytes = bins.map(([lo, hi]) => { const d = []; for (let i = tail; i < frames; i++) if (perFrameBlock[i] >= lo && perFrameBlock[i] < hi) d.push(perFrameDt[i]); d.sort((a, b) => a - b); return { lo, n: d.length, dtMed: Math.round((d[d.length >> 1] || 0) * 10) / 10, dtP90: Math.round((d[Math.floor(d.length * 0.9)] || 0) * 10) / 10 }; });
      const list = [...rows.entries()].map(([k, r]) => ({ key: k, bytesPerFrame: Math.round(r.bytes / n), callsPerFrame: Math.round(r.calls / n * 10) / 10, fullPerFrame: Math.round(r.full / n * 10) / 10, max: r.max }))
        .sort((a, b) => b.bytesPerFrame - a.bytesPerFrame);
      const total = list.reduce((s, r) => s + r.bytesPerFrame, 0);
      return { frames, heavy, fullCanvas, byBytes, totalPerFrame: total, blockMed: q(block, 0.5), blockP95: q(block, 0.95), blockMax: block[block.length - 1] || 0, blockOver1MB: over1m, perFrameMed: sorted[sorted.length >> 1] || 0, perFrameP95: sorted[Math.floor(sorted.length * 0.95)] || 0, perFrameMax: sorted[sorted.length - 1] || 0, rows: list.slice(0, top) };
    },
  };
  return census;
}
