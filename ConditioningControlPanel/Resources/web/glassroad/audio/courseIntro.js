// Course reveals get a short stereo atmosphere on the existing SFX bus. No song or spoken voice.
import { COURSE_INTRO_SECONDS } from '../render/courseIntroMath.js';

export const INTRO_SOUND = Object.freeze({ seconds: COURSE_INTRO_SECONDS, sampleRate: 24000, fadeS: .035 });
const TAU = Math.PI * 2, clamp = x => Math.max(0, Math.min(1, x));
const COURSES = new Set(['threshold', 'mainframe', 'keep', 'boudoir', 'goonworks']);

/** The same PCM supplies live playback and preview exports. Only the current course is cached. */
export function renderCourseIntroAudio(courseId, sampleRate = INTRO_SOUND.sampleRate) {
  const course = COURSES.has(courseId) ? courseId : 'threshold';
  const rate = Math.max(8000, Math.min(48000, Math.round(sampleRate) || INTRO_SOUND.sampleRate));
  const length = Math.round(INTRO_SOUND.seconds * rate), channels = [new Float32Array(length), new Float32Array(length)];
  let seed = 1029;
  const noise = () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed / 2147483648 - 1; };
  function voice(at, duration, level, pan, sound, attack = .012, decay = .4) {
    const start = Math.round(at * rate), count = Math.ceil(duration * rate);
    const left = Math.sqrt((1 - pan) / 2), right = Math.sqrt((1 + pan) / 2);
    for (let i = 0; i < count && start + i < length; i++) {
      const t = i / rate, envelope = (1 - Math.exp(-t / attack)) * Math.exp(-t / decay) * clamp((duration - t) / .12);
      const value = sound(t, i) * envelope * level;
      channels[0][start + i] += value * left; channels[1][start + i] += value * right;
    }
  }
  function bell(at, hz, level, pan, duration = 1.4) {
    voice(at, duration, level, pan, t => Math.sin(TAU * hz * t) + .27 * Math.sin(TAU * hz * 2.71 * t) * Math.exp(-t / .15), .008, .35);
  }
  function thump(at, level, pan, pitch = 54) {
    voice(at, .8, level, pan, t => Math.sin(TAU * (pitch * t + 48 * .04 * (1 - Math.exp(-t / .04))))
      + noise() * .28 * Math.exp(-t / .025), .006, .17);
  }
  function air(at, duration, level, pan, high = false) {
    let filtered = 0;
    voice(at, duration, level, pan, () => { const raw = noise(); filtered += (raw - filtered) * .06; return high ? (raw - filtered) * .3 : filtered; }, .09, duration * .65);
  }
  function sweep(at, duration, from, to, level, pan) {
    const slope = (to - from) / duration;
    voice(at, duration, level, pan, t => Math.sin(TAU * (from * t + slope * t * t / 2)), .035, duration * .75);
  }

  // Quiet continuous body bridges each cut. Threshold's two ears carry a six-Hz pitch difference.
  for (let i = 0; i < length; i++) {
    const t = i / rate, breath = .8 + .2 * Math.sin(TAU * t / 7);
    for (let ch = 0; ch < 2; ch++) {
      const hz = course === 'threshold' ? 174 + ch * 6 : course === 'mainframe' ? 62 + ch * .4
        : course === 'keep' ? 87 + ch * .6 : course === 'boudoir' ? 220 + ch * 1.5 : 110 + ch;
      const level = course === 'threshold' ? .024 : .009;
      channels[ch][i] = level * breath * (Math.sin(TAU * hz * t) + .18 * Math.sin(TAU * hz * 1.5 * t));
    }
  }
  for (let shot = 0; shot < 4; shot++) {
    const at = shot * 2.5, pan = shot % 2 ? .55 : -.55;
    if (course === 'mainframe') {
      thump(at + .12, .17, pan); thump(at + 1.35, .105, -pan, 68);
      sweep(at + .36, 1.05, 165 + shot * 30, 62, .026, -pan);
      air(at + .08, 1.4, shot === 2 ? .16 : .08, pan);
      for (let i = 0; i < 3; i++) bell(at + .7 + i * .16, 520 + i * 130, .019, pan * (1 - i));
    } else if (course === 'threshold') {
      [880, 1174.66, 1320, 1760].forEach((hz, i) => {
        bell(at + .1 + i * .24, hz * (shot === 2 ? .75 : 1), .048, i % 2 ? .8 : -.8);
        bell(at + .55 + i * .24, hz, .012, i % 2 ? -.7 : .7);
      });
      air(at + .1, 1.8, .027, pan, true);
    } else if (course === 'keep') {
      thump(at + .14, .115, pan, 43); bell(at + .18, 146.83, .052, -pan, 2.1);
      for (let i = 0; i < 4; i++) bell(at + .65 + i * .13, 680 + (i % 2) * 370, .024, pan);
      air(at + .3, 1.9, .085, -pan);
    } else if (course === 'boudoir') {
      air(at + .13, 1.7, .09, pan, true);
      [659.25, 987.77, 1318.51].forEach((hz, i) => bell(at + .22 + i * .41, hz, .043, i % 2 ? -pan : pan));
      sweep(at + .95, .55, 420, 240, .012, -pan);
    } else {
      sweep(at + .14, .5, 175, 62, .09, pan);
      [0, 1, 2].forEach(i => sweep(at + .8 + i * .22, .15, 320 + i * 75, 930, .033, i % 2 ? -pan : pan));
      thump(at + 1.55, .075, -pan, 80); air(at + .4, 1.25, .06, pan);
    }
  }
  // A soft landing resolves the reveal before countdown. No sound carries into the song at GO.
  for (let ch = 0; ch < 2; ch++) for (let i = 0; i < length; i++) {
    const t = i / rate, edge = clamp(t / .3) * clamp((INTRO_SOUND.seconds - t) / .8);
    channels[ch][i] *= edge * edge;
  }
  return { course, sampleRate: rate, duration: length / rate, channels };
}

