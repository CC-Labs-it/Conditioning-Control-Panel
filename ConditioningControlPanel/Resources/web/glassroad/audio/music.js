/* audio/music.js - one music voice on the shared context (CONTRACT-B1 section 3).
 *
 * playChart(url) loads a chart.json (handoff-1022/RACE-AUDIO-BRIEF.md: bpm, downbeatS, sections)
 * and its audio (chart.audio relative to the chart, else the chart's basename with .mp3), decodes
 * it and loops it. playFile(file) plays a local File the player picked, beat from a set bpm.
 * `beat` reads the song position off the context clock, so a paused (suspended) context holds the
 * beat too. B1 sets only the bpm on the game clock; there is no phase lock yet. No chart ships in B1.
 * No speech is made here: the music is whatever the file holds.
 * Music road (handoff-1022/MUSIC-ROAD-BRIEF.md): `song` is the normalised chart (audio/chart.js) and
 * `songS` the position the player HEARS (context time minus outputLatency), the one clock every music
 * visual reads. A chart's decoded audio is kept, so playing the same chart again restarts it at once.
 */
import { mixGet } from './mix.js';
import { normalizeChart } from './chart.js';

/** Beat at song time tS: { bpm, phase 0..1, index } (index < 0 before the first downbeat). */
export function beatAt(chart, tS) {
  const bpm = chart && chart.bpm > 0 ? chart.bpm : 96;
  const pos = ((Number(tS) || 0) - ((chart && Number(chart.downbeatS)) || 0)) * bpm / 60;
  const index = Math.floor(pos);
  return { bpm, phase: pos - index, index };
}

/** Index of the chart section playing at tS (the last one started), 0 before any. */
export function sectionAt(chart, tS) {
  const list = (chart && Array.isArray(chart.sections)) ? chart.sections : [];
  let idx = 0;
  for (let k = 0; k < list.length; k++) if ((Number(list[k].startS) || 0) <= tS) idx = k;
  return idx;
}

