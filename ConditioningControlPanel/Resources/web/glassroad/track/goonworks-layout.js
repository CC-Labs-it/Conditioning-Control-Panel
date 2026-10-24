// GOONWORKS route stations. Geometry, scenery and mechanics share these authored ranges.
const RAD = Math.PI / 180;
const line = (id, group, len, y, w = 36, extra = {}) => ({ id, group, len, y, w, ...extra });
const arc = (id, group, turn, r, y, w = 36, extra = {}) => ({ id, group, turn, r, y, w, ...extra });
export const GOONWORKS_TIERS = Object.freeze([0, 32, 64, 96]);
export const goonworksPieceLength = p => p.turn ? Math.abs(p.turn) * RAD * (p.r + (p.rEnd || p.r)) / 2 : p.len;
export const goonworksSegmentCount = p => p.jump ? 3 : Math.max(1, Math.round(goonworksPieceLength(p) / (p.rEnd ? 10.8 : 10)));
export function walkGoonworks(pieces) {
  let x = 0, z = 0, heading = 0; const starts = [];
  for (const p of pieces) {
    starts.push({ x, z, heading });
    if (p.turn) {
      const sign = Math.sign(p.turn), next = heading + p.turn * RAD, grow = sign * ((p.rEnd || p.r) - p.r);
      x += sign * p.r * (Math.sin(next) - Math.sin(heading)) + grow * Math.sin(next);
      z += sign * p.r * (Math.cos(heading) - Math.cos(next)) - grow * Math.cos(next); heading = next;
    } else { x += Math.cos(heading) * p.len; z += Math.sin(heading) * p.len; }
  }
  return { x, z, heading, starts };
}
function pieces() {
  const p = [
    line('start-terrace', 'start', 100, 0, 40),
    arc('rabbit-bank-in', 'rabbit-hole', 60, 90, -12, 36, { surface: 'grip', roll: 0 }),
    arc('rabbit-s-bend', 'rabbit-hole', -120, 90, -12, 36, { surface: 'grip', roll: 0 }),
    arc('rabbit-exit', 'rabbit-hole', 60, 90, 0, 36, { surface: 'grip', roll: 0 }),
    arc('grotto-climb', 'screen-grotto', 90, 90, 32),
    line('grotto-gallery', 'screen-grotto', 180, 32),
    arc('mill-approach', 'aperture-mill', 90, 85, 32, 44),
    line('mill-hall', 'aperture-mill', 200, 32, 44),
    arc('quiz-approach', 'pop-quiz', 90, 90, 32, 44),
    line('quiz-ballroom', 'pop-quiz', 180, 32, 64, { wFrom: 44 }),
    arc('ribbon-entry', 'ribbon-helix', -90, 75, 32, 32, { wFrom: 64 }),
    arc('ribbon-braid', 'ribbon-helix', 720, 95, 96, 32, { linear: true, roll: 10 }),
    line('leapfrog-approach', 'leapfrog', 60, 96, 36, { wFrom: 32 }),
    line('leapfrog-field', 'leapfrog', 300, 96, 36, { field: true }),
    line('leapfrog-exit', 'leapfrog', 40, 96),
    arc('pinball-approach', 'pinball-launch', 90, 90, 96, 44),
    line('pinball-table', 'pinball-launch', 160, 96, 44),
    line('pinball-ramp', 'pinball-launch', 40, 106, 44, { kick: true }),
    line('pinball-gap', 'pinball-launch', 20, 80, 44, { jump: true, linear: true }),
    line('pinball-landing', 'pinball-launch', 70, 80, 44),
    arc('fold-approach', 'grand-fold', 90, 90, 80, 36),
    line('fold-overlook', 'grand-fold', 180, 80, 36),
    arc('fold-descent', 'grand-fold', 540, 75, 32, 76, { rEnd: 115, wFrom: 36, linear: true, roll: 8 }),
    line('fold-runout', 'grand-fold', 100, 0, 36, { wFrom: 76 }),
    line('finish-underpass', 'finish', 40, 0, 36),
    arc('finish-west-turn', 'finish', -90, 90, 0),
    line('finish-crossing', 'finish', 90, 0),
    arc('finish-home-turn', 'finish', -90, 90, 0),
    line('home-straight', 'finish', 200, 0, 40),
  ];
  // Two closure dimensions are solved from the authored route, never by an invisible final connector.
  const end = walkGoonworks(p);
  p.find(v => v.id === 'home-straight').len -= end.x;
  p.find(v => v.id === 'finish-crossing').len -= end.z;
  if (p.some(v => !v.turn && v.len < 20)) throw Error('GOONWORKS closure has a short or reversed straight');
  return p;
}
export const GOONWORKS_PIECES = Object.freeze(pieces().map(Object.freeze));
const CACHE = new WeakMap();
export function getGoonworksLayout(track) {
  if (track.id !== 'goonworks') return null;
  if (CACHE.has(track)) return CACHE.get(track);
  let node = 0;
  const landmarks = GOONWORKS_PIECES.map(p => {
    const from = node; node += goonworksSegmentCount(p);
    return { id: p.id, group: p.group, node: from, from, to: node, s0: track.nodeS[from],
      s1: node < track.nodeCount ? track.nodeS[node] : track.lengthM, widthM: p.w };
  });
  const byId = Object.fromEntries(landmarks.map(v => [v.id, v]));
  const sections = track.sections.map(s => ({ ...s, s0: s.fromS, s1: s.toS || track.lengthM,
    widthM: track.frameAt((s.fromS + (s.toS || track.lengthM)) / 2).w }));
  const bounds = { minX: Infinity, maxX: -Infinity, minZ: Infinity, maxZ: -Infinity, minY: Infinity, maxY: -Infinity };
  for (let s = 0; s < track.lengthM; s += 5) {
    const f = track.frameAt(s), m = f.w / 2;
    for (const [axis, k] of [['X', 0], ['Z', 2]]) { bounds['min' + axis] = Math.min(bounds['min' + axis], f.p[k] - m); bounds['max' + axis] = Math.max(bounds['max' + axis], f.p[k] + m); }
    bounds.minY = Math.min(bounds.minY, f.p[1]); bounds.maxY = Math.max(bounds.maxY, f.p[1]);
  }
  const range = id => sections.find(s => s.id === id);
  const tube = { ...range('rabbit-hole'), radiusM: 20, blendM: 35, windows: true };
  const field = { ...byId['leapfrog-field'], id: 'goonworks-leapfrog', lanes: [-8, 0, 8], widthM: 36,
    stones: track.fields.find(f => f.id === 'goonworks-leapfrog')?.stones || [] };
  const launch = { rampS0: byId['pinball-ramp'].s0, rampS1: byId['pinball-gap'].s0,
    gapS0: byId['pinball-gap'].s0, gapS1: byId['pinball-landing'].s0,
    landingS0: byId['pinball-landing'].s0, landingS1: byId['pinball-landing'].s1, widthM: 44, gapM: 20, riseM: 10 };
  const layout = { landmarks, byId, sections, bounds, tiers: GOONWORKS_TIERS, tube, field, launch, routes: track.routes };
  CACHE.set(track, layout); return layout;
}
export function goonworksRange(track, id) {
  const layout = getGoonworksLayout(track);
  return layout?.byId[id] || layout?.sections.find(s => s.id === id) || null;
}
