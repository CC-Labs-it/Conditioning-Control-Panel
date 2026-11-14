// The dressing room's boot cover (owner 2026-10-30): a real byte count over Rain's two big files, a short
// "almost ready" phase for parse and shader compile, and an OPAQUE cover until the first fully dressed
// frame is on screen. Then the cover goes OUT and the room comes IN. Pure maths up top (node tests);
// the DOM part only reads it. Calm (Gentle Visuals, reduced motion) only lengthens fades, never moves.

/** English lines; the host's query `strings` (loc keys rain_wardrobe_load_*) replaces them. */
export const LOAD_STR = {
  loadTitle: '{companion} is getting dressed',
  loadSize: '{loaded} of {total} MB',
  loadAlmost: 'Almost ready',
};

/** Rain's big files and their size when nothing better is known (the 2026-10-30 pack). */
export const BIG_FILES = ['avatar.glb', 'pelvis-data.js'];
export const FALLBACK_BYTES = { 'avatar.glb': 240069668, 'pelvis-data.js': 59827244 };

/** Bytes are this share of the bar; parse, fit, colour and compile fill the rest. */
export const BYTES_SHARE = 0.88;

/**
 * Progress over several files. `sizes` = known totals (the host's file sizes), else a server total,
 * else the fallback. A file never counts more than its total and the bar never runs backwards.
 */
export function createLoadProgress({ sizes = {}, files = BIG_FILES, fallback = FALLBACK_BYTES } = {}) {
  const rows = new Map();
  for (const name of files) {
    const known = Number(sizes[name]);
    rows.set(name, { loaded: 0, total: known > 0 ? known : (Number(fallback[name]) || 0), sure: known > 0 });
  }
  let finish = 0, shown = 0;
  return {
    /** One onProgress / stream tick. `total` (Content-Length) is used only when the host sent no size. */
    bytes(name, loaded, total) {
      const row = rows.get(name);
      if (!row) return;
      const t = Number(total);
      if (!row.sure && t > 0) { row.total = t; row.sure = true; }
      const l = Math.max(0, Number(loaded) || 0);
      if (l > row.total) row.total = l;
      row.loaded = Math.max(row.loaded, l);
    },
    /** The file is all in (a stream end or a loader resolve), whatever the count said. */
    done(name) { const row = rows.get(name); if (row) { row.total = Math.max(row.total, row.loaded) || 1; row.loaded = row.total; } },
    /** The almost-ready phase, 0..1 (parse, fit, colours, compile). */
    stage(f) { finish = Math.max(finish, Math.min(1, Math.max(0, Number(f) || 0))); },
    get loaded() { let n = 0; for (const r of rows.values()) n += r.loaded; return n; },
    get total() { let n = 0; for (const r of rows.values()) n += r.total; return n; },
    get bytesDone() { for (const r of rows.values()) if (r.loaded < r.total) return false; return true; },
    /** The bar, 0..1, monotonic. */
    fraction() {
      const total = this.total;
      const bytes = total > 0 ? Math.min(1, this.loaded / total) : 0;
      const f = this.bytesDone ? BYTES_SHARE + (1 - BYTES_SHARE) * finish : BYTES_SHARE * bytes;
      shown = Math.max(shown, Math.min(1, f));
      return shown;
    },
    /** The small line under the title: "120 of 300 MB", then "Almost ready". */
    line(T = LOAD_STR) {
      if (this.bytesDone) return T.loadAlmost || LOAD_STR.loadAlmost;
      return fill(T.loadSize || LOAD_STR.loadSize, { loaded: mb(this.loaded), total: mb(this.total) });
    },
  };
}

export const mb = bytes => String(Math.round((Number(bytes) || 0) / 1e6));
export const fill = (text, values) => String(text).replace(/\{(\w+)\}/g, (m, k) => (k in values ? String(values[k]) : m));

/**
 * The cover's rule: it stays opaque until the look is dressed AND a frame of it has been drawn
 * (and, in the app, the host's init has landed or timed out). Only then may it leave.
 */
