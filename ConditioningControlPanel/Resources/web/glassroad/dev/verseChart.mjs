// Private verse renders stay outside the game tree. Adapt their timing data for the music road.
export const MAINFRAME_CHART = '/private-audio/tracks-2026-10-24/mainframe/out/v3/race-chart.json';

export function verseChart(raw, energy = null) {
  if (raw.fake || !(raw.durationS > 0) || !(raw.bpm > 0)) throw new Error('Expected a rendered verse chart');
  const lines = raw.lines.map((l, i) => ({ ...l, i }));
  const gates = raw.gates || [];
  const clean = w => String(w).toLowerCase().replace(/[^a-z']/g, '');
  const words = raw.words.map(w => {
    const gate = gates.find(g => Math.abs(g.startS - w.startS) < .025 && clean(g.w) === clean(w.w));
    const line = lines.find(l => w.startS >= l.startS - .025 && w.startS <= l.endS + .025 && l.role === w.role);
    return { ...w, line: line?.i ?? -1, gate: !!gate, bubble: false };
  });
  return {
    id: raw.id, title: raw.title, version: 1, durationS: raw.durationS,
    audio: `${raw.id}_phone.mp3`, bpm: raw.bpm, downbeatS: raw.downbeatS || 0,
    sections: raw.sections.map((s, i) => ({ ...s,
      mood: ['build', 'groove', 'peak'][i] || 'outro', loop: i === 1 ? .95 : .6,
      energy: [.45, .7, .85][i] ?? .4 })),
    lines, words,
    triggers: gates.filter(g => g.trigger).map(g => ({ kind: 'word', w: g.w, startS: g.startS })),
    energy: energy?.energy || {}, hits: energy?.hits || {},
  };
}

// A small offline envelope from the exact mix. These are frequency bands, not separated stems.
// The live analyser still supplies the floor's bass/mid/treble response during playback.
export function mixEnvelope(pcm, sampleRate = 8000, fps = 25) {
  const frames = Math.ceil(pcm.length / sampleRate * fps), block = sampleRate / fps;
  const mix = [], bass = [], drums = [];
  let low = 0, previous = 0;
  const alpha = 1 - Math.exp(-2 * Math.PI * 180 / sampleRate);
  for (let f = 0; f < frames; f++) {
    let m = 0, b = 0, d = 0, n = 0;
    for (let i = Math.floor(f * block); i < Math.min(pcm.length, Math.floor((f + 1) * block)); i++) {
      const v = pcm[i]; low += alpha * (v - low);
      m += v * v; b += low * low; d += (v - previous) ** 2; previous = v; n++;
    }
    mix.push(Math.sqrt(m / Math.max(1, n))); bass.push(Math.sqrt(b / Math.max(1, n))); drums.push(Math.sqrt(d / Math.max(1, n)));
  }
  const scale = a => {
    const sorted = [...a].sort((x, y) => x - y), peak = sorted[Math.floor(sorted.length * .98)] || 1;
    return a.map(v => Math.round(Math.min(1, v / peak) * 255));
  };
  const hits = { kick: [], snare: [], hats: [] };
  for (const [kind, a] of [['kick', bass], ['snare', drums]]) {
    const scaled = scale(a);
    let last = -1;
    for (let i = 1; i < a.length - 1; i++) {
      if (scaled[i] > 90 && a[i] > a[i - 1] * 1.08 && a[i] >= a[i + 1] && i / fps - last > .18) {
        hits[kind].push([i / fps, scaled[i] / 255]); last = i / fps;
      }
    }
  }
  return { energy: { fps, mix: scale(mix), bass: scale(bass), drums: scale(drums), vocals: [] }, hits };
}
