import { S } from './state.js';
import { wrap } from '../core/math.js';

export const CUE_KINDS = Object.freeze(['VEIL', 'MARQUEE', 'SHIFT', 'GANTRY', 'BAR', 'BURST', 'SWARM', 'PROP', 'STOMP']);
const clean = value => String(value || '').toLowerCase().replace(/[\u2018\u2019]/g, "'").replace(/[^\p{L}\p{N}'\s]/gu, '').trim();
const smooth = x => { const u = Math.max(0, Math.min(1, x)); return u * u * (3 - 2 * u); };
const frame = () => ({ p: [0, 0, 0], t: [0, 0, 1], n: [0, 1, 0], b: [1, 0, 0], w: 0 });
const local = (p, f, axis) => p.reduce((s, x, i) => s + (x - f.p[i]) * f[axis][i], 0);

/** Private anchors use one-based occurrences; unresolved or malformed anchors never invent a time. */
export function resolveCueAnchor(chart, anchor) {
  if (!anchor || !!anchor.word === !!anchor.event || !Number.isInteger(anchor.n) || anchor.n < 1) return null;
  const offset = anchor.offsetS ?? 0;
  if (!Number.isFinite(offset)) return null;
  let matches;
  if (anchor.word) {
    const wanted = clean(anchor.word).split(/\s+/), words = chart?.words || [];
    matches = words.filter((_, i) => wanted.length && wanted.every((w, j) => w && clean(words[i + j]?.w) === w));
  } else matches = (chart?.events || []).filter(e => e.event === anchor.event);
  const at = matches[anchor.n - 1]?.startS;
  return Number.isFinite(at) && at + offset >= 0 ? at + offset : null;
}

/** Schema v1: id, kind, mode, anchor, optional endAnchor/durationS/leadS, params and jolt anchors.
 * Only generic kinds and parameters ship here. Charts and their timing sources remain private. */
export function songCues(chart) {
  const result = [], seen = new Set();
  for (const cue of Array.isArray(chart?.cues) ? chart.cues : []) {
    if (!cue || typeof cue.id !== 'string' || !cue.id || seen.has(cue.id) || !CUE_KINDS.includes(cue.kind) || !['CROSS', 'WATCH'].includes(cue.mode)) continue;
    const startS = resolveCueAnchor(chart, cue.anchor);
    const duration = cue.durationS ?? 4;
    const endS = cue.endAnchor ? resolveCueAnchor(chart, cue.endAnchor) : startS + duration;
    if (startS === null || endS === null || !Number.isFinite(endS) || endS < startS || startS >= chart.durationS) continue;
    seen.add(cue.id);
    result.push({ id: cue.id, kind: cue.kind, mode: cue.mode, startS, endS,
      leadS: Number.isFinite(cue.leadS) ? Math.max(.35, Math.min(12, cue.leadS)) : 4,
      params: cue.params && typeof cue.params === 'object' ? { ...cue.params } : {},
      joltTimes: (Array.isArray(cue.jolts) ? cue.jolts : []).map(a => resolveCueAnchor(chart, a)).filter(t => t !== null && t >= startS && t <= endS).sort((a,b) => a-b) });
  }
  return result.sort((a, b) => a.startS - b.startS);
}

/** Cosmetic player-local transport. Never writes kart state, authority clocks, or song playback. */
export function createSongCueRun(chart, track) {
  const cues = songCues(chart), rows = cues.map(c => ({ ...c, frame: frame(), state: 'wait', alpha: 0 }));
  const live = [], fired = [], f = frame(), result = { live, fired, revision: 0 };
  let lastT = null, previous = null, disposed = false;
  function reset() {
    for (const r of rows) { r.state = 'wait'; r.alpha = 0; r.locked = false; r.hitS = null; r.missS = null; }
    lastT = null; previous = null; live.length = fired.length = 0; result.revision++;
  }
  reset();
  function place(row, st, ahead) {
    const fork = track.forkAtSeg?.(st[S.seg]);
    row.approachForkId = fork?.id || null;
    if (fork && fork.localS(st[S.s]) + ahead <= fork.lengthM) {
      row.s = fork.mainS(fork.localS(st[S.s]) + ahead);
      fork.frameAt(fork.localS(row.s), row.frame); row.forkId = fork.id;
    } else {
      row.s = wrap(fork ? fork.toS + ahead - (fork.lengthM - fork.localS(st[S.s])) : st[S.s] + ahead, track.lengthM);
      track.frameAt(row.s, row.frame); row.forkId = null;
    }
    return row.frame.w > 0 && row.frame.p.every(Number.isFinite);
  }
  function update(tS, st, { paused = false, gentle = false } = {}) {
    fired.length = 0;
    if (disposed) return result;
    if (paused) return result;
    if (!Number.isFinite(tS)) { if (lastT !== null) reset(); return result; }
    if (lastT !== null && (tS < lastT || tS - lastT > .5)) reset();
    if (tS === lastT) return result;
    const prevT = lastT, prev = previous;
    const p = [st[S.posX], st[S.posY], st[S.posZ]];
    const teleported = !!prev && Math.hypot(...p.map((v, i) => v - prev[i])) > 20;
    previous = p; lastT = tS; live.length = 0;
    const tangent = (track.pathFrameAt ? track.pathFrameAt(st[S.s], st[S.seg], f) : track.frameAt(st[S.s], f)).t;
    const speed = st[S.velX] * tangent[0] + st[S.velY] * tangent[1] + st[S.velZ] * tangent[2];
    const invalid = st[S.respawnPhase] || teleported || speed < 2;
    const forkId = track.forkAtSeg?.(st[S.seg])?.id || null;
    const rise = Math.max(.3, typeof gentle === 'object' ? gentle.rise(.35) : gentle ? .6 : .35);
    for (const row of rows) {
      const calm = typeof gentle === 'object' ? gentle.on : gentle;
      const baseFade = row.mode === 'WATCH' ? (calm ? 2.4 : 1.6) : .6;
      const fade = typeof gentle === 'object' ? gentle.fade(baseFade) : calm ? Math.max(1, baseFade) : baseFade;
      const remain = row.startS - tS;
      if (row.state === 'done' || remain > row.leadS) continue;
      if (row.state === 'wait') {
        // Reconstruct a held WATCH after a seek without replaying its impact or burst.
        if (prevT === null && row.mode === 'WATCH' && remain <= 0 && tS < row.endS && !invalid) {
          row.state = 'hit'; row.hitS = row.startS; row.bornS = tS;
        } else {
          if (remain < .25) { row.state = 'done'; continue; }
          if (invalid) continue;
          row.state = 'live'; row.bornS = tS;
        }
      }
      const completingFork = row.forkId === null && forkId === row.approachForkId;
      if (invalid || (row.locked && row.forkId !== forkId && !completingFork)) {
        if (row.missS === null) { row.missS = tS; row.missAlpha = row.alpha; row.state = 'miss'; }
      }
      if (row.state !== 'miss' && !row.locked) {
        // WATCH rigs remain ahead during their hold. CROSS planes stop tracking before impact.
        if (!place(row, st, speed * (Math.max(0, remain) + (row.mode === 'WATCH' ? 1.5 : 0)))) {
          row.state = 'done'; continue;
        }
        row.locked = row.mode === 'CROSS' && remain <= .25;
      }
      if (row.state === 'live' && prev && !invalid) {
        let hit = row.mode === 'WATCH' && prevT < row.startS && tS >= row.startS;
        if (row.mode === 'CROSS' && row.locked && Math.abs(remain) <= .35) {
          const a = local(prev, row.frame, 't'), b = local(p, row.frame, 't');
          if (a < 0 && b >= 0 && b > a) {
            const u = -a / (b - a), at = p.map((v, i) => prev[i] + (v - prev[i]) * u);
            hit = Math.abs(local(at, row.frame, 'b')) <= row.frame.w / 2 + 1 && local(at, row.frame, 'n') >= -.6 && local(at, row.frame, 'n') <= 6;
          }
        }
        if (hit) { row.state = 'hit'; row.hitS = tS; fired.push(row); }
      }
      const outS = row.mode === 'CROSS' ? (row.hitS ?? row.startS + .35) : row.endS;
      row.alpha = row.state === 'miss' ? row.missAlpha * (1 - smooth((tS - row.missS) / fade))
        : smooth((tS - row.bornS) / rise) * (1 - smooth((tS - outS) / fade));
      if (tS >= (row.missS ?? outS) + fade) { row.state = 'done'; continue; }
      live.push(row);
    }
    return result;
  }
  return { update, reset, cues, get live() { return live; }, get revision() { return result.revision; },
    dispose() { reset(); disposed = true; } };
}
