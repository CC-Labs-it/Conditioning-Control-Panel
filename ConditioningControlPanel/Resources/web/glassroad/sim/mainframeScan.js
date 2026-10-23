// Mainframe scan walls: four visible phases on the song clock, with a permanent outside bypass.
import { S, RESPAWN } from './state.js';

export function scanZones(track) {
  if (track.id !== 'mainframe') return [];
  return [210, 835].map((s, i) => ({ id: 'scan-' + i, s, len: 8, width: 10, offset: i * 8 }));
}

export function scanPose(zone, timeS, bpm = 120) {
  const beat = ((Math.max(0, timeS) * bpm / 60 + zone.offset) % 16 + 16) % 16;
  const phase = beat < 4 || beat >= 13 ? 'clear' : beat < 8 ? 'warn' : beat < 12 ? 'sweep' : 'hold';
  const u = Math.max(0, Math.min(1, (beat - 8) / 4));
  return { phase, x: -3 + 6 * (u * u * (3 - 2 * u)),
    opacity: phase === 'clear' ? Math.max(0, 1 - (beat - 13) / 2) * (beat >= 13 ? 0.28 : 0)
      : phase === 'warn' ? Math.min(1, (beat - 4) / 1.5) * 0.18 : 0.28,
    active: phase === 'sweep' || phase === 'hold' };
}

// Both rendering and contact use this position, including the chart's downbeat offset.
export function scanClock(music, raceTimeS) {
  const song = music && music.song;
  return song && Number.isFinite(music.songS)
    ? { timeS: music.songS - (song.downbeatS || 0), bpm: song.bpm || 120 }
    : { timeS: raceTimeS, bpm: 120 };
}

export function createMainframeScan(track, events) {
  const zones = scanZones(track), previous = new Map(), latched = new Set();
  let lastT = null, safeUntil = -Infinity;
  const api = { ready: true, hits: 0, update, reset };
  function reset() { previous.clear(); latched.clear(); lastT = null; safeUntil = -Infinity; api.ready = true; api.hits = 0; }
  function update(racers, timeS, bpm = 120) {
    if (!zones.length || !Number.isFinite(timeS)) return;
    // A seek or loop must show a full bar of warning before contact becomes live again.
    if (lastT !== null && (timeS < lastT - 0.02 || timeS - lastT > 0.5)) {
      previous.clear(); latched.clear(); safeUntil = timeS + 240 / bpm;
    }
    api.ready = timeS >= safeUntil;
    for (const racer of racers) {
      const st = racer.st, id = racer.i;
      const now = { s: st[S.s], x: st[S.lateral], h: st[S.height], t: timeS };
      const prev = previous.get(id) || now;
      previous.set(id, now);
      if (racer.finished || st[S.respawnPhase] !== RESPAWN.NONE || !api.ready) continue;
      // Teleports and lap wraps never draw a collision line through the rest of the course.
      const ds = now.s - prev.s;
      for (const zone of zones) {
        const key = id + ':' + zone.id;
        const near = now.s >= zone.s - 3 && now.s <= zone.s + zone.len + 3;
        if (!near) latched.delete(key);
        if (latched.has(key) || Math.abs(ds) > 15) continue;
        let hit = false;
        // Swept probes cover the kart path and the moving wall, even on a low frame rate.
        for (let n = 0; n <= 4 && !hit; n++) {
          const a = n / 4, s = prev.s + ds * a, x = prev.x + (now.x - prev.x) * a;
          const h = prev.h + (now.h - prev.h) * a, pose = scanPose(zone, prev.t + (timeS - prev.t) * a, bpm);
          hit = pose.active && s >= zone.s - 0.7 && s <= zone.s + zone.len + 0.7
            && h >= -0.5 && h <= 4 && Math.abs(x - pose.x) <= zone.width / 2 + 0.65;
        }
        if (hit) {
          latched.add(key);
          st[S.stalled] = Math.max(st[S.stalled], 0.4);
          api.hits++;
          events.emit('scan-hit', { racer: id, zone: zone.id, durationS: 0.4 });
        }
      }
    }
    lastT = timeS;
  }
  return api;
}
