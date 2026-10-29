import { createSpline } from './spline.js';

/** Preserve authored water stations by their source node interval and fractional distance. */
export function mapSpillStations(json, runtimeNodeS, rows = json.waterSpills) {
  if (!Array.isArray(rows) || !rows.length) return rows;
  // Generators place these rows before aerial shaping, on the original authored spline.
  const source = createSpline(json.nodes.map(node => node.p), json.nodes.map(node => node.flags.includes('jump')));
  const sourceS = source.nodeS, sourceLength = source.lengthM, runtimeLength = runtimeNodeS.at(-1);
  const station = s => {
    if (!Number.isFinite(s)) return s;
    const lap = Math.floor(s / sourceLength), at = s - lap * sourceLength;
    let lo = 0, hi = sourceS.length - 2;
    while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (sourceS[mid] <= at) lo = mid; else hi = mid - 1; }
    const fraction = (at - sourceS[lo]) / Math.max(1e-9, sourceS[lo + 1] - sourceS[lo]);
    return lap * runtimeLength + runtimeNodeS[lo] + fraction * (runtimeNodeS[lo + 1] - runtimeNodeS[lo]);
  };
  return rows.map(row => ({ ...row, fromS: station(row.fromS), toS: station(row.toS) }));
}
