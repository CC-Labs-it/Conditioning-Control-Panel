// The host owns the 36-post library. Keep its already-metered files in this
// visit's memory, with only a handful of paused browser players decoded ahead.
const MAX_FILE = 3 * 1024 * 1024;
const playerLeases = new WeakMap();

export function createPlayer(doc, row, src = row.url) {
  let node;
  if (row.kind === 'clip') {
    node = doc.createElement('video');
    node.muted = node.defaultMuted = node.loop = node.playsInline = true;
    node.setAttribute('muted', '');
    node.setAttribute('playsinline', '');
    node.disablePictureInPicture = true;
    node.preload = 'auto';
  } else {
    node = doc.createElement('img');
    node.alt = '';
    node.draggable = false;
    node.decoding = 'async';
  }
  node.dataset.kind = row.kind;
  node.src = src;
  return node;
}

export function mediaReady(node) {
  return node?.tagName === 'VIDEO' ? node.readyState >= 2 && node.videoWidth > 0 :
    Boolean(node?.complete && node.naturalWidth);
}

export function releasePlayer(node) {
  if (node.tagName === 'VIDEO') node.pause();
  node.removeAttribute('src');
  if (node.tagName === 'VIDEO') node.load();
  node.remove();
  playerLeases.get(node)?.();
  playerLeases.delete(node);
}

export function createMediaCache(onAvailable, doc = globalThis.document, fetchFile = globalThis.fetch) {
  const entries = new Map(), retired = new Set();
  let active = false, queued = false, clock = 0;
  const wake = () => {
    if (queued || !active || !doc) return;
    queued = true;
    queueMicrotask(() => { queued = false; if (active) fill(); });
  };
  function releaseBytes(entry) {
    if (entry.blobUrl) URL.revokeObjectURL(entry.blobUrl);
    entry.blobUrl = null;
    retired.delete(entry);
  }
  function detach(entry) {
    entry.stopWaiting?.();
    const node = entry.node;
    entry.node = null;
    return node;
  }
  function remove(entry, force) {
    entry.job?.abort();
    const node = detach(entry);
    if (node) releasePlayer(node);
    if (!force && entry.leases && entry.blobUrl) retired.add(entry);
    else releaseBytes(entry);
  }
  async function download(entry) {
    const job = new AbortController();
    entry.job = job;
    const timer = setTimeout(() => job.abort(), 10000);
    try {
      const reply = await fetchFile(entry.row.url, {signal:job.signal, credentials:'same-origin', mode:'same-origin', cache:'no-store', redirect:'error'});
      if (!reply.ok || Number(reply.headers.get('content-length')) > MAX_FILE) throw Error('Media unavailable');
      const bytes = await reply.blob();
      if (!bytes.size || bytes.size > MAX_FILE) throw Error('Media size outside the host limit');
      if (!job.signal.aborted && entries.get(entry.row.url) === entry) entry.blobUrl = URL.createObjectURL(bytes);
    } catch {
      // Keep the approved host URL as a fallback on a transfer failure.
      if (active && job.signal.reason !== 'paused' && entries.get(entry.row.url) === entry) entry.fetchFailed = true;
    } finally {
      clearTimeout(timer);
      entry.job = null;
      if (entries.get(entry.row.url) === entry && (entry.blobUrl || entry.fetchFailed)) onAvailable();
      wake();
    }
  }
  function prepare(entry) {
    const node = createPlayer(doc, entry.row, entry.blobUrl || entry.row.url);
    entry.node = node;
    const event = entry.row.kind === 'clip' ? 'loadeddata' : 'load';
    const stop = () => {
      clearTimeout(timer);
      node.removeEventListener(event, ready);
      node.removeEventListener('error', failed);
      entry.stopWaiting = null;
    };
    const ready = () => { stop(); wake(); };
    const failed = () => {
      stop(); entry.node = null; entry.decodeFailed = true;
      releasePlayer(node); wake();
    };
    const timer = setTimeout(failed, 9000);
    node.addEventListener(event, ready, {once:true});
    node.addEventListener('error', failed, {once:true});
    entry.stopWaiting = stop;
    if (mediaReady(node)) ready();
  }
  function fill() {
    const list = [...entries.values()];
    let downloads = list.filter(e => e.job).length;
    let decodes = list.filter(e => e.stopWaiting).length;
    const used = {photos:0, motion:0};
    for (const e of list) if (e.node) used[e.row.kind === 'picture' ? 'photos' : 'motion']++;
    // Prioritize unused entries within each kind when replacing a consumed player.
    list.sort((a,b) => a.last - b.last);
    for (const e of list) {
      if (e.cacheBytes && !e.blobUrl) {
        if (!e.fetchFailed && !e.job && downloads < 2) { downloads++; void download(e); }
        continue;
      }
      const photo = e.row.kind === 'picture', group = photo ? 'photos' : 'motion';
      if (!e.node && !e.decodeFailed && decodes < 2 && used[group] < (photo ? 2 : 4)) {
        used[group]++; decodes++; prepare(e);
      }
    }
  }
  function acquire(row) {
    const entry = entries.get(row.url);
    if (!entry?.blobUrl) return {url:row.url,release() {}};
    entry.leases++;
    let live = true;
    return {url:entry.blobUrl, release() {
      if (!live) return;
      live = false;
      if (--entry.leases === 0 && retired.has(entry)) releaseBytes(entry);
    }};
  }
  return {
    update(rows, cacheBytes = false) {
      const urls = new Set(rows.map(row => row.url));
      for (const [url,e] of entries) if (!urls.has(url)) { entries.delete(url); remove(e,false); }
      for (const row of rows) if (!entries.has(row.url)) {
        entries.set(row.url,{row,cacheBytes,leases:0,last:0,node:null,job:null,blobUrl:null});
      }
      wake();
    },
    setActive(on) {
      active = on && Boolean(doc);
      if (active) wake();
      else for (const e of entries.values()) {
        e.job?.abort('paused');
        if (e.stopWaiting) releasePlayer(detach(e));
      }
    },
    clear() {
      for (const e of entries.values()) remove(e,true);
      for (const e of retired) releaseBytes(e);
      entries.clear();
    },
    prepared(url) { return mediaReady(entries.get(url)?.node); },
    available(url) {
      const e = entries.get(url);
      return !active || !e?.cacheBytes || Boolean(e.blobUrl || e.fetchFailed);
    },
    acquire,
    take(row, owner) {
      const entry = entries.get(row.url);
      const lease = acquire(row);
      const node = entry && detach(entry) || createPlayer(owner, row, lease.url);
      if (entry) entry.last = ++clock;
      playerLeases.set(node, lease.release);
      wake();
      return node;
    },
  };
}
