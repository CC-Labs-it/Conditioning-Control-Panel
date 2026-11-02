// Visible water can retain grip physics in the authored coolant canal.
// Share this rule between the surface solver and contact audio.
export function isWaterSurface(surface, section = '') {
  return surface === 'stream' || surface === 'pool' || section === 'coolant-canal';
}
