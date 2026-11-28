// glTF requests each buffer view separately. Large views share a worker message while
// small attributes use the stock decoder, which avoids a message round trip for each one.
const minimumBytes = 32 * 1024;
const startWorker = () => new Worker(new URL('./meshopt-worker.js', import.meta.url), {type:'module', name:'Expo geometry'});

export function createRoomDecoder(local, openWorker=startWorker) {
  let worker, closed=false, queued=[], active=[];
  function stop() {
    if (!worker) return;
    worker.onmessage=worker.onerror=worker.onmessageerror=null;
    worker.terminate();
    worker=null;
  }
  function fallback() {
    stop();
    const waiting=active.concat(queued);
    active=[]; queued=[];
    for (const job of waiting) job.resolve(local.decodeGltfBufferAsync(job.count,job.stride,job.source,job.mode,job.filter));
  }
  function flush() {
    if (!queued.length || active.length || !worker) return;
    active=queued; queued=[];
    try {
      // The incoming view usually shares its backing buffer with every other mesh in the GLB.
      // Keep that buffer intact for the main loader and in case the worker cannot run.
      const batch=active.map(({count,stride,source,mode,filter})=>({count,stride,source:new Uint8Array(source),mode,filter}));
      worker.postMessage(batch,batch.map(job=>job.source.buffer));
    } catch { fallback(); }
  }
  function receive({data}) {
    if (!Array.isArray(data) || data.length!==active.length) { fallback(); return; }
    const done=active; active=[];
    for (let i=0;i<done.length;i++) {
      if (data[i] instanceof Uint8Array) done[i].resolve(data[i]);
      else done[i].reject(new Error(data[i].error));
    }
    flush();
  }
  // Overlap worker/Wasm startup with the first room request. Embedded hosts can disallow Workers.
  try {
    if (local.supported) {
      worker=openWorker();
      worker.onmessage=receive;
      worker.onmessageerror=fallback;
      worker.onerror=event=>{event.preventDefault?.();fallback();};
    }
  } catch { stop(); }
  return {
    supported:local.supported,
    decodeGltfBufferAsync(count,stride,source,mode,filter) {
      if (closed) return Promise.reject(new DOMException('Expo closed','AbortError'));
      if (!worker || count*stride<minimumBytes) return local.decodeGltfBufferAsync(count,stride,source,mode,filter);
      return new Promise((resolve,reject)=>{
        queued.push({count,stride,source,mode,filter,resolve,reject});
        if (queued.length===1 && !active.length) queueMicrotask(flush);
      });
    },
    dispose() {
      closed=true; stop();
      const error=new DOMException('Expo closed','AbortError');
      for (const job of active.concat(queued)) job.reject(error);
      active=[]; queued=[];
    }
  };
}
