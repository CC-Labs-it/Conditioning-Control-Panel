// The selected recording and its timing sources remain in the owner's private audio folder.
export const BOUDOIR_CHART = '/private-audio/boudoir/race-chart.json';
export const BOUDOIR_AUDIO = '/private-audio/boudoir/remix-v5.mp3';
export const BOUDOIR_SHA256 = 'cd4a8bff5ae2456a2ba5748130bb8759cd8266b019423407863eab5ae55dabad';

// Read the render's event log, including quoted commas and escaped quotes. Never execute its script.
export function remixEvents(csv) {
  const rows = []; let row = [], value = '', quoted = false;
  for (let i = 0; i <= csv.length; i++) {
    const c = csv[i] ?? '\n';
    if (c === '"') {
      if (quoted && csv[i + 1] === '"') { value += '"'; i++; } else quoted = !quoted;
    } else if (!quoted && (c === ',' || c === '\n')) {
      row.push(value.replace(/\r$/, '')); value = '';
      if (c === '\n') { if (row.some(Boolean)) rows.push(row); row = []; }
    } else value += c;
  }
  if (quoted || rows.shift()?.join(',') !== 't,bus,note') throw new Error('Invalid private event log');
  return rows.map(r => {
    if (r.length !== 3 || !r[0].trim() || !Number.isFinite(Number(r[0]))) throw new Error('Invalid private event');
    return { startS: Number(r[0]), bus: r[1], note: r[2] };
  });
}

function sectionsFrom(arrangement, durationS, firstWordS) {
  const sections = [], authored = [...arrangement.matchAll(/^#\s*(hook\s+\d+|verse\s+\d+|bridge|outro)\s*\(([\d.]+)-([\d.]+)\)/gm)]
    .map(m => ({ id: m[1].replace(/\s+/g, '-'), startS: Number(m[2]), endS: Number(m[3]) }));
  let cursor = 0;
  const gap = endS => {
    if (endS > cursor) sections.push({ id: 'groove-' + sections.length, startS: cursor, endS,
      mood: cursor === 0 ? 'intro' : 'groove', energy: .55, loop: .65 });
    cursor = endS;
  };
  gap(firstWordS);
  for (const s of authored) {
    if (s.startS < cursor || s.endS <= s.startS || s.endS > durationS) throw new Error('Invalid private arrangement section');
    gap(s.startS);
    const mood = s.id.startsWith('hook') ? 'peak' : s.id === 'bridge' ? 'break' : s.id === 'outro' ? 'outro' : 'build';
    sections.push({ ...s, mood, energy: mood === 'peak' ? .95 : mood === 'break' ? .5 : .65,
      loop: mood === 'peak' ? .95 : .55 });
    cursor = s.endS;
  }
  if (!authored.length) throw new Error('Missing private arrangement sections');
  if (cursor < durationS) sections.push({ id: 'tail', startS: cursor, endS: durationS, mood: 'outro', energy: .2, loop: .25 });
  return sections;
}

export function boudoirChart(beat, aligned, arrangement, events, durationS, envelope = {}) {
  if (!Number.isFinite(durationS) || durationS <= 0 || !Number.isFinite(beat?.bpm) || beat.bpm < 40 || beat.bpm > 240
      || !Number.isFinite(beat?.beat0) || !Number.isFinite(beat?.bar0) || beat.beat0 < 0
      || Math.abs(beat.beat0 - beat.bar0) > .001) throw new Error('Invalid private beat analysis');
  if (!Array.isArray(aligned) || !aligned.length || aligned.some((w, i) => !w.w || !Number.isFinite(w.s)
      || !Number.isFinite(w.e) || w.s < 0 || w.e < w.s || w.e > durationS || (i > 0 && w.s < aligned[i - 1].s)))
    throw new Error('Invalid private word alignment');
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
    if (!Number.isFinite(event.startS) || event.startS < 0 || event.startS >= durationS) throw new Error('Private event outside recording');
    // These are the actual overlay placements from this render, not an older mix's transcription.
    if (event.bus === 'classic' && /^wh_[a-z_]+$/.test(event.note))
      triggers.push({ kind: 'word', w: event.note.slice(3).replaceAll('_', ' '), startS: event.startS });
    else if (event.bus === 'classic' && /^drop_[a-z_]+$/.test(event.note))
      triggers.push({ kind: 'drop', startS: event.startS });
  }
  return { id: 'boudoir-remix-v5', title: 'Think Pink - remix v5', version: 1, audio: BOUDOIR_AUDIO,
    durationS, bpm: beat.bpm, downbeatS: beat.beat0, lines, words, events,
    triggers: triggers.sort((a, b) => a.startS - b.startS), sections: sectionsFrom(arrangement, durationS, aligned[0].s),
    energy: envelope.energy || {}, hits: envelope.hits || {} };
}
