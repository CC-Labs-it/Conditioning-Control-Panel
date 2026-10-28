/* Authored Boudoir stations. A winding vanity, shelf and wardrobe route, in metres. */
const RAD = Math.PI / 180;
const line = (id, len, y, w = 36, extra = {}) => ({ id, len, y, w, ...extra });
const bend = (id, turn, r, y, extra = {}) => ({ id, turn, r, y, w: 36, ...extra });
export const boudoirPieceLength = p => p.turn ? Math.abs(p.turn) * RAD * p.r : p.len;
export const boudoirSegmentCount = p => Math.max(1, p.jump ? Math.ceil(boudoirPieceLength(p) / 10) : Math.round(boudoirPieceLength(p) / 10));
export function walkBoudoir(pieces) {
  let x = 0, z = 0, heading = 0; const starts = [];
  for (const p of pieces) {
    starts.push({ x, z, heading });
    if (p.turn) {
      const next = heading + p.turn * RAD, radius = Math.sign(p.turn) * p.r;
      x += radius * (Math.sin(next) - Math.sin(heading));
      z += radius * (Math.cos(heading) - Math.cos(next)); heading = next;
    } else { x += p.len * Math.cos(heading); z += p.len * Math.sin(heading); }
  }
  return { x, z, heading, starts };
}
function pieces() {
  const p = [
    line('vanity-grid', 180, 0, 40, { profile: { begin: .4, crest: 2, pinch: 4 } }), bend('powder-sweep', -60, 110, 4, { w: 40 }),
    line('perfume-court', 160, 4, 44), bend('powder-return', 120, 110, 10, { w: 40 }),
    bend('ribbon-weave', -60, 110, 10), line('drawer-hall', 200, 10, 34),
    bend('shelf-approach', 90, 100, 14), bend('shelf-climb', 720, 82, 98, { linear: true, w: 32 }),
    line('shelf-gallery', 180, 98, 36, { edge: 'L', profile: { crest: 6, weave: 13, pinch: 8, bank: 5 } }),
    bend('shelf-weave-in', -45, 100, 104), bend('shelf-weave-cross', 90, 100, 108), bend('shelf-weave-out', -45, 100, 102),
    line('upper-gallery', 130, 102, 36, { profile: { crest: 4, pinch: 8, bank: -7 } }),
    bend('upper-weave-in', 45, 95, 106), bend('upper-weave-cross', -90, 95, 106), bend('upper-weave-out', 45, 95, 102),
    line('upper-runout', 370 - 190 * Math.SQRT2, 102), bend('upper-turn', -90, 100, 102),
    line('wardrobe-bridge', 200, 102, 38), bend('wardrobe-in', 90, 110, 102),
    bend('wardrobe-descent', 810, 88, 12, { linear: true, w: 32, stepWidth: true }),
    line('wardrobe-out', 120, 12, 46),
    bend('pearl-slalom-in', -45, 90, 16), bend('pearl-slalom-cross', 90, 90, 20), bend('pearl-slalom-out', -45, 90, 18),
    line('bath-approach', 160, 18, 40),
    bend('bath-weave-in', -45, 95, 23, { w: 40 }), bend('bath-weave-cross', 90, 95, 23, { w: 40 }), bend('bath-weave-out', -45, 95, 18, { w: 40 }),
    line('bath-stream', 340, 18, 40, { surface: 'stream', flow: [4, 0] }),
    bend('bath-turn', 90, 110, 18, { w: 40, surface: 'stream', flow: [4, 0] }),
    line('bath-chute', 110, -14, 40, { surface: 'stream', flow: [7, 0] }),
    line('bath-pool', 80, -14, 40, { surface: 'pool' }),
    line('compacts-approach', 70, -10, 40), line('compacts', 300, -10, 40, { field: true }),
    line('compacts-exit', 90, -10, 40), line('mirror-approach', 60, 0, 40),
    line('mirror-ramp', 50, 14, 40, { kick: true }), line('mirror-gap', 24, -6, 40, { jump: true, linear: true }),
    line('mirror-landing', 140, -6, 40), bend('home-turn', 90, 110, 0, { w: 40 }), line('home', 100, 0, 40),
  ];
  const end = walkBoudoir(p);
  p.find(row => row.id === 'bath-approach').len += end.x;
  p.find(row => row.id === 'mirror-approach').len += end.z;
  if (p.some(row => !row.turn && row.len < 20)) throw Error('Boudoir closure needs more room: ' + JSON.stringify(p.filter(row => !row.turn && row.len < 20)));
  return p;
}
export const BOUDOIR_PIECES = Object.freeze(pieces().map(Object.freeze));
export function getBoudoirLayout(track) {
  if (track.id !== 'boudoir') return null;
  let node = 0;
  const landmarks = BOUDOIR_PIECES.map(p => {
    const from = node; node += boudoirSegmentCount(p);
    return { id: p.id, node: from, to: node, s0: track.nodeS[from], s1: node < track.nodeCount ? track.nodeS[node] : track.lengthM, widthM: p.w };
  });
  const find = id => landmarks.find(row => row.id === id);
  const halls = ['drawer-hall', 'wardrobe-bridge'].map(id => ({ id, s0: find(id).s0, s1: find(id).s1, widthM: find(id).widthM, heightM: 26 }));
  const perfume = [
    { id: 'powder-cloud', landmark: 'perfume-court', start: 55, len: 60, x: -6, w: 24, offsetS: 0 },
    { id: 'drawer-cloud', landmark: 'drawer-hall', start: 75, len: 50, x: -5, w: 16, offsetS: 2.5 },
    { id: 'wardrobe-cloud', landmark: 'wardrobe-bridge', start: 85, len: 55, x: 5, w: 18, offsetS: 5 },
  ].map(({ landmark, start, len, ...zone }) => ({ ...zone, s0: find(landmark).s0 + start, s1: find(landmark).s0 + start + len }));
  const plan = walkBoudoir(BOUDOIR_PIECES);
  const furniture = ['shelf-climb', 'wardrobe-descent'].map(id => {
    const i = BOUDOIR_PIECES.findIndex(p => p.id === id), p = BOUDOIR_PIECES[i], start = plan.starts[i];
    return { id, x: start.x - Math.sign(p.turn) * p.r * Math.sin(start.heading), z: start.z + Math.sign(p.turn) * p.r * Math.cos(start.heading), radiusM: p.r - 27, baseY: -24, topY: 136 };
  });
  if (track.courseShape?.pieces) for (const item of furniture) {
    const piece = track.courseShape.pieces.find(p => p.id === item.id), range = find(item.id);
    const f = track.frameAt((range.s0 + range.s1) / 2), flat = Math.hypot(f.t[0], f.t[2]);
    const radius = Math.sign(piece.turn) * piece.r;
    item.x = f.p[0] - radius * f.t[2] / flat;
    item.z = f.p[2] + radius * f.t[0] / flat;
    item.radiusM = piece.r - 27;
  }
  const ramp = find('mirror-ramp'), gap = find('mirror-gap'), landing = find('mirror-landing');
  const mirrorJump = { rampS0: ramp.s0, rampS1: gap.s0, gapS0: gap.s0, gapS1: landing.s0, landingS0: landing.s0, landingS1: landing.s1, widthM: 40, riseM: 14, gapM: 24 };
  const stoneFields = [{ id: 'boudoir-compacts', s0: find('compacts').s0, s1: track.fields.find(f => f.id === 'boudoir-compacts')?.toS ?? find('compacts').s1, lanes: [-9, 0, 9], widthM: 40, platformWidthM: 32, platformLengthM: 26, gapM: 3 }];
  const bounds = { minX: Infinity, maxX: -Infinity, minZ: Infinity, maxZ: -Infinity, minY: Infinity, maxY: -Infinity };
  for (let s = 0; s < track.lengthM; s += 10) {
    const f = track.frameAt(s), half = f.w / 2;
    bounds.minX = Math.min(bounds.minX, f.p[0] - half); bounds.maxX = Math.max(bounds.maxX, f.p[0] + half);
    bounds.minZ = Math.min(bounds.minZ, f.p[2] - half); bounds.maxZ = Math.max(bounds.maxZ, f.p[2] + half);
    bounds.minY = Math.min(bounds.minY, f.p[1]); bounds.maxY = Math.max(bounds.maxY, f.p[1]);
  }
  return { landmarks, halls, perfume, furniture, mirrorJump, stoneFields, bounds, widthM: 36 };
}
