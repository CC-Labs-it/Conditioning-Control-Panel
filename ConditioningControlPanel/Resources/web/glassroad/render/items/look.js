/* render/items/look.js - the art numbers of the effect bubbles and the screen effects (SCREENFX lane).
 * Feel numbers (durations, radii, rules) stay in feel.json `items.*`; these only say how things look.
 * No three.js here, so tests/render-*.test.mjs read it under bare node. Colours are CSS hex or 0..1 rgb.
 */

/** Per quality tier (render/quality.js Q.name): pooled objects and how alive the icons are. */
export const ITEM_TIERS = {
  high: { bubbles: 48, motes: 10, iconPx: 256, iconFps: 30, puddles: 12, copies: 3 },
  mid: { bubbles: 36, motes: 8, iconPx: 192, iconFps: 30, puddles: 8, copies: 3 },
  phone: { bubbles: 24, motes: 5, iconPx: 128, iconFps: 20, puddles: 6, copies: 2 },
};

/** Mote colour per item id (the effect colour), 'r,g,b' 0..255 like DtRH chaosField tints. */
export const MOTE_TINT = {
  flash: '255,236,246', spiral: '120,236,224', brainDrain: '150,255,170', freeze: '190,236,255',
  blackout: '170,140,255', pink: '255,96,190', melt: '255,120,200', shatter: '255,210,140',
  surge: '255,205,90', glass: '220,240,255', echo: '205,195,240', golden: '255,214,110', box: '255,190,235',
};

