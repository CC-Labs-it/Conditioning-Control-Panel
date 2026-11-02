// Clean driving opens a higher ceiling; the heard song shapes its gradual rise.
import { S, RESPAWN } from './state.js';
import { clamp } from '../core/math.js';
import { lastAtOrBefore, energyOf, sectionOf } from '../audio/chart.js';

const MEMORY = new WeakMap();
const memory = st => {
  let m = MEMORY.get(st);
  if (!m) { m = { clean: 0, hold: 0, drive: 0 }; MEMORY.set(st, m); }
  return m;
};
export function resetCleanPace(st, collision = false) {
  const m = memory(st); m.clean = 0; m.hold = collision ? 1.5 : 0; m.drive = 0;
}

/** No live analyser or wall clock: chart/decoded-file timing is shared by all solo racers. */
export function songDrive(song, timeS) {
  if (!song || !Number.isFinite(timeS) || !(song.bpm > 0)) return 0;
  const beats = song.beats || [], i = lastAtOrBefore(beats, timeS);
  const interval = i >= 0 && i + 1 < beats.length ? beats[i + 1] - beats[i] : 60 / song.bpm;
  const bpm = interval > .15 && interval < 2 ? 60 / interval : song.bpm;
  const tempo = clamp((bpm - 75) / 85, 0, 1);
  let energy = 0;
  if (song.energy) energy = Math.max(energyOf(song, 'mix', timeS), energyOf(song, 'drums', timeS));
  const section = song.sections?.length ? sectionOf(song, timeS) : null;
  if (section) energy = Math.max(energy, Number(section.energy) || 0);
  if (song.barEnergy?.length) {
    const bar = Math.max(0, lastAtOrBefore(song.downbeats || [], timeS));
    energy = Math.max(energy, clamp((song.barEnergy[bar] || 0) * 4, 0, 1));
  }
  const mood = section?.mood === 'drop' || section?.mood === 'peak' ? 1 : 0;
  return clamp(.45 * tempo + .45 * energy + .1 * mood, 0, 1);
}

export function cleanPace(st, act, dt, feel, { enabled = false, drive = 0 } = {}) {
  const m = memory(st);
  if (!enabled) { resetCleanPace(st); return 1; }
  if (!(dt > 0)) return 1 + m.clean * (feel.get('cleanPace.cleanGain') + feel.get('cleanPace.musicGain') * m.drive);
  if (st[S.respawnPhase] !== RESPAWN.NONE || st[S.stalled] > 0) { resetCleanPace(st, true); return 1; }
  m.hold = Math.max(0, m.hold - dt);
  const moving = st[S.engineV] >= feel.get('cleanPace.minMps') && act.throttle > .1 && act.brake < .1;
  if (moving && m.hold <= 0) m.clean = Math.min(1, m.clean + dt / feel.get('cleanPace.buildS'));
  else if (!moving) m.clean = Math.max(0, m.clean - dt / feel.get('cleanPace.releaseS'));
  m.drive += (clamp(drive, 0, 1) - m.drive) * (1 - Math.exp(-dt / feel.get('cleanPace.musicEaseS')));
  return 1 + m.clean * (feel.get('cleanPace.cleanGain') + feel.get('cleanPace.musicGain') * m.drive);
}
