/* audio/feelVoices.js - the feel wave voices (2026-10-26): big-hit sub thump + duck, the drift squeal
 * meter and its release notes, the final-lap percussion layer, the rocket-start rev and launch
 * stingers, the hitter chime and positional incoming hits, and the wind layer.
 *
 * Same rules as audio/bus.js: one context, one clock (ctx.currentTime, on the playing song's beat
 * grid when a song plays, else the feel.clock.bpm grid), one variable per voice (drift charge for the
 * squeal, speedPct for the wind, rev level for the rev). Physical impacts play now; pitched notes wait
 * for the next grid step. Every level is a sample gain on its own node, never the master. Every held
 * voice glides in and out (setTargetAtTime), nothing snaps.
 *
 * Events it hears: wall, bump, item-hit (big hits), drift-release, lap, finish, go, rev, launch,
 * hitter-hit. rev / launch / hitter-hit / lap.final come from the race-moments lane; when they never
 * fire nothing here plays. The pure maths below is what tests/audio-feel-wave.test.mjs pins.
 */
import { mixGet } from './mix.js';
import { noiseBuffer, SOUND } from './engine.js';

const clamp01 = (x) => (x > 0 ? (x < 1 ? x : 1) : 0);
const hz = (semis) => SOUND.rootHz * Math.pow(2, semis / 12);

/** Pitch variants for the sub thump: a fixed 4-step cycle, +-4% at most (never Math.random). */
export const HIT_VARIANTS = [0, 0.04, -0.035, 0.018];
export const hitVariantRate = (k) => 1 + HIT_VARIANTS[Math.abs(k | 0) % HIT_VARIANTS.length];

/**
 * How big a hit is for the mix: { thump 0..1, duck gain floor 0..1 } or null (not a big hit).
 * Floors from audio.hitDuckWall / hitDuckItem / hitDuckBump (lower = deeper).
 */
export function hitSize(name, p = {}, feel = null) {
  p = p || {};
  if (name === 'wall') {
    let full = 12; try { full = feel.get('wall.cueFullDvMps'); } catch (e) { /* default */ }
    const v = clamp01((Number(p.dv) || 0) / full);
    if (v < mixGet(feel, 'hitThumpMin')) return null;
    return { thump: v, floor: 1 - (1 - mixGet(feel, 'hitDuckWall')) * v };
  }
  if (name === 'item-hit') {
    if (p.victim !== 0 || (p.verdict && p.verdict !== 'hit')) return null;
    return { thump: 1, floor: mixGet(feel, 'hitDuckItem') };
  }
  if (name === 'bump') {
    if ((Number.isInteger(p.a) || Number.isInteger(p.b)) && p.a !== 0 && p.b !== 0) return null;   // two bots: not my hit
    const v = clamp01((Number(p.dv) || 0) / mixGet(feel, 'bumpFullDvMps'));
    if (v < 0.3) return null;
    return { thump: 0, floor: 1 - (1 - mixGet(feel, 'hitDuckBump')) * v };
  }
  return null;
}

/**
 * Seconds from now to the next grid step (div steps per beat: 2 = 8ths, 4 = 16ths).
 * beat = music.beat ({bpm, phase}) when a song plays, else null (the grid runs on the context clock).
 */
export function gridWait(now, bpm, div = 2, beat = null, rate = 1) {
  const b = beat && beat.bpm > 0 ? beat.bpm : (bpm > 0 ? bpm : 96);
  const step = 60 / b / div / (rate > 0 ? rate : 1);
  const pos = beat ? (Number(beat.phase) || 0) * div : ((Number(now) || 0) / step);
  const frac = pos - Math.floor(pos);
  return frac < 1e-3 ? 0 : (1 - frac) * step;
}

/** Drift squeal from the one variable, charge 0..1: { gain, hz }; silent at 0. */
export function squealPlan(charge, feel = null) {
  const c = clamp01(charge);
  if (c <= 0) return { gain: 0, hz: mixGet(feel, 'squealLowHz') };
  return {
    gain: mixGet(feel, 'squealGain') * (0.35 + 0.65 * c),
    hz: mixGet(feel, 'squealLowHz') * Math.pow(2, c * mixGet(feel, 'squealOct')),
  };
}

