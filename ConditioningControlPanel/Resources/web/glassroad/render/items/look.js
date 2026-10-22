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
  popRingM: [0.4, 2.4], popMotes: 12, popSpeedMps: 3.2, popGravityMps2: 3,
  // melt puddle (trap): the spiral mask, wobbling and dripping
  meltPuddleMul: 1.25, meltArms: 7, meltDripAmp: 0.32, meltWobble: 0.12, meltTurnRadps: 0.35, meltHz: 0.45,
  meltDark: '#3a0c46', meltPink: '#ff7cc8', meltBands: [0.3, 0.7], meltGloss: '#ffe6f6', meltLiftM: 0.04,
  // pink wave: a flat ring opening along the road
  waveColour: '#ff5cbf', waveAlpha: 0.45, waveWidthM: 3,
  // shields
  shieldR: 1.55, shieldUpM: 0.75, goldTint: '#ffcf6b',
  echoSideM: 1.7, echoBackM: 1.4, echoSwayM: 0.35, echoAlpha: 0.38, echoFlickerHz: 2.5, echoFlickerAmp: 0.15,
  goldMotes: 14,
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
  drainEdge: [0.22, 0.8], drainGrey: 0.9, drainBlur: 1,
  // Freeze: frost from the edges in
  frostEdge: [0.55, 1.05], frostNoise: 7, frostColour: '#dff4ff', frostAlpha: 0.65, frostBlur: 0.35, frostVeins: 0.16,
  // Blackout: an iris closes to a circle
  irisFrom: 1.3, irisTo: 0.3, irisSoft: 0.08, irisAt: [0.5, 0.56],
  // Pink: a wash with the contrast down
  pinkTint: [1.0, 0.62, 0.85], pinkContrast: 0.5, pinkLift: 0.12,
  // Melt: the whole view runs down like wet paint
  meltRunUv: 0.06, meltDripUv: 0.1, meltStreak: 2.5, meltWobbleUv: 0.012, meltCols: 9, meltHz: 0.6, meltBlur: 0.75, meltSat: 1.15,
  // Shatter: a soft radial smear and a brief tilt
  shatterSmear: 0.06, shatterTiltRad: 0.07, shatterTiltHz: 1.4, shatterTaps: 6,
};
