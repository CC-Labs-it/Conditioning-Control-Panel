// Metres. Room coordinates stay fixed when the dome dissolves.
export const START = Object.freeze({ x: 0, z: 64 });
const names = [
  ['first', 'The First Press'], ['hoof', 'Hoof Simulator'], ['habitat', 'The Human Habitat'],
  ['twelve', 'The Hall of the Twelve'], ['dome', 'There is nothing to see here'], ['shop', 'Exit through the gift shop'],
];
export const ROOMS = Object.freeze(names.map(([id, label], i) => {
  const angle = (-150 + i * 60) * Math.PI / 180;
  return Object.freeze({ id, label, angle, x: Math.sin(angle) * 44, z: Math.cos(angle) * 44, width: 24, depth: 26 });
}));
export function localPoint(room, x, z) {
  const dx = x - room.x, dz = z - room.z, s = Math.sin(room.angle), c = Math.cos(room.angle);
  return { x: dx * c - dz * s, z: dx * s + dz * c };
}
export function roomAt(x, z) {
  return ROOMS.find(room => {
    const p = localPoint(room, x, z);
    return room.id === 'dome' ? Math.hypot(p.x, p.z) < 13 : Math.abs(p.x) < 12 && Math.abs(p.z) < 13;
  })?.id || (z < -19 && Math.abs(x) < 5 ? 'contract' : 'plaza');
}
function onFloor(x, z) {
  if (Math.hypot(x, z) <= 18 || Math.hypot(x, z - 63) <= 11) return true;
  if (Math.abs(x) <= 4 && z >= 16 && z <= 63) return true;
  if (Math.abs(x) <= 2.5 && z >= -120 && z <= -16) return true;
  if (Math.abs(x) <= 5 && z >= -128 && z <= -118) return true;
  for (const room of ROOMS) {
    const p = localPoint(room, x, z);
    if (room.id === 'dome' ? Math.hypot(p.x, p.z) <= 13 : Math.abs(p.x) <= 12 && Math.abs(p.z) <= 13) return true;
    if (Math.abs(p.x) <= 3 && p.z >= -28 && p.z <= -12) return true;
  }
  return false;
}
export function canWalk(x, z, radius = .28) {
  if (!Number.isFinite(x) || !Number.isFinite(z)) return false;
  // Sample the capsule perimeter so corners cannot cut through a jamb.
  for (let i = 0; i < 8; i++) {
    const a = i * Math.PI / 4;
    if (!onFloor(x + Math.cos(a) * radius, z + Math.sin(a) * radius)) return false;
  }
  return onFloor(x, z);
}
