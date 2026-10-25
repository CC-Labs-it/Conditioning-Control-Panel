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
 * Polish 5 (S3): playChart(url, { align }) lands Go on a bar downbeat. align(song) answers the song position the
 * player should hear NOW (goDownbeat(song, goInS) less the seconds left to Go, the countdown's own clock);
 * the source is scheduled later (a short silence) or started part way in to match. Go itself never moves
 * (online shares it through the room's startAt). No align, or a picked file: the song starts at once, as before.
 * Polish 6 (BEAT): the voice registers itself as the live music (audio/songShape.js setLiveMusic) so the beat
 * shake reads songS and the song with no extra wiring. A picked file has no chart: its decoded samples get one
 * analysis (songShape.analyzeBuffer: tempo, beats, bar energy), cached per buffer, exposed as `analysis`.
 */
import { mixGet } from './mix.js';
import { normalizeChart } from './chart.js';
import { analyzeBuffer, setLiveMusic, liveMusic } from './songShape.js';
import { createMusicReactivity } from './reactivity.js';

/** The one-time analysis of an AudioBuffer-like ({ numberOfChannels, sampleRate, getChannelData(i) }), or null. */
export function analyzeAudioBuffer(buf) {
  try {
    if (!buf || typeof buf.getChannelData !== 'function') return null;
    const chs = [];
    for (let i = 0; i < Math.min(buf.numberOfChannels || 1, 2); i++) chs.push(buf.getChannelData(i));
    return analyzeBuffer(chs, buf.sampleRate);
  } catch (e) { console.warn('[music] analysis', e); return null; }
}

/** Beat at song time tS: { bpm, phase 0..1, index } (index < 0 before the first downbeat). */
export function beatAt(chart, tS) {
  const bpm = chart && chart.bpm > 0 ? chart.bpm : 96;
  const pos = ((Number(tS) || 0) - ((chart && Number(chart.downbeatS)) || 0)) * bpm / 60;
  const index = Math.floor(pos);
  return { bpm, phase: pos - index, index };
}

/**
 * The bar downbeat Go should land on when the song would naturally start goInS seconds before Go: the last
 * downbeat at or before goInS (the song waits under a bar, its pickup leads into Go), else the first one
 * (the song starts that far in). Downbeats from the chart list, else every four beats from downbeatS.
 */
export function goDownbeat(song, goInS) {
  const go = Math.max(0, Number(goInS) || 0);
  const list = song && song.downbeats && song.downbeats.length ? song.downbeats : null;
  if (list) {
    let d = list[0];
    for (let i = 0; i < list.length && list[i] <= go + 1e-9; i++) d = list[i];
    return d;
  }
  const bpm = song && song.bpm > 0 ? song.bpm : 96, bar = 240 / bpm, first = Math.max(0, (song && Number(song.downbeatS)) || 0);
  return go < first ? first : first + Math.floor((go - first) / bar + 1e-9) * bar;
}

/**
 * When to start the source for an aligned song: the song position heard at context time `now` must be posNow.
 * startT = the context time the song's 0 s left the source (songTime's base). @returns {{ startT, when, offset }}
 * (source.start(when, offset)); a future startT waits, a past one starts now that far in (wrapped on durS).
 */
export function alignStart(now, latency, posNow, durS = 0) {
  const startT = now - Math.max(0, latency || 0) - (Number(posNow) || 0);
  if (startT >= now) return { startT, when: startT, offset: 0 };
  let offset = now - startT;
  if (durS > 0) offset %= durS;
  return { startT, when: now, offset };
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
  const reactive = createMusicReactivity(ctx, gain);
  const reactiveOptions = { playing: false, muted: false, paused: false, gentle: false, songS: 0 };
  let src = null, startT = 0, durS = 0, chart = null, song = null, bpm = 96, volume = 1, token = 0;
  let cached = null;   // { url, buffer, chart, song }: the last chart's decode, reused by a restart
  let cachedFile = null; // One personally selected File, retained across course transitions.
  let analysis = null, analyzed = null;   // a picked file's beat analysis, and the buffer it belongs to
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
    reactive.clear();
    const t = ctx.currentTime;
    try {
      gain.gain.cancelScheduledValues(t);
      gain.gain.setTargetAtTime(0, t, Math.max(fadeS / 3, 0.01));
      s.stop(t + fadeS + 0.05);
    } catch (e) { try { s.stop(); } catch (e2) { /* gone */ } }
  }

  function start(buffer, c, b, align = null) {
    stopSrc(g('musicFadeOutS'));
    const s = ctx.createBufferSource();
    s.buffer = buffer; s.loop = true;
    s.connect(gain);
    s.onended = () => {
      try { s.disconnect(); } catch (e) { /* gone */ }
      if (src === s) { src = null; reactive.clear(); }
    };
    const t = ctx.currentTime;
    durS = buffer && buffer.duration > 0 ? buffer.duration : 0;
    chart = c; bpm = b; song = c ? (cached && cached.chart === c ? cached.song : normalizeChart(c)) : null;
    if (c) analysis = null;
    else if (analyzed !== buffer) { analysis = analyzeAudioBuffer(buffer); analyzed = buffer; }
    let pos = null;
    if (song && typeof align === 'function') { try { pos = align(song); } catch (e) { pos = null; } }
    if (Number.isFinite(pos)) {
      const a = alignStart(t, latency(), pos, durS);
      s.start(a.when, a.offset);
      startT = a.startT;
    } else { s.start(t); startT = t; }
    src = s;
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

  const api = {
    /**
     * Load and loop a chart's track. false when anything is missing (no chart ships in B1).
     * opts.align(song) -> the song position to hear now (see goDownbeat), asked once the audio is decoded.
     */
    async playChart(chartUrl, { align = null } = {}) {
      const my = ++token;
      try {
        if (cached && cached.url === String(chartUrl)) return start(cached.buffer, cached.chart, cached.chart.bpm, align);
        if (typeof fetchFn !== 'function') return false;
        const r = await fetchFn(chartUrl);
        if (!r || !r.ok) return false;
        const c = await r.json();
        if (my !== token || !validChart(c)) return false;
        const a = await fetchFn(chartAudioUrl(chartUrl, c));
        if (!a || !a.ok) return false;
        const bytes = await a.arrayBuffer();
        if (my !== token) return false;
        const buf = await decode(bytes);
        if (my !== token) return false;           // a later play or stop won
        cached = { url: String(chartUrl), buffer: buf, chart: c, song: normalizeChart(c) };
        return start(buf, c, c.bpm, align);
      } catch (e) { console.warn('[music] chart', e); return false; }
    },
    /** Play a File the player picked. The beat runs at opts.bpm (default the game's clock.bpm). */
    async playFile(file, { bpm } = {}) {
      const my = ++token;
      try {
        if (!file || typeof file.arrayBuffer !== 'function') return false;
        const b = bpm > 0 ? bpm : feelBpm();
        if (cachedFile?.file === file) return start(cachedFile.buffer, null, b);
        const bytes = await file.arrayBuffer();
        if (my !== token) return false;
        const buf = await decode(bytes);
        if (my !== token) return false;
        cachedFile = { file, buffer: buf };
        return start(buf, null, b);
      } catch (e) { console.warn('[music] file', e); return false; }
    },
    /** Abandon an old course's asynchronous selection without stopping its current recording. */
    cancelPending() { token++; },
    stop() { token++; stopSrc(g('musicFadeOutS')); chart = null; song = null; },
    setVolume(v) {
      volume = Math.max(0, Math.min(1, Number(v) || 0));
      if (volume === 0) reactive.clear(songTime());
      if (src) { try { gain.gain.setTargetAtTime(volume, ctx.currentTime, 0.05); } catch (e) { gain.gain.value = volume; } }
    },
    get playing() { return !!src; },
    /** Persistent 0..1 music-only envelope. readReactivity updates it once per render. */
    get reactivity() { return reactive.frame; },
    readReactivity(dt, opts = null) {
      reactiveOptions.playing = !!src;
      reactiveOptions.muted = volume === 0 || !!opts?.muted;
      reactiveOptions.paused = !!opts?.paused;
      reactiveOptions.gentle = !!opts?.gentle;
      reactiveOptions.songS = songTime();
      return reactive.read(dt, reactiveOptions);
    },
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
    /** A picked file's beat analysis (songShape.analyzeBuffer), null for a chart or nothing playing. */
    get analysis() { return src && !song ? analysis : null; },
    dispose() { token++; cached = cachedFile = analyzed = analysis = null; stopSrc(0.05); reactive.dispose(); try { gain.disconnect(); } catch (e) { /* gone */ } if (liveMusic() === api) setLiveMusic(null); },
  };
  setLiveMusic(api);
  return api;
}
