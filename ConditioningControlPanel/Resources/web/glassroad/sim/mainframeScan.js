// Mainframe scan walls: four visible phases on the song clock, with a permanent outside bypass.
import { S, BOOST, RESPAWN } from './state.js';
import { applyBoost, boostHoldLeftS } from './boost.js';

/** Named runtime span shared by the course's environment, canal support and timed obstacles. */
export function mainframeSpan(track, id) {
  return track.id === 'mainframe' ? track.sections?.find(section => section.id === id) || null : null;
}

export function scanZones(track) {
  if (track.id !== 'mainframe') return [];
  const canal = mainframeSpan(track, 'coolant-canal'), submerged = mainframeSpan(track, 'submerged');
  const memory = mainframeSpan(track, 'memory-crossing'), bridge = mainframeSpan(track, 'skybridge');
  if (!canal || !submerged || !memory || !bridge) return [];
  const positions = [180, canal.fromS + 85, canal.fromS + 170, bridge.fromS + 160, memory.toS - 35, submerged.fromS + 130];
  return positions.map((s, i) => ({ id: 'scan-' + i, s, len: i === 1 || i === 2 ? 10 : 8,
    width: i === 1 || i === 2 ? 18 : 12, height: i === 1 || i === 2 ? 15 : 5.5,
    travel: i === 1 || i === 2 ? 4 : 3, offset: [0, 5, 11, 3, 9, 6][i] }));
}

export function scanPose(zone, timeS, bpm = 120) {
  const absoluteBeat = Math.max(0, timeS) * bpm / 60 + zone.offset;
  const beat = ((absoluteBeat % 16) + 16) % 16;
  const effect = Math.floor(absoluteBeat / 16) % 2 ? 'boost' : 'slow';
  const phase = beat < 4 || beat >= 14 ? 'clear' : beat < 8 ? 'warn' : beat < 12 ? 'sweep' : 'hold';
  const u = Math.max(0, Math.min(1, (beat - 8) / 4));
  const travel = zone.travel || 3;
  return { phase, effect, color: effect === 'boost' ? 'green' : 'yellow',
    nextEffect: effect === 'boost' ? 'slow' : 'boost', warning: phase === 'warn', changingSoon: beat >= 14,
    x: -travel + 2 * travel * (u * u * (3 - 2 * u)),
    opacity: phase === 'clear' ? Math.max(0, 1 - (beat - 14) / 2) * (beat >= 14 ? 0.28 : 0)
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
  const zones = scanZones(track), previous = new Map(), latched = new Set(), profile = {};
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
      if (racer.kind === 'remote' || racer.finished || st[S.respawnPhase] !== RESPAWN.NONE || !api.ready) continue;
      // Teleports and lap wraps never draw a collision line through the rest of the course.
      const ds = now.s - prev.s;
      for (const zone of zones) {
        const key = id + ':' + zone.id;
        const near = now.s >= zone.s - 3 && now.s <= zone.s + zone.len + 3;
        if (!near) latched.delete(key);
        if (latched.has(key) || Math.abs(ds) > 15) continue;
        let hit = false, hitEffect = 'slow';
        // Swept probes cover the kart path and the moving wall, even on a low frame rate.
        for (let n = 0; n <= 4 && !hit; n++) {
          const a = n / 4, s = prev.s + ds * a, x = prev.x + (now.x - prev.x) * a;
          const h = prev.h + (now.h - prev.h) * a, sampleT = prev.t + (timeS - prev.t) * a, pose = scanPose(zone, sampleT, bpm);
          // Walls rise from the centreline, while kart height is measured above the curved support normal.
          const support = track.canalProfile ? track.canalProfile(s, x, profile) : null;
          const wallHeight = support ? support.height + h * Math.hypot(1, support.slope, support.alongSlope) : h;
          hit = sampleT >= safeUntil && pose.active && s >= zone.s - 0.7 && s <= zone.s + zone.len + 0.7
            && wallHeight >= -0.5 && wallHeight <= zone.height && Math.abs(x - pose.x) <= zone.width / 2 + 0.65;
          if (hit) hitEffect = pose.effect;
        }
        if (hit) {
          latched.add(key);
          if (hitEffect === 'boost') {
            if (!(racer.feel && st[S.boostAmount] >= .18 && boostHoldLeftS(st,racer.feel) > 0))
              applyBoost(st, BOOST.PAD, 0, racer.feel, racer.events || { emit() {} }, { amount: 0.18, holdS: 0.7 });
          } else st[S.stalled] = Math.max(st[S.stalled], 0.5);
          api.hits++;
          events.emit('scan-hit', { racer: id, zone: zone.id, durationS: hitEffect === 'boost' ? 0.7 : 0.5,
            effect: hitEffect, color: hitEffect === 'boost' ? 'green' : 'yellow' });
        }
      }
    }
    lastT = timeS;
  }
  return api;
}
