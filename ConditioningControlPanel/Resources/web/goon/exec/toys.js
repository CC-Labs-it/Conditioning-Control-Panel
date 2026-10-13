/* ============================================================================
 * exec/toys.js — GoonElement.ToyPatterns (5) + GoonPayloadKind.ToyPattern (5).
 *
 * NO DOM. This renderer's whole job is to translate cues and payloads into the
 * two messages the host understands and hand them to the bridge:
 *
 *   { type:'toy-pattern', intensity, durationMs, pattern, source:'element'|'payload' }
 *   { type:'toy-stop' }
 *
 * The bridge (sibling H passes `bridge.send` in, or null) may be absent —
 * standalone in a browser there is no host, and caps.haptics is false until the
 * haptics-v2 overhaul lands. A null bridge is NOT an error: the renderer logs
 * what it would have sent and stays perfectly in step, so the executor's active
 * registry and the HUD's heat are identical with and without a toy.
 *
 * PRECEDENCE: a payload OUTRANKS the element bed while it runs (it cost the
 * opponent 2 charges), and when it ends the bed is restored — not stopped. Only
 * the last live run sends 'toy-stop', so a finished payload can never silence a
 * toy the ramp still wants running.
 *
 * Uniform renderer shape — see the banner in exec/flashes.js. (This module takes
 * one extra dependency, `toyBridge`, in the same options bag.)
 * ==========================================================================*/

const clamp01 = (n) => (typeof n === 'number' && n === n ? (n < 0 ? 0 : n > 1 ? 1 : n) : 0);
const soon = (fn, ms) => {
  const t = setTimeout(fn, Math.max(0, ms | 0));
  if (t && typeof t.unref === 'function') t.unref();
  return t;
};

/** The host plays a sustained bed as one 60 s segment, so a bed that outlives it is re-sent. */
export const BED_KEEPALIVE_MS = 45000;

/**
 * THE CONSENT SHEET'S TOY CAP, put on the cue itself. A toy cue never asks for more than the
 * cap both players signed, and it carries the cap so the host can clamp again (the page is
 * untrusted input there). `sheet` is match.consentSheet; a missing cap leaves the cue alone
 * and the host falls back to its own default.
 *
 * THE OTHER PLAYER CAN ONLY LOWER IT. `ownCap` is the figure this player last set on their own
 * toy slider (OWN_TOY_CAP_DEFAULT until they touch it). A sheet the opponent proposed with a
 * higher number, even one this player then signed, never lifts a cue past the player's own.
 */
export const OWN_TOY_CAP_DEFAULT = 0.7;

export function capToyMessage(msg, sheet, ownCap) {
  if (!msg || msg.type !== 'toy-pattern') return msg;
  const signed = sheet && typeof sheet.toy_cap === 'number' && sheet.toy_cap === sheet.toy_cap
    ? clamp01(sheet.toy_cap) : null;
  if (signed === null) return msg;
  const own = typeof ownCap === 'number' && ownCap === ownCap ? clamp01(ownCap) : OWN_TOY_CAP_DEFAULT;
  const cap = Math.min(signed, own);
  return Object.assign({}, msg, { intensity: Math.min(clamp01(msg.intensity), cap), cap });
}

export function createToys({ logger, toyBridge } = {}) {
  const log = logger || null;
  const info = (m) => { if (log && log.info) log.info(`[gg:toys] ${m}`); };
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:toys] ${m}`); };

  const send = (msg) => {
    if (typeof toyBridge !== 'function') { info(`no toy bridge — would send ${JSON.stringify(msg)}`); return false; }
    try { toyBridge(msg); return true; }
    catch (e) { warn(`toy bridge threw: ${e && e.message}`); return false; }
  };

  let element = null;        // {intensity, durationMs, pattern}
  const payloads = new Set();
  let keepalive = 0;

  function dropKeepalive() {
    if (!keepalive) return;
    try { clearInterval(keepalive); } catch (_e) { /* ignore */ }
    keepalive = 0;
  }
  /* A sustained bed (durationMs 0) is re-sent while it lasts; a payload keeps the floor. */
  function armKeepalive() {
    dropKeepalive();
    if (!element || element.durationMs !== 0) return;
    keepalive = setInterval(() => {
      if (!element) { dropKeepalive(); return; }
      if (payloads.size === 0) pushElement();
    }, BED_KEEPALIVE_MS);
    if (keepalive && typeof keepalive.unref === 'function') keepalive.unref();
  }

  function pushElement() {
    if (!element) return;
    send({
      type: 'toy-pattern',
      intensity: element.intensity,
      durationMs: element.durationMs,
      pattern: element.pattern,
      source: 'element',
    });
  }

  function releaseOne() {
    // Last one out turns the toy off; otherwise hand control back to the bed.
    if (payloads.size === 0 && !element) send({ type: 'toy-stop' });
    else if (payloads.size === 0 && element) pushElement();
  }

  return {
    name: 'toys',

    start(cue) {
      const c = cue || {};
      element = {
        intensity: clamp01(c.intensity),
        durationMs: Math.max(0, c.durationMs | 0),     // 0 = sustained until stop
        pattern: c.pattern || null,
      };
      if (payloads.size === 0) pushElement();          // a live payload keeps the floor
      armKeepalive();
    },

    setIntensity(v) {
      if (!element) return;
      element.intensity = clamp01(v);
      if (payloads.size === 0) pushElement();
    },

    stop() {
      element = null;
      dropKeepalive();
      if (payloads.size === 0) send({ type: 'toy-stop' });
    },

    /** ToyPattern payload: outranks the bed for duration_ms, then hands it back. */
    renderPayload(payload, done) {
      const p = payload || {};
      const durationMs = Math.max(1000, (p.duration_ms | 0) || 30000);
      const run = { endTimer: 0 };
      payloads.add(run);

      send({
        type: 'toy-pattern',
        intensity: clamp01(p.intensity !== undefined ? p.intensity : 0.6),
        durationMs,
        pattern: p.pattern || null,
        source: 'payload',
      });

      let finished = false;
      const settle = (endured) => {
        if (finished) return;
        finished = true;
        try { clearTimeout(run.endTimer); } catch (_e) { /* ignore */ }
        payloads.delete(run);
        releaseOne();
        if (typeof done === 'function') { try { done(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
      };
      run.endTimer = soon(() => settle(true), durationMs);
      return () => settle(false);
    },
  };
}

export default createToys;
