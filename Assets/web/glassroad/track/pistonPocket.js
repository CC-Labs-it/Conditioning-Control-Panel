// The road cut, sliding lid and half-sunk head share these dimensions and one stroke.
const clamp = v => Math.max(0, Math.min(1, v));
const ease = v => { v = clamp(v); return v * v * (3 - 2 * v); };
export const PISTON_TOP_M = 1.6;
export function pistonPocket(row) {
  const side = row.side === -1 ? -1 : 1, edge = row.roadWidth / 2;
  const activeX = side * Math.max(row.w / 2 + (row.pocketClearM ?? 3), side * row.x - row.travelM);
  const inner = activeX - side * (row.w / 2 + .15);
  const minX = side < 0 ? -edge : inner, maxX = side < 0 ? inner : edge;
  return { s: row.s, len: row.len + .3, x: (minX + maxX) / 2, w: maxX - minX, minX, maxX,
    topM: PISTON_TOP_M, baseM: -PISTON_TOP_M, heightM: PISTON_TOP_M * 2,
    parkX: side * (edge + row.w / 2 + .5), activeX };
}
export function pistonPocketPose(row, extension) {
  const pocket = pistonPocket(row), open = ease(extension / .2);
  const headX = pocket.parkX + (pocket.activeX - pocket.parkX) * ease((extension - .2) / .8);
  const panelY = -2.1 * ease(extension / .08);
  const panelShift = row.side * (pocket.w + .3) * ease((extension - .08) / .12);
  return { headX, headY: pocket.baseM, topM: pocket.topM, panelX: pocket.x + panelShift, panelY, panelShift, open };
}
export function pistonPocketContains(pocket, s, lateral, lengthM) {
  const ds = ((s - pocket.s + lengthM * 1.5) % lengthM) - lengthM / 2;
  return Math.abs(ds) <= pocket.len / 2 && lateral >= pocket.minX && lateral <= pocket.maxX;
}
