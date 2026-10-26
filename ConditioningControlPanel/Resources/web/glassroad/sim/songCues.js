import { S } from './state.js';
import { wrap } from '../core/math.js';
import { fitMarquee, DROP_S } from './marqueeView.js';

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
  // A re-dropping rig drops a fresh, fixed screen on each of its beats instead of one rig riding along.
  for (const cue of result.splice(0)) {
    if (!cue.params.redrop) { result.push(cue); continue; }
    for (const [i, at] of [cue.startS, ...cue.joltTimes].entries())
      result.push({ ...cue, id: i ? cue.id + '~' + i : cue.id, startS: at, joltTimes: [], params: { ...cue.params } });
  }
  return result.sort((a, b) => a.startS - b.startS);
}

/** Kinds that live in the sky or on the whole course, never at one road spot. */
export const SKY_KINDS = Object.freeze(['SHIFT', 'BAR']);
const zoned = row => SKY_KINDS.includes(row.kind) || !!row.params.zone;
const SPEED_MIN = 12, PASS_M = 30;
// Hanging screens need enough approach room to fit the whole face above the driving view.
export function watchStandoff(row, speed) {
  if (row.kind !== 'MARQUEE') return speed * (row.params.standoffS ?? 1.5);
  const requested = Number.isFinite(row.params.standoffS) ? row.params.standoffS : 3;
  return Math.max(60, speed * Math.max(3, Math.min(8, requested)));
}

/** Cosmetic player-local transport. Never writes kart state, authority clocks, or song playback.
 * CROSS planes track the predicted crossing and lock 0.25 s early. WATCH pieces are placed once, at a fixed
 * road spot the player reaches standoffS after the cue, and stay there: the player drives up to and past them.
 * Sky and zone pieces have no spot; their renderers stream world dressing from the player's position. */
