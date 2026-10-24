/* Complete local cached clips only. Their blob copies are revoked at source-off, expiry,
 * replacement and disposal. Hosts may supply the same manifest through card-media messages.
 */
import { urlAllowed } from '../render/mediaMath.js';
import { selectScrolllerSlots } from '../render/mediaSelection.js';
import { MAX_MEDIA_CLIPS } from '../render/mediaVariety.js';
import { bindCardMediaActivation } from './cardMediaActivation.js';
export function validCardSource(src, origin, now = Date.now()) {
  return src?.kind === 'video' && src.credit === 'scrolller' && typeof src.url === 'string'
    && urlAllowed(src.url, origin) && Number.isFinite(src.expiresAt) && src.expiresAt > now
    && src.expiresAt <= now + 86400000;
}
export function createCardMedia(media, { bridge, origin = globalThis.location?.origin || '',
  makeVideo = url => Object.assign(document.createElement('video'), { muted: true, loop: true, playsInline: true, src: url }),
} = {}) {
  const slots = selectScrolllerSlots(media?.slots || [], { share: media?.scrolllerShare }), entries = new Map();
  const shown = slots.map(() => null);
  let order = [], disposed = false, authority = 0, refreshTimer = 0, retryTimer = 0, expiryTimer = 0;
  let manifestController = null;
  const live = e => !disposed && entries.get(e.key) === e && validCardSource(e.source, origin);

  function syncSlots() {
    const available = order.map(key => entries.get(key)).filter(e => e?.el && live(e));
    const healthy = new Set(available.map(e => e.el));
    const next = slots.map((slot, i) => {
      const desired = order.length ? entries.get(order[i % order.length]) : null;
      if (desired?.el && live(desired)) return desired.el;
      // A late peer replaces only its intended panels, never every healthy fallback.
      if (healthy.has(shown[i])) return shown[i];
      return available.length ? available[i % available.length].el : null;
    });
    // Release every changing borrower first: a full GPU pool cannot admit a new owner
    // while any old reference remains. Unchanged borrowers keep their texture and playback.
    slots.forEach((slot, i) => {
      if (shown[i] && shown[i] !== next[i]) { media.setSource(slot.id, null); shown[i] = null; }
    });
    slots.forEach((slot, i) => {
      if (shown[i] === next[i]) return;
      const el = next[i];
      if (media.setSource(slot.id, { kind: 'video', el, borrowed: true, credit: 'scrolller' }) !== false) shown[i] = el;
    });
  }
  function release(e) {
    e.controller?.abort();
    if (e.el) {
      try { e.el.pause(); e.el.removeAttribute('src'); e.el.load(); } catch { /* already detached */ }
      e.el = null;
    }
    if (e.blob) { URL.revokeObjectURL(e.blob); e.blob = null; }
  }
  function prune() {
    const expired = [];
    for (const [key, e] of entries) if (!validCardSource(e.source, origin)) { entries.delete(key); expired.push(e); }
    order = order.filter(key => entries.has(key));
    syncSlots(); expired.forEach(release);
  }
  function schedule() {
    clearTimeout(retryTimer); clearTimeout(expiryTimer);
    if (disposed) return;
    let retry = Infinity, expiry = Infinity;
    for (const e of entries.values()) {
      expiry = Math.min(expiry, e.source.expiresAt);
      if (!e.el && !e.promise && e.retryAt) retry = Math.min(retry, e.retryAt);
    }
    if (expiry < Infinity) expiryTimer = setTimeout(() => { prune(); schedule(); }, Math.max(1, expiry - Date.now()));
    if (retry < Infinity) retryTimer = setTimeout(() => {
      prune();
      for (const e of entries.values()) load(e);
      schedule();
    }, Math.max(1, retry - Date.now()));
  }
  function load(e) {
    if (!live(e) || e.el || Date.now() < e.retryAt) return Promise.resolve();
    if (e.promise) return e.promise;
    const controller = new AbortController(); e.controller = controller;
    const timeout = setTimeout(() => controller.abort(), 15000);
    e.promise = Promise.resolve().then(async () => {
      let blob = null;
      try {
        const r = await fetch(e.source.url, { cache: 'no-store', signal: controller.signal });
        if (!live(e) || controller.signal.aborted) return;
        if (!r.ok || !/^video\//.test(r.headers.get('content-type') || '')) throw Error('unavailable clip');
        const data = await r.blob();
        if (!live(e) || controller.signal.aborted) return;
        if (!data.size || data.size > 24 * 1024 * 1024) throw Error('invalid clip size');
        blob = URL.createObjectURL(data);
        const el = makeVideo(blob);
        e.el = el; e.blob = blob; blob = null; e.failures = 0; e.retryAt = 0;
        // Every source is checked again here. A peer may have expired during this download.
        prune();
      } catch {
        if (live(e)) {
          e.failures++;
          e.retryAt = Date.now() + Math.min(30000, 1000 * 2 ** Math.min(5, e.failures - 1));
        }
      } finally {
        if (blob) URL.revokeObjectURL(blob);
        clearTimeout(timeout); e.promise = null; e.controller = null;
        schedule();
      }
    });
    return e.promise;
  }
  async function reconcile(manifest) {
    if (disposed) return;
    const sources = manifest?.enabled === true && Array.isArray(manifest.sources) ? manifest.sources : [];
    const wanted = new Map();
    const capacity = media?.videoCapacity ?? MAX_MEDIA_CLIPS;
    const limit = Math.min(MAX_MEDIA_CLIPS, slots.length, Number.isFinite(capacity) ? Math.max(0, Math.floor(capacity)) : MAX_MEDIA_CLIPS);
    for (const s of sources) {
      if (wanted.size >= limit) break;
      if (!validCardSource(s, origin)) continue;
      const key = JSON.stringify([s.url, s.expiresAt]);
      if (!wanted.has(key)) wanted.set(key, { ...s });
    }
    if (!slots.length) wanted.clear();
    const removed = [];
    for (const [key, e] of entries) if (!wanted.has(key)) { entries.delete(key); removed.push(e); }
    for (const [key, source] of wanted) if (!entries.has(key)) entries.set(key,
      { key, source, el: null, blob: null, controller: null, promise: null, retryAt: 0, failures: 0 });
    order = [...wanted.keys()];
    syncSlots(); removed.forEach(release); schedule();
    // Healthy owners and in-flight owners are retained. Polling cannot restart their playback.
    await Promise.all(order.map(key => load(entries.get(key))));
  }
  async function apply(manifest) {
    if (disposed) return;
    authority++;
    manifestController?.abort();
    await reconcile(manifest);
  }
  const off = bridge?.on('card-media', apply);
  async function refresh() {
    if (disposed) return;
    const mine = authority, controller = new AbortController(); manifestController = controller;
    const timeout = setTimeout(() => controller.abort(), 10000);
    try {
      const r = await fetch('/__card-media/manifest.json', { cache: 'no-store', signal: controller.signal });
      if (r.ok && !disposed && !controller.signal.aborted && mine === authority) {
        const manifest = await r.json();
        if (!disposed && !controller.signal.aborted && mine === authority) await reconcile(manifest);
      }
    } catch { prune(); }
    finally {
      clearTimeout(timeout);
      if (manifestController === controller) manifestController = null;
      if (!disposed) refreshTimer = setTimeout(refresh, 1000);
    }
  }
  const visible = () => { prune(); schedule(); };
  globalThis.document?.addEventListener?.('visibilitychange', visible);
  // Only the dev adapter uses this endpoint; hosted feeds arrive on the host bridge.
  const local = ['127.0.0.1', 'localhost'].includes(globalThis.location?.hostname);
  const stopActivation = local ? bindCardMediaActivation(async () => {
    const response = await fetch('/__card-media/start', { method: 'POST', cache: 'no-store', signal: AbortSignal.timeout(10000) });
    if (!response.ok) throw Error('Card media unavailable');
  }) : null;
  const ready = local ? refresh() : Promise.resolve();
  return { apply, ready, dispose() {
    if (disposed) return;
    disposed = true; authority++; manifestController?.abort();
    clearTimeout(refreshTimer); clearTimeout(retryTimer); clearTimeout(expiryTimer);
    off?.(); stopActivation?.(); globalThis.document?.removeEventListener?.('visibilitychange', visible);
    const removed = [...entries.values()]; entries.clear(); order = [];
    syncSlots(); removed.forEach(release);
  } };
}