/** Drift charge 0..1 from the per-frame flags: raw driftCharge over the last tier time, else the tier / 3. */
export function driftCharge01(flags = {}, feel = null) {
  if (!flags || !flags.drifting || flags.onRoad === false) return 0;
  if (Number.isFinite(flags.driftCharge)) {
    let tiers = [0.5, 1.2, 2.1]; try { tiers = feel.get('drift.tierS'); } catch (e) { /* default */ }
    return clamp01(flags.driftCharge / tiers[tiers.length - 1]);
  }
  return clamp01((flags.driftTier | 0) / 3);
}

/** The release note per tier: three distinct pitches (semitones over the root). */
export const RELEASE_SEMIS = [12, 16, 19];
export const releaseSemis = (tier) => (tier > 0 ? RELEASE_SEMIS[Math.min(3, tier | 0) - 1] : null);

/** Wind from the one variable, speed 0..1 of top speed: silent under audio.windFromPct. */
export function windPlan(speed01, feel = null) {
  const from = mixGet(feel, 'windFromPct');
  const k = clamp01((clamp01(speed01) - from) / (1 - from));
  return { gain: mixGet(feel, 'windGain') * k * k, cutHz: 300 + (mixGet(feel, 'windTopHz') - 300) * k };
}

/** The final-lap flag: the payload's own flag, else lap === laps - 1 when the event carries counts. */
export function isFinalLap(p = {}, laps = 3) {
  if (!p) return false;
  if (typeof p.final === 'boolean') return p.final;
  const n = laps > 0 ? laps : 3;
  return (p.lap | 0) === n - 1 && n > 1;
}

/** Positional pan for a hit from a relative bearing (radians, + = right); centre when unknown. */
export const bearingPan = (from) => (Number.isFinite(from) ? Math.max(-1, Math.min(1, Math.sin(from))) : 0);

/** Rev voice from the one variable, level 0..1: { gain, hz }. */
export function revPlan(level, feel = null) {
  const l = clamp01(level);
  return { gain: l > 0 ? mixGet(feel, 'revGain') * (0.3 + 0.7 * l) : 0, hz: 60 * Math.pow(2, l * 1.3) };
}

/** Percussion step k on the 8th grid: kick on beats, a hat on the off 8ths. */
export const percStep = (k) => ((k | 0) % 2 === 0 ? 'kick' : 'hat');

/**
 * The voices. out = the sfx bus (impacts and stingers, not ducked), bed = the ducked bed (squeal,
 * wind, rev, percussion). opts.music() = the music api or null; opts.laps() = this race's laps;
 * opts.duck(durationS, floor) = the bus duck.
 */