/** The audio url for a chart: chart.audio relative to the chart url, else <basename>.mp3. */
export function chartAudioUrl(chartUrl, chart) {
  const base = String(chartUrl);
  if (chart && typeof chart.audio === 'string' && chart.audio) {
    try { return new URL(chart.audio, base).href; } catch (e) { /* a relative base */ }
    const dir = base.slice(0, base.lastIndexOf('/') + 1);
    return dir + chart.audio;
  }
  const q = base.search(/[?#]/), path = q >= 0 ? base.slice(0, q) : base;
  const dot = path.lastIndexOf('.'), slash = path.lastIndexOf('/');
  return (dot > slash ? path.slice(0, dot) : path) + '.mp3';
}

/** A chart is usable when it has a positive bpm and a sections list. */
export function validChart(c) {
  return !!c && typeof c === 'object' && c.bpm > 0 && Array.isArray(c.sections);
}

/** @returns {Music} */
export function createMusic(ctx, out, feel, { fetchFn = globalThis.fetch } = {}) {
  const gain = ctx.createGain();
  gain.gain.value = 0;
  gain.connect(out);
  let src = null, startT = 0, durS = 0, chart = null, song = null, bpm = 96, volume = 1, token = 0;
  let cached = null;   // { url, buffer, chart, song }: the last chart's decode, reused by a restart
  const g = (k) => mixGet(feel, k);
  const feelBpm = () => { try { return feel.get('clock.bpm'); } catch (e) { return 96; } };

  // what the speakers play now: the context clock less the output path (outputLatency, else baseLatency)
  const latency = () => { const o = ctx.outputLatency; return Number.isFinite(o) && o > 0 ? o : (Number.isFinite(ctx.baseLatency) ? ctx.baseLatency : 0); };
  function songTime() {
    if (!src) return 0;
    const t = ctx.currentTime - startT - latency();
    if (t < 0) return 0;
    return durS > 0 ? t % durS : t;
  }

  function stopSrc(fadeS) {
    if (!src) return;
    const s = src; src = null;
    const t = ctx.currentTime;
    try {
      gain.gain.cancelScheduledValues(t);
      gain.gain.setTargetAtTime(0, t, Math.max(fadeS / 3, 0.01));
      s.stop(t + fadeS + 0.05);
    } catch (e) { try { s.stop(); } catch (e2) { /* gone */ } }
  }

  function start(buffer, c, b) {
    stopSrc(g('musicFadeOutS'));
    const s = ctx.createBufferSource();
    s.buffer = buffer; s.loop = true;
    s.connect(gain);
    const t = ctx.currentTime;
    s.start(t);
    src = s; startT = t; durS = buffer && buffer.duration > 0 ? buffer.duration : 0;
    chart = c; bpm = b; song = c ? (cached && cached.chart === c ? cached.song : normalizeChart(c)) : null;
    gain.gain.cancelScheduledValues(t);
    gain.gain.setValueAtTime(0, t);
    gain.gain.setTargetAtTime(volume, t, Math.max(g('musicFadeInS') / 3, 0.01));
    return true;
  }

  async function decode(bytes) {
    // the promise form, with the callback form for old Safari
    return await new Promise((res, rej) => {
      const p = ctx.decodeAudioData(bytes, res, rej);
      if (p && typeof p.then === 'function') p.then(res, rej);
    });
  }

  return {
    /** Load and loop a chart's track. false when anything is missing (no chart ships in B1). */
    async playChart(chartUrl) {
      const my = ++token;
      try {
        if (cached && cached.url === String(chartUrl)) return start(cached.buffer, cached.chart, cached.chart.bpm);
        if (typeof fetchFn !== 'function') return false;
        const r = await fetchFn(chartUrl);
        if (!r || !r.ok) return false;
        const c = await r.json();
        if (!validChart(c)) return false;
        const a = await fetchFn(chartAudioUrl(chartUrl, c));
        if (!a || !a.ok) return false;
        const buf = await decode(await a.arrayBuffer());
        if (my !== token) return false;           // a later play or stop won
        cached = { url: String(chartUrl), buffer: buf, chart: c, song: normalizeChart(c) };
        return start(buf, c, c.bpm);
      } catch (e) { console.warn('[music] chart', e); return false; }
    },
    /** Play a File the player picked. The beat runs at opts.bpm (default the game's clock.bpm). */
    async playFile(file, { bpm } = {}) {
      const my = ++token;
      try {
        if (!file || typeof file.arrayBuffer !== 'function') return false;
        const buf = await decode(await file.arrayBuffer());
        if (my !== token) return false;
        const b = bpm > 0 ? bpm : feelBpm();
        return start(buf, null, b);
      } catch (e) { console.warn('[music] file', e); return false; }
    },
    stop() { token++; stopSrc(g('musicFadeOutS')); chart = null; song = null; },
    setVolume(v) {
      volume = Math.max(0, Math.min(1, Number(v) || 0));
      if (src) { try { gain.gain.setTargetAtTime(volume, ctx.currentTime, 0.05); } catch (e) { gain.gain.value = volume; } }
    },
    get playing() { return !!src; },
    get chart() { return chart; },
    /** The normalised chart (audio/chart.js) of the playing track, or null (a picked file has none). */
    get song() { return src ? song : null; },
    /** Song position the player hears, seconds (wraps on the loop); 0 when nothing plays. */
    get songS() { return songTime(); },
    /** Song position in seconds (wraps on the loop). */
    get timeS() { return songTime(); },
    get beat() {
      if (!src) return null;
      return beatAt(chart || { bpm, downbeatS: 0 }, songTime());
    },
    get sectionIdx() { return chart ? sectionAt(chart, songTime()) : 0; },
    dispose() { token++; stopSrc(0.05); try { gain.disconnect(); } catch (e) { /* gone */ } },
  };
}
