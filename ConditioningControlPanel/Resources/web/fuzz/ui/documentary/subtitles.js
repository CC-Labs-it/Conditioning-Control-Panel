import { NARRATION_SUBTITLES, SUBTITLE_TIMING } from './narration-subtitles.js';

export { SUBTITLE_TIMING };

/** Estimated phrase timing follows the media clock, including pause and resume. */
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
