/* render/musicRoadMath.js - the music road's conductor (handoff-1022/MUSIC-ROAD-BRIEF.md section 2). Pure.
 *
 * musicFrame(song, tS, ctx, out) turns the normalised chart (audio/chart.js) and the song position the
 * player hears into the numbers the road, rails and helix read this frame. Everything is a function of
 * tS alone (no accumulated frame time), so a pause, a seek or a slow frame can never drift the picture off
 * the music. Modes come from the section: break goes calm and glassy, a repetitive groove (high `loop`)
 * winds the DNA helix round the road, drop / peak go big. A mode change crossfades over the first bar
 * of the new section (its IN) while the old one leaves (its OUT), so it lands on the section edge, a bar
 * line, never mid-beat. Events: a drop sends a pressure wave down the road that reaches the kart on the
 * drop, a trigger word shatters the road into a waveform, kicks ripple the lanes outward, the hits shake
 * the lines, the bass breathes the edge lanes, the vocals draw the edge lines, echoes shimmer the edge on
 * their side. Gentle: smaller, slower, no shatter flash (the caller asks photosafe for every flash).
 */
import { beatOf, barOf, sectionOf, hitEnv, energyOf, lastAtOrBefore } from '../audio/chart.js';

export const MUSIC_LOOK = Object.freeze({
  // line vibration: lateral metres at full hit strength, its wavelength along the road and speed
  vibM: 0.32, vibLambdaM: 7, vibHz: 2.2, vibGentle: 0.35,
  // kick ripple: rings running from the centre line out to the edges
  rippleMin: 0.35, rippleMps: 16, rippleLifeS: 0.55,
  // drop pressure wave: starts waveAheadM ahead one bar before the drop, reaches the kart on it
  waveAheadM: 150, waveTailS: 0.8, waveGentle: 0.5,
  // trigger shatter: rise, hold and fall in bars
  shatterRiseS: 0.05, shatterBars: 2, shatterGentle: 0.45,
  // edges draw the melody: lateral metres at full vocals, and the boost a shatter gives
  melodyM: 0.55, melodyShatterM: 1.6,
  // bass breath of the edge lanes (width share)
  bassWidth: 0.7,
  // helix: section loop share that starts and fills it
  helixLoop: [0.8, 0.9], helixSpinHz: 0.25, helixGentle: 0.5,
  // echo shimmer
  echoGain: 0.9, echoFadeS: 0.25,
});

const ON_MOODS_HELIX = new Set(['build', 'drop', 'groove', 'peak']);

/** Mode weights of one section: { calm, helix, big } each 0..1. */
export function sectionModes(sec, L = MUSIC_LOOK, out = { calm: 0, helix: 0, big: 0 }) {
  if (!sec) { out.calm = out.helix = out.big = 0; return out; }
  const m = sec.mood;
  out.calm = m === 'break' ? 1 : m === 'intro' || m === 'outro' ? 0.4 : 0;
  const lo = L.helixLoop[0], hi = L.helixLoop[1];
  const k = Math.max(0, Math.min(1, (sec.loop - lo) / (hi - lo)));
  out.helix = ON_MOODS_HELIX.has(m) ? k * k * (3 - 2 * k) : 0;
  out.big = m === 'drop' || m === 'peak' ? 1 : 0;
  return out;
}

const smooth = (x) => { const k = Math.max(0, Math.min(1, x)); return k * k * (3 - 2 * k); };

