const shades = new Set(['red', 'cyan', 'purple', 'gold']);

function initialShade(index) {
  if (index === 51) return 'purple';
  const value = index * 17 % 100;
  if (value < 2) return 'gold';
  if (value < 10) return 'purple';
  return value < 55 ? 'red' : 'cyan';
}

// Seed colors once. Motion frames can remove, insert or reorder capsule IDs.
export function createCapsulePalette(frames, savedPile) {
  const colors = new Map();
  for (const frame of frames) {
    for (const [index, capsule] of frame.entries()) {
      if (!colors.has(capsule.id)) colors.set(capsule.id, initialShade(index));
    }
  }
  for (const [index, capsule] of (savedPile || []).entries()) {
    // Old piles stored the source artwork color. Recover what was actually on screen.
    colors.set(capsule.id, shades.has(capsule.shade) ? capsule.shade : initialShade(index));
  }
  return colors;
}
