// dev/hitchMath.mjs - pure helpers for dev/hitch-check.mjs (tested in tests/dev-hitch.test.mjs).
// A row is one animation frame from the page probe: { at, gap, work, phase, lap, s, song, tier, scale, heap,
// gl: { link, linkN, up, upN, upBytes, draw, drawN, big: [] }, h: { sim, fx, audio, cam, world, render, hud, steps } }.

const r1 = (x) => Math.round(x * 10) / 10;

/** Nearest-rank percentile of a numeric array (0 when empty). */
export function pct(a, p) {
  if (!a.length) return 0;
  const s = [...a].sort((x, y) => x - y);
  return s[Math.min(s.length - 1, Math.max(0, Math.ceil(s.length * p) - 1))];
}

/** p50/p95/p99/max frame gap, frames over 25 and 50 ms, and the work p95, over rows. */
export function summarize(rows) {
  const g = rows.map((r) => r.gap), w = rows.map((r) => r.work);
  return {
    frames: rows.length, p50: r1(pct(g, 0.5)), p95: r1(pct(g, 0.95)), p99: r1(pct(g, 0.99)), max: r1(g.length ? Math.max(...g) : 0),
    over25: g.filter((x) => x > 25).length, over50: g.filter((x) => x > 50).length,
    workP50: r1(pct(w, 0.5)), workP95: r1(pct(w, 0.95)), workMax: r1(w.length ? Math.max(...w) : 0),
  };
}

/** Rows whose gap is over ms, with the row before (for tier and heap deltas). */
export function spikesOf(rows, ms = 25) {
  const out = [];
  for (let i = 0; i < rows.length; i++) if (rows[i].gap > ms) out.push({ row: rows[i], prev: i > 0 ? rows[i - 1] : null, t0: rows[0].at });
  return out;
}

/** The biggest section of the frame (boot.js ?hitch=1 timers), as [name, ms] or null. */
export function topSection(h) {
  if (!h) return null;
  let best = null;
  for (const k of ['sim', 'fx', 'audio', 'cam', 'world', 'render', 'hud']) if (h[k] > 0 && (!best || h[k] > best[1])) best = [k, h[k]];
  return best;
}

/**
 * Name the likely cause of one spike. Order: a tier or scale step, a program link, an upload, a GC (heap
 * drop over 1 MB), the biggest JS section when the work fills most of the gap, else 'outside' (the time
 * went outside the rAF callbacks: compositor, GPU wait, a GC with no heap reading, other tasks).
 */
export function attribute({ row, prev, t0 }) {
  const gl = row.gl || {}, h = row.h || null;
  const heapDrop = prev && prev.heap && row.heap ? (prev.heap - row.heap) / 1048576 : 0;
  const step = prev && (prev.tier !== row.tier || prev.scale !== row.scale) ? (prev.tier + '->' + row.tier + ' ' + prev.scale + '->' + row.scale) : '';
  const sec = topSection(h);
  let cause = 'outside';
  if (step && prev.tier !== row.tier) cause = 'tier-step';
  else if ((gl.link || 0) > 5 || (gl.linkN || 0) > 0 && (gl.link || 0) > row.gap * 0.3) cause = 'link';
  else if ((gl.up || 0) > 5) cause = 'upload';
  else if (heapDrop > 1) cause = 'gc';
  else if ((gl.draw || 0) > row.gap * 0.5) cause = 'gl-draw';
  else if (row.work > row.gap * 0.6 && sec) cause = 'js-' + sec[0];
  const at = r1((row.at - t0) / 1000);
  const secs = h ? ['sim', 'fx', 'audio', 'cam', 'world', 'render', 'hud'].map((k) => k + ' ' + r1(h[k] || 0)).join(' ') + ' steps ' + h.steps : 'no sections';
  const line = at + 's ' + r1(row.gap) + 'ms work ' + r1(row.work) + ' ' + cause + ' | ' + row.phase + ' lap ' + row.lap + ' s ' + Math.round(row.s || 0) +
    (row.song !== null && row.song !== undefined ? ' song ' + row.song : '') + (row.mr ? ' road ' + row.mr.join('/') : '') + ' tier ' + row.tier + ' x' + row.scale +
    ' | ' + secs + ' | gl link ' + r1(gl.link || 0) + '/' + (gl.linkN || 0) + ' up ' + r1(gl.up || 0) + '/' + (gl.upN || 0) + ' ' + Math.round((gl.upBytes || 0) / 1024) + 'k draw ' + r1(gl.draw || 0) + '/' + (gl.drawN || 0) +
    (heapDrop > 0.25 ? ' | heap -' + r1(heapDrop) + 'MB' : '') + (step ? ' | step ' + step : '') + (gl.big && gl.big.length ? ' | ' + gl.big.join(', ') : '');
  return { at, gap: r1(row.gap), work: r1(row.work), cause, phase: row.phase, lap: row.lap, s: Math.round(row.s || 0), song: row.song, line };
}
