// Solo machinery follows the heard recording; online callers leave this adapter disabled.
import { trapArrangement } from './trapArrangement.js';
const clocks = new WeakMap();
const finite = Number.isFinite;
export const MACHINERY_PACE = 1;
export const machineryDuration = (seconds, frame) => seconds / (frame?.pace || 1);
export function setMachineryBeat(track, frame) {
  if (frame?.enabled && frame.fallback && finite(frame.raceS))
    frame = { ...frame, songS: frame.raceS, bpm: 96, offsetS: 0 };
  if (!frame?.enabled || !finite(frame.songS) || !finite(frame.raceS) || !(frame.bpm >= 40 && frame.bpm <= 240)) {
    clocks.delete(track); return;
  }
  const old = clocks.get(track), beatS = 60 / frame.bpm, offsetS = frame.offsetS || 0;
  const beat = (frame.songS - offsetS) / beatS;
  const predicted = old ? old.songS + (old.playing ? frame.raceS - old.raceS : 0) : frame.songS;
  const jumped = old ? (frame.raceS < old.raceS || Math.abs(frame.songS - predicted) > .18 || old.bpm !== frame.bpm || !!old.fallback !== !!frame.fallback || old.song !== frame.song) : frame.raceS > .5;
  // A seek or wrapped recording visibly opens the machinery, then grants four warning beats.
  const epochBeat = jumped ? Math.ceil(beat) + 4 : old?.epochBeat || 0;
  clocks.set(track, { ...frame, playing: frame.playing !== false, beatS, offsetS, epochBeat,
    rearming: !!jumped || !!old?.rearming, arrangement: trapArrangement(frame.song, track.id) });
}
export function machineryBeatAt(track, raceS) {
  const c = clocks.get(track); if (!c) return null;
  const songS = c.songS + (c.playing && finite(raceS) ? raceS - c.raceS : 0);
  const beat = (songS - c.offsetS) / c.beatS - c.epochBeat;
  // One machinery beat per heard beat. Seek grace remains four real beats.
  return { beat: beat * MACHINERY_PACE, beatS: c.beatS / MACHINERY_PACE,
    songBeat: (songS - c.offsetS) / c.beatS, epochBeat: c.epochBeat, arrangement: c.arrangement,
    pace: MACHINERY_PACE, fallback: !!c.fallback, safe: beat < 0, rearming: c.rearming };
}
// A missing recording still gives every physical trap the same deterministic musical grid.
export function hazardBeatAt(track, raceS) {
  const frame = machineryBeatAt(track, raceS);
  // New side pistons author explicit heard beats, independent of legacy machinery subdivisions.
  return frame ? { ...frame, beat: frame.beat / (frame.pace || 1), beatS: frame.beatS * (frame.pace || 1), pace: 1 }
    : { beat: Math.max(0, raceS || 0) * 2, beatS: .5, safe: false, rearming: false };
}
export const beatCycle = (beat, period) => ((beat % period) + period) % period;
export function machineryRearming(frame, offset = 0, period = 16) {
  return frame?.safe || (frame?.rearming && frame.beat < beatCycle(-offset, period));
}
// Continuous motion: a short eased gear stroke on the beat and a slow crawl between strokes.
export function gearBeatTravel(beat) {
  const whole = Math.floor(beat), f = beat - whole, t = Math.min(1, f / .28);
  return whole + f * .2 + t * t * (3 - 2 * t) * .8;
}