export function createRevealGate(needs = ['dressed', 'drawn']) {
  const marks = new Set();
  let failed = false;
  return {
    mark(what) { if (needs.includes(what)) marks.add(what); return this.open; },
    fail() { failed = true; },
    get open() { return !failed && needs.every(n => marks.has(n)); },
    get opaque() { return !this.open; },
  };
}

/** How the cover leaves and the room arrives (ms). Calm: fades only, and longer. */
export function revealTimings(calm) {
  return calm
    ? { coverOut: 700, roomIn: 900, stagger: 0, move: false }
    : { coverOut: 260, roomIn: 620, stagger: 40, move: true };
}

// ---- big modules: pelvis-data.js is fetched with a byte count, then imported from a blob ----

const prefetched = new Map();

/** Fetch `url` counting bytes (onBytes(loaded, total)); resolves a blob: URL the page can import. */
export function prefetchModule(url, onBytes, fetcher = globalThis.fetch) {
  const job = (async () => {
    const res = await fetcher(url);
    if (!res.ok) throw new Error(`${url.split('/').pop()}: HTTP ${res.status}`);
    const total = Number(res.headers?.get?.('content-length')) || 0;
    let loaded = 0;
    const parts = [];
    if (res.body && typeof res.body.getReader === 'function') {
      const reader = res.body.getReader();
      for (;;) {
        const { done, value } = await reader.read();
        if (done) break;
        parts.push(value); loaded += value.byteLength; onBytes?.(loaded, total);
      }
    } else { const buf = await res.arrayBuffer(); parts.push(buf); loaded = buf.byteLength; onBytes?.(loaded, total); }
    return URL.createObjectURL(new Blob(parts, { type: 'text/javascript' }));
  })();
  prefetched.set(url, job);
  return job;
}

/** What to import for `url`: the prefetched blob when there is one, else the url itself. */
export async function moduleUrl(url) {
  const job = prefetched.get(url);
  if (!job) return url;
  try { return await job; } catch { return url; }
}

/** Drop a blob once imported (the module stays loaded). */
export function releaseModule(url) {
  const job = prefetched.get(url);
  prefetched.delete(url);
  job?.then(u => { if (String(u).startsWith('blob:')) URL.revokeObjectURL(u); }, () => {});
}

// ---- DOM ----

/** Paint the cover: bar, title, line. rAF-throttled by the caller. */
export function paintCover(doc, progress, T, companion) {
  const $ = id => doc.getElementById(id);
  const f = progress.fraction();
  const bar = $('loadBar');
  if (bar) bar.style.transform = `scaleX(${f.toFixed(4)})`;
  const meter = $('loadProgress');
  if (meter) { meter.setAttribute('aria-valuenow', String(Math.round(f * 100))); }
  if ($('loadMessage')) $('loadMessage').textContent = fill(T.loadTitle || LOAD_STR.loadTitle, { companion });
  if ($('loadLine')) $('loadLine').textContent = progress.line(T);
  return f;
}

/** Cover OUT, room IN. Resolves when the room has settled. */
export function reveal(doc, calm, wait = ms => new Promise(r => setTimeout(r, ms))) {
  const t = revealTimings(calm);
  const root = doc.documentElement, cover = doc.getElementById('loading');
  root.style.setProperty('--cover-out', `${t.coverOut}ms`);
  root.style.setProperty('--room-in', `${t.roomIn}ms`);
  root.style.setProperty('--room-stagger', `${t.stagger}ms`);
  root.classList.toggle('calm-in', !t.move);
  cover?.classList.add('out');
  doc.body.classList.remove('booting');
  doc.body.classList.add('entering');
  return wait(Math.max(t.coverOut, t.roomIn) + 8 * t.stagger + 40).then(() => {
    if (cover && !cover.classList.contains('error')) { cover.hidden = true; cover.classList.remove('out'); }
    doc.body.classList.remove('entering');
  });
}
