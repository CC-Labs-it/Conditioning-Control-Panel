/* sheet/reuse.js - one loaded page, many opens (CONTRACT.md "Reuse"). The WPF sheet keeps its page between opens
 * instead of loading it again (a load is about 2 s in WebView2): after a close it sends { type:'reset' }, the host
 * seam (host.js) drops every answer, init and listener of the open that ended, and this controller disposes that
 * mount and mounts again. The new mount says ready and waits, quiet and empty, for the next open's init.
 * A mount that never met its init when the reset came is simply abandoned (its ready() never answers). */

import { mountSheet } from './main.js';

export function runSheet(host, opts = {}, mount = mountSheet) {
  let drop = null, turn = 0;
  const start = () => {
    const mine = ++turn;
    drop = null;
    const p = Promise.resolve(mount(host, { ...opts, bind: (d) => { if (mine === turn) drop = d; else d(); } }));
    p.catch((e) => { try { host.log(`sheet mount failed: ${e && e.message}`); } catch (x) { /* host gone */ } });
    return p;
  };
  if (typeof host.onReset === 'function') host.onReset(() => { const d = drop; drop = null; if (d) d(); start(); });
  return { start, get mounts() { return turn; } };
}