// per song: the drop times (triggers kind drop and sfx kind drop, merged within a beat) and the trigger words
const EVENTS = new WeakMap();
export function songEvents(song) {
  let e = EVENTS.get(song);
  if (e) return e;
  // a drop swell sfx lands on its hitS (its startS is where the swell starts), so it merges with its trigger
  const drops = [];
  for (const t of song.triggers) if (t.kind === 'drop') drops.push([t.startS, t.strength]);
  for (const s of song.sfx) if (s.kind === 'drop') drops.push([s.hitS, -1]);   // no strength of its own: the trigger's wins
  drops.sort((a, b) => a[0] - b[0]);
  const beat = 60 / song.bpm, merged = [];
  for (const d of drops) {
    const last = merged[merged.length - 1];
    if (!last || d[0] - last[0] > beat) merged.push(d.slice());
    else last[1] = Math.max(last[1], d[1]);
  }
  const words = [];
  for (const t of song.triggers) if (t.kind === 'word') words.push(t.startS);
  e = { drops: Float64Array.from(merged, (d) => d[0]), dropStrength: Float32Array.from(merged, (d) => (d[1] < 0 ? 1 : d[1])), words: Float64Array.from(words) };
  EVENTS.set(song, e);
  return e;
}

/** A zeroed frame: every number the road reads; `on` false means the road plays as it always did. */
export function createMusicFrame() {
  return { on: false, tS: 0, beatPhase: 0, beatIndex: 0, barPhase: 0, barIndex: 0, barLenS: 2,
    kick: 0, snare: 0, hats: 0, bass: 0, vocals: 0, mix: 0,
    calm: 0, helix: 0, big: 0, sectionId: '',
    vibM: 0, rippleAge: 9, rippleAmp: 0,
    waveD: 0, waveAmp: 0, waveIdx: -1,
    shatter: 0, shatterIdx: -1, melodyM: 0,
    echoL: 0, echoR: 0, helixPhase: 0 };
}

const REST = Object.freeze(createMusicFrame());
const A = { calm: 0, helix: 0, big: 0 }, B = { calm: 0, helix: 0, big: 0 };
const TMP = { index: 0, phase: 0, lenS: 2 };
const BAR = { index: 0, phase: 0, lenS: 2 }, BEAT = { index: 0, phase: 0, lenS: 0.5 };

/**
 * One frame of the music road. ctx: { gentle }. Returns out (createMusicFrame shape). song null or tS
 * not finite: out.on = false and every effect at rest.
 */
