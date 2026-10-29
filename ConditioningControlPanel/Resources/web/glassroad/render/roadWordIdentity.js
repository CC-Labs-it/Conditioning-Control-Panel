import { abs, clamp, cos, float, floor, fract, max, mix, sin, smoothstep, step, vec2, vec3 } from 'three/tsl';

// The clock belongs to the song and pickup state. Nothing here advances independently.
export const ROAD_WORD_IDENTITY = Object.freeze({
  none: 0, slide: 1, spark: 2, pink: 3, sink: 4, glass: 5,
  rolling: 6, reset: 7, empty: 8, locked: 9, burn: 10,
});
export const WORD_IDENTITY_COLLECT_SECONDS = .6;

const one = () => float(1);
const random = (x) => fract(sin(x.mul(127.1).add(311.7)).mul(43758.5453));
const box = (p) => step(0, p.x).mul(step(p.x, 1)).mul(step(0, p.y)).mul(step(p.y, 1));
const rotate = (p, angle) => {
  const c = cos(angle), s = sin(angle);
  return vec2(p.x.mul(c).sub(p.y.mul(s)), p.x.mul(s).add(p.y.mul(c)));
};

/**
 * A single inverse mapping for the existing RGB glyph atlas and its contour.
 * identity = (kind, seconds from sung onset, pickup progress or -1, character count).
 * u.identityMotion = 0 for Gentle/off, .5 for Less, 1 for Full.
 * Apply mask to EVERY source layer, including glow, and coreMask to filled ink only.
 * Returned lift is metres along the road normal, bounded independently of UV motion.
 * This creates nodes once. It never creates textures, materials or a frame clock.
 */