export const ITEM_LOOK = {
  // bubbles (world metres)
  bubbleR: 0.55, heldR: 0.42, heldUpM: 2.1, heldBobM: 0.12, heldBobHz: 0.6,
  // the player's own rides low in the cup's back right; hidden in the world while the HUD slot shows it
  playerHeldScale: 0.7, playerHeldAt: [0.8, 1.05, 0.35], playerHeldInWorld: false,
  // a bubble near the camera shrinks so it spans at most this share of the view's height (shields exempt)
  nearMaxFrac: 0.08,
  throwR: 0.6, lobR: 0.9, trapR: 0.5, trapHoverM: 0.45, trapUnarmedAlpha: 0.45, pickupR: 0.7, pickupHoverM: 1.3,
  shellRim: 0.9, shellAlpha: 0.12, shellWhite: 0.55, iconFill: 0.727, shellFill: 0.96, // the art/fx bubble spans 186 of 256 px
  rollSwapS: 0.14, rollSwapGentleS: 0.3,
  // motes: a slow ring round each bubble, a little outward and up, then over again
  moteRingMul: [1.15, 1.9], moteRiseM: 0.35, moteSizeM: 0.09, moteLifeS: [6, 10], moteTurnRadps: 0.2,
  // item boxes: a spinning card that turns into a bubble when driven through
  boxSizeM: 1.6, boxHoverM: 1.25, boxBobM: 0.15, boxBobHz: 0.5, boxSpinRadps: 1.4, boxTurnS: 0.35,
  boxCards: ['card-drop', 'card-lens', 'card-orbit', 'card-petal'], boxColour: '#ffd1ec', echoColour: '#cdc3f0',
  // wave 2: the item-card GLB (preferred over boxCards) with a slowly breathing edge, height in metres
  boxCard: 'item-card', boxCardM: 1.45, cardEdgeColour: '#ff7cc8', cardEdgeGlow: 1.6, cardEdgeBreath: 0.3,
  cardEdgeHz: 0.4, cardEdgeGentle: 0.6,
  // wave 2: held, thrown, trap and box-turn bubbles wear the effect-bubble GLB shell (instanced, one draw)
  shellModel: 'effect-bubble', shellGlassPow: 2.4, shellGlassRim: 1.0, shellGlassBody: 0.06, shellGlassSpec: 0.9,
  // the Melt trap prefers the wave 2 melt-spiral mask; the puddle keeps this share of alpha between its arms
  meltMask: 'fx-melt-spiral', meltFloor: 0.55,
  // pops: a ring that opens and a spray of motes
  popRingM: [0.6, 3.6], popMotes: 22, popMoteSizeM: 0.24, popSpeedMps: 4.8, popGravityMps2: 3,
  // melt puddle (trap): the spiral mask, wobbling and dripping
  meltPuddleMul: 1.25, meltArms: 7, meltDripAmp: 0.32, meltWobble: 0.12, meltTurnRadps: 0.35, meltHz: 0.45,
  meltDark: '#3a0c46', meltPink: '#ff7cc8', meltBands: [0.3, 0.7], meltGloss: '#ffe6f6', meltLiftM: 0.04,
  // pink wave: a flat ring opening along the road
  waveColour: '#ff5cbf', waveAlpha: 0.45, waveWidthM: 3,
  // shields
  shieldR: 1.55, shieldUpM: 0.75, goldTint: '#ffcf6b',
  echoSideM: 1.7, echoBackM: 1.4, echoSwayM: 0.35, echoAlpha: 0.38, echoFlickerHz: 2.5, echoFlickerAmp: 0.15,
  goldMotes: 14,
  // ---- polish 2 ----
  // the glass catches the light: a highlight that drifts round the top left, an inner caustic glint
  // low on the far side; the living icon breathes (scale share, Hz; Gentle keeps a third)
  shineDriftRad: 0.5, shineHz: 0.11, causticGain: 0.55, causticSharp: 10, breathAmp: 0.05, breathHz: 0.35,
  // throws: a sparkling ribbon of motes along the last trailPts positions (one every trailStepM)
  trailPts: 22, trailStepM: 0.45, trailSizeM: 0.36, trailAlpha: 0.9, trailTwinkleHz: 0.9,
  // lobs: a dotted arc (a dot every lobDotStepM) and a soft shadow plus a pulsing ring at the target
  lobDots: 18, lobDotStepM: 1.6, lobDotSizeM: 0.3, lobShadowR: [0.9, 2.2], lobShadowAlpha: 0.5, markColour: '#ff8fd0',
  markPulseHz: 1.2, markRingM: 2.4, markAlpha: 0.55,
  // traps: a slow pulse (scale share, Hz) and a warning ring that wakes from warnM[0] to full at warnM[1]
  trapPulse: 0.07, trapPulseHz: 0.8, trapWarnM: [18, 5], trapWarnRingM: 2.2, trapWarnAlpha: 0.6, trapWarnColour: '#ffb3d9',
  // impacts (an effect lands, a shield soaks it): a shockwave ring on the road and a soft glow that never
  // spans more than impactGlowFrac of the view's height; the glow asks photosafe and skips when refused
  impactRingM: [0.8, 11], impactRingS: 0.6, impactRingAlpha: 0.95, impactGlowM: 1.6, impactGlowFrac: 0.18, impactGlowAlpha: 0.55,
  // only an impact this near and on screen asks photosafe for its glow (the shared budget is the player's)
  impactAskM: 60,
  impactGlowS: 0.32,
  // item boxes: a glint sweeps the card every glintEveryS; taken, it bursts into confetti cards
  glintEveryS: 2.4, glintS: 0.45, glintSizeM: 0.9, confetti: { high: 36, mid: 26, phone: 16 }, confettiSizeM: 0.48,
  confettiSpeedMps: [4.5, 10], confettiGravityMps2: 7, confettiS: 1.5,
  // polish 3: the spectrum, pink and violet still leading
  confettiColours: ['#ff4fb0', '#ffffff', '#ffd23a', '#22e6ff', '#9b4dff', '#a8ff2e', '#ff2bd6', '#ff8c1a'],
  // ---- polish 4: items as events ----
  // a throw leaves charged: the bubble starts launchPop bigger and settles by launchS, a glow swells and a ring
  // opens on the road where it left (the glow capped to impactGlowFrac of the view like an impact)
  launchS: 0.4, launchPop: 0.7, launchGlowM: 2.4, launchGlowAlpha: 0.45, launchRingM: [0.6, 5],
  // the trail you can follow: a soft halo under every other ribbon point
  trailHaloMul: 2.8, trailHaloAlpha: 0.22,
  // impacts: a wider pop ring, more motes, a second shockwave a beat later, confetti in the kind's colour and
  // a fountain of glitter (counts per tier)
  impactPopRingM: [1, 6.5], impactMotesMul: 1.8, impactRing2S: 0.12, impactRing2Mul: 0.6,
  hitConfetti: { high: 24, mid: 16, phone: 10 }, fountain: { high: 18, mid: 12, phone: 8 },
  fountainSpeedMps: [3.5, 7.5], fountainGravityMps2: 6, fountainS: 0.9, fountainSizeM: 0.32,
};