export function createFeelVoices(ctx, out, bed, feel, bus, { music = () => null, laps = () => 3, duck = () => {}, alive = () => true } = {}) {
  const g = (k) => mixGet(feel, k);
  const bpm = () => { try { return feel.get('clock.bpm'); } catch (e) { return 96; } };
  let nbuf = null, thumps = 0;
  const glide = (p, v, tc) => { try { p.setTargetAtTime(v, ctx.currentTime, Math.max(tc, 0.005)); } catch (e) { p.value = v; } };
  const noiseSrc = () => { if (!nbuf) nbuf = noiseBuffer(ctx); const s = ctx.createBufferSource(); s.buffer = nbuf; s.loop = true; return s; };
  const panned = (pan) => {
    if (!pan || typeof ctx.createStereoPanner !== 'function') return out;
    const p = ctx.createStereoPanner(); p.pan.value = pan; p.connect(out); return p;
  };
  const wait = (div = 2) => { const m = music(); return gridWait(ctx.currentTime, bpm(), div, m && m.playing ? m.beat : null, m?.rate || 1); };

  /** One enveloped tone (THUD curve: a fast attack, an exponential fall). */
  function tone(freq, { type = 'sine', dur = 0.12, vol = 0.1, attack = 0.004, glideTo = 0, at = 0, dest = out } = {}) {
    const t = ctx.currentTime + Math.max(0, at), o = ctx.createOscillator(), gn = ctx.createGain();
    o.type = type; o.frequency.setValueAtTime(freq, t);
    if (glideTo) o.frequency.exponentialRampToValueAtTime(glideTo, t + dur);
    gn.gain.setValueAtTime(0, t);
    gn.gain.linearRampToValueAtTime(vol, t + attack);
    gn.gain.setTargetAtTime(0, t + attack, Math.max(dur / 4, 0.01));
    o.connect(gn); gn.connect(dest); o.start(t); o.stop(t + attack + dur + 0.1);
  }
  function burst({ dur = 0.1, vol = 0.05, freq = 800, q = 0.8, sweep = 0, at = 0, type = 'bandpass', dest = out } = {}) {
    const t = ctx.currentTime + Math.max(0, at), s = noiseSrc(), f = ctx.createBiquadFilter(), gn = ctx.createGain();
    s.loop = false; f.type = type; f.Q.value = q; f.frequency.setValueAtTime(freq, t);
    if (sweep) f.frequency.exponentialRampToValueAtTime(sweep, t + dur);
    gn.gain.setValueAtTime(vol, t); gn.gain.setTargetAtTime(0, t + dur * 0.3, Math.max(dur / 3, 0.01));
    s.connect(f); f.connect(gn); gn.connect(dest); s.start(t); s.stop(t + dur + 0.2);
  }

  // held voices: squeal (band-passed noise), wind (low-passed noise), rev (a saw under a low-pass), percussion bus
  const held = (type, freq, q) => {
    const s = noiseSrc(), f = ctx.createBiquadFilter(), gn = ctx.createGain();
    f.type = type; f.frequency.value = freq; f.Q.value = q; gn.gain.value = 0;
    s.connect(f); f.connect(gn); gn.connect(bed); s.start(); return { s, f, g: gn };
  };
  const squeal = held('bandpass', g('squealLowHz'), 9);
  const wind = held('lowpass', 300, 0.5);
  const revO = ctx.createOscillator(), revF = ctx.createBiquadFilter(), revG = ctx.createGain();
  revO.type = 'sawtooth'; revO.frequency.value = 60; revF.type = 'lowpass'; revF.frequency.value = 700; revG.gain.value = 0;
  revO.connect(revF); revF.connect(revG); revG.connect(bed); revO.start();
  const perc = ctx.createGain(); perc.gain.value = 0; perc.connect(bed);
  let rev = 0, revT = -1e9, finalOn = false, nextStepT = 0, stepK = 0;

  function bigHit(name, p) {
    const h = hitSize(name, p, feel);
    if (!h) return;
    if (h.thump > 0) {
      const r = hitVariantRate(thumps++), v = g('thumpGain') * (0.5 + 0.5 * h.thump);
      tone(g('thumpHz') * r, { dur: g('thumpDurS'), vol: v, glideTo: g('thumpHz') * 0.62 * r });   // now, never quantised
    }
    duck(0, h.floor);
  }
  function releaseNote(p) {
    const s = releaseSemis(p && p.tier);
    if (s === null) return;
    burst({ dur: 0.09, vol: 0.035, freq: 2400, q: 1.5, sweep: 900 });   // the physical skid: now
    tone(hz(s), { type: 'triangle', dur: 0.22, vol: 0.07, at: wait(2) });
  }
  function setFinal(on) {
    if (on === finalOn) return;
    finalOn = on;
    glide(perc.gain, on ? g('finalPercGain') : 0, on ? g('finalPercInS') / 3 : 0.2);
    let rate = 1; try { rate = on ? feel.get('music.finalLapRate') : 1; } catch (e) { /* default */ }
    music()?.setRate?.(rate);
    if (on) { nextStepT = ctx.currentTime + wait(2); stepK = 0; }
  }
  function launch(p) {
    const grade = p && p.grade, w = wait(4);
    glide(revG.gain, 0, 0.08); rev = 0;
    if (grade === 'perfect') {
      burst({ dur: 0.35, vol: 0.05, freq: 600, q: 0.7, sweep: 5200 });
      for (const [s, at] of [[12, 0], [19, 0.08], [24, 0.16]]) tone(hz(s), { type: 'triangle', dur: 0.3, vol: 0.07, at: w + at });
    } else if (grade === 'good') {
      for (const [s, at] of [[12, 0], [19, 0.08]]) tone(hz(s), { type: 'triangle', dur: 0.22, vol: 0.05, at: w + at });
    } else if (grade === 'early') {
      burst({ dur: 0.14, vol: 0.06, freq: 220, q: 0.6, type: 'lowpass' });
      tone(95, { type: 'square', dur: 0.12, vol: 0.025, glideTo: 58 });
    }
  }
  function hitterHit(p) {
    if (!p) return;
    if (p.hitter === 0 && p.racer !== 0) {
      const w = wait(4);
      tone(hz(24), { type: 'triangle', dur: 0.14, vol: 0.06, at: w });
      tone(hz(31), { type: 'sine', dur: 0.2, vol: 0.04, at: w + 0.05 });
    } else if (p.racer === 0) {
      burst({ dur: 0.12, vol: 0.06, freq: 1300, q: 0.9, sweep: 400, dest: panned(bearingPan(p.from)) });
    }
  }

  const live = (fn) => (p) => { if (alive()) fn(p); };
  const offs = [
    bus.on('wall', live((p) => bigHit('wall', p))),
    bus.on('item-hit', live((p) => bigHit('item-hit', p))),
    bus.on('bump', live((p) => bigHit('bump', p))),
    bus.on('drift-release', live(releaseNote)),
    bus.on('lap', live((p) => { if (isFinalLap(p, laps())) setFinal(true); })),
    bus.on('finish', live(() => setFinal(false))),
    bus.on('rev', live((p) => { rev = clamp01(p && p.level); revT = ctx.currentTime; })),
    bus.on('go', live(() => { rev = 0; glide(revG.gain, 0, 0.08); })),
    bus.on('launch', live(launch)),
    bus.on('hitter-hit', live(hitterHit)),
  ];

  return {
    /** Once a frame from audio.update: the same speedPct and flags the engine reads. */
    update(speedPct, flags = {}) {
      const now = ctx.currentTime, mute = flags.active === false;
      const sq = squealPlan(mute ? 0 : driftCharge01(flags, feel), feel);
      glide(squeal.g.gain, sq.gain, sq.gain > 0 ? 0.05 : 0.08); glide(squeal.f.frequency, sq.hz, 0.06);
      const wd = windPlan(mute ? 0 : speedPct, feel);
      glide(wind.g.gain, wd.gain, 0.15); glide(wind.f.frequency, wd.cutHz, 0.15);
      if (now - revT > 0.4) rev = 0;                               // no rev for a while: let it go
      const rv = revPlan(rev, feel);
      glide(revG.gain, rv.gain, 0.06); glide(revO.frequency, rv.hz, 0.05);
      if (finalOn && !mute) {
        const step = 60 / bpm() / 2 / (music()?.rate || 1);
        if (nextStepT < now) nextStepT = now + wait(2);
        while (nextStepT < now + 0.12) {
          const at = nextStepT - now;
          if (percStep(stepK++) === 'kick') tone(58, { dur: 0.1, vol: 0.5, glideTo: 40, at, dest: perc });
          else burst({ dur: 0.04, vol: 0.18, freq: 7000, q: 0.7, type: 'highpass', at, dest: perc });
          nextStepT += step;
        }
      }
    },
    /** Restart: every held voice glides out, the final lap ends, the song rate goes home. */
    reset() {
      rev = 0; setFinal(false);
      for (const p of [squeal.g.gain, wind.g.gain, revG.gain, perc.gain]) glide(p, 0, 0.05);
    },
    get inspect() { return { finalLap: finalOn, rev, thumps }; },
    dispose() {
      offs.forEach((off) => off());
      for (const n of [squeal.s, wind.s, revO]) { try { n.stop(); } catch (e) { /* stopped */ } }
      for (const n of [squeal.g, wind.g, revG, perc]) { try { n.disconnect(); } catch (e) { /* gone */ } }
    },
  };
}
