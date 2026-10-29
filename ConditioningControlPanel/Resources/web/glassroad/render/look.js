/* render/look.js - the render look table. Feel numbers (what the player tunes) live in feel.json;
 * these are the art numbers of the greybox look, in one place so no module hard-codes them.
 * Colours are CSS hex. Section `palette` and `sky` ids in track JSON key into PALETTES and SKIES.
 * Polish 3 lanes keep their own tables, each overridable from here by an optional key: LOOK.juice
 * (render/atlas.js JUICE_LOOK), LOOK.camKick (render/cameraMath.js), LOOK.world (render/colossiMath.js
 * WORLD_DEFAULTS: scatter per tier, haze, colossi), LOOK.media (render/mediaMath.js tiers and frames). The
 * colour system is render/spectrum.js (SPECTRUM_LOOK); the spiral gates read render/spiralMath.js SPIRAL_LOOK.
 */

/** Three colours per palette: [band A, band B, band C]. The sky reads the same three. */
export const PALETTES = {
  dawn: ['#ff7eb6', '#ffc2df', '#ffc46b'],
  deep: ['#d0306f', '#3f2bd0', '#1a1440'],
  afterglow: ['#ffb36b', '#ff6f91', '#fff1d6'],
};
export const FALLBACK_PALETTE = 'dawn';

/** Sky presets: spiral twist (bands per log radius) and turn rate. */
export const SKIES = {
  'spiral-slow': { twist: 5, turnRadps: 0.05 },
  'spiral-deep': { twist: 7, turnRadps: 0.09 },
  'spiral-wide': { twist: 3.5, turnRadps: 0.04 },
};
export const FALLBACK_SKY = 'spiral-slow';

