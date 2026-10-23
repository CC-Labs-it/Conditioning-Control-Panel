/* net/lobbyApi.js - the lobby routes (NET.md sections 12 and 13) over one of two transports (plan 3.4):
 *   fetchTransport  web and dev: a direct fetch (web: the cookie session; dev and node bots: a bearer header)
 *   hostTransport   desktop: page -> host `net { id, method, path, body }`, host -> page `net-reply { id, status, body }`
 *                   (the C# side relays /v2/race/ only and adds the account token; the page never holds it)
 * Every call answers the body on 2xx and throws an Error with `code` (the lobby's `error`) and `status` otherwise.
 * Only /v2/race/ paths ever leave. Three-free, DOM-free.
 */

const PREFIX = '/v2/race/';
/** A race link token (the lobby mints 22 base64url chars); anything else never leaves the page. */
export const LINK_TOKEN = /^[A-Za-z0-9_-]{16,64}$/;
const refuse = (code) => { const e = new Error('lobby ' + code); e.code = code; e.status = 0; return Promise.reject(e); };

export function fetchTransport({ base = '', headers = {}, fetchImpl = globalThis.fetch, credentials = 'include' } = {}) {
  return async (method, path, body) => {
    const res = await fetchImpl(base + path, {
      method, credentials, cache: 'no-store',
      headers: { ...(body !== undefined ? { 'content-type': 'application/json' } : {}), ...headers },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
    let data = null;
    try { data = await res.json(); } catch (e) { data = null; }
    return { status: res.status, body: data };
  };
}

export function hostTransport(bridge, { timeoutMs = 15000, timers = globalThis } = {}) {
  const wait = new Map();
  let next = 1;
  bridge.on('net-reply', (m) => {
    const w = wait.get(m.id);
    if (!w) return;
    wait.delete(m.id); timers.clearTimeout(w.t);
    let b = m.body;
    if (typeof b === 'string') { try { b = JSON.parse(b); } catch (e) { b = null; } }
    w.res({ status: m.status | 0, body: b });
  });
  return (method, path, body) => new Promise((res) => {
    const id = 'race' + next++;
    const t = timers.setTimeout(() => { wait.delete(id); res({ status: 0, body: { error: 'timeout' } }); }, timeoutMs);
    wait.set(id, { res, t });
    bridge.send({ type: 'net', id, method, path, body: body === undefined ? null : body });
  });
}

export function createLobbyApi(transport, { timers = globalThis } = {}) {
  async function call(method, path, body) {
    if (!path.startsWith(PREFIX)) throw new Error('net: lobby path refused');
    const { status, body: b } = await transport(method, path, body);
    if (status >= 200 && status < 300) return b || {};
    const err = new Error('lobby ' + ((b && b.error) || status));
    err.code = (b && b.error) || (status === 0 ? 'network' : 'http'); err.status = status;
    throw err;
  }
  const api = {
    open: (o) => call('POST', PREFIX + 'open', o),
    list: () => call('GET', PREFIX + 'open'),
    join: ({ roomId, look }) => call('POST', PREFIX + 'join', { roomId, look }),
    quick: ({ track, look } = {}) => call('POST', PREFIX + 'quick', track ? { track, look } : { look }),
    invite: ({ friendUid, roomId }) => call('POST', PREFIX + 'invite', { friendUid, roomId }),
    leave: ({ roomId }) => call('POST', PREFIX + 'leave', { roomId }),
    board: (track) => call('GET', PREFIX + 'board/' + encodeURIComponent(track)),
    // race a friend through a link (NET.md section 13): mint or reuse, revoke, the public lookup, claim, access
    makeLink: () => call('POST', PREFIX + 'link', {}),
    revokeLink: () => call('POST', PREFIX + 'link/revoke', {}),
    linkInfo: (token) => (LINK_TOKEN.test(token) ? call('GET', PREFIX + 'link/' + token) : refuse('gone')),
    claimLink: (token, look, platform) => (LINK_TOKEN.test(token)
      ? call('POST', PREFIX + 'link/claim', { token, ...(look ? { look } : {}), ...(platform ? { platform } : {}) }) : refuse('gone')),
    access: () => call('GET', PREFIX + 'access'),
    // race songs from our own storage (NET.md section 14): { songs: [{ id, title, chartUrl, exp }] }, signed room urls
    songs: () => call('GET', PREFIX + 'songs'),
    /** Poll quick until a seat comes (the lobby's retryMs, at least 500 ms) or maxWaitMs passes. */
    async quickMatch({ track, look, maxWaitMs = 60000, isCancelled = () => false } = {}) {
      const t0 = Date.now();
      for (;;) {
        const r = await api.quick({ track, look });
        if (!r.waiting) return r;
        if (isCancelled() || Date.now() - t0 > maxWaitMs) { const e = new Error('lobby wait'); e.code = 'wait'; throw e; }
        await new Promise((res) => timers.setTimeout(res, Math.max(500, r.retryMs | 0)));
      }
    },
  };
  return api;
}
