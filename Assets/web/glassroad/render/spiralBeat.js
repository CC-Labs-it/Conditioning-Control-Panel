import { beatOf } from '../audio/chart.js';

// Motion only. No brightness flash, no synthetic beat during silence, no phase resets.
export function createSpiralBeatClock() {
  let amount = 0;
  const beat = { index: 0, phase: 0, lenS: .5 };
  const emptyBeats = [], grid = { bpm: 96, beats: emptyBeats, downbeatS: 0 };
  return { update(dt, view = {}, gentle = false) {
    if (view.paused || view.audioReactive?.frozen || !(dt > 0)) return 1 + amount;
    const a = view.audioReactive, song = view.beatSong || view.song;
    let target = 0;
    if (a?.active && song?.bpm > 0 && Number.isFinite(view.songS)) {
      grid.bpm = song.bpm; grid.beats = song.beats || emptyBeats; grid.downbeatS = song.downbeatS || 0;
      beatOf(grid, view.songS, beat);
      const onset = Math.exp(-beat.phase * 9);
      target = Math.min(1, onset * .7 + Math.max(a.bass || 0, a.mid || 0) * .3) * (gentle ? .22 : 1);
    }
    const tau = gentle ? .35 : target > amount ? .045 : .16;
    amount += (target - amount) * (1 - Math.exp(-Math.min(dt, .1) / tau));
    return 1 + amount;
  } };
}
