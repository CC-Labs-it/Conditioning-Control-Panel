/* render/look.js - the render look table. Feel numbers (what the player tunes) live in feel.json;
 * these are the art numbers of the greybox look, in one place so no module hard-codes them.
 * Colours are CSS hex. Section `palette` and `sky` ids in track JSON key into PALETTES and SKIES.
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
  snapM: 20, nearM: 0.1, farM: 6000, skyRadiusM: 3000, skyArms: 7, skyContrast: 0.8, skyDim: 0.55, skyGapDim: 0.72,
  skyEyeTiltDeg: 68, skyEyeTcS: 2, rescueWhite: 0.45,
  // road bands (u = lateral metres, v = s metres)
  bandLenM: 14, bandWarpPerM: 0.35, bandWarpAmp: 0.15, bandScrollMps: 6, roadAlpha: 0.72,
  seamGain: 0.35, beatBreath: 0.06, beatBreathRise: 0.15, roadGain: 0.8,
  railHeightM: 0.9, railAlpha: 0.25, railGain: 0.9, railNearM: [4, 16],
  padStripeM: 1.5, padScrollMps: 8, padPulseGain: 1.5, padColour: '#ffd36b', lipColour: '#ffffff',
  // kart greybox (saucer radius about eyeBack / 9)
  saucerR: 0.72, cupColour: '#fff4fa', saucerColour: '#ffd36b', emiColour: '#ff8cc6', faceColour: '#2a1630',
  lightDir: [0.35, 0.85, 0.4], ambient: 0.55, rimGain: 0.35,
  // floating props for speed reference
  props: 36, propSideM: [12, 30], propUpM: [-8, 14], propSizeM: [1.5, 5], propColour: '#e9dcff',
  // post
  bloomStrength: 0.22, bloomRadius: 0.3, bloomThreshold: 1.0, bloomBoostGain: 1.5,
  vignetteIn: 0.35, vignetteOut: 0.95, vignetteDark: 0.55, edgeTint: '#ff4fa3', flashTint: '#fff0c8',
  // particles
  sparkRate: 140, sparkSpeedMps: [2, 6], sparkSizeM: 0.14, sparkSpreadMps: 1.5, burstSpreadMps: 4, burstCount: 60,
  dustCount: 40, dustSpeedMps: 3, dustSizeM: 0.4, dustColour: '#f4eef8', dustLifeS: 0.6,
  exhaustRate: 90, exhaustSizeM: 0.3, exhaustLifeS: 0.35, exhaustColour: '#ffb3d9',
  particleDrag: 2.5, particleGravityMps2: 6,
  // speed lines (kart-local, flowing past along -Z)
  lineLenM: [6, 14], lineRadiusM: [5, 12], lineSpanM: 70, lineFlowMps: 45, lineWidthM: 0.04,
  lineFromPct: 0.7, lineGain: 1.6, lineAlpha: 0.35,
  tendrilColour: '#fff6e6', tendrilM: 60, tendrilGain: 0.45, ringColour: '#ffd36b', ringMaxM: 4,
};