export function roadWordIdentityUv(loc, identity, u) {
  const kind = identity.x, elapsed = identity.y;
  const motion = clamp(u.identityMotion, 0, 1);
  const collected = step(0, identity.z);
  const progress = clamp(identity.z, 0, 1);
  const out = smoothstep(0, 1, progress).mul(collected);
  const fade = one().sub(smoothstep(.66, 1, progress).mul(collected));
  // Finish arrival before the collectible light turns on at -0.6 s. A racer
  // reaching the marking early still sees the settled, readable source face.
  const entry = one().sub(smoothstep(-2.4, -1.15, elapsed));
  const action = smoothstep(-.6, .15, elapsed).mul(one().sub(out));
  const chars = clamp(identity.w, 1, 18);
  const cell = floor(clamp(loc.x, .0001, .9999).mul(chars));
  const center = cell.add(.5).div(chars);
  const seed = random(cell.add(kind.mul(19)));
  const localX = loc.x.sub(center).mul(chars);
  const calm = one().sub(entry);
  let uv = vec2(loc.x, loc.y), mask = one(), coreMask = one();
  let tint = vec3(1), tintMix = float(0), lift = float(0);

  // Masks select styles without shader control-flow or additional draw calls.
  const apply = (id, p, coverage, color, strength, height = float(0), core = one()) => {
    const select = one().sub(step(.49, abs(kind.sub(id))));
    uv = uv.add(p.sub(loc).mul(select));
    mask = mask.mul(mix(1, coverage, select));
    coreMask = coreMask.mul(mix(1, core, select));
    tint = mix(tint, color, select);
    tintMix = tintMix.add(strength.mul(select));
    lift = lift.add(height.mul(select));
  };

  // SLIDE: a rigid face arrives skewed, seats, then peels forward in staggered slabs.
  // Pickup changes only each slab's longitudinal coordinate, so no slab can borrow
  // the neighbouring letter's pixels as it moves.
  const slidePeel = smoothstep(seed.mul(.24), seed.mul(.24).add(.64), progress).mul(collected);
  const slideX = entry.mul(.18);
  const slideLean = entry.mul(.13);
  const slideUv = vec2(loc.x.add(slideX.add(loc.y.sub(.5).mul(slideLean)).mul(motion)),
    loc.y.add(slidePeel.mul(.95).mul(motion)));
  apply(1, slideUv, box(slideUv).mul(fade), vec3(.56, .94, 1),
    calm.mul(.28).add(out.mul(.36)), slidePeel.mul(.08).mul(motion));

  // SPARK: one travelling charge front belongs to the source strokes. Collection
  // opens fine gaps in that same source, leaving no intact second word beneath it.
  const chargeAt = clamp(elapsed.add(.2).div(.7), 0, 1);
  const charge = one().sub(smoothstep(.035, .16, abs(loc.x.sub(chargeAt))));
  const sparkCell = floor(loc.x.mul(chars.mul(4))).add(floor(loc.y.mul(13)).mul(97));
  const sparkSeed = random(sparkCell);
  const sparkBreak = smoothstep(sparkSeed.mul(.55).add(.12), sparkSeed.mul(.55).add(.25), out);
  const sparkCoverage = one().sub(sparkBreak.mul(motion)).mul(fade);
  apply(2, loc, sparkCoverage, vec3(1, .91, .37),
    charge.mul(.75).mul(motion).add(out.mul(.22)), out.mul(.1).mul(motion));

  // PINK: rounded elastic ink stretches around its own centre, then draws into a
  // thin viscous thread. The inverse map stays continuous across the source face.
  const pinkBreath = sin(clamp(elapsed, 0, 2).mul(3.1)).mul(.025).mul(action);
  const pinkPull = out.mul(.8).mul(motion);
  const pinkUv = vec2(loc.x.sub(.5).div(one().add(pinkBreath.mul(motion)).add(pinkPull.mul(.38))).add(.5),
    loc.y.sub(.5).div(max(.14, one().sub(pinkPull))).add(.5)
      .add(sin(loc.x.mul(12.5)).mul(pinkBreath).mul(motion)));
  apply(3, pinkUv, box(pinkUv).mul(fade), vec3(1, .24, .63),
    calm.mul(.38).add(out.mul(.2)), out.mul(.055).mul(motion));

  // SINK: weight pools along the near edge and the complete source drains into it.
  // Continuous coordinates prevent the horizontal seams of strip-based warps.
  const sink = action.mul(.2).add(out.mul(.9)).mul(motion);
  const sinkUv = vec2(loc.x.sub(.5).div(one().add(sink.mul(.28))).add(.5)
    .add(sin(loc.y.mul(10).add(elapsed.mul(.7))).mul(.007).mul(sink)),
    loc.y.sub(.18).div(max(.09, one().sub(sink.mul(.82)))).add(.18).add(sink.mul(.11)));
  apply(4, sinkUv, box(sinkUv).mul(fade), vec3(.36, .88, .91),
    action.mul(.18).add(out.mul(.42)), sink.mul(-.035));

  // GLASS: every tile is a piece of the original source, with its own inverse
  // transform and source-cell bounds. Vacated regions contain no original word.
  const grid = vec2(chars.mul(1.6), 4);
  const tile = floor(loc.mul(grid)), tileCenter = tile.add(.5).div(grid);
  const glassSeed = random(tile.x.add(tile.y.mul(29)));
  const glassOut = smoothstep(glassSeed.mul(.2), glassSeed.mul(.2).add(.65), progress).mul(collected).mul(motion);
  const glassLocal = loc.sub(tileCenter).mul(grid);
  const glassMapped = rotate(glassLocal.sub(vec2(glassSeed.sub(.5).mul(glassOut.mul(2)), glassOut.mul(.8))),
    glassSeed.sub(.5).mul(glassOut.mul(2.8)));
  const glassUv = glassMapped.div(grid).add(tileCenter);
  const glassBounds = box(glassMapped.add(.5));
  const glassCrack = one().sub(smoothstep(.018, .035, abs(fract(loc.x.mul(grid.x)).sub(.5))))
    .mul(action).mul(.18).mul(motion);
  apply(5, glassUv, mix(1, glassBounds, motion).mul(fade), vec3(.62, .87, 1),
    glassCrack.add(out.mul(.5)), out.mul(.13).mul(motion));

  // ROLLING: staggered rounded cells tumble into a stable reading pose. Rotation
  // returns exactly to zero before the collectible window. Pickup rolls forwards.
  const rollAngle = entry.mul(seed.mul(.9).add(.7)).add(out.mul(2.4)).mul(motion);
  const rollPhase = entry.mul(seed.mul(.15).add(.2)).sub(out.mul(.65)).mul(motion);
  const rollLocal = vec2(localX, loc.y.sub(.5).sub(rollPhase));
  const rolled = rotate(rollLocal, rollAngle);
  const rollUv = vec2(rolled.x.div(chars).add(center), rolled.y.add(.5));
  const rollBounds = step(-.5, rolled.x).mul(step(rolled.x, .5));
  apply(6, rollUv, mix(1, rollBounds, motion).mul(box(rollUv)).mul(fade), vec3(.8, 1, .31),
    calm.mul(.2).add(out.mul(.3)), sin(out.mul(3.14159265)).mul(.12).mul(motion));

  // RESET: a single orderly resolve sweep restores displaced scan bands. No
  // blinking or strobing; collection is one clean reverse sweep through the ink.
  const resetFront = clamp(elapsed.add(.4).div(.55), 0, 1);
  const unresolved = smoothstep(resetFront.sub(.04), resetFront.add(.04), loc.x).mul(entry);
  const band = floor(loc.y.mul(12));
  const resetUv = vec2(loc.x.add(random(band).sub(.5).mul(.18).mul(unresolved).mul(motion)), loc.y);
  const resetErase = one().sub(smoothstep(loc.x.sub(.06), loc.x.add(.06), out.mul(1.15).sub(.08)));
  const resetLine = one().sub(smoothstep(.012, .055, abs(loc.x.sub(resetFront))));
  apply(7, resetUv, box(resetUv).mul(mix(1, resetErase, motion)).mul(fade), vec3(.42, 1, .73),
    resetLine.mul(.62).mul(motion).add(out.mul(.2)));

  // EMPTY: only filled ink drains during the meaning hold; the luminous hollow
  // contour survives. Collection then folds and removes that remaining shell.
  const drain = smoothstep(.18, .75, elapsed).mul(motion);
  const drainFront = float(1.12).sub(drain.mul(1.24));
  const emptyCore = one().sub(smoothstep(drainFront.sub(.055), drainFront.add(.055), loc.y));
  const emptyUv = vec2(loc.x.sub(.5).div(max(.06, one().sub(out.mul(.94).mul(motion)))).add(.5),
    loc.y.add(out.mul(.16).mul(motion)));
  apply(8, emptyUv, box(emptyUv).mul(fade), vec3(.62, .82, 1),
    drain.mul(.4).add(out.mul(.18)), float(0), emptyCore);

  // LOCKED: two hard halves compress to an exact lock. The seated face has no
  // wobble. Pickup unlocks them in opposite directions before the shared fade.
  const side = step(.5, loc.x).mul(2).sub(1);
  const lockGap = entry.mul(.13).add(out.mul(.48)).mul(motion);
  const lockUv = vec2(loc.x.sub(side.mul(lockGap)),
    loc.y.sub(.5).div(one().sub(entry.mul(.11).mul(motion))).add(.5));
  const lockHalf = one().sub(abs(step(.5, lockUv.x).sub(step(.5, loc.x))));
  apply(9, lockUv, box(lockUv).mul(mix(1, lockHalf, motion)).mul(fade), vec3(.96, .77, .39),
    calm.mul(.35).add(out.mul(.22)), out.mul(.06).mul(motion));

  // BURN: a spatial erosion front consumes the actual face from its near edge.
  // Its amber rim is attached to the surviving source, never an extra word.
  const flameNoise = sin(loc.x.mul(chars.mul(4.7))).mul(.045)
    .add(sin(loc.x.mul(67).add(elapsed.mul(1.4))).mul(.018));
  const burnRise = out.mul(1.2).sub(.1);
  const burnHeight = loc.y.add(flameNoise.mul(motion));
  const burnCoverage = smoothstep(burnRise.sub(.025), burnRise.add(.025), burnHeight);
  const burnRim = one().sub(smoothstep(.025, .12, abs(burnHeight.sub(burnRise)))).mul(collected);
  const burnUv = vec2(loc.x.add(sin(loc.y.mul(19).add(elapsed)).mul(.012).mul(out).mul(motion)),
    loc.y.sub(out.mul(.1).mul(motion)));
  apply(10, burnUv, box(burnUv).mul(mix(1, burnCoverage, motion)).mul(fade), vec3(1, .37, .075),
    calm.mul(.16).add(burnRim.mul(.8).mul(motion)), out.mul(.14).mul(motion));

  return { uv, mask: clamp(mask, 0, 1), coreMask: clamp(coreMask, 0, 1),
    tint, tintMix: clamp(tintMix, 0, 1), lift };
}
