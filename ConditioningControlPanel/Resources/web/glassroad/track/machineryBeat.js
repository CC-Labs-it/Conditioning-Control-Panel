// Solo machinery follows the heard recording; online callers leave this adapter disabled.
const clocks = new WeakMap();
const finite = Number.isFinite;
export function setMachineryBeat(track, frame) {
  if (!frame?.enabled || !finite(frame.songS) || !finite(frame.raceS) || !(frame.bpm >= 40 && frame.bpm <= 240)) {
    clocks.delete(track); return;
  }
  const old = clocks.get(track), beatS = 60 / frame.bpm, offsetS = frame.offsetS || 0;
  const beat = (frame.songS - offsetS) / beatS;
  const predicted = old ? old.songS + (old.playing ? frame.raceS - old.raceS : 0) : frame.songS;
  const jumped = old ? (frame.raceS < old.raceS || Math.abs(frame.songS - predicted) > .18 || old.bpm !== frame.bpm) : frame.raceS > .5;
  // A seek or wrapped recording visibly opens the machinery, then grants four warning beats.
  const epochBeat = jumped ? Math.ceil(beat) + 4 : old?.epochBeat || 0;
  clocks.set(track, { ...frame, playing: frame.playing !== false, beatS, offsetS, epochBeat,
    rearming: !!jumped || !!old?.rearming });
}
export function machineryBeatAt(track, raceS) {
  const c = clocks.get(track); if (!c) return null;
  const songS = c.songS + (c.playing && finite(raceS) ? raceS - c.raceS : 0);
  const beat = (songS - c.offsetS) / c.beatS - c.epochBeat;
  return { beat, beatS: c.beatS, safe: beat < 0, rearming: c.rearming };
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
