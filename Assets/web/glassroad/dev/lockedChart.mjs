// Read-only adapter for cached, already-rendered voice takes. Never synthesizes or remixes audio.
export const KEEP_CHART = '/private-audio/locked-cock/out/v3/race-chart.json';
const SR = 44100, tokens = text => (text.match(/[A-Za-z']+/g) || []).map(w => w.toLowerCase().replaceAll("'", ''));
const ock = text => /(?:lock|locked|cock|clock|knock|mock|block|shock|tock|unlock)$/.test(tokens(text).at(-1) || '');

export function lockedChunks(script) {
  const lines = script.split(/\r?\n/).map(s => s.trim()).filter(s => s && !s.startsWith('#'));
  const groups = []; let group = [], length = 0;
  for (const line of lines) {
    if (group.length && length + line.length + 5 > 1500) { groups.push(group); group = []; length = 0; }
    group.push(line); length += line.length + 5;
  }
  if (group.length) groups.push(group);
  return groups;
}
function alignedWords(a) {
  const words = []; let word = null, tag = false;
  a.characters.forEach((c, i) => {
    if (c === '[') tag = true;
    if (tag || !/[A-Za-z']/.test(c)) { if (word) { words.push(word); word = null; } if (c === ']') tag = false; return; }
    if (!word) word = { w: '', startS: a.character_start_times_seconds[i] };
    word.w += c; word.endS = a.character_end_times_seconds[i];
  });
  if (word) words.push(word);
  return words;
}
function spansOf(words, group) {
  let cursor = 0;
  return group.map(line => {
    const want = tokens(line), low = words.map(w => tokens(w.w).join(''));
    for (let start = cursor; start < words.length; start++) {
      let end = start;
      for (const token of want) {
        let found = false;
        for (let n = 1; n <= 3 && end + n <= words.length; n++) {
          if (low.slice(end, end + n).join('') === token) { end += n; found = true; break; }
        }
        if (!found) { end = -1; break; }
      }
      if (end > start) { cursor = end; return [start, end - 1]; }
    }
    throw new Error('Cached voice does not match the requested script');
  });
}

/** Match mix_v3.py's sample-exact voice slicing and pauses; metadata comes from the existing takes. */
export function lockedChart(script, takes, sampleCounts, notes, mantras = []) {
  const groups = lockedChunks(script), lines = [], words = [], triggers = [];
  if (groups.length !== takes.length || takes.some(t => t.fake)) throw new Error('Expected all real cached takes');
  let at = Math.floor(1.2 * SR);
  groups.forEach((group, i) => {
    const ws = alignedWords(takes[i].alignment), spans = spansOf(ws, group);
    let trim = 0;
    if (['sarcastic', 'smug'].includes(ws[0]?.w.toLowerCase())) {
      trim = Math.max(0, Math.floor((ws[spans[0][0]].startS - .03) * SR));
      for (const w of ws) { w.startS -= trim / SR; w.endS -= trim / SR; }
    }
    let first = 0;
    for (let j = 0; j < group.length; j++) {
      if (!ock(group[j]) && j < group.length - 1) continue;
      const a = spans[first][0], b = spans[j][1];
      const s0 = Math.max(0, Math.floor((ws[a].startS - .04) * SR));
      const s1 = Math.min(sampleCounts[i] - trim, Math.floor((ws[b].endS + .08) * SR));
      if (!(s1 > s0)) throw new Error('Invalid cached voice span');
      const offset = (at - s0) / SR;
      for (let k = first; k <= j; k++) {
        const [wa, wb] = spans[k], index = lines.length;
        const line = { i: index, text: group[k], startS: offset + ws[wa].startS, endS: offset + ws[wb].endS };
        lines.push(line);
        for (let w = wa; w <= wb; w++) words.push({ ...ws[w], startS: offset + ws[w].startS, endS: offset + ws[w].endS, line: index, bubble: false, gate: false });
        const key = s => s.toLowerCase().replace(/[^a-z0-9' ]+/g, '').trim();
        if (mantras.some(m => key(m) === key(line.text)) && line.startS > 12) triggers.push({ kind: 'word', w: line.text, startS: line.startS });
        if (tokens(line.text).length <= 3 && ock(line.text)) triggers.push({ kind: 'drop', startS: offset + ws[wb].startS });
      }
      at += s1 - s0 + Math.floor(.10 * SR); first = j + 1;
    }
  });
  const durationS = (at + Math.floor(2.2 * SR)) / SR;
  if (!(notes.bpm > 0) || Math.abs(durationS - notes.seconds) > .1) throw new Error('Cached timing does not match the selected mix duration');
  return { id: 'locked-cock', title: 'Locked Cock', version: 1, audio: 'locked-cock_phone.mp3', durationS, bpm: notes.bpm,
    downbeatS: 0, lines, words, triggers, sections: Array.from({ length: 4 }, (_, i) => ({ id: `lap-${i + 1}`,
      startS: durationS * i / 4, endS: durationS * (i + 1) / 4, mood: i === 3 ? 'peak' : 'groove', energy: .65, loop: .9 })) };
}
