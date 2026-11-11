/* rt-linux-spike/spike.js - injected FIRST into Racing Thoughts' index.html by bench-server.mjs (classic script, runs
 * before any module). Measures one run in whatever engine opened the page and POSTs the record to the bench server.
 * URL params it reads: spikeRun (id), spikeSec (race seconds, default 45), spikeOpts (options record JSON written to
 * localStorage before boot), spikeScrolller=check (fetch test of the Scrolller block). The page's own params
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

  try { const o = qs.get('spikeOpts'); if (o) localStorage.setItem('glassroad.options.v1', o); } catch (e) { pushErr('options: ' + e); }

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
    const samples = [];
    for (let i = 0; i < SECONDS; i++) {
      await sleep(1000);
      const s = snap(); if (s) samples.push(s);
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
    mark('done');
    return rep;
  }

  async function main() {
    let rep;
    try { rep = await drive(); } catch (e) { rep = { run: RUN, failed: 'driver: ' + e.message, errors }; }
    try { rep.caps = await caps(); } catch (e) { pushErr('caps: ' + e.message); }
    try { rep.hashes = await hashes(); } catch (e) { pushErr('hashes: ' + e.message); }
    if (qs.get('spikeScrolller') === 'check') { try { rep.scrolllerCheck = await scrolller(); } catch (e) { pushErr('scrolller: ' + e.message); } }
    rep.gamepads = pads;
    rep.endS = r1((performance.now() - t0) / 1000);
    await post('result', rep);
    document.title = 'SPIKE-DONE';
  }
  if (document.readyState === 'loading') addEventListener('DOMContentLoaded', () => void main(), { once: true });
  else void main();
})();
