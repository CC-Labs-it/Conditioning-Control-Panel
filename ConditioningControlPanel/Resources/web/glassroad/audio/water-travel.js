// A continuous wake, spray and rounded bubble bed, on the existing SFX/duck bus.
import { S } from '../sim/state.js';
import { makeQuery } from '../track/query.js';
import { createImmersion } from '../track/immersion.js';
import { isWaterSurface } from '../track/waterSurface.js';

const unit = x => Number.isFinite(x) ? Math.max(0, Math.min(1, x)) : 0;

// Probe the kart's actual supporting road, including fork bridges over water.
// Camera submersion belongs to the output muffle, not to tyre contact.
export function createWaterContact(track) {
  const q = makeQuery(), immersion = createImmersion(track);
  const result = { waterSurface: false, waterDepth: 0, waterSteer: 0, waterSpeedPct: 0 };
  return (st, active = true, topMps = 30) => {
    result.waterSurface = false; result.waterDepth = 0; result.waterSteer = 0; result.waterSpeedPct = 0;
    if (!active || st[S.respawnPhase] > 0) return result;
    result.waterDepth = immersion(st[S.s], st[S.posY]);
    result.waterSteer = st[S.steerSlew];
    result.waterSpeedPct = unit(Math.hypot(st[S.velX], st[S.velY], st[S.velZ]) / Math.max(1, topMps));
    if (st[S.grounded] > 0 && !st[S.airborne]) {
      const ground = track.query(st[S.posX], st[S.posY], st[S.posZ], st[S.seg] | 0, q);
      const section = ground.forkId ? '' : track.sections?.find(s => ground.s >= s.fromS && ground.s < s.toS)?.id;
      result.waterSurface = ground.onRoad && isWaterSurface(ground.surface, section);
    }
    return result;
  };
}

function texture(ctx, bubbles) {
  const rate = ctx.sampleRate || 44100, length = Math.ceil(rate * (bubbles ? 3.7 : 2.3));
  const buffer = ctx.createBuffer(2, length, rate);
  let seed = bubbles ? 173 : 419;
  const random = () => { seed = (1664525 * seed + 1013904223) >>> 0; return seed / 4294967296; };
  for (let channel = 0; channel < 2; channel++) {
    const data = buffer.getChannelData(channel);
    if (!bubbles) {
      for (let i = 0; i < length; i++) data[i] = (random() * 2 - 1) *
        (.72 + .16 * Math.sin(i / rate * 43) + .12 * Math.sin(i / rate * 71 + channel));
    } else {
      // Overlapping, descending droplets avoid a pitched oscillator drone.
      for (let start = 0; start < length; start += Math.floor(rate * (.025 + random() * .085))) {
        const duration = .04 + random() * .11, hz = 190 + random() * 520;
        const count = Math.ceil(rate * duration), amplitude = .13 + random() * .18;
        for (let j = 0; j < count && start + j < length; j++) {
          const t = j / rate, envelope = Math.sin(Math.PI * j / count) ** 2 * Math.exp(-t * 26);
          data[start + j] += amplitude * envelope * Math.sin(2 * Math.PI * hz * (t - t * t / (duration * 3)));
        }
      }
    }
    // A quiet seam prevents a periodic click when the two loops wrap.
    const fade = Math.ceil(rate * .015);
    for (let i = 0; i < fade; i++) { data[i] *= i / fade; data[length - 1 - i] *= i / fade; }
  }
  return buffer;
}

export function createWaterTravel(ctx, out) {
  const spray = ctx.createGain(), wake = ctx.createGain(), bubbles = ctx.createGain();
  for (const gain of [spray, wake, bubbles]) { gain.gain.value = 0; gain.connect(out); }
  const noise = ctx.createBufferSource(), drops = ctx.createBufferSource();
  noise.buffer = texture(ctx, false); drops.buffer = texture(ctx, true);
  const high = ctx.createBiquadFilter(); high.type = 'highpass'; high.frequency.value = 750; high.Q.value = .5;
  const air = ctx.createBiquadFilter(); air.type = 'lowpass'; air.frequency.value = 2800; air.Q.value = .6;
  const low = ctx.createBiquadFilter(); low.type = 'lowpass'; low.frequency.value = 420; low.Q.value = .65;
  const round = ctx.createBiquadFilter(); round.type = 'lowpass'; round.frequency.value = 850; round.Q.value = .5;
  noise.connect(high); high.connect(air); air.connect(spray);
  noise.connect(low); low.connect(wake); drops.connect(round); round.connect(bubbles);
  for (const source of [noise, drops]) { source.loop = true; source.start(); }
  let dead = false, wet = false, depth = 0, levels = [0, 0, 0];
  const glide = (p, v, tc = .12) => {
    if (!p) return;
    if (p.setTargetAtTime) p.setTargetAtTime(v, ctx.currentTime, tc); else p.value = v;
  };
  function update(speed, flags = {}) {
    if (dead) return;
    const active = !flags.paused;
    wet = active && !!flags.waterSurface && !!flags.onRoad && !flags.airborne;
    depth = active ? unit(flags.waterDepth) : 0;
    const v = unit(flags.waterSpeedPct ?? speed), turn = unit(Math.abs(flags.waterSteer || 0));
    const moving = Math.sqrt(v) * (flags.boosting ? 1.12 : 1);
    const contact = wet ? moving : 0;
    levels = [contact * (.18 + turn * .05) * (1 - .85 * depth),
      contact * .24, (contact * .12 + depth * (.10 + v * .10))];
    const tc = wet || depth > 0 ? .09 : .07;
    [spray, wake, bubbles].forEach((n, i) => glide(n.gain, levels[i], tc));
    glide(air.frequency, 1800 + v * 4200 + turn * 1200);
    glide(low.frequency, 240 + v * 420);
    glide(round.frequency, 950 - depth * 450);
    glide(drops.playbackRate, .7 + v * .65);
  }
  function reset() {
    wet = false; depth = 0; levels = [0, 0, 0];
    for (const n of [spray, wake, bubbles]) {
      n.gain.cancelScheduledValues?.(ctx.currentTime); n.gain.value = 0;
    }
  }
  return {
    update, reset,
    get inspect() { return Object.freeze({ available: !dead, wet, depth, spray: levels[0], wake: levels[1], bubbles: levels[2] }); },
    dispose() {
      if (dead) return; reset(); dead = true;
      for (const source of [noise, drops]) source.stop();
      for (const n of [noise, drops, high, air, low, round, spray, wake, bubbles]) n.disconnect();
    },
  };
}