export function createSongCueRun(chart, track) {
  const cues = songCues(chart), rows = cues.map(c => ({ ...c, frame: frame(), state: 'wait', alpha: 0 }));
  const live = [], fired = [], f = frame(), result = { live, fired, revision: 0, playerS: 0, speed: 0 };
  let lastT = null, previous = null, disposed = false;
  function reset() {
    for (const r of rows) { r.state = 'wait'; r.alpha = 0; r.locked = false; r.hitS = null; r.missS = null; r.passS = null;
      r.replacedS = null; r.shiftM = 0; r.liftM = 0; r.fallM = null; r.faceScale = 1; }
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
  // A dropping screen hangs where the player's own chase view sees all of it at the impact (sim/marqueeView.js):
  // far enough to fit under the top edge, on the kart's line leaning toward the view centre, falling into frame.
  const probe = { frame: frame() }, kartAt = { frame: frame() };
  const rise = (p, x) => (p.forkId === null && track.canalProfile ? track.canalProfile(p.s, x, {}).height : 0) || 0;
  function hang(row, st, leadM, plan, view) {
    const minM = watchStandoff(row, plan), lane = Number.isFinite(st[S.lateral]) ? st[S.lateral] : 0;
    if (!place(kartAt, st, leadM)) return place(row, st, leadM + minM);
    const k = kartAt.frame, up = rise(kartAt, lane);
    const kart = { p: k.p.map((v, i) => v + k.b[i] * lane + k.n[i] * up), t: k.t, n: k.n, b: k.b };
    const sides = row.params.count === 3 ? [-1, 0, 1] : row.params.count === 2 ? [-1, 1] : [0];
    const fit = fitMarquee({ kart, lane, sides, minM, view, spot: d => place(probe, st, leadM + d) ? probe.frame : null,
      lift: (d, x) => rise(probe, x) });
    if (!place(row, st, leadM + (fit ? fit.aheadM : minM))) return false;
    if (fit) { row.shiftM = fit.shiftM; row.liftM = fit.liftM; row.fallM = fit.fallM; row.faceScale = fit.scale; }
    return true;
  }
  // A newer drop retires every earlier screen still hanging ahead: it leaves as the next one starts to fall,
  // so a quick second "keep" never lands hidden behind the first. Screens dropping together stay together.
  function retireOlder(row) {
    for (const other of rows) if (other !== row && other.kind === 'MARQUEE' && other.locked && other.replacedS === null
      && (other.state === 'live' || other.state === 'hit') && other.startS < row.startS - .05) other.replacedS = row.startS - DROP_S;
  }
  // Signed along-road distance from the player to a spot, wrapped to the nearer way round.
  const along = (row, st) => { const d = wrap(row.s - st[S.s], track.lengthM); return d > track.lengthM / 2 ? d - track.lengthM : d; };
  function update(tS, st, { paused = false, gentle = false, view = null } = {}) {
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
    const plan = Math.max(SPEED_MIN, speed);
    result.playerS = st[S.s]; result.speed = speed;
    const lost = st[S.respawnPhase] || teleported, slow = speed < 2;
    const forkId = track.forkAtSeg?.(st[S.seg])?.id || null;
    const calm = typeof gentle === 'object' ? gentle.on : gentle;
    const rise = Math.max(.3, typeof gentle === 'object' ? gentle.rise(.35) : gentle ? .6 : .35);
    for (const row of rows) {
      const baseFade = row.mode === 'WATCH' ? (calm ? 2.4 : 1.6) : .6;
      const fade = typeof gentle === 'object' ? gentle.fade(baseFade) : calm ? Math.max(1, baseFade) : baseFade;
      const remain = row.startS - tS, sky = zoned(row), spot = row.mode === 'WATCH' && !sky;
      if (row.state === 'done' || remain > row.leadS) continue;
      if (row.state === 'wait') {
        if (row.mode === 'WATCH' && remain <= 0) {
          // A seek into a hold rebuilds it without replaying its impact; a late spot lands ahead, already down.
          if (prevT !== null || tS >= row.endS || (spot && (lost || slow))) { row.state = 'done'; continue; }
          row.state = 'hit'; row.hitS = row.startS; row.bornS = tS;
          if (spot && !(row.kind === 'MARQUEE' ? hang(row, st, 0, plan, view) : place(row, st, watchStandoff(row, plan)))) { row.state = 'done'; continue; }
          row.locked = true;
          if (row.kind === 'MARQUEE') retireOlder(row);
        } else {
          if (remain < .25) { row.state = 'done'; continue; }
          if (lost || (!sky && slow)) continue;
          row.state = 'live'; row.bornS = tS;
        }
      }
      const completingFork = row.forkId === null && forkId === row.approachForkId;
      const offBranch = !sky && row.locked && row.forkId !== forkId && !completingFork;
      if (row.missS === null && row.state !== 'passed' && (lost && !sky || (row.mode === 'CROSS' && slow) || offBranch)) {
        row.missS = tS; row.missAlpha = row.alpha; row.state = 'miss';
      }
      if (row.state === 'live' && !row.locked && !sky) {
        // CROSS planes follow the predicted crossing until 0.25 s out. WATCH spots settle 1.3 s out and then stay.
        const ahead = row.mode === 'WATCH' ? plan * Math.max(0, remain) + watchStandoff(row, plan) : speed * Math.max(0, remain);
        const lock = row.mode === 'CROSS' ? remain <= .25 : remain <= (row.params.lockS ?? 1.3);
        const dropping = lock && row.mode === 'WATCH' && row.kind === 'MARQUEE';
        if (!(dropping ? hang(row, st, plan * Math.max(0, remain), plan, view) : place(row, st, ahead))) { row.state = 'done'; continue; }
        row.locked = lock;
        if (dropping) retireOlder(row);
      }
      if (row.state === 'live' && prev && !lost) {
        let hit = row.mode === 'WATCH' && prevT < row.startS && tS >= row.startS;
        if (row.mode === 'CROSS' && row.locked && !slow && Math.abs(remain) <= .35) {
          const a = local(prev, row.frame, 't'), b = local(p, row.frame, 't');
          if (a < 0 && b >= 0 && b > a) {
            const u = -a / (b - a), at = p.map((v, i) => prev[i] + (v - prev[i]) * u);
            hit = Math.abs(local(at, row.frame, 'b')) <= row.frame.w / 2 + 1 && local(at, row.frame, 'n') >= -.6 && local(at, row.frame, 'n') <= 6;
          }
        }
        if (hit) { row.state = 'hit'; row.hitS = tS; fired.push(row); }
      }
      // A fixed spot the player has driven past is behind the camera: let it go quickly.
      if (spot && row.locked && row.state === 'hit' && row.passS === null && along(row, st) < -PASS_M) { row.passS = tS; row.passAlpha = row.alpha; row.state = 'passed'; }
      row.ahead = sky ? 0 : along(row, st);
      const outS = row.mode === 'CROSS' ? (row.hitS ?? row.startS + .35) : Math.min(row.endS + (row.params.exitS ?? 0), row.replacedS ?? Infinity);
      if (row.state === 'miss') row.alpha = row.missAlpha * (1 - smooth((tS - row.missS) / fade));
      else if (row.state === 'passed') row.alpha = row.passAlpha * (1 - smooth((tS - row.passS) / .5));
      else row.alpha = smooth((tS - row.bornS) / rise) * (1 - smooth((tS - outS) / fade));
      const gone = row.state === 'miss' ? row.missS + fade : row.state === 'passed' ? row.passS + .5 : outS + fade;
      if (tS >= gone) { row.state = 'done'; continue; }
      live.push(row);
    }
    return result;
  }
  return { update, reset, cues, get live() { return live; }, get revision() { return result.revision; },
    dispose() { reset(); disposed = true; } };
}
