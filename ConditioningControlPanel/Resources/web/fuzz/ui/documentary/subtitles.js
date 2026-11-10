import { NARRATION_SUBTITLES as ESTIMATED_SUBTITLES } from './narration-subtitles.js';
import { NARRATION_ALIGNMENT } from './narration-alignment.js';

export const SUBTITLE_TIMING = 'mixed';
const letters = text => String(text).toLowerCase().replace(/[^\p{L}\p{N}]/gu, '');

/** Keep the approved phrase text, using the first and last acoustically aligned word. */
export function alignSubtitlePhrases(cues, alignment) {
  if (!alignment?.words?.length || !(alignment.duration > 0)) return null;
  let offset = 0;
  const words = alignment.words.map(word => {
    const text = letters(word.word), start = offset; offset += text.length;
    return { ...word, text, from: start, to: offset };
  }).filter(word => word.text);
  // Hyphenated words can be one aligner token. Compare characters before assigning ranges.
  if (cues.map(cue => letters(cue[2])).join('') !== words.map(word => word.text).join('')) return null;
  offset = 0;
  return cues.map(([, , text]) => {
    const from = offset; offset += letters(text).length;
    const first = words.find(word => word.to > from);
    const last = words.findLast(word => word.from < offset);
    return [first.start / alignment.duration, Math.min(1, (last.end + .1) / alignment.duration), text];
  }).map((cue, index, all) => [cue[0], Math.min(cue[1], all[index + 1]?.[0] ?? 1), cue[2]]);
}

const aligned = new Set();
export const NARRATION_SUBTITLES = Object.freeze(Object.fromEntries(Object.entries(ESTIMATED_SUBTITLES).map(([id, cues]) => {
  const actual = alignSubtitlePhrases(cues, NARRATION_ALIGNMENT[id]);
  if (actual) aligned.add(id);
  return [id, actual || cues];
})));
export function subtitleTiming(id) { return aligned.has(id) ? 'aligned' : 'estimated'; }

/** Phrase timing follows the media clock, including pause, resume and duration corrections. */
export function subtitleAt(id, time, duration, subtitles = NARRATION_SUBTITLES) {
  if (!(duration > 0) || !Number.isFinite(time) || time < 0) return '';
  const progress = time / duration;
  const cues = subtitles[id] || [];
  for (const [from, to, text] of cues) if (progress >= from && progress < to) return text;
  return '';
}

export function subtitleBoundaries(id, duration, subtitles = NARRATION_SUBTITLES) {
  if (!(duration > 0)) return [];
  return (subtitles[id] || []).flatMap(([from, to]) => [from * duration, to * duration]);
}