/**
 * Polish 4, items as events. HIT_LOOK: what a hit does to the kart and, for the player, the camera and screen
 * (fx/reactions.js kick, fx/effects.js tint, render/karts.js rim, burst and stun stars). The tint keeps the
 * picture's luminance and only moves its hue, strongest at the edges; Gentle halves it.
 */
export const HIT_LOOK = {
  kickPushM: 0.6, kickInS: 0.06, kickOutS: 0.5,
  tintInS: 0.12, tintHoldS: 0.12, tintOutS: 0.7, tintAmt: 0.6, tintGentle: 0.5, tintRefusedMul: 0.5, tintRefusedInS: 0.3,
  tintEdge: [0.12, 0.75], tintCentre: 0.25,
  // the kart's rim in the kind's hue while stunned; the hue drifts round (lightness held), Gentle holds it still
  rimLight: 0.62, rimSat: 1,
  // the impact burst: a wide ring of the kind's colour plus white sparkles
  burstSpeedMps: 7.5, burstSizeM: 0.42, burstLifeS: 0.75, burstUp: 1.4, burstPlayerMul: 0.35,
};

/** Self items wrap the kart in an aura while they run (render/karts.js): colour, how strong, how the bands climb. */
export const AURA_LOOK = {
  colours: { surge: '#ffb43a', glass: '#9fe4ff', echo: '#b49cff', golden: '#ffd66e' },
  // a coloured shell you see the kart through: peak alpha on rivals, and on the player's own (right under the
  // camera, so it must never push the middle of the view to white) playerAlpha with a softer rim
  size: [1.55, 1.3, 1.9], upM: 0.75, alpha: 0.55, playerAlpha: 0.3, fresPow: 1.6, bands: 3, bandClimbHz: 0.9, bandTwist: 2,
  gentle: 0.5, ringM: 2.1, ringAlpha: 0.6,
  // motes rising off the aura, per second (high tier; mid and phone scale by the tier's mote share)
  motesPerS: 22, moteSpeedMps: 1.8, moteSizeM: 0.22, moteLifeS: 0.8,
};

/** The HUD item slot (render/items/slotfx.js): motes orbit the held bubble, a roll shuffles cards. */
export const SLOT_LOOK = {
  orbitN: 7, orbitPhoneN: 4, orbitHz: 0.18, orbitR: 0.47, orbitTilt: 0.35, orbitSizePx: [1.4, 3.2], orbitAlpha: 0.85,
  shuffleS: 0.11, shuffleDx: 0.32, shuffleRotDeg: 9, shuffleScale: 0.9, gentleMul: 0.4,
};

