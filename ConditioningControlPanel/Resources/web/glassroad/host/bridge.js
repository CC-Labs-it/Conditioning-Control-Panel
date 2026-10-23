/* host/bridge.js - the page side of host protocol v2 (plan 3.4).
 *
 * Hosted (WebView2, window.chrome.webview): the host queues everything until the page
 * says ready, and the page buffers any message that lands before its handler, so neither
 * side races the other's boot. The bridge answers ping with pong and sends heartbeat
 * every 2 s itself. Unhosted (a plain browser): init resolves at once with the browser
 * defaults (?gentle=1, ?renderer=webgl2|webgpu, coarse pointer = touch) so the page runs
 * standalone. Escape never sends exit: it only pauses (host/hostEscape.js).
 */

export const PROTOCOL = 2;
const HEARTBEAT_MS = 2000;
const INIT_WAIT_MS = 3000;

/** The browser defaults of CONTRACT section 7, read from a location search string. */
export function defaultInit({ search = '', coarse = false } = {}) {
  const q = new URLSearchParams(search);
  const r = q.get('renderer');
  return {
    v: 2,
    gentle: { on: q.get('gentle') === '1', fadeMs: 600, riseMs: 300, speed: 0.5 },
    caps: {
      platform: 'web', net: false, relayBase: null, texturableRemote: false, gamepad: true, touch: !!coarse,
      forceRenderer: r === 'webgl2' || r === 'webgpu' ? r : null,
      hostSfx: false, trackPick: false, canSurface: false, mediaControls: false, songs: [],
    },
    identity: null,
  };
}

/** A host song chart url: same origin, a plain path to a .json file (CONTRACT section 7, caps.songs). */
export const SONG_URL = /^\/[A-Za-z0-9/_.-]+\.json$/;
const DEV_SONGS = Object.freeze(['/private-audio/pink-road/chart.json', '/private-audio/pink-road/chart.v0.json']);

/**
 * The race song charts to try, in order: `?chart=0` none, `?chart=<url>` that one, the dev host (8790) its
 * private list, else the host's `caps.songs` (each checked against SONG_URL, at most 8), else none.
 */
export function songCharts({ search = '', devHost = false, songs = null } = {}) {
  const q = new URLSearchParams(search);
  const c = q.get('chart');
  if (c === '0') return [];
  if (c) return [c];
  if (devHost) return [...DEV_SONGS];
  if (!Array.isArray(songs)) return [];
  return songs.filter((u) => typeof u === 'string' && !u.includes('..') && !u.startsWith('//') && SONG_URL.test(u)).slice(0, 8);
}

/** A host init laid over the defaults (missing fields keep their default). */
export function mergeInit(base, m = {}) {
  return {
    v: 2,
    gentle: { ...base.gentle, ...(m.gentle || {}) },
    caps: { ...base.caps, ...(m.caps || {}) },
    identity: m.identity || null,
  };
}

export function createBridge({ win = globalThis.window } = {}) {
  const webview = win?.chrome?.webview || null;
  const handlers = new Map();   // type -> Set(fn)
  const early = [];             // messages that came before any handler of their type
  const base = defaultInit({
    search: win?.location?.search || '',
    coarse: !!win?.matchMedia?.('(pointer: coarse)')?.matches,
  });
  let resolveInit, beat = 0, waitT = 0;
  const init = new Promise((res) => { resolveInit = res; });

  function send(msg) {
    try { webview?.postMessage(msg); } catch (e) { /* host gone */ }
  }

  function dispatch(m) {
    if (!m || typeof m.type !== 'string') return;
    if (m.type === 'ping') { send({ type: 'pong', t: m.t }); return; }
    if (m.type === 'init') { if (waitT) win.clearTimeout(waitT); resolveInit(mergeInit(base, m)); }
    const set = handlers.get(m.type);
    if (set && set.size) for (const fn of [...set]) { try { fn(m); } catch (e) { console.warn('[bridge]', m.type, e); } }
    else if (m.type !== 'init') early.push(m);
  }

  function on(type, fn) {
    let set = handlers.get(type);
    if (!set) handlers.set(type, (set = new Set()));
    set.add(fn);
    for (let i = 0; i < early.length; i++) {
      if (early[i].type === type) { const m = early.splice(i--, 1)[0]; try { fn(m); } catch (e) { console.warn('[bridge]', type, e); } }
    }
    return () => set.delete(fn);
  }

  function log(level, msg) {
    if (msg === undefined) { msg = level; level = 'debug'; }
    if (webview) send({ type: 'log', level: String(level), msg: String(msg).slice(0, 400) });
    else console.log('[glassroad]', level, msg);
  }

  if (webview) {
    webview.addEventListener('message', (e) => {
      let m = e.data;
      if (typeof m === 'string') { try { m = JSON.parse(m); } catch (err) { return; } }
      dispatch(m);
    });
    beat = win.setInterval(() => send({ type: 'heartbeat', t: Date.now() }), HEARTBEAT_MS);
    waitT = win.setTimeout(() => { log('warn', 'no init from host, browser defaults'); resolveInit(base); }, INIT_WAIT_MS);
  } else {
    resolveInit(base);
  }

  return {
    hosted: !!webview, protocol: PROTOCOL, init, on, send, log,
    ready() { send({ type: 'ready', protocol: PROTOCOL }); },
    runStarted(data = {}) { send({ type: 'run-started', ...data }); },
    runEnded({ timeS, laps } = {}) { send({ type: 'run-ended', timeS, laps }); },
    telemetry(data = {}) { send({ type: 'telemetry', ...data }); },
    dispose() { if (beat) win.clearInterval(beat); if (waitT) win.clearTimeout(waitT); handlers.clear(); },
  };
}
