/* net/socket.js - one room WebSocket (NET.md sections 3 and 4). Text frames are checked against the room ->
 * client table, binary frames decoded as batches; anything malformed is dropped and counted. No reconnect here:
 * net/room.js owns the retry (a new socket needs a fresh ticket from the lobby).
 * The WebSocket class is injected (browser global, node 22+ global, or a test fake).
 */
import { decodeFrame, decodeMsg, encodeMsg, encodeFrame, BIN } from './protocol.js';

/** Close codes after which a reconnect is pointless (NET.md section 4). */
export const FINAL_CLOSE = Object.freeze([4000, 4003, 4004, 4009, 4010]);

export function openSocket(url, { WS = globalThis.WebSocket, onOpen, onMsg, onBatch, onClose } = {}) {
  if (typeof WS !== 'function') throw new Error('net: no WebSocket');
  const ws = new WS(url);
  ws.binaryType = 'arraybuffer';
  let open = false, closed = false, dropped = 0;
  ws.onopen = () => { open = true; if (onOpen) onOpen(); };
  ws.onmessage = (e) => {
    const d = e.data;
    if (typeof d === 'string') {
      const m = decodeMsg(d, 'r2c');
      if (m) { if (onMsg) onMsg(m); } else dropped++;
      return;
    }
    const f = decodeFrame(d);
    if (f && f.type === BIN.BATCH) { if (onBatch) onBatch(f); } else dropped++;
  };
  ws.onclose = (e) => {
    if (closed) return;
    closed = true; open = false;
    if (onClose) onClose({ code: (e && e.code) || 1006, reason: (e && e.reason) || '' });
  };
  ws.onerror = () => {};
  return {
    /** Send a client -> room message object. @returns false when not open */
    send(m) { if (!open) return false; ws.send(encodeMsg(m, 'c2r')); return true; },
    /** Send an UPLOAD frame of wire karts. */
    upload(ms, karts) { if (!open) return false; ws.send(encodeFrame(BIN.UPLOAD, ms, karts)); return true; },
    close(code = 1000, reason = '') { if (closed) return; try { ws.close(code, reason); } catch (e) { /* gone */ } },
    get open() { return open; }, get closed() { return closed; }, get dropped() { return dropped; }, ws,
  };
}
