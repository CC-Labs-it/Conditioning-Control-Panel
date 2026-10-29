// Sparse road-word accents and tiny whole-glyph motion share the heard song clock.
import { songEvents } from './musicRoadMath.js';

const unit = value => Number.isFinite(value) ? Math.max(0, Math.min(1, value)) : 0;
const valueOf = value => value && typeof value === 'object' && 'value' in value ? value.value : value;
const FUNCTION_WORDS = new Set(('a an the and or but if then than as at by for from in into of on onto out over to up with without '
  + 'am is are was were be been being i me my mine we us our ours you your yours he him his she her hers it its they them their theirs '
  + 'this that these those who whom what which when where why how do does did have has had can could may might will would shall should must '
  + 'not no so just very too more less all any each both either neither some such only also about here there '
  + 'im youre theyre weve youve ive youd youll ill id dont doesnt didnt cant couldnt wont wouldnt thats theres lets').split(' '));
const hasContent = text => (text.match(/[a-z0-9]+(?:'[a-z0-9]+)*/gi) || [])
  .some(token => !FUNCTION_WORDS.has(token.toLowerCase().replaceAll("'", '')));

/** One highlight per phrase and event, picked once from eligible painted words, never from lyric text. */
export function planRoadWordAccents(song, script) {
  const out = new Float32Array(script?.length || 0);
  if (!song || !Array.isArray(script) || !script.length) return out;
  const beat = 60 / (Number.isFinite(song.bpm) && song.bpm > 0 ? song.bpm : 96);
  // The exact hit word may belong to a bubble. A nearby eligible floor word may take the accent,
  // at most two beats / 1.2 seconds away, without changing that word's time or its eligibility.
  const windowS = Math.min(1.2, Math.max(.25, beat * 2)), spacingS = Math.max(1.25, Math.min(3, beat * 4));
  const source = Array.isArray(song.triggers) && Array.isArray(song.sfx) ? song
    : { ...song, triggers: song.triggers || [], sfx: song.sfx || [] };
  const events = songEvents(source), moments = [];
  for (const time of events.drops) moments.push({ time, strength: 1 });
  for (const trigger of source.triggers) if (trigger.kind === 'word' && (trigger.strength ?? 1) >= .6)
    moments.push({ time: trigger.startS, strength: .65 });
  for (const section of song.sections || []) if (section.mood === 'peak')
    moments.push({ time: section.startS, strength: .65 });
  // Drops win a shared phrase or nearby moment even if a lesser accent occurs earlier in the song.
  moments.sort((a, b) => b.strength - a.strength || a.time - b.time);
  const phrases = new Set(), selected = [];
  for (const moment of moments) {
    if (!Number.isFinite(moment.time)) continue;
    let nearest = -1, distance = Infinity;
    for (let i = 0; i < script.length; i++) {
      const word = script[i];
      if (!word || !Number.isFinite(word.startS) || typeof word.text !== 'string' || !hasContent(word.text)) continue;
      const delta = Math.abs(word.startS - moment.time);
      if (delta <= windowS && delta < distance) { nearest = i; distance = delta; }
    }
    if (nearest < 0 || out[nearest]) continue;
    const word = script[nearest], phrase = word.phrase ?? nearest;
    if (phrases.has(phrase) || selected.some(time => Math.abs(time - word.startS) < spacingS)) continue;
    out[nearest] = moment.strength;
    phrases.add(phrase); selected.push(word.startS);
  }
  return out;
}

/** `reactive` may be the shared render uniforms, their visual state, or a direct analyser frame. */
export function frameRoadWordMusic(tS, mf, reactive, { gentle = false, motion = 'full' } = {}, out = {}) {
  const validTime = Number.isFinite(tS), time = validTime ? Math.max(0, tS) : 0;
  out.time = time; out.kick = out.snare = out.hats = out.vibration = 0;
  if (!validTime || gentle || motion === 'off') return out;
  const less = motion === 'less' ? .5 : 1;
  const visual = reactive?.visual || reactive;
  if (visual != null) {
    // An explicitly supplied silent source is authoritative. A chart cannot invent an audible hit.
    if (valueOf(visual.active) !== true) return out;
    out.kick = Math.max(unit(valueOf(visual.kick)), unit(visual.flux?.[0] * 3.5)) * less;
    out.snare = Math.max(unit(valueOf(visual.snare)), unit(visual.flux?.[1] * 3.5)) * less;
    out.hats = Math.max(unit(valueOf(visual.hats)), unit(visual.flux?.[2] * 3.5)) * less;
  } else if (mf?.on) {
    out.kick = unit(mf.kick) * less; out.snare = unit(mf.snare) * less; out.hats = unit(mf.hats) * less;
  }
  const vibration = Math.sin(time * Math.PI * 34) * out.kick * .045
    + Math.sin(time * Math.PI * 58) * out.snare * .022
    + Math.sin(time * Math.PI * 86) * out.hats * .009;
  out.vibration = Math.max(-.08, Math.min(.08, vibration));
  return out;
}
