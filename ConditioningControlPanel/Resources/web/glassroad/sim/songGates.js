import { S } from './state.js';
import { wrap } from '../core/math.js';

const smooth = x => { const u = Math.max(0, Math.min(1, x)); return u * u * (3 - 2 * u); };
const local = (p, f, axis) => p.reduce((sum, x, i) => sum + (x - f.p[i]) * f[axis][i], 0);
const frame = () => ({ p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] });

/** Selected recorded words and musical drops, with no invented spoken text or audio playback. */
export function songGateCues(song) {
  const cues = [], words = song?.words || [];
  for (const t of song?.triggers || []) {
    if (t.kind === 'drop' || t.w?.trim()) cues.push({ startS: t.startS, endS: t.startS + .6,
      text: t.kind === 'drop' ? 'DROP' : t.w.trim(), kind: t.kind === 'drop' ? 'drop' : 'phrase' });
  }
  for (const w of words) if (w.gate && !cues.some(c => Math.abs(c.startS - w.startS) < .25 && c.kind === 'phrase')) {
    const line = song.lines?.find(l => l.i === w.line);
    const text = line?.text?.trim();
    cues.push({ startS: w.startS, endS: w.endS, kind: 'phrase',
      text: text && text.length <= 64 && text.split(/\s+/).length <= 10 ? text : w.w });
  }
  // A recorded phrase owns its moment. Thin nearby musical drops rather than losing the voice cue.
  cues.sort((a, b) => (a.kind === b.kind ? a.startS - b.startS : a.kind === 'phrase' ? -1 : 1));
  const kept = [];
  for (const cue of cues) if (Number.isFinite(cue.startS) && cue.startS >= 0 &&
    !kept.some(c => Math.abs(c.startS - cue.startS) < 2)) kept.push(cue);
  return kept.sort((a, b) => a.startS - b.startS).map((c, i) => ({ ...c, id: `song-${i}` }));
}

/** Player-local, song-clock gates. Their planes lock before contact; misses never move the song. */
export function createSongGateRun(song, track) {
  const cues = songGateCues(song), live = [], f = frame();
  const rows = cues.map(c => ({ ...c, state: 'wait', s: 0, frame: frame(), forkId: null, locked: false, bornS: 0, alpha: 0 }));
  let lastT = null, previous = null, hits = 0;
  function reset() { for (const row of rows) { row.state = 'wait'; row.locked = false; } lastT = null; previous = null; hits = 0; live.length = 0; }
  function place(row, st, remaining, speed) {
    const fork = track.forkAtSeg?.(st[S.seg]);
    const ahead = speed * Math.max(0, remaining);
    if (fork && fork.localS(st[S.s]) + ahead <= fork.lengthM) {
      row.s = fork.mainS(fork.localS(st[S.s]) + ahead);
      fork.frameAt(fork.localS(row.s), row.frame); row.forkId = fork.id;
    } else {
      row.s = wrap(fork ? fork.toS + ahead - (fork.lengthM - fork.localS(st[S.s])) : st[S.s] + ahead, track.lengthM);
      track.frameAt(row.s, row.frame); row.forkId = null;
    }
    const f = row.frame;
    return f.w >= 8 && !f.flags?.some(v => ['jump', 'gap'].includes(v));
  }
  function update(tS, st, { gentle = false } = {}) {
    if (!Number.isFinite(tS)) { live.length = 0; previous = null; return { live, hit: null }; }
    if (lastT !== null && tS < lastT - .5) reset();
    if (tS === lastT) return { live, hit: null };
    const dt = lastT === null ? 0 : tS - lastT, seek = dt > .5;
    lastT = tS;
    const p = [st[S.posX], st[S.posY], st[S.posZ]], prev = previous;
    const teleported = !!prev && Math.hypot(...p.map((v, i) => v - prev[i])) > 20;
    previous = p; live.length = 0;
    const fork = track.forkAtSeg?.(st[S.seg]);
    const tangent = (track.pathFrameAt ? track.pathFrameAt(st[S.s], st[S.seg], f) : track.frameAt(st[S.s], f)).t;
    const speed = Math.max(0, st[S.velX] * tangent[0] + st[S.velY] * tangent[1] + st[S.velZ] * tangent[2]);
    let hit = null;
    for (const row of rows) {
      const remain = row.startS - tS;
      if (row.state === 'done' || remain > 4) continue;
      if (row.state === 'wait') {
        if (remain < .25 || st[S.respawnPhase] || speed < 2) { if (remain < .25) row.state = 'done'; continue; }
        row.state = 'live'; row.bornS = tS;
      }
      if (st[S.respawnPhase] || teleported || seek) { row.state = 'done'; continue; }
      if (row.state === 'live' && !row.locked) {
        if (!place(row, st, remain, speed)) { if (remain <= .25) row.state = 'done'; continue; }
        row.locked = remain <= .25;
      }
      if (row.locked && row.forkId !== (fork?.id || null)) { row.state = 'done'; continue; }
      if (row.state === 'live' && row.locked && prev && speed > 1 && Math.abs(remain) <= .35) {
        const a = local(prev, row.frame, 't'), b = local(p, row.frame, 't');
        if (a < 0 && b >= 0 && b > a) {
          const u = -a / (b - a), at = p.map((v, i) => prev[i] + (v - prev[i]) * u);
          if (Math.abs(local(at, row.frame, 'b')) <= row.frame.w / 2 && local(at, row.frame, 'n') >= -.6 && local(at, row.frame, 'n') <= 6) {
            row.state = 'hit'; row.hitS = tS; hits++; hit = row;
          }
        }
      }
      const outS = row.state === 'hit' ? row.hitS + 1 : row.startS + .35;
      row.alpha = smooth((tS - row.bornS) / (gentle ? .6 : .35)) * (1 - smooth((tS - outS) / .6));
      if (tS > outS + .6) { row.state = 'done'; continue; }
      live.push(row);
    }
    return { live, hit };
  }
  return { update, reset, cues, get hits() { return hits; } };
}
