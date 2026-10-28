import { BOOST_TURN } from './boostSafety.js';
import { makeFrame } from './frames.js';
import { makeQuery } from './query.js';

const BLOCKERS = new WeakMap();
function blockers(track) {
  let ranges = BLOCKERS.get(track);
  if (!ranges) {
    ranges = [
      ...(track.fields || []).map(f => [f.fromS, f.toS]),
      ...(track.courts || []).map(c => [c.fromS, c.toS]),
      ...(track.aerials || []).map(a => [a.gapS0, a.gapS1]),
      ...(track.jumps || []).map(j => [j.s, j.landS]),
      ...(track.routes || []).map(r => [r.openS0, r.openS1]),
    ];
    BLOCKERS.set(track, ranges);
  }
  return ranges;
}
const wrap = (s,L) => (s % L + L) % L;

/** A pad's complete painted lane and carry must remain a supported, guarded road. */
export function safeBoostCorridor(track, pad) {
  const L = track.lengthM, s = pad.s ?? track.nodeS[pad.node];
  const start = s - 4, end = s + pad.len + BOOST_TURN.rolloutM;
  // Reject known void/open districts before probing geometry. Intervals include the lap seam.
  for (const [a,b0] of blockers(track)) {
    const b = b0 < a ? b0 + L : b0;
    for (const shift of [-L,0,L]) if (start <= b+shift && end >= a+shift) return false;
  }
  const half = Math.max(0, (pad.w || 0)/2), x = pad.x || 0;
  const lanes = half ? [x-half,x,x+half] : [x];
  const frame = makeFrame(), surface = makeFrame(), q = makeQuery();
  for (let d = -4; ; d = Math.min(pad.len+BOOST_TURN.rolloutM, d+4)) {
    const at = wrap(s+d,L);
    track.frameAt(at,frame);
    const flags = frame.flags;
    if (flags.includes('jump') || flags.includes('magnet') || flags.includes('open')
      || Math.abs(frame.t[1]) > .3 || frame.w/2 < Math.abs(x)+half+2) return false;
    // Partial authored flags can still have a physically restored rail outside a short route mouth.
    if (!flags.includes('rail') && !(track.routes || []).some(r => frame.seg >= r.from && frame.seg < r.to
      && (at < r.openS0 || at > r.openS1) && flags.includes(r.side==='L'?'railR':'railL'))) return false;
    for (const lane of lanes) {
      track.surfaceAt(at,lane,surface);
      track.query(...surface.p,surface.seg,q);
      const ds = Math.abs(q.s-at);
      if (!q.onRoad || !q.railed || Math.min(ds,L-ds) > .5 || Math.abs(q.height) > .2
        || q.normal.reduce((sum,v,k) => sum+v*surface.n[k],0) < .98) return false;
    }
    if (d === pad.len+BOOST_TURN.rolloutM) break;
  }
  return true;
}
