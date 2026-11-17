/* host.js - the seam between the page and whoever hosts it (CONTRACT.md "Host seam").
 * Every host answers the same shape: { ready() -> init, request(op, body, idem) -> body, wallet(sp), close(reason), signIn(), wear(id), log(msg) }.
 *   createHost(win)  standalone: frames over window.chrome.webview (WPF) or postMessage to the parent (web, phone).
 *                    out { type:'ready' } { type:'request', reqId, op, body, idem } { type:'wallet', sp } { type:'close', reason } { type:'signin' }
 *                        { type:'wear', id } (an owned card's Wear; the WPF host opens Rain's dressing room for a Rain row)
 *                    in  { type:'init', ... } { type:'reply', reqId, body } { type:'worn', id, ok } (WPF, after an in-place Wear)
 *                        { type:'reset' } (WPF sheet, after a close: drop this open, mount again, say ready; sheet/reuse.js)
 *   asHost(obj)      mounted inside a game: a plain object with the same methods (missing ones are no-ops).
 * request() never rejects: a missing reply answers { ok:false, reason:'offline' }. */

const OFFLINE = { ok: false, reason: 'offline' };
const mintId = () => Array.from(crypto.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, '0')).join('');

export function createHost(win = window, { timeoutMs = 15000 } = {}) {
  const webview = win.chrome && win.chrome.webview;
  const parent = !webview && win.parent && win.parent !== win ? win.parent : null;
  const post = (m) => { try { if (webview) webview.postMessage(m); else if (parent) parent.postMessage(m, '*'); } catch (e) { /* host gone */ } };
  const waiting = new Map();
  let init = null, initWaiters = [];
  const wornHears = [], atHears = [], resetHears = [];
  const onFrame = (m) => {
    if (!m || typeof m.type !== 'string') return;
    if (m.type === 'reset') {
      // The host keeps this page for its next open (CONTRACT "Reuse"): every answer, init and listener of the open
      // that ended is dropped unanswered (its mount is gone), then the reuse controller mounts again and says ready.
      for (const done of waiting.values()) clearTimeout(done.timer);
      waiting.clear();
      init = null; initWaiters = [];
      wornHears.length = 0; atHears.length = 0;
      resetHears.slice().forEach((f) => { try { f(m); } catch (e) { /* page error stays local */ } });
      return;
    }
    if (m.type === 'wallet-at') { atHears.forEach((f) => { try { f(m); } catch (e) { /* page error stays local */ } }); return; }
    if (m.type === 'init') { init = m; initWaiters.forEach((f) => f(m)); initWaiters = []; }
    else if (m.type === 'worn') wornHears.forEach((f) => { try { f(m); } catch (e) { /* page error stays local */ } });
    else if (m.type === 'reply' && waiting.has(m.reqId)) { waiting.get(m.reqId)(m.body || OFFLINE); waiting.delete(m.reqId); }
  };
  if (webview) webview.addEventListener('message', (e) => onFrame(e.data));
  else win.addEventListener('message', (e) => { if (parent && e.source === parent) onFrame(e.data); });
  const hosted = !!(webview || parent);
  return {
    hosted,
    ready() {
      if (init) return Promise.resolve(init);
      const p = new Promise((res) => initWaiters.push(res));
      post({ type: 'ready', protocol: 1 });
      return hosted ? p : Promise.resolve({});
    },
    request(op, body = {}, idem) {
      if (!hosted) return Promise.resolve(OFFLINE);
      const reqId = mintId();
      return new Promise((res) => {
        const timer = setTimeout(() => { waiting.delete(reqId); res(OFFLINE); }, timeoutMs);
        const done = (b) => { clearTimeout(timer); res(b); };
        done.timer = timer;
        waiting.set(reqId, done);
        post({ type: 'request', reqId, op, body, idem });
      });
    },
    wallet(sp) { post({ type: 'wallet', sp }); },
    close(reason = 'back') { post({ type: 'close', reason }); },
    signIn() { post({ type: 'signin' }); },
    wear(id) { if (typeof id === 'string' && id) post({ type: 'wear', id }); },
    onWorn(f) { if (typeof f === 'function') wornHears.push(f); },
    onWalletAt(f) { if (typeof f === 'function') atHears.push(f); },
    /** The host's { type:'reset' } (sheet/reuse.js listens; a reset never clears this list). */
    onReset(f) { if (typeof f === 'function') resetHears.push(f); },
    walletLand(k) { if (Number.isFinite(k)) post({ type: 'wallet-land', k: Math.max(0, Math.min(1, k)) }); },
    /** The stars' whole paths, once per flight (sheet/fx.js handOff): the host draws them over its bar. */
    walletStars(plan) { if (plan && Array.isArray(plan.stars)) post({ type: 'wallet-stars', dir: plan.dir === 'out' ? 'out' : 'in', n: plan.n, e: plan.e, stars: plan.stars }); },
    log(msg) { post({ type: 'log', level: 'debug', msg: String(msg).slice(0, 400) }); },
  };
}

export function asHost(o = {}) {
  const call = (f, ...a) => { try { return typeof f === 'function' ? f.apply(o, a) : undefined; } catch (e) { return undefined; } };
  return {
    hosted: true,
    ready: () => Promise.resolve(typeof o.ready === 'function' ? call(o.ready) : o.init || {}),
    request: (op, body = {}, idem) => Promise.resolve().then(() => o.request(op, body, idem)).catch(() => OFFLINE).then((r) => r || OFFLINE),
    wallet: (sp) => call(o.wallet, sp),
    close: (reason = 'back') => call(o.close, reason),
    signIn: () => call(o.signIn),
    wear: (id) => call(o.wear, id),
    onWorn: (f) => call(o.onWorn, f),
    onWalletAt: (f) => call(o.onWalletAt, f),
    walletLand: (k) => call(o.walletLand, k),
    walletStars: (plan) => call(o.walletStars, plan),
    log: (msg) => call(o.log, msg),
  };
}
