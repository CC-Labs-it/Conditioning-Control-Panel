/* rt-linux-spike/spike.js - injected FIRST into Racing Thoughts' index.html by bench-server.mjs (classic script, runs
 * before any module). Measures one run in whatever engine opened the page and POSTs the record to the bench server.
 * URL params it reads: spikeRun (id), spikeSec (race seconds, default 45), spikeOpts (options record JSON written to
 * localStorage before boot), spikeScrolller=check (fetch test of the Scrolller block), spikeMedia=files ("My files" with
 * real media from the bench server's --media folder: see media() below; spikeMediaSeed picks which files). The page's own params
 * (bot, seed, quality, track, renderer) pass through untouched.
 * Same frame probe as dev/perf-check.mjs (every rAF callback timed, summed per animation frame), so Windows and Linux
 * numbers compare. The engine runs with vsync ON: fps is what a player sees, capped at the display rate. */
(() => {
  const qs = new URLSearchParams(location.search);
  const RUN = qs.get('spikeRun');
  if (!RUN) return;
  const SECONDS = Number(qs.get('spikeSec') || 45);
  const t0 = performance.now();
  const errors = [];
  const pushErr = (s) => { if (errors.length < 40) errors.push(String(s).slice(0, 400)); };

  // no spikeOpts (the -auto runs) = no record at all: engines keep localStorage between runs, so an earlier pinned
  // run's record would otherwise pin quality high again
  try { const o = qs.get('spikeOpts'); if (o) localStorage.setItem('glassroad.options.v1', o); else localStorage.removeItem('glassroad.options.v1'); } catch (e) { pushErr('options: ' + e); }

  // "My files" rows: the files choice is stored before boot (host/localLibrary.js MEDIA_SOURCE_KEY), the files are
  // fetched now and handed in at the garage (media() below)
  const MEDIA = qs.get('spikeMedia') === 'files';
  if (MEDIA) { try { localStorage.setItem('glassroad.mediaSource.v1', 'files'); } catch (e) { pushErr('media choice: ' + e); } }
  const blobUrls = [];
  if (MEDIA) {
    const cu = URL.createObjectURL.bind(URL);
    URL.createObjectURL = (o) => { if (o instanceof File) blobUrls.push({ name: o.name, type: o.type, size: o.size }); return cu(o); };
  }
  const mediaPick = MEDIA ? pickMedia() : null;

  addEventListener('error', (e) => pushErr('error: ' + (e.message || e.type) + (e.filename ? ' @' + e.filename.split('/').pop() + ':' + e.lineno : '')), true);
  addEventListener('unhandledrejection', (e) => pushErr('rejection: ' + ((e.reason && (e.reason.stack || e.reason.message)) || e.reason)));
  const cerr = console.error.bind(console);
  console.error = (...a) => { pushErr('console: ' + a.map((x) => (x && x.message) || String(x)).join(' ')); cerr(...a); };

  // frame probe
  const raf = window.requestAnimationFrame.bind(window);
  const P = { on: false, t: -1, last: 0, work: [], gaps: [], at: [] };
  window.requestAnimationFrame = (cb) => raf((t) => {
    const a = performance.now();
    if (t !== P.t) {
      P.t = t;
      if (P.on) { if (P.last) { P.gaps.push(a - P.last); P.at.push(a); } P.work.push(0); }
      P.last = a;
    }
    cb(t);
    if (P.on && P.work.length) P.work[P.work.length - 1] += performance.now() - a;
  });
  const start = () => { P.on = true; P.last = 0; P.work = []; P.gaps = []; P.at = []; };
  const take = () => { const r = { work: P.work, gaps: P.gaps, at: P.at }; P.on = false; P.work = []; P.gaps = []; P.at = []; return r; };

  // audio contexts the page makes
  const ctxs = [];
  for (const k of ['AudioContext', 'webkitAudioContext']) {
    const C = window[k];
    if (typeof C !== 'function') continue;
    const W = function (...a) { const c = new C(...a); ctxs.push(c); return c; };
    W.prototype = C.prototype;
    Object.setPrototypeOf(W, C);
    try { window[k] = W; } catch (e) { /* frozen */ }
  }

  // gamepads (press a button on a plugged-in pad while the window is open)
  const pads = [];
  addEventListener('gamepadconnected', (e) => pads.push({ id: e.gamepad.id, mapping: e.gamepad.mapping, buttons: e.gamepad.buttons.length, axes: e.gamepad.axes.length, atS: r1((performance.now() - t0) / 1000) }));

  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  function r1(x) { return Math.round(x * 100) / 100; }
  const pct = (a, p) => { if (!a.length) return 0; const s = [...a].sort((x, y) => x - y); return s[Math.min(s.length - 1, Math.ceil(s.length * p) - 1)]; };
  function worstWindow(gaps, at, ms) {
    let worst = Infinity, j = 0;
    for (let i = 0; i < at.length; i++) {
      while (at[i] - at[j] > ms) j++;
      if (at[i] - at[0] < ms) continue;
      const n = i - j + 1, span = at[i] - at[j] + gaps[j];
      worst = Math.min(worst, n * 1000 / span);
    }
    return Number.isFinite(worst) ? Math.round(worst) : 0;
  }
  const post = (path, body) => fetch('/__spike/' + path + '?run=' + encodeURIComponent(RUN), { method: 'POST', body: JSON.stringify(body) }).catch(() => {});
  const mark = (phase) => post('mark', { phase, atS: r1((performance.now() - t0) / 1000) });
  const G = () => window.__glassroad || null;
  const SCALARS = ['phase', 'backend', 'gpu', 'tier', 'renderScale', 'fps', 'lap', 's', 'lengthM', 'timeS', 'place', 'drawCalls', 'triangles', 'art', 'chart'];
  const snap = () => { const g = G(); if (!g) return null; const o = {}; for (const k of SCALARS) if (g[k] !== undefined && typeof g[k] !== 'object') o[k] = g[k]; return o; };

  async function caps() {
    const c = { ua: navigator.userAgent, w: innerWidth, h: innerHeight, dpr: devicePixelRatio, cores: navigator.hardwareConcurrency, deviceMemory: navigator.deviceMemory || null };
    c.webgpu = !!navigator.gpu;
    if (navigator.gpu) {
      try {
        const ad = await navigator.gpu.requestAdapter();
        const info = ad && (ad.info || (ad.requestAdapterInfo ? await ad.requestAdapterInfo() : null));
        c.webgpuAdapter = ad ? (info ? { vendor: info.vendor, architecture: info.architecture, device: info.device, description: info.description } : 'adapter, no info') : 'no adapter';
      } catch (e) { c.webgpuAdapter = 'error: ' + e.message; }
    }
    try {
      const gl = document.createElement('canvas').getContext('webgl2');
      const ext = gl && gl.getExtension('WEBGL_debug_renderer_info');
      c.webgl2 = gl ? { renderer: ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER), vendor: ext ? gl.getParameter(ext.UNMASKED_VENDOR_WEBGL) : gl.getParameter(gl.VENDOR), maxTex: gl.getParameter(gl.MAX_TEXTURE_SIZE) } : null;
      const lose = gl && gl.getExtension('WEBGL_lose_context'); if (lose) lose.loseContext();
    } catch (e) { c.webgl2 = 'error: ' + e.message; }
    // "My files" on Linux: local pictures and clips go in as blob urls, so the engine must decode them
    const v = document.createElement('video');
    c.canPlay = {};
    for (const [k, t] of [['mp4_h264', 'video/mp4; codecs="avc1.42E01E"'], ['webm_vp9', 'video/webm; codecs="vp9"'], ['webm_vp8', 'video/webm; codecs="vp8"'], ['mp4_av1', 'video/mp4; codecs="av01.0.05M.08"'], ['mp3', 'audio/mpeg'], ['ogg_opus', 'audio/ogg; codecs="opus"']]) c.canPlay[k] = v.canPlayType(t) || 'no';
    try {
      const png = Uint8Array.from(atob('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='), (ch) => ch.charCodeAt(0));
      const bmp = await createImageBitmap(new Blob([png], { type: 'image/png' }));
      c.blobImageDecode = bmp.width === 1 ? 'ok' : 'odd size';
    } catch (e) { c.blobImageDecode = 'error: ' + e.message; }
    c.gamepadApi = typeof navigator.getGamepads === 'function';
    c.jsHeapMB = performance.memory ? Math.round(performance.memory.usedJSHeapSize / 1048576) : null;
    return c;
  }

  // online at launch: every engine must build the same course hash as Node (the room's cards)
  async function hashes() {
    const out = { engine: {}, node: null, match: null };
    try {
      const [{ onlineCourseHash }, { loadTrack }, { createFeel }] = await Promise.all([
        import('/net/courseHash.js'), import('/track/load.js'), import('/core/feel.js')]);
      const feelJson = await (await fetch('/feel.json', { cache: 'no-store' })).json();
      const feel = createFeel(feelJson).toJSON();
      for (const id of ['threshold', 'keep', 'goonworks', 'boudoir', 'mainframe']) {
        try {
          const tj = await (await fetch('/tracks/' + id + '.json', { cache: 'no-store' })).json();
          out.engine[id] = await onlineCourseHash(tj, feel, loadTrack(tj, { moving: true }));
        } catch (e) { out.engine[id] = 'error: ' + e.message; }
      }
      out.node = await (await fetch('/__spike/hashes')).json();
      out.match = Object.keys(out.engine).every((k) => out.node && out.node[k] === out.engine[k]);
    } catch (e) { out.error = e.message; }
    return out;
  }

  async function scrolller() {
    const probe = async (url) => { try { await fetch(url, { mode: 'no-cors', cache: 'no-store' }); return 'reached'; } catch (e) { return 'blocked'; } };
    return { scrolller: await probe('https://scrolller.com/favicon.ico?spike=' + Date.now()), control: await probe('https://www.example.com/?spike=' + Date.now()) };
  }

  function audio(c0) {
    const c = ctxs.find((x) => x.state !== 'closed') || null;
    if (!c) return { contexts: ctxs.length, note: 'the page made no AudioContext' };
    const a = { contexts: ctxs.length, state: c.state, sampleRate: c.sampleRate, baseLatencyMs: c.baseLatency != null ? r1(c.baseLatency * 1000) : null,
      outputLatencyMs: c.outputLatency != null ? r1(c.outputLatency * 1000) : null };
    if (c0 && c0.ctx === c) {
      const dPerf = (performance.now() - c0.perf) / 1000, dCtx = c.currentTime - c0.t;
      a.windowS = r1(dPerf);
      // + = the audio clock runs ahead of the frame clock; a music road on songS drifts by this much a minute
      a.driftMsPerMin = dPerf > 1 ? r1((dCtx - dPerf) / dPerf * 60000) : null;
    }
    return a;
  }

  // The same pool the game samples (pictures and clips, at most MAX_FEED_ROWS = 12), but picked with a seeded shuffle
  // so every engine gets the same files; the game's own pickFiles then keeps all of them.
  async function pickMedia() {
    const idx = await (await fetch('/spike-media/index.json', { cache: 'no-store' })).json();
    // optional narrowing for controlled checks: spikeMediaExt (e.g. "gif", "jpe?g|png"), spikeMediaMinMB, spikeMediaMaxMB
    const ext = qs.get('spikeMediaExt') ? new RegExp('\\.(' + qs.get('spikeMediaExt') + ')$', 'i') : null;
    const minB = Number(qs.get('spikeMediaMinMB') || 0) * 1e6, maxB = Number(qs.get('spikeMediaMaxMB') || Infinity) * 1e6;
    const pool = idx.filter((f) => /^(image|video)\//.test(f.type || '') && f.size > 0 && (!ext || ext.test(f.name)) && f.size >= minB && f.size <= maxB);
    let a = Number(qs.get('spikeMediaSeed') || 7) >>> 0;
    const rnd = () => { a = (a + 0x6D2B79F5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
    for (let i = pool.length - 1; i > 0; i--) { const j = Math.floor(rnd() * (i + 1)); [pool[i], pool[j]] = [pool[j], pool[i]]; }
    const chosen = pool.slice(0, 12), t = performance.now();
    const files = await Promise.all(chosen.map(async (f) => new File([await (await fetch('/spike-media/' + f.path.split('/').map(encodeURIComponent).join('/'))).blob()], f.name, { type: f.type, lastModified: 0 })));
    return { files, chosen, poolSize: pool.length, fetchMs: Math.round(performance.now() - t) };
  }
  // Hand the files in the way the Options "Choose files" button does after its picker (hud/courseMedia.js emits
  // 'glassroad-course-media' {kind:'files'}; boot.js then runs pickFiles + library.setFiles + setSource('files')).
  // boot.js listens from the 'garage' milestone and only while the state is boot/menu, so dispatch right then.
  async function media() {
    const out = { seam: "document 'glassroad-course-media' {kind:'files'} (what hud/courseMedia.js emits after its file picker)" };
    try {
      const m = await mediaPick;
      Object.assign(out, { poolSize: m.poolSize, fetchMs: m.fetchMs, files: m.chosen.map((f) => ({ name: f.path, type: f.type, mb: Math.round(f.size / 1e5) / 10 })) });
      for (const end = performance.now() + 120000; performance.now() < end; await sleep(50)) {
        const g = G(); if (g && g.loading && (g.loading.milestones || []).some((x) => x.stage === 'garage')) break;
      }
      out.atS = r1((performance.now() - t0) / 1000); out.phaseAt = G() && G().phase;
      document.dispatchEvent(new CustomEvent('glassroad-course-media', { bubbles: true, detail: { trackId: qs.get('track') || 'threshold', kind: 'files', files: m.files } }));
      out.byType = m.chosen.reduce((c, f) => { const k = f.name.split('.').pop().toLowerCase(); c[k] = (c[k] || 0) + 1; return c; }, {});
      out._files = m.files;
    } catch (e) { out.error = String(e && e.message || e); }
    return out;
  }
  // after the race: can this engine decode each file it was given (GIF / MP4 especially)?
  async function decodeCheck(files) {
    const res = [];
    for (const f of files || []) {
      const u = URL.createObjectURL(new Blob([f], { type: f.type }));
      let r;
      if (f.type.startsWith('image/')) {
        r = await new Promise((ok) => { const im = new Image(); const to = setTimeout(() => ok('timeout'), 10000); im.onload = () => { clearTimeout(to); ok('ok ' + im.naturalWidth + 'x' + im.naturalHeight); }; im.onerror = () => { clearTimeout(to); ok('error'); }; im.src = u; });
      } else {
        r = await new Promise((ok) => { const v = document.createElement('video'); v.muted = true; v.preload = 'auto'; const to = setTimeout(() => ok('timeout (readyState ' + v.readyState + ')'), 10000);
          v.onloadeddata = () => { clearTimeout(to); ok('ok ' + v.videoWidth + 'x' + v.videoHeight); }; v.onerror = () => { clearTimeout(to); ok('error ' + (v.error && v.error.code) + ' ' + ((v.error && v.error.message) || '')); }; v.src = u; });
      }
      URL.revokeObjectURL(u);
      res.push({ name: f.name, type: f.type, result: r });
    }
    return res;
  }

  // Read-only look at the world-screen faces (render/webMediaScreens.js puts one <div><img><video> per slot in .gr-web-media):
  // shown = visible faces; pending = a face whose <img> has a src that has not decoded yet (it stays hidden meanwhile);
  // clipsHeld = faces holding a clip src. Nothing is changed.
  function faceDom() {
    const root = document.querySelector('.gr-web-media'); if (!root) return { domSlots: 0 };
    let shown = 0, pending = 0, stills = 0, clipsHeld = 0;
    for (const el of root.children) {
      const img = el.querySelector('img'), v = el.querySelector('video');
      if (el.style.display === 'block') shown++;
      if (img && img.getAttribute('src')) { stills++; if (!(img.complete && img.naturalWidth)) pending++; }
      if (v && v.getAttribute('src')) clipsHeld++;
    }
    return { domSlots: root.children.length, shown, pending, stillSrcs: stills, clipsHeld };
  }

  async function drive() {
    const rep = { run: RUN, url: location.search, seconds: SECONDS, errors };
    // GARAGE FIRST: the menu opens while the course's lights still warm, and the frame loop (which writes
    // debug.phase) can sit behind that warm, so ?bot=1's attract start may skip 'menu' in debug: take the first
    // live phase, and the boot times from the page's own loading milestones
    const LIVE = ['menu', 'intro', 'countdown', 'racing', 'failed'];
    let st = null;
    for (const end = performance.now() + 120000; performance.now() < end; await sleep(100)) {
      st = snap();
      if (st && LIVE.includes(st.phase)) break;
    }
    const g = G();
    rep.loading = g && g.loading ? { startedAtS: r1(g.loading.startedAt / 1000), totalMs: g.loading.totalMs, milestones: (g.loading.milestones || []).slice(0, 40) } : null;
    if (!st || !LIVE.includes(st.phase) || st.phase === 'failed') {
      rep.failed = 'never reached the garage (' + (st && st.phase) + ')';
      rep.lastFailure = g && g.lastFailure ? JSON.parse(JSON.stringify(g.lastFailure)) : null;
      return rep;
    }
    const ms = (stage) => { const m = rep.loading && rep.loading.milestones.find((x) => x.stage === stage); return m ? r1((g.loading.startedAt + m.ms) / 1000) : null; };
    rep.garageS = ms('garage') ?? ms('ready');       // boot to a usable garage (menu card up)
    rep.warmS = ms('lights') ?? ms('shaders');        // boot to every course shader / mirror / water warm
    rep.liveS = r1((performance.now() - t0) / 1000);  // first live phase seen by the probe
    rep.firstPhase = st.phase;
    mark('menu');
    start();
    if (st.phase === 'menu') {
      await sleep(1000);
      const door = document.querySelector('#card [data-act=race]');
      if (door) door.click();
    }
    for (const end = performance.now() + 60000; performance.now() < end; await sleep(100)) { st = snap(); if (st && st.phase === 'racing' && st.timeS > 1) break; }
    const cd = take();
    rep.countdown = { frames: cd.gaps.length, worstGapMs: r1(Math.max(0, ...cd.gaps)), worst250msFps: worstWindow(cd.gaps, cd.at, 250),
      worst1sFps: worstWindow(cd.gaps, cd.at, 1000), p95WorkMs: r1(pct(cd.work, 0.95)) };
    if (!st || st.phase !== 'racing') { rep.failed = 'race did not start (' + (st && st.phase) + ')'; return rep; }
    await sleep(2000);
    const live = ctxs.find((x) => x.state !== 'closed');
    const c0 = live ? { ctx: live, t: live.currentTime, perf: performance.now() } : null;
    start();
    const samples = [], cmStats = [];
    for (let i = 0; i < SECONDS; i++) {
      await sleep(1000);
      const s = snap(); if (s) samples.push(s);
      if (MEDIA) { try { const cm = G() && G().cardMedia; if (cm) cmStats.push(Object.assign({}, cm.webPictures || {}, faceDom())); } catch (e) { /* none */ } }
      if (i % 3 === 0) mark('race');
      if (s && s.phase === 'failed') break;
    }
    const m = take();
    const fpsGap = pct(m.gaps, 0.5), last = samples[samples.length - 1] || {};
    rep.race = {
      frames: m.gaps.length,
      fpsMean: m.gaps.length ? Math.round(m.gaps.length * 1000 / m.gaps.reduce((x, y) => x + y, 0)) : 0,
      fpsMedian: fpsGap ? Math.round(1000 / fpsGap) : 0,
      low1pctFps: m.gaps.length ? Math.round(1000 / pct(m.gaps, 0.99)) : 0,
      worst1sFps: worstWindow(m.gaps, m.at, 1000),
      gapP95Ms: r1(pct(m.gaps, 0.95)), worstGapMs: r1(Math.max(0, ...m.gaps)),
      hitches50: m.gaps.filter((x) => x > 50).length, hitches100: m.gaps.filter((x) => x > 100).length,
      cpuMedianMs: r1(pct(m.work, 0.5)), cpuP95Ms: r1(pct(m.work, 0.95)),
      backend: last.backend, gpu: last.gpu, tier: last.tier, renderScale: last.renderScale, phase: last.phase,
      tiersSeen: [...new Set(samples.map((x) => x.tier))], scalesSeen: [...new Set(samples.map((x) => x.renderScale))],
      drawCalls: last.drawCalls, triangles: last.triangles,
    };
    rep.audio = audio(c0);
    // the world screens showing the files (render/media.js stats().webPictures: faces shown, clips held, clips playing)
    if (MEDIA) rep.cardMedia = { maxFaces: Math.max(0, ...cmStats.map((x) => x.faces || 0)), maxClips: Math.max(0, ...cmStats.map((x) => x.clips || 0)),
      maxPlaying: Math.max(0, ...cmStats.map((x) => x.playing || 0)), samples: cmStats.length,
      medianFaces: pct(cmStats.map((x) => x.faces || 0), 0.5), medianPending: pct(cmStats.map((x) => x.pending || 0), 0.5),
      maxPending: Math.max(0, ...cmStats.map((x) => x.pending || 0)), medianStillSrcs: pct(cmStats.map((x) => x.stillSrcs || 0), 0.5),
      domSlots: Math.max(0, ...cmStats.map((x) => x.domSlots || 0)), perSecond: cmStats.map((x) => [x.faces, x.pending, x.playing, x.clips]) };
    mark('done');
    return rep;
  }

  async function main() {
    let rep;
    const mediaTask = MEDIA ? media() : null;
    try { rep = await drive(); } catch (e) { rep = { run: RUN, failed: 'driver: ' + e.message, errors }; }
    try { rep.caps = await caps(); } catch (e) { pushErr('caps: ' + e.message); }
    try { rep.hashes = await hashes(); } catch (e) { pushErr('hashes: ' + e.message); }
    if (qs.get('spikeScrolller') === 'check') { try { rep.scrolllerCheck = await scrolller(); } catch (e) { pushErr('scrolller: ' + e.message); } }
    if (MEDIA) {
      try {
        const m = await mediaTask; const files = m._files; delete m._files;
        m.blobUrlsMadeByGame = blobUrls.length; m.gameBlobTypes = blobUrls.reduce((c, f) => { c[f.type] = (c[f.type] || 0) + 1; return c; }, {});
        m.decode = await decodeCheck(files);
        rep.media = m;
      } catch (e) { pushErr('media: ' + e.message); }
    }
    rep.gamepads = pads;
    rep.endS = r1((performance.now() - t0) / 1000);
    await post('result', rep);
    document.title = 'SPIKE-DONE';
  }
  if (document.readyState === 'loading') addEventListener('DOMContentLoaded', () => void main(), { once: true });
  else void main();
})();