export function createCourseIntroAudio(ctx, out) {
  let buffer = null, cachedCourse = null, voice = null, retiring = null, dead = false;
  function prepare(courseId) {
    if (dead) return;
    const course = COURSES.has(courseId) ? courseId : 'threshold';
    if (course === cachedCourse && buffer) return;
    const rendered = renderCourseIntroAudio(course);
    buffer = ctx.createBuffer(2, rendered.channels[0].length, rendered.sampleRate);
    rendered.channels.forEach((samples, ch) => buffer.getChannelData(ch).set(samples)); cachedCourse = course;
  }
  function drop(v) {
    v.source.onended = null; v.source.disconnect(); v.gain.disconnect();
    if (voice === v) voice = null;
    if (retiring === v) retiring = null;
  }
  // a retiring voice finishes its own fade and scheduled stop (#3084: a per-frame stop cut it ~16 ms in = a click);
  // it is dropped once the clock passes its stop time even if onended never arrives
  function settle() { if (retiring && ctx.currentTime >= retiring.endsAt) drop(retiring); }
  function stop(immediate = false) {
    if (immediate) { for (const v of [retiring, voice]) if (v) { try { v.source.stop(); } catch { /* ended */ } drop(v); } return; }
    settle();
    if (!voice) return;
    const v = voice; voice = null; retiring = v; v.endsAt = ctx.currentTime + INTRO_SOUND.fadeS;
    v.gain.gain.cancelScheduledValues(ctx.currentTime);
    v.gain.gain.setTargetAtTime(0, ctx.currentTime, INTRO_SOUND.fadeS / 4);
    v.source.stop(v.endsAt);
  }
  return {
    prepare,
    update(courseId, elapsed, { active = false, paused = false, gentle = false } = {}) {
      if (dead) return;
      if (!active || paused || ctx.state !== 'running' || !Number.isFinite(elapsed) || elapsed < 0 || elapsed >= INTRO_SOUND.seconds) { stop(); return; }
      const course = COURSES.has(courseId) ? courseId : 'threshold';
      settle();
      if (voice && (course !== voice.course || Math.abs(voice.offset + ctx.currentTime - voice.started - elapsed) > .12)) stop();
      // one voice at a time: a re-entry waits out the retiring fade (a frame or two), then starts at the live offset
      if (!voice && retiring) return;
      if (!voice) {
        prepare(course);
        const source = ctx.createBufferSource(), gain = ctx.createGain();
        source.buffer = buffer; source.connect(gain); gain.connect(out); gain.gain.value = 0;
        voice = { source, gain, course, offset: elapsed, started: ctx.currentTime };
        const v = voice; source.onended = () => drop(v); source.start(ctx.currentTime, elapsed);
      }
      const level = gentle ? .55 : 1;
      if (voice.level !== level) { voice.gain.gain.setTargetAtTime(level, ctx.currentTime, .04); voice.level = level; }
    },
    stop,
    get inspect() { return { playing: !!voice, course: cachedCourse, voices: Number(!!voice) + Number(!!retiring), offset: voice?.offset ?? null }; },
    dispose() { if (dead) return; dead = true; stop(true); buffer = null; cachedCourse = null; },
  };
}
