import { ladderSemis } from './mix.js';

// Same pentatonic family as the platform/Breakout ladder. Each course changes the instrument,
// not the game's musical key. The final glint lands at the visual pulse's 300 ms crest.
export const GATE_VOICES = Object.freeze({
  threshold: { octave: 12, type: 'triangle', decay: .32, overtone: 2, shimmer: .025 },
  mainframe: { octave: 12, type: 'sine', decay: .18, overtone: 3, shimmer: .018 },
  keep: { octave: 0, type: 'sine', decay: .58, overtone: 2, shimmer: .035 },
  boudoir: { octave: 12, type: 'sine', decay: .48, overtone: 4, shimmer: .014 },
  goonworks: { octave: 7, type: 'triangle', decay: .24, overtone: 2, shimmer: .03 },
});

export function phraseGateNotes(course, index = 0, gentle = false) {
  const voice = GATE_VOICES[course] || GATE_VOICES.threshold;
  const rung = Number.isFinite(index) ? Math.max(0, Math.trunc(index)) % 5 : 0;
  const root = ladderSemis(rung) + voice.octave, level = gentle ? .72 : 1;
  const tone = (semis, type, at, dur, vol, ratio = 1) => ({ semis, ratio, type, at, dur,
    vol: vol * level, attack: gentle ? .02 : .008 });
  return [
    tone(root, voice.type, 0, voice.decay, .115),
    tone(root - 12, 'sine', 0, .22, .045),
    tone(root + 7, voice.type, .075, voice.decay * .8, .055),
    tone(root + 12, 'sine', gentle ? .5 : .3, voice.decay, .04),
    tone(root, 'sine', 0, voice.decay * 1.15, voice.shimmer, voice.overtone),
  ];
}

export function playPhraseGate(syn, { course, index = 0, gentle = false, song = false } = {}) {
  if (song) return; // The recording already supplies this moment's voice and musical drop.
  for (const { semis, ratio, ...opts } of phraseGateNotes(course, index, gentle)) {
    syn.tone(syn.semis(semis) * ratio, opts);
  }
}
