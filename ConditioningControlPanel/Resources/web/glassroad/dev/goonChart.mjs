// Timing adapters read the owner's private analysis; neither lyrics nor recordings are bundled.
export const GOON_CHART = '/private-audio/goonworks/race-chart.json';
export const GOON_AUDIO = '/private-audio/goonworks/remix-v4.mp3';
export const GOON_SHA256 = '9c78cf118fe098b52fb2b1de1caf49fdc986f9ab31b15f9c5ca12ac45cdeb41f';

const key = text => String(text).toLowerCase().replace(/[^a-z0-9]/g, '');
export function goonChart(beat, aligned, arrangement, durationS, envelope = {}) {
  const bpm = Number(arrangement.match(/\bBPM\s*=\s*([\d.]+)/)?.[1]);
  const downbeatS = Number(arrangement.match(/\bBAR0\s*=\s*([\d.]+)/)?.[1]);
  if (!Number.isFinite(durationS) || !(durationS > 0) || !(bpm > 0) || !Number.isFinite(downbeatS)
      || !Number.isFinite(beat?.bpm) || !Number.isFinite(beat?.bar0)
      || Math.abs(beat.bpm / 2 - bpm) > .01 || Math.abs(beat.bar0 - downbeatS) > .01)
    throw new Error('Private beat analysis does not match the selected arrangement');
  if (!Array.isArray(aligned) || !aligned.length || aligned.some((w, i) => !w.w
      || !Number.isFinite(w.s) || !Number.isFinite(w.e) || w.s < 0 || w.e < w.s || w.e > durationS
      || (i > 0 && w.s < aligned[i - 1].s))) throw new Error('Invalid private word alignment');
  const lines = [], words = [];
  for (const w of aligned) {
    let line = lines.at(-1);
    if (!line || w.s - line.endS > .6 || w.s - line.startS > 5) {
      line = { i: lines.length, text: '', startS: w.s, endS: w.e }; lines.push(line);
    }
    line.text += (line.text ? ' ' : '') + w.w; line.endS = w.e;
    words.push({ w: w.w, startS: w.s, endS: w.e, line: line.i, gate: false, bubble: false });
  }
  const source = arrangement.split(/\r?\n/).filter(line => !line.trimStart().startsWith('#')).join('\n');
  const phrases = [...new Set([...source.matchAll(/classic\("wh_([a-z_]+)"/g)].map(m => m[1]))];
  const triggers = [];
  for (const phrase of phrases) {
    const tokens = phrase.split('_').map(key);
    for (let i = 0; i + tokens.length <= words.length; i++) {
      const span = words.slice(i, i + tokens.length);
      if (span.every((w, j) => key(w.w) === tokens[j]) && span.at(-1).endS - span[0].startS < 4)
        triggers.push({ kind: 'word', w: span.map(w => w.w).join(' '), startS: span[0].startS });
    }
  }
  const drops = [...new Set([...source.matchAll(/swell\(([\d.]+)\s*,/g)].map(m => Number(m[1])))];
  for (const startS of drops) if (startS > 0 && startS < durationS) triggers.push({ kind: 'drop', startS });
  return { id: 'goonworks-remix-v4', title: 'Keep Watching - remix v4', version: 1, audio: GOON_AUDIO,
    durationS, bpm, downbeatS, lines, words, triggers: triggers.sort((a, b) => a.startS - b.startS),
    sections: [{ id: 'recording', startS: 0, endS: durationS, mood: 'groove', energy: .7, loop: .85 }],
    energy: envelope.energy || {}, hits: envelope.hits || {} };
}