export const LOOK = {
  snapM: 20, nearM: 0.1, farM: 6000, skyRadiusM: 3000, skyArms: 7, skyContrast: 0.64, skyDim: 0.44, skyGapDim: 0.55,
  skyEyeTiltDeg: 68, skyEyeTcS: 2, rescueWhite: 0.45,
  // road bands (u = lateral metres, v = s metres)
  bandLenM: 14, bandWarpPerM: 0.35, bandWarpAmp: 0.15, bandScrollMps: 6, roadAlpha: 0.72,
  seamGain: 0.35, beatBreath: 0.06, beatBreathRise: 0.15, roadGain: 0.8,
  // EDGES lane: the rail is 1.2 m (track/mesh.js RAIL_H); LOOK.edges overrides render/edgesMath.js EDGE_LOOK (rail posts,
  // top line, the open-edge strip, haze and end posts)
  railHeightM: 1.2, railAlpha: 0.25, railGain: 0.9, railNearM: [4, 16], edges: {},
  padStripeM: 1.5, padScrollMps: 8, padPulseGain: 1.5, padColour: '#ffd36b', lipColour: '#ffffff',
  // kart greybox (saucer radius about eyeBack / 9)
  saucerR: 0.72, cupColour: '#fff4fa', saucerColour: '#ffd36b', emiColour: '#ff8cc6', faceColour: '#2a1630',
  lightDir: [0.35, 0.85, 0.4], ambient: 0.55, rimGain: 0.35,
  // floating props for speed reference
  props: 36, propSideM: [12, 30], propUpM: [-8, 14], propSizeM: [1.5, 5], propColour: '#e9dcff',
  // post
  bloomStrength: 0.22, bloomRadius: 0.3, bloomThreshold: 1.0, bloomBoostGain: 1.5,
  vignetteIn: 0.35, vignetteOut: 0.95, vignetteDark: 0.55, edgeTint: '#ff4fa3', flashTint: '#fff0c8',
  // speed wave 2026-10-28: the view widens at speed (render/cameraMath.js SPEED_FEEL_DEFAULTS; LOOK.speedFeel overrides
  // any key) and the screen edges blur toward the centre: 0 inside speedBlurIn of the half diagonal (the middle third
  // stays sharp), the outermost tap steps speedBlurUv of the centre distance at full strength
  speedFeel: {}, speedBlurIn: 0.33, speedBlurUv: 0.05,
  // particles
  sparkRate: 140, sparkSpeedMps: [2, 6], sparkSizeM: 0.14, sparkSpreadMps: 1.5, burstSpreadMps: 4, burstCount: 60,
  dustCount: 40, dustSpeedMps: 3, dustSizeM: 0.4, dustColour: '#f4eef8', dustLifeS: 0.6,
  exhaustRate: 90, exhaustSizeM: 0.3, exhaustLifeS: 0.35, exhaustColour: '#ffb3d9',
  particleDrag: 2.5, particleGravityMps2: 6,
  // speed lines (kart-local, flowing past along -Z)
  lineLenM: [6, 14], lineRadiusM: [5, 12], lineSpanM: 70, lineFlowMps: 45, lineWidthM: 0.04,
  lineFromPct: 0.7, lineGain: 1.6, lineAlpha: 0.35,
  tendrilColour: '#fff6e6', tendrilM: 60, tendrilGain: 0.45, ringColour: '#ffd36b', ringMaxM: 4,
  // wave 1 art (render/art.js, render/dressing.js; art/ART.md for tile sizes and bound radii)
  artRoadTileM: [12, 16], artBandSpan: 0.999, artHaze: 0.06, artHazeTileM: 4, artRailCore: 0.6, artRailCoreAlpha: 0.35,
  artChevronM: 6, artChevronWideM: 4, artChevronFloor: 0.25, artGalaxyGain: 0.54, artStarGain: 0.7,
  // A deeper colour field leaves room for the bright road, stars and accepted giant silhouettes.
  skySpectrum: { armGain: 1, nebGain: 0.58, hazeGain: 1.15 },
  skySeparation: { horizonGain: 0.7, zenithGain: 0.9, horizonBand: 0.42 },
  // Keep the musical colour and glitter; distinguish glass highlights from its luminous pattern.
  roadPolish: { depthGain: 0.1, rimGain: 0.17, reflGain: 0.46, reflF0: 0.055 },
  roadSpectrum: { edgeGain: 0.34, lineGain: 0.5 },
  railTube: { coreGain: 0.78, wallGain: 0.72, streakGain: 0.82, lowGain: 0.16 },
  artRingAheadM: [12, 22, 32], artRingScale: 1, artRingRiseS: 0.12, artRingOpenS: 0.35, artRingHideS: 1.2,
  artRingTurnRadps: 0.8, artGentleFace: 0.5, artFinishFace: 0.3, artFinishClearM: 1.5,
  artLight: { sky: '#ffe3f1', ground: '#2a1640', hemi: 1.6, sun: 1.4 },
  // owner 2026-10-22: props read as landmarks, not confetti (about 3x the first pass)
  artScatter: 90, artScatterSideM: [6, 50], artScatterUpM: [-30, 40], artScatterMarginM: 4, artScatterAirM: 10,
  artScatterKinds: [
    { id: 'card-drop', w: 3, scale: [8, 16], spin: 0.35 }, { id: 'card-lens', w: 3, scale: [8, 16], spin: 0.35 },
    { id: 'card-orbit', w: 3, scale: [8, 16], spin: 0.35 }, { id: 'card-petal', w: 3, scale: [8, 16], spin: 0.35 },
    { id: 'pocket-watch', w: 3, scale: [6, 12], spin: 0.3 }, { id: 'bubble', w: 4, scale: [4, 10], spin: 0.15 },
    { id: 'spiral-disc', w: 2, scale: [6, 14], spin: 0.45 }, { id: 'slot-cabinet-lod0', w: 1.5, scale: [4, 7], spin: 0.1 },
    { id: 'chess-king-lod0', w: 1, scale: [6, 12], spin: 0.18 }, { id: 'chess-queen-lod0', w: 1, scale: [6, 12], spin: 0.18 },
    { id: 'chess-rook-lod0', w: 1, scale: [6, 12], spin: 0.18 }, { id: 'chess-bishop-lod0', w: 1, scale: [6, 12], spin: 0.18 },
    { id: 'chess-knight-lod0', w: 1, scale: [6, 12], spin: 0.18 }, { id: 'chess-pawn-lod0', w: 1.5, scale: [6, 12], spin: 0.18 },
  ],
  // B1 karts (render/karts.js, render/kartPose.js). Tint 0 is the player; the mix keeps the porcelain readable
  kartTints: ['#ffffff', '#ff7eb6', '#7fc8ff', '#ffc46b', '#b48cff', '#7fe8c0', '#ff9a7a', '#e6e0ff'],
  kartTintMix: 0.8, ghostEmissive: '#fff4fa', ghostGain: 0.45, ghostRiseS: 0.15,
  // ART5: a rival's Petal / Trinket porcelain mixes this far toward its cup colourway (render/wardrobe.js);
  // LOOK.rig may override render/kartPose.js RIG_DEFAULTS (clip thresholds, fades, the Gentle clip list)
  vehicleTintMix: 0.6,
  leanPerRadps: 0.13, leanMaxRad: 0.2, leanTcS: 0.12, driftLeanRad: 0.1, boostPitchRad: 0.05, pitchTcS: 0.2,
  wobbleRad: 0.26, wobbleHz: 3.2, wobbleTcS: 0.35, wobbleGentle: 0.4, botSquashGoneS: 0.25,
  antSwayPerMps2: 0.012, antSwayMaxRad: 0.5, antTcS: 0.09, armsUpRad: 0.9, armsTcS: 0.18,
  tagNearM: 40, tagFadeM: 8, tagHideNearM: [5, 10], tagUpM: 1.75, tagSizeM: [1.3, 0.32], tagFont: '600 34px system-ui, sans-serif',
  // B1 particles: shapes 0 soft disc, 1 sparkle (four arms), 2 petal; tier ladder adds rate, size and petals
  sparkRateByTier: [70, 130, 190], sparkSizeByTier: [0.1, 0.15, 0.2], sparkPetalByTier: [0, 0.1, 0.3],
  sparkSpin: 6, burstPetals: 0.35, botSparkMul: 0.6,
  ringPool: 8, ringColour: '#fff4fa', ringLifeS: 0.45, ringMaxM: [1.8, 2.6, 3.4], ringWidth: 0.1, ringGain: 0.45,
  bumpSparks: [10, 36], bumpColours: ['#ffffff', '#ff8cc6', '#ffd36b'], bumpSpeedMps: 5,
  popCount: 26, popColours: ['#bff4ff', '#ffc2df', '#ffffff', '#e6d6ff'], popSpeedMps: 3.5, popSizeM: 0.16,
  boxCount: 22, boxColours: ['#ffd36b', '#ffffff', '#ff8cc6'],
  wispRate: 60, wispColour: '#e8f6ff', wispSizeM: 0.12, wispSpeedMps: 14, wispLifeS: 0.3,
  // boost ribbon (render/wakes.js): a flat strip behind the saucer, one draw for every kart
  // fixer: the ribbon fades out between these distances from the eye (the lens rule; the kart rides about 6.5 m out)
  trailLensM: [2.5, 7],
  trailWidthM: 0.55, trailUpM: 0.12, trailStepM: 0.55, trailAlpha: 0.7, trailFadeTcS: 0.25, padTrail: '#ffd36b', launchTrail: '#ffffff',
  // ambient motes (render/motes.js): a wrapped box around the player; deep sections thicken them
  moteBoxM: [70, 26, 70], moteSizeM: [0.05, 0.16], moteDrift: [0.012, 0.02, 0.012], moteBase: 0.3, moteDeep: 1,
  moteAlpha: 0.55, moteNearM: 2.5, moteGentle: 0.5,
  // road glass sheen from road-glass-normal: a fixed key light and a view fresnel, no extra light pass
  sheenDir: [-0.3, 0.75, -0.6], sheenPow: 40, sheenGain: 0.25, sheenBump: 0.35, sheenFresnel: 0.07, sheenGentle: 0.5,
  // menu attract orbit around the player kart
  menuOrbitRadps: 0.18, menuOrbitM: 4.6, menuOrbitUpM: 1.6, menuLookUpM: 0.7,
  // polish 1: finish arch membrane (render/dressing.js): fade-in distance, lit share, Gentle share, spin share
  artMembraneNearM: [5, 18], artMembraneLit: 0.22, artMembraneGentle: 0.5, artMembraneTurnMul: 0.6,
  // polish 1: particle atlas numbers (render/atlas.js re-exports this as FXART_LOOK)
  fxArt: Object.freeze({
    // sprite size share per cell (the art has a gutter and soft edges; the old discs filled the sprite)
    cellSize: Object.freeze({ disc: 1, spark: 2, glint: 1.5, glitter: 1.8, petal: 1.2, ring: 1.6, smoke: 1.6,
      drip: 1.1, shard: 1.2, frost: 1.5, swirl: 1.4, bubble: 1.5, star: 1.3 }),
    ringCellR: 0.58,              // the ring cell's radius as a share of the cell half width (measured)
    // ambient motes: petal and swirl shares (afterglow brings petals, deep brings swirls)
    motePetal: [0.12, 0.55], moteSwirl: [0, 0.35], moteCellSize: 1.3,
    // item-hit bursts (Freeze crystals, Golden stars, Melt drips)
    hitCount: 18, hitSpeedMps: 3, hitSizeM: 0.2,
    frostColours: Object.freeze(['#dff4ff', '#bfe8ff', '#ffffff']),
    goldColours: Object.freeze(['#ffd36b', '#fff1c2', '#ffffff']),
    meltColours: Object.freeze(['#ff7cc8', '#ffc2df', '#e6a0ff']),
  }),
};
