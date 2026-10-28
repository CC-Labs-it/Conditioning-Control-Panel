import { gripYaw, gripOf, yawParams } from '../sim/steer.js';
import { makeFrame } from './frames.js';

export const BOOST_SAFETY_REVISION = 2;
// Browser-free loaders also build room cards. Tests pin these defaults to feel.json.
export const BOOST_FEEL = Object.freeze({
  speed: { topMps: 37.5 }, cleanPace: { cleanGain: .3, musicGain: .26 },
  pad: { amount: .2, holdS: 1 }, boost: { riseS: .1, tailTcS: .5 }, engine: { brakeMps2: 27.5 },
  items: { tetherTowCap: 1.15 },
  steer: { yawRateDegps: 171.875, yawSpeedRefMps: 31.25, calmFromMps: 10, cruiseMps: 25,
    cruiseYawMul: .46, topCalmMps: 37.5, topYawMul: .45, boostCalmMps: 48.75, boostYawMul: .35, userMul: 1 },
});
const accessor = feel => typeof feel?.get === 'function' ? feel
  : { get: path => path.split('.').reduce((v, key) => v?.[key], feel || BOOST_FEEL) };

/** Worst solo pad envelope, with room to react, finish the tail and then brake. */
export function boostTurnProfile(input = BOOST_FEEL) {
  const feel = accessor(input), cruise = feel.get('speed.topMps');
  const solo = cruise * (1 + feel.get('cleanPace.cleanGain') + feel.get('cleanPace.musicGain'));
  const peak = solo * Math.max(1, feel.get('items.tetherTowCap')) * (1 + feel.get('pad.amount'));
  const reactionM = peak * .4;
  const forcedM = peak * (feel.get('boost.riseS') + feel.get('pad.holdS') + 5 * feel.get('boost.tailTcS'));
  const brakingM = (peak * peak - cruise * cruise) / (2 * feel.get('engine.brakeMps2'));
  return { rolloutM: forcedM + reactionM + brakingM, forcedM: forcedM + reactionM, sampleM: 2,
    radiusM: peak / gripYaw(peak, feel) / .9, cruiseRadiusM: cruise / gripYaw(cruise, feel) / .9,
    padSpeedMps: peak, cruiseSpeedMps: cruise, brakeMps2: feel.get('engine.brakeMps2') };
}
export const BOOST_TURN = Object.freeze(boostTurnProfile());

/** Maximum horizontal bend on the actual lane, including the pad and its full runoff. */
export function boostTurnRadius(track, pad, clearance = false, input = BOOST_FEEL) {
  const profile = input === BOOST_FEEL ? BOOST_TURN : boostTurnProfile(input), feel = accessor(input);
  const steering = yawParams(feel);
  const s = pad.s ?? track.nodeS[pad.node], half = Math.max(0, (pad.w || 0) / 2);
  const lanes = half > 0 ? [(pad.x || 0) - half, pad.x || 0, (pad.x || 0) + half] : [pad.x || 0];
  const views = lanes.map(() => [makeFrame(), makeFrame(), makeFrame()]);
  let lastCenterS = NaN, lastRightS = NaN;
  let peak = 0;
  const end = pad.len + profile.rolloutM;
  for (let d = -4; ; d = Math.min(end, d + profile.sampleM)) {
    const brakeD = Math.max(0, d - pad.len - profile.forcedM);
    const speed = Math.sqrt(Math.max(profile.cruiseSpeedMps ** 2,
      profile.padSpeedMps ** 2 - 2 * profile.brakeMps2 * brakeD));
    const required = speed / gripOf(speed, steering) / .9;
    const centerS = s + d, leftS = centerS - 2, rightS = centerS + 2;
    for (let lane = 0; lane < lanes.length; lane++) {
    const x = lanes[lane], f = views[lane];
    // Reuse only identical sample coordinates, including floating-point boundary cases.
    if (leftS === lastCenterS && centerS === lastRightS) {
      const old = f[0]; f[0] = f[1]; f[1] = f[2]; f[2] = track.surfaceAt(rightS, x, old);
    } else {
      f[0] = track.surfaceAt(leftS, x, f[0]); f[1] = track.surfaceAt(centerS, x, f[1]); f[2] = track.surfaceAt(rightS, x, f[2]);
    }
    const a = f[0].p, b = f[1].p, c = f[2].p;
    const ax = b[0] - a[0], az = b[2] - a[2], bx = c[0] - b[0], bz = c[2] - b[2];
    const al = Math.hypot(ax, az), bl = Math.hypot(bx, bz);
    // Vertical loop crowns have no horizontal heading; they are intentional launches.
    const angle = al < .6 || bl < .6 ? 0 : Math.atan2(Math.abs(ax * bz - az * bx), ax * bx + az * bz);
    const curvature = angle / Math.max(.001, (al + bl) / 2);
    peak = Math.max(peak, curvature * (clearance ? required : 1));
    if (clearance && peak > 1) return 1 / peak;
    }
    lastCenterS = centerS; lastRightS = rightS;
    if (d === end) break;
  }
  return peak > 0 ? 1 / peak : Infinity;
}

export function safeBoostTurn(track, pad, feel = BOOST_FEEL) {
  return boostTurnRadius(track, pad, true, feel) >= 1;
}
