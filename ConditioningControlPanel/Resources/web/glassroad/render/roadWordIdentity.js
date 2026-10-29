import { abs, clamp, cos, float, floor, fract, max, mix, sin, smoothstep, step, vec2, vec3 } from 'three/tsl';

// The clock belongs to the song and pickup state. Nothing here advances independently.
export const ROAD_WORD_IDENTITY = Object.freeze({
  none: 0, slide: 1, spark: 2, pink: 3, sink: 4, glass: 5,
  rolling: 6, reset: 7, empty: 8, locked: 9, burn: 10,
  breath: 11, echo: 12, focus: 13, liquid: 14, fragment: 15,
  elastic: 16, rise: 17, heavy: 18, pulse: 19, fade: 20,
  chain: 21, code: 22, gather: 23, switch: 24, orbit: 25,
  rough: 26, glow: 27, ice: 28, fold: 29, split: 30,
  rain: 31, warm: 32, shiver: 33, spectrum: 34, metal: 35, green: 36,
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

  // The wider vocabulary shares a few clocks and source partitions, not a single
  // generic wobble. Every family below owns a physical action and a distinct out.
  const live = action.mul(motion), release = out.mul(motion), arrive = entry.mul(motion);
  const clock = clamp(elapsed.add(.6), 0, 6);
  const arch = one().sub(loc.x.sub(.5).mul(2).mul(loc.x.sub(.5).mul(2)));
  const slotBounds = (p) => step(-.5, p.x.sub(center).mul(chars)).mul(step(p.x.sub(center).mul(chars), .5));
  const inkNoise = random(floor(loc.x.mul(chars.mul(7))).add(floor(loc.y.mul(37)).mul(71)));
  const drift = sin(clock.mul(2.1));

  // BREATH: a single rounded inhalation expands the complete word. Exhalation
  // draws it back into a small point without separating neighbouring letters.
  const breathScale = one().add(sin(clamp(clock, 0, 2.6).mul(1.2)).mul(.07).mul(live)).sub(release.mul(.82));
  const breathUv = loc.sub(.5).div(max(.12, breathScale)).add(.5);
  apply(11, breathUv, box(breathUv).mul(fade), vec3(.76, .98, .9), calm.mul(.25).add(release.mul(.22)), live.mul(.025));

  // ECHO: one filled middle impression and two hollow impressions of the source.
  // All three stay in this marking's bounds; a returning wave folds them together.
  const echoSpread = live.mul(one().sub(out));
  const echoRow = floor(clamp(loc.y, .0001, .9999).mul(3));
  const echoSource = vec2(loc.x, fract(loc.y.mul(3)));
  const echoUv = mix(loc, echoSource, echoSpread);
  const echoOuter = abs(echoRow.sub(1));
  const echoCore = one().sub(echoOuter.mul(echoSpread));
  apply(12, echoUv, box(echoUv).mul(fade), vec3(.67, .78, 1), calm.mul(.3).add(release.mul(.3)), float(0), echoCore);

  // FOCUS: an optical aperture opens around a clear centre. The narrow highlight
  // scans the original strokes once, then the aperture shuts on collection.
  const focusWidth = one().sub(release.mul(.99));
  const focusMask = one().sub(smoothstep(focusWidth.mul(.5).sub(.035), focusWidth.mul(.5).add(.035), abs(loc.x.sub(.5))));
  const focusSweep = one().sub(smoothstep(.025, .13, abs(loc.x.sub(clamp(clock.div(1.3), 0, 1)))));
  apply(13, loc, focusMask.mul(fade), vec3(.68, .94, 1), calm.mul(.15).add(focusSweep.mul(.6).mul(live)));

  // LIQUID: a travelling sideways current bends the whole source continuously.
  // Pickup stretches the lower ink into long threads instead of rigid shards.
  const liquidFlow = sin(loc.y.mul(9).sub(clock.mul(2.3))).mul(.023).mul(live);
  const liquidUv = vec2(loc.x.add(liquidFlow).add(sin(loc.y.mul(17)).mul(.09).mul(release)),
    loc.y.sub(.22).div(max(.1, one().sub(release.mul(.86)))).add(.22).add(release.mul(.1)));
  apply(14, liquidUv, box(liquidUv).mul(fade), vec3(.32, .92, .77), calm.mul(.34).add(release.mul(.22)));

  // FRAGMENT: diagonal faults separate rigid portions of the same source. Pieces
  // move apart at pickup and are clipped to their provenance before sampling.
  const faultCell = floor(loc.y.mul(4).add(loc.x.mul(2)));
  const faultDirection = fract(faultCell.mul(.5)).mul(4).sub(1);
  const fragmentUv = vec2(loc.x.add(faultDirection.mul(.26).mul(release)), loc.y.add(seed.sub(.5).mul(.18).mul(release)));
  const fragmentSame = one().sub(step(.5, abs(floor(fragmentUv.y.mul(4).add(fragmentUv.x.mul(2))).sub(faultCell))));
  apply(15, fragmentUv, box(fragmentUv).mul(mix(1, fragmentSame, motion)).mul(fade), vec3(.79, .92, 1), calm.mul(.2).add(release.mul(.4)));

  // ELASTIC: opposing width/height changes preserve the impression of rubber
  // volume. A travelling rebound precedes the long elastic pickup stretch.
  const rubber = sin(clock.mul(3.2).sub(loc.x.mul(4))).mul(.065).mul(live);
  const elasticUv = vec2(loc.x.sub(.5).div(one().add(rubber).add(release.mul(.5))).add(.5),
    loc.y.sub(.5).div(max(.1, one().sub(rubber).sub(release.mul(.87)))).add(.5));
  apply(16, elasticUv, box(elasticUv).mul(fade), vec3(1, .65, .84), calm.mul(.3).add(release.mul(.2)), sin(out.mul(3.14159265)).mul(.07).mul(motion));

  // RISE: staggered steps lift the baseline, followed by an upward collection.
  const stair = cell.div(max(chars.sub(1), 1)).sub(.5);
  const riseUv = vec2(loc.x, loc.y.sub(stair.mul(.12).mul(live)).add(arrive.mul(.27)).sub(release.mul(.85)));
  apply(17, riseUv, box(riseUv).mul(fade), vec3(.75, 1, .47), calm.mul(.25).add(release.mul(.35)), release.mul(.1));

  // HEAVY: the face compresses into the road with a broad foot. Its final impact
  // flattens the source along that foot instead of flying upwards.
  const weight = live.mul(.1).add(release.mul(.79));
  const heavyUv = vec2(loc.x.sub(.5).div(one().add(weight.mul(.18))).add(.5),
    loc.y.sub(.19).div(max(.08, one().sub(weight))).add(.19));
  apply(18, heavyUv, box(heavyUv).mul(fade), vec3(.79, .69, .52), calm.mul(.42).add(release.mul(.16)), weight.mul(-.035));

  // PULSE: a measured broad wave moves through the source. The collection turns
  // that wave into a single concentric compression, without repeated flashes.
  const pulseBeat = sin(clamp(clock, 0, 2.4).mul(3.4)).mul(.045).mul(live);
  const pulseUv = loc.sub(.5).div(one().add(pulseBeat).add(release.mul(.48))).add(.5);
  const pulseRing = abs(loc.sub(.5).length().sub(release.mul(.72)));
  const pulseCut = smoothstep(.025, .11, pulseRing).mul(release).add(one().sub(release));
  apply(19, pulseUv, pulseCut.mul(fade), vec3(1, .6, .42), calm.mul(.32).add(abs(pulseBeat).mul(4)), pulseBeat.mul(.7));

  // FADE: fine source grains release into a rising vapour field. Once removed,
  // a grain never reforms during that collection, including when seeking.
  const vapourUv = vec2(loc.x.add(sin(loc.y.mul(12).add(clock)).mul(.035).mul(release)), loc.y.sub(release.mul(.28)));
  const vapourMask = smoothstep(out.sub(.08), out.add(.08), inkNoise.add(.12));
  apply(20, vapourUv, box(vapourUv).mul(mix(1, vapourMask, motion)).mul(fade), vec3(.72, .78, .94), calm.mul(.21), release.mul(.09));

  // CHAIN: the baseline hangs under tension. Pulling it taut lifts the middle;
  // collection releases alternating links in opposing longitudinal directions.
  const chainSag = arch.mul(.065).mul(live);
  const linkSign = fract(cell.mul(.5)).mul(4).sub(1);
  const chainUv = vec2(loc.x, loc.y.add(chainSag).add(linkSign.mul(.72).mul(release)));
  apply(21, chainUv, box(chainUv).mul(fade), vec3(.82, .86, .93), calm.mul(.42).add(release.mul(.2)), arch.mul(.03).mul(live));

  // CODE: exact source pixels resolve from coarse digital blocks during arrival.
  // Collection clears columns in a deterministic sequence, without random flicker.
  const codeGrid = vec2(chars.mul(8), 24);
  const quantized = floor(loc.mul(codeGrid)).add(.5).div(codeGrid);
  const codeUv = mix(loc, quantized, arrive);
  const codeColumn = floor(loc.x.mul(chars.mul(3))).div(chars.mul(3));
  const codeMask = one().sub(smoothstep(codeColumn.mul(.65).add(.12), codeColumn.mul(.65).add(.22), out));
  const codeRead = one().sub(smoothstep(.015, .07, abs(loc.x.sub(clamp(clock.div(1.2), 0, 1)))));
  apply(22, codeUv, mix(1, codeMask, motion).mul(fade), vec3(.41, 1, .64), calm.mul(.2).add(codeRead.mul(.48).mul(live)));

  // GATHER: loose source columns draw into a tight assembly. Pickup contracts
  // inward from both ends, retaining the complete source rather than replacing it.
  const gatherScale = one().add(arrive.mul(.45)).sub(live.mul(.035)).sub(release.mul(.83));
  const gatherUv = vec2(loc.x.sub(.5).div(max(.1, gatherScale)).add(.5), loc.y.sub(.5).div(one().sub(release.mul(.6))).add(.5));
  apply(23, gatherUv, box(gatherUv).mul(fade), vec3(.83, .92, .69), calm.mul(.28).add(release.mul(.32)));

  // SWITCH: the face behaves as a hinged plate. Its bright seated state is stable;
  // collection turns the plate edge-on along a definite horizontal hinge.
  const hingeScale = one().sub(arrive.mul(.76)).sub(release.mul(.94));
  const switchUv = vec2(loc.x, loc.y.sub(.5).div(max(.06, hingeScale)).add(.5));
  apply(24, switchUv, box(switchUv).mul(fade), vec3(.67, 1, .39), calm.mul(.43), release.mul(.06));

  // ORBIT: a shallow orbital baseline carries the source around a curved path.
  // Collection winds the complete word into a turning central aperture.
  const orbitAngle = release.mul(1.8);
  const orbitLocal = rotate(loc.sub(.5), orbitAngle).div(max(.12, one().sub(release.mul(.82))));
  const orbitUv = vec2(orbitLocal.x.add(.5), orbitLocal.y.add(.5).add(sin(loc.x.mul(6.283).add(clock)).mul(.035).mul(live)));
  apply(25, orbitUv, box(orbitUv).mul(fade), vec3(.72, .59, 1), calm.mul(.34).add(release.mul(.2)), release.mul(.075));

  // ROUGH: tiny mineral ledges interrupt the source edge, then erosion strips
  // carry the same ink away. The held word keeps a solid, legible interior.
  const roughRow = floor(loc.y.mul(22));
  const roughUv = vec2(loc.x.add(random(roughRow).sub(.5).mul(.012).mul(live)), loc.y);
  const roughMask = smoothstep(out.mul(1.1).sub(.05), out.mul(1.1).add(.06), inkNoise);
  apply(26, roughUv, mix(1, roughMask, release).mul(fade), vec3(.88, .67, .46), calm.mul(.36).add(release.mul(.24)));

  // GLOW: illumination blooms from the centre through the real source strokes.
  // A hollow neon edge remains briefly after its filled interior loses energy.
  const illumination = one().sub(smoothstep(.08, .38, abs(loc.x.sub(.5))));
  const glowCore = one().sub(smoothstep(.12, .7, out).mul(motion));
  apply(27, loc, fade, vec3(1, .96, .61), calm.mul(.34).add(illumination.mul(.4).mul(live)), float(0), glowCore);

  // ICE: sharp oblique facets refract the ink. At pickup cracks spread through
  // the source, separating frozen pieces rather than adding a generic glow.
  const iceFacet = floor(loc.x.mul(chars.mul(2)).add(loc.y.mul(3)));
  const iceDirection = random(iceFacet).sub(.5);
  const iceUv = vec2(loc.x.add(iceDirection.mul(.007).mul(live)), loc.y.add(iceDirection.mul(.34).mul(release)));
  const frost = abs(fract(loc.x.mul(chars.mul(2)).add(loc.y.mul(3))).sub(.5));
  const iceMask = smoothstep(release.mul(.37).sub(.025), release.mul(.37).add(.025), frost);
  apply(28, iceUv, box(iceUv).mul(mix(1, iceMask, release)).mul(fade), vec3(.53, .87, 1), calm.mul(.4).add(release.mul(.25)));

  // FOLD: alternating panels close into an accordion. The inverse source-cell
  // clipping prevents a closing panel from dragging another letter onto itself.
  const foldScale = max(.06, one().sub(release.mul(.94)).sub(arrive.mul(.65)));
  const foldUv = vec2(localX.div(foldScale).div(chars).add(center),
    loc.y.add(linkSign.mul(abs(localX)).mul(.2).mul(release)));
  apply(29, foldUv, box(foldUv).mul(mix(1, slotBounds(foldUv), motion)).mul(fade), vec3(.93, .79, .64), calm.mul(.26).add(release.mul(.3)));

  // SPLIT: the source opens like a pair of doors, exposing a real empty seam.
  // Each half remains bounded to its original half before disappearing.
  const splitUv = vec2(loc.x.sub(side.mul(release.mul(.48))), loc.y.add(side.mul(.13).mul(release)));
  const splitBound = one().sub(abs(step(.5, splitUv.x).sub(step(.5, loc.x))));
  apply(30, splitUv, box(splitUv).mul(mix(1, splitBound, motion)).mul(fade), vec3(.64, .87, .98), calm.mul(.3).add(release.mul(.26)));

  // RAIN: individual narrow source columns condense at different heights, then
  // descend in a staggered shower. Their x coordinate never borrows other ink.
  const rainColumn = random(floor(loc.x.mul(chars.mul(2))));
  const rainFall = smoothstep(rainColumn.mul(.23), rainColumn.mul(.23).add(.6), progress).mul(collected).mul(motion);
  const rainUv = vec2(loc.x, loc.y.add(sin(clock.mul(1.6).add(rainColumn.mul(4))).mul(.018).mul(live)).add(rainFall.mul(.95)));
  apply(31, rainUv, box(rainUv).mul(fade), vec3(.51, .78, 1), calm.mul(.32).add(release.mul(.2)));

  // WARM: neighbouring strokes lean inward and the baseline rounds into an
  // affectionate arch. Pickup closes that arch softly around its own source.
  const warmth = arch.mul(.045).mul(live);
  const warmUv = vec2(loc.x.sub(.5).div(one().sub(live.mul(.025)).sub(release.mul(.55))).add(.5),
    loc.y.add(warmth).sub(.5).div(one().sub(release.mul(.7))).add(.5));
  apply(32, warmUv, box(warmUv).mul(fade), vec3(1, .56, .55), calm.mul(.42).add(release.mul(.18)));

  // SHIVER: the entire source huddles inward with a small coherent shiver. The
  // pickup turns that huddle into a narrow vertical thread, without strobing.
  const shiver = sin(clock.mul(7)).mul(.006).mul(live);
  const shiverUv = vec2(loc.x.sub(.5).div(max(.08, one().sub(live.mul(.035)).sub(release.mul(.9)))).add(.5).add(shiver),
    loc.y.add(arch.mul(.025).mul(live)));
  apply(33, shiverUv, box(shiverUv).mul(fade), vec3(.81, .69, .96), calm.mul(.32).add(release.mul(.25)));

  // SPECTRUM: prismatic ink stays attached to the source through a broad liquid
  // colour front. Literal named hues use their own identity instead.
  const spectralPhase = loc.x.mul(6.283).add(clock.mul(.6).mul(motion));
  const spectralInk = vec3(sin(spectralPhase).mul(.3).add(.7),
    sin(spectralPhase.add(2.094)).mul(.3).add(.7), sin(spectralPhase.add(4.189)).mul(.3).add(.7));
  const spectrumUv = vec2(loc.x, loc.y.add(sin(loc.x.mul(9).sub(clock)).mul(.023).mul(live)).sub(release.mul(.7)));
  apply(34, spectrumUv, box(spectrumUv).mul(fade), spectralInk, calm.mul(.7).add(release.mul(.15)));

  // METAL: a broad reflection rolls across rigid raised ink. The final reflection
  // turns into a thin edge as the polished plate rotates away.
  const metalBand = one().sub(smoothstep(.035, .2, abs(loc.x.add(loc.y.mul(.23)).sub(clamp(clock.div(1.6), 0, 1.3)))));
  const metalUv = vec2(loc.x.sub(.5).div(max(.07, one().sub(release.mul(.93)))).add(.5), loc.y);
  const metalInk = mix(vec3(.47, .61, .75), vec3(.97, 1, 1), metalBand.mul(motion).add(one().sub(motion).mul(.5)));
  apply(35, metalUv, box(metalUv).mul(fade), metalInk, calm.mul(.57).add(release.mul(.28)), live.mul(.035));

  // GREEN is literal pigment, with one slow advancing edge and a draining out.
  const greenUv = vec2(loc.x.add(sin(loc.y.mul(8)).mul(.009).mul(live)), loc.y.add(release.mul(.75)));
  apply(36, greenUv, box(greenUv).mul(fade), vec3(.31, .96, .49), calm.mul(.7).add(release.mul(.12)));

  return { uv, mask: clamp(mask, 0, 1), coreMask: clamp(coreMask, 0, 1),
    tint, tintMix: clamp(tintMix, 0, 1), lift };
}