/** The screen effects (render/screenfx.js). uv units are fractions of the view height unless named. */
export const SCREEN_LOOK = {
  gentleMul: 0.5, gentleTimeMul: 0.5,
  keepImage: 0.18, keepBright: [0.55, 1.0], keepBrightGain: 0.85,  // never blind: a floor plus the highlights
  blurMaxUv: 0.022, blurTaps: 8,
  // Flash: a rounded card, the B1 stand-in picture is the spiral icon on paper
  flashAlpha: 0.9, flashRound: 0.06, flashPaper: '#fff3fa', flashInk: '#ff4fa3', flashOffset: [0.06, -0.04],
  flashPop: 0.08, flashCornerAt: [0.16, 0.5], flashShadow: 0.25,
  flashCornerScale: 0.35,  // only when fx sends no params.scale (Gentle sends items.gentleFlashScale)
  // Spiral: turning arms over the middle
  spiralArms: 5, spiralTwist: 6, spiralTurnRadps: 1.6, spiralAlpha: 0.42, spiralR: [0.12, 0.62],
  spiralColour: '#ff8fd0', spiralDim: 0.55,
  // Brain Drain: edges blur and grey, centre sharp
  drainEdge: [0.2, 0.75], drainGrey: 1, drainBlur: 1,
  // Freeze: frost from the edges in
  frostEdge: [0.55, 1.05], frostNoise: 7, frostColour: '#dff4ff', frostAlpha: 0.8, frostBlur: 0.35, frostVeins: 0.16,
  // Blackout: an iris closes to a circle
  irisFrom: 1.3, irisTo: 0.26, irisSoft: 0.08, irisAt: [0.5, 0.56],
  // Pink: a wash with the contrast down
  pinkTint: [1.0, 0.58, 0.84], pinkContrast: 0.6, pinkLift: 0.12,
  // Melt: the whole view runs down like wet paint
  meltRunUv: 0.08, meltDripUv: 0.12, meltStreak: 2.8, meltWobbleUv: 0.016, meltCols: 9, meltHz: 0.6, meltBlur: 0.75, meltSat: 1.15,
  // Shatter: a soft radial smear and a brief tilt
  shatterSmear: 0.08, shatterTiltRad: 0.07, shatterTiltHz: 1.4, shatterTaps: 6,
  // ---- polish 2: each effect has a readable IN and OUT ----
  // Melt: colour channels run apart down the view; drips fall from the top (columns, view heights per s, length)
  meltChromaUv: 0.016, meltDripCols: 26, meltDripSpeed: 0.55, meltDripLen: 0.4, meltDripGloss: 0.22, meltDripTint: '#ff7cc8',
  // Spiral: a full-view spiral of two bands that opens from the centre with the amount and twirls the view a little
  spiralBands: ['#ff8fd0', '#7ce8e0'], spiralFullAlpha: 0.5, spiralOpenR: 1.25, spiralTwirlRad: 0.4, spiralHole: 0.05, spiralEdge: 0.1,
  // Freeze: a frost front crystallises in from the edges (frontAt = where it stops at full), crystal glints on the front
  frostFrontAt: 0.34, frostFrontSoft: 0.09, frostCells: 22, frostGlint: 0.45,
  // Shatter: cracks grow out from the middle (crackS), then the shards slide apart (slideUv) and settle on the OUT;
  // shards are cells in log-polar space (spokes round the middle, rings per e-fold of radius); seam and the gap it
  // opens as the shards part in view heights
  shatterSpokes: 9, shatterRings: 1.7, shatterCrackS: 0.28, shatterSlideS: 0.35, shatterSlideUv: 0.04, shatterCrackW: 0.004,
  shatterGapW: 0.006, shatterCrackDark: 0.45,
  // Blackout: a soft rim on the closing iris
  irisRimPow: 6, irisRimColour: '#ff8fd0', irisRimAlpha: 0.35,
  // Pink: the wash closes in from the edges round a heart (heart size at no amount, at full)
  pinkHeartR: [1.6, 0.55], pinkHeartSoft: 0.22, pinkEdgeTint: '#ff5cbf', pinkEdgeAlpha: 0.5, pinkRimW: 0.05, pinkRimAlpha: 0.45,
  // Brain Drain: the grey edge creeps inward by this much of the view as the amount rises
  drainCreep: 0.45,
  // ---- polish 4: bolder, and clear about when it ends ----
  // the frame meter: a band round the whole view edge in the effect's colour that drains clockwise from the top
  // as the effect runs out (width in view heights); the badge: the effect's icon in a disc low in the middle,
  // with a ring that drains the same way (centre in uv, radius in view heights)
  meterW: 0.014, meterGlowW: 0.06, meterAlpha: 0.95, meterGlow: 0.3, meterShade: 0.3, meterTrack: '#2a1238',
  badgeAt: [0.5, 0.915], badgeR: 0.05, badgeRingW: 0.013, badgePaper: '#2a1238', badgeAlpha: 0.95, badgePop: 0.35, badgePopS: 0.45,
  meterGentle: 0.8,
  meterColours: {
    flash: '#ff4fb0', spiral: '#22e6ff', brainDrain: '#5dff7a', freeze: '#6fd2ff', blackout: '#9b6bff', pink: '#ff3fae',
    melt: '#ff6ad0', shatter: '#ffb02e',
  },
};
