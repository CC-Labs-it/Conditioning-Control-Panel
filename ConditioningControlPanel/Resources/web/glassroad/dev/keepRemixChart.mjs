// The selected recording, words and arrangement stay in the owner's private audio folder.
export const KEEP_REMIX_CHART = '/private-audio/keep/race-chart.json';
export const KEEP_REMIX_AUDIO = '/private-audio/keep/remix-v5.mp3';
export const KEEP_REMIX_SHA256 = '2649c1f6cc193521abfb7dd1a6b7d6d24f39f05db07ca1aacbb9bb1200a682ca';

function sectionsFrom(arrangement, durationS) {
  if (typeof arrangement !== 'string') throw new Error('Missing private arrangement');
  const sections = [];
  let cursor = 0, count = 0;
  const headings = /^#\s*(intro|verse\s+\d+|chorus\s+\d+|final\s+chorus|instrumental\s+break\s+\d+|bridge|outro)\b(.*)$/gm;
  for (const match of arrangement.matchAll(headings)) {
    const range = match[2].match(/^\s*\(\s*(\d+(?:\.\d+)?)\s*-\s*(\d+(?:\.\d+)?)\s*\)/);
    if (!range) throw new Error('Invalid private arrangement section');
    const startS = Number(range[1]), endS = Number(range[2]), id = match[1].replace(/\s+/g, '-');
    if (startS < cursor || endS <= startS || endS > durationS)
      throw new Error('Invalid private arrangement timeline');
    if (startS > cursor) sections.push({ id: 'groove-' + sections.length, startS: cursor, endS: startS,
      mood: cursor === 0 ? 'intro' : 'groove', energy: .55, loop: .65 });
    const mood = id.includes('chorus') ? 'peak' : id === 'bridge' || id.startsWith('instrumental-break') ? 'break'
      : id === 'intro' || id === 'outro' ? id : 'build';
    sections.push({ id, startS, endS, mood, energy: mood === 'peak' ? .95 : mood === 'break' ? .5 : .65,
      loop: mood === 'peak' ? .95 : .55 });
    cursor = endS; count++;
  }
  if (!count) throw new Error('Missing private arrangement sections');
  if (cursor < durationS) sections.push({ id: 'tail', startS: cursor, endS: durationS,
    mood: 'outro', energy: .2, loop: .25 });
  return sections;
}

function grid(origin, step, durationS) {
  const points = [];
  for (let i = 0; origin + i * step < durationS; i++) points.push(origin + i * step);
  return points;
}

export function keepRemixChart(beat, aligned, arrangement, events, durationS, envelope = {}) {
  if (!Number.isFinite(durationS) || durationS <= 0 || !Number.isFinite(beat?.bpm)
      || beat.bpm < 40 || beat.bpm > 240 || !Number.isFinite(beat?.beat0) || !Number.isFinite(beat?.bar0)
      || beat.beat0 < 0 || beat.bar0 < 0 || beat.beat0 >= durationS || beat.bar0 >= durationS)
    throw new Error('Invalid private beat analysis');
  const beatS = 60 / beat.bpm, barOffset = Math.round((beat.bar0 - beat.beat0) / beatS);
  // Beat and bar origins may differ by a whole beat; private analysis rounds seconds.
  if (barOffset < 0 || barOffset > 3 || Math.abs(beat.bar0 - beat.beat0 - barOffset * beatS) > .002)
    throw new Error('Private beat and bar origins disagree');
  if (!Array.isArray(aligned) || !aligned.length || aligned.some((w, i) => typeof w?.w !== 'string' || !w.w.trim()
      || !Number.isFinite(w.s) || !Number.isFinite(w.e) || w.s < 0 || w.e < w.s || w.e > durationS
      || (i > 0 && w.s < aligned[i - 1].s))) throw new Error('Invalid private word alignment');
  if (!Array.isArray(events) || events.some(e => !Number.isFinite(e?.startS) || e.startS < 0 || e.startS >= durationS))
    throw new Error('Private event outside recording');
  const lines = [], words = [];
  for (const w of aligned) {
    let line = lines.at(-1);
    if (!line || w.s - line.endS > .6 || w.s - line.startS > 5) {
      line = { i: lines.length, text: '', startS: w.s, endS: w.e }; lines.push(line);
    }
    line.text += (line.text ? ' ' : '') + w.w; line.endS = w.e;
    words.push({ w: w.w, startS: w.s, endS: w.e, line: line.i, gate: false, bubble: false });
  }
  const triggers = [];
  for (const event of events) {
    if (event.bus !== 'classic') continue;
    if (/^wh_[a-z]+(?:_[a-z]+)*$/.test(event.note))
      triggers.push({ kind: 'word', w: event.note.slice(3).replaceAll('_', ' '), startS: event.startS });
    // These events are voiced words; their suffix names the word actually heard.
    else if (/^drop_[a-z]+(?:_[a-z]+)*$/.test(event.note))
      triggers.push({ kind: 'word', w: event.note.slice(5).replaceAll('_', ' '), startS: event.startS });
  }
  return { id: 'keep-remix-v5', title: 'Locked - remix v5', version: 1, audio: KEEP_REMIX_AUDIO,
    durationS, bpm: beat.bpm, downbeatS: beat.bar0,
    beats: grid(beat.beat0, beatS, durationS), downbeats: grid(beat.bar0, 4 * beatS, durationS), lines, words,
    triggers: triggers.sort((a, b) => a.startS - b.startS), sections: sectionsFrom(arrangement, durationS),
    energy: envelope.energy || {}, hits: envelope.hits || {} };
}