export function musicFrame(song, tS, { gentle = false } = {}, out = createMusicFrame(), L = MUSIC_LOOK) {
  if (!song || !Number.isFinite(tS)) return Object.assign(out, REST);
  out.on = true; out.tS = tS;
  beatOf(song, tS, BEAT); barOf(song, tS, BAR);
  out.beatPhase = BEAT.phase; out.beatIndex = BEAT.index;
  out.barPhase = BAR.phase; out.barIndex = BAR.index; out.barLenS = BAR.lenS;
  out.kick = hitEnv(song, 'kick', tS, 0.012, 0.11);
  out.snare = hitEnv(song, 'snare', tS, 0.012, 0.14);
  out.hats = hitEnv(song, 'hats', tS, 0.006, 0.05);
  out.bass = energyOf(song, 'bass', tS);
  out.vocals = energyOf(song, 'vocals', tS);
  out.mix = energyOf(song, 'mix', tS);

  // modes: the section's weights, crossfaded from the previous section's over the first bar
  const secs = song.sections;
  const sec = sectionOf(song, tS);
  out.sectionId = sec ? sec.id : '';
  sectionModes(sec, L, B);
  const i = sec ? secs.indexOf(sec) : -1;
  let f = 1;
  if (i > 0 && tS >= sec.startS) {
    sectionModes(secs[i - 1], L, A);
    f = smooth((tS - sec.startS) / barOf(song, sec.startS + 1e-3, TMP).lenS);
  } else { A.calm = B.calm; A.helix = B.helix; A.big = B.big; }
  out.calm = A.calm + (B.calm - A.calm) * f;
  out.helix = A.helix + (B.helix - A.helix) * f;
  out.big = A.big + (B.big - A.big) * f;

  const g = gentle;
  // the lines shake with the hits, less in a calm stretch, more when it goes big
  out.vibM = L.vibM * Math.min(1, out.kick * 0.9 + out.snare * 0.55 + out.hats * 0.15)
    * (1 - out.calm * 0.8) * (1 + out.big * 0.5) * (g ? L.vibGentle : 1);

  // kick ripple: the last strong kick at or before now
  const kh = song.hits.kick;
  let ki = lastAtOrBefore(kh.t, tS);
  while (ki >= 0 && kh.v[ki] < L.rippleMin && tS - kh.t[ki] < L.rippleLifeS) ki--;
  if (ki >= 0 && tS - kh.t[ki] < L.rippleLifeS) {
    out.rippleAge = tS - kh.t[ki];
    out.rippleAmp = kh.v[ki] * (1 - out.rippleAge / L.rippleLifeS) * (1 - out.calm * 0.7) * (g ? 0.5 : 1);
  } else { out.rippleAge = 9; out.rippleAmp = 0; }

  // drop pressure wave: the next drop within a bar, or the one just passed within the tail
  const ev = songEvents(song);
  out.waveAmp = 0; out.waveD = 0; out.waveIdx = -1;
  const di = lastAtOrBefore(ev.drops, tS);
  const next = di + 1 < ev.drops.length ? di + 1 : -1;
  if (next >= 0) {
    const T = ev.drops[next], lead = barOf(song, T - 1e-3, TMP).lenS;
    if (T - tS <= lead) {
      const k = 1 - (T - tS) / lead;                     // 0 one bar out, 1 on the drop
      out.waveD = L.waveAheadM * (1 - k); out.waveAmp = smooth(k * 3) * (0.5 + 0.5 * k); out.waveIdx = next;
    }
  }
  if (out.waveIdx < 0 && di >= 0 && tS - ev.drops[di] < L.waveTailS) {
    const age = tS - ev.drops[di];
    out.waveD = -age * L.waveAheadM / Math.max(0.2, out.barLenS);   // runs on past the kart at the same speed
    out.waveAmp = 1 - smooth(age / L.waveTailS); out.waveIdx = di;
  }
  if (out.waveIdx >= 0) out.waveAmp *= 0.6 + 0.4 * ev.dropStrength[out.waveIdx];
  if (g) out.waveAmp *= L.waveGentle;

  // trigger shatter: rises in shatterRiseS (Gentle: its riseMs floor comes from the caller), holds, falls
  out.shatter = 0; out.shatterIdx = -1;
  const wi = lastAtOrBefore(ev.words, tS);
  if (wi >= 0) {
    const age = tS - ev.words[wi], len = L.shatterBars * out.barLenS;
    if (age < len) {
      const rise = g ? 0.3 : L.shatterRiseS;
      out.shatter = Math.min(1, age / rise) * (1 - smooth((age - len * 0.35) / (len * 0.65))) * (g ? L.shatterGentle : 1);
      out.shatterIdx = wi;
    }
  }
  out.melodyM = (L.melodyM * out.vocals * (1 - out.calm * 0.5) + L.melodyShatterM * out.shatter) * (g ? 0.6 : 1);

  // echoes: an envelope over each layered phrase on its own side
  out.echoL = 0; out.echoR = 0;
  const eiLast = lastAtOrBefore(song.echoStarts, tS);
  for (let k = eiLast; k >= 0 && k > eiLast - 4; k--) {
    const e = song.echoes[k];
    if (tS > e.endS + L.echoFadeS) continue;
    const inK = Math.min(1, (tS - e.startS) / L.echoFadeS), outK = Math.min(1, (e.endS + L.echoFadeS - tS) / L.echoFadeS);
    const v = Math.max(0, Math.min(inK, outK)) * Math.max(0.35, Math.abs(e.pan)) * L.echoGain * (g ? 0.5 : 1);
    if (e.pan < 0) out.echoL = Math.max(out.echoL, v); else out.echoR = Math.max(out.echoR, v);
  }

  // helix turn: locked to the bar grid so it turns with the groove (whole turns per four bars)
  out.helixPhase = (out.barIndex + out.barPhase) * 0.25 * Math.PI * 2 * (g ? L.helixGentle : 1);
  return out;
}
