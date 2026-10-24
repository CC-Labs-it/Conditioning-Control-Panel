/* Original prison architecture. Pure world-space pieces also provide physical media mounts. */
import { keepDetails } from './keepDetails.js';
export function prisonPlan(track, layout, { phone = false } = {}) {
  const boxes = [], drums = [], mounts = [], buildings = [], missingBricks = [];
  const box = (material, name, p, size, yaw = 0, tilt = 0) => boxes.push({ material, name, p, size, yaw, tilt });
  const ground = Math.min(-25, ...(layout.towers || []).map((t) => t.baseY));
  const road = [];
  for (let s = 0; s < track.lengthM; s += 12) road.push({ s, ...track.frameAt(s) });
  const nearest = (p) => road.reduce((a, f) => Math.hypot(f.p[0] - p[0], f.p[2] - p[2]) < Math.hypot(a.p[0] - p[0], a.p[2] - p[2]) ? f : a);
  const transform = (a, x, y, z) => [a.x + x * Math.cos(a.yaw || 0) + z * Math.sin(a.yaw || 0), y,
    a.z - x * Math.sin(a.yaw || 0) + z * Math.cos(a.yaw || 0)];
  const local = (a, material, name, x, y, z, size, yaw = 0, tilt = 0) => box(material, name, transform(a, x, y, z), size, (a.yaw || 0) + yaw, tilt);
  function facade(a, side, length, face, levels) {
    const n = Math.max(2, Math.floor(length / (phone ? 16 : 11))), pitch = length / n;
    const angle = side * Math.PI / 2, nx = Math.sin(angle), nz = Math.cos(angle), tx = Math.cos(angle), tz = -Math.sin(angle);
    const at = (u, y, depth = 0) => [tx * u + nx * (face + depth), y, tz * u + nz * (face + depth)];
    for (let floor = 0; floor < levels; floor++) {
      const y = a.baseY + 4 + floor * 10;
      for (let j = 0; j < n; j++) {
        const u = (j + 0.5) * pitch - length / 2, broken = floor >= levels - 2 && (j + side * 3) % 7 >= (floor === levels - 1 ? 3 : 5);
        if (broken) {
          const p = at(u, y + 1.5, 0.4);
          local(a, 'stone', 'broken-upper-wall', ...p, [pitch * 0.8, 3, 3], angle, (j % 2 ? 1 : -1) * 0.12);
          continue;
        }
        if ((j + floor * 5 + side * 3) % 9 === 2 && j > 0 && j < n - 1) {
          // Four remaining pieces surround a genuinely absent brick, exposing the recessed core.
          local(a, 'stone', 'chipped-masonry', ...at(u, y - .05), [pitch + .05, .9, 3.2], angle);
          local(a, 'stone', 'chipped-masonry', ...at(u, y + 5.95), [pitch + .05, 7.1, 3.2], angle);
          local(a, 'stone', 'chipped-masonry', ...at(u - pitch / 2 + .4, y + 1.4), [.8, 2, 3.2], angle);
          local(a, 'stone', 'chipped-masonry', ...at(u + 1.6, y + 1.4), [pitch - 3.2, 2, 3.2], angle);
          missingBricks.push(transform(a, ...at(u - pitch / 2 + 2, y + 1.4)));
        } else local(a, 'stone', 'cell-pier', ...at(u, y + 4.5), [pitch + .05, 10, 3.2], angle);
        if (j % 5 === 1 && floor % 2 === 0) for (let k = 0; k < 4; k++) {
          const zig = k % 2 ? .32 : -.15;
          local(a, 'dark', 'masonry-crack', ...at(u - pitch * .32 + zig, y + 5 + k * 1.1, 1.66), [.16, 1.4, .1], angle, zig);
        }
        local(a, 'dark', 'cell-recess', ...at(u + .7, y + 4.6, 1.7), [3.1, 4.8, .2], angle);
        for (const k of [-1, 0, 1]) local(a, 'iron', 'cell-bars', ...at(u + .7 + k, y + 4.6, 1.88), [.23, 4.9, .22], angle);
        local(a, 'iron', 'cell-crossbar', ...at(u + .7, y + 3.3, 1.9), [3.2, .24, .22], angle);
        local(a, 'cap', 'cell-sill', ...at(u + .7, y + 1.9, 1.8), [3.9, .65, 1], angle);
      }
    }
  }
  function building(a, { media = false } = {}) {
    if (!(a.w > 0 && a.d > 0 && a.topY > a.baseY)) return;
    buildings.push(a);
    const h = a.topY - a.baseY, levels = Math.max(1, Math.floor((h - 5) / 10));
    local(a, 'dark', 'prison-cellblock-core', 0, a.baseY + (h - 25) / 2, 0, [a.w - 5, h - 25, a.d - 5]);
    local(a, 'stone', 'prison-foundation', 0, a.baseY + 2, 0, [a.w + 2, 4, a.d + 2]);
    facade(a, 0, a.w, a.d / 2, levels); facade(a, 2, a.w, a.d / 2, levels);
    facade(a, 1, a.d, a.w / 2, levels); facade(a, 3, a.d, a.w / 2, levels);
    // Thick corner buttresses and uneven roof fragments give the jail a broken silhouette.
    for (const x of [-1, 1]) for (const z of [-1, 1]) {
      local(a, 'stone', 'prison-buttress', x * (a.w / 2 - 0.5), a.baseY + h * 0.45, z * (a.d / 2 - 0.5), [5.5, h * 0.9, 5.5]);
      local(a, 'cap', 'broken-parapet', x * (a.w / 2 - 1), a.topY + (x === z ? 2 : -1), z * (a.d / 2 - 1), [6, x === z ? 6 : 3, 6]);
    }
    for (let j = 0; j < 7; j++) {
      if (j === 1 || j === 2 || j === 5) continue;
      local(a, 'roof', 'broken-roof-wing', (j - 3) * a.w / 7, a.topY - 1 - j % 3, 0,
        [a.w / 7 + 0.5, 1.5, a.d * (j % 2 ? 0.7 : 0.45)], 0, (j % 2 ? 1 : -1) * 0.12);
    }
    if (media && mounts.filter((m) => m.frame === 'tunnel').length < 8) {
      const sides = [0, 1, 2, 3].map((side) => {
        const angle = side * Math.PI / 2, face = side % 2 ? a.w / 2 : a.d / 2;
        const p = transform(a, Math.sin(angle) * (face + 2.2), 0, Math.cos(angle) * (face + 2.2));
        const f = nearest(p); return { side, p, f, distance: Math.hypot(p[0] - f.p[0], p[2] - f.p[2]) };
      }).sort((a, b) => a.distance - b.distance);
      const face = sides[0], yaw = (a.yaw || 0) + face.side * Math.PI / 2;
      const b = [Math.cos(yaw), 0, -Math.sin(yaw)], n = [0, 1, 0], t = [Math.sin(yaw), 0, Math.cos(yaw)];
      const p = [face.p[0], Math.min(a.topY - 9, face.f.p[1] + 12), face.p[2]];
      box('dark', 'prison-media-tablet', p.map((v, i) => v - t[i] * .3), [18, 10, .5], yaw);
      mounts.push({ id: a.id + '-facade', s: face.f.s, p, b, n, t, w: 17, h: 9, frame: 'tunnel' });
    }
  }
  for (const a of layout.architecture?.cellblocks || layout.castleBlocks || []) {
    // A broad prison is an asymmetric E of real wings and open exercise courts, not one solid box.
    if (a.w > 160 && a.d > 100) {
      const parts = [[0, -a.d * 0.35, a.w, a.d * 0.28, a.topY],
        [-a.w * 0.41, a.d * 0.12, a.w * 0.18, a.d * 0.7, a.topY - 15],
        [a.w * 0.39, a.d * 0.11, a.w * 0.22, a.d * 0.7, a.topY - 29],
        [-a.w * 0.04, a.d * 0.04, a.w * 0.15, a.d * 0.55, a.topY - 8]];
      parts.forEach(([x, z, w, d, topY], i) => { const p = transform(a, x, 0, z); building({ ...a, id: a.id + '-wing-' + i, x: p[0], z: p[2], w, d, topY }, { media: true }); });
    } else building(a, { media: true });
  }
  for (const hall of layout.architecture?.halls || []) {
    const middle = (hall.s0 + hall.s1) / 2, f = track.frameAt(middle), yaw = Math.atan2(-f.t[0], -f.t[2]);
    const length = hall.s1 - hall.s0, width = f.w, wing = hall.wingM || 17, height = hall.heightM || 27;
    for (const side of [-1, 1]) {
      const x = side * (width / 2 + wing / 2 + 3.5), p = f.p.map((v, i) => v + f.b[i] * x);
      building({ id: hall.id + (side < 0 ? '-west-cells' : '-east-cells'), x: p[0], z: p[2], w: wing, d: length,
        baseY: Math.min(ground, f.p[1] - 4), topY: f.p[1] + height + (side < 0 ? 6 : 0), yaw });
      for (let j = 0; j < 4; j++) {
        const s = hall.s0 + length * (j + 0.5) / 4, at = track.frameAt(s), off = side * (at.w / 2 + 1.45);
        const p = at.p.map((v, i) => v + at.b[i] * off + at.n[i] * 10), normal = at.b.map((v) => -side * v);
        box('stone', 'gallery-media-tablet', p.map((v, i) => v - normal[i] * .6), [14.4, 8.4, 1], Math.atan2(normal[0], normal[2]));
        mounts.push({ id: hall.id + '-wall-' + side + '-' + j, s, p,
          b: at.t.map((v) => -side * v), n: [...at.n], t: at.b.map((v) => -side * v), w: 13, h: 7, frame: 'billboard' });
      }
    }
    // The racer passes beneath a massive entrance bridge and its barred upper cells.
    for (const s of [hall.s0 + 5, hall.s1 - 5]) {
      const at = track.frameAt(s), a = { x: at.p[0], z: at.p[2], yaw };
      local(a, 'stone', 'prison-gatehouse-bridge', 0, at.p[1] + 21, 0, [width + wing * 2 + 7, 8, 10]);
      for (let j = -2; j <= 2; j++) {
        local(a, 'dark', 'gatehouse-window', j * 5, at.p[1] + 21, 5.1, [3.3, 4, 0.3]);
        for (const k of [-1, 0, 1]) local(a, 'iron', 'gatehouse-bars', j * 5 + k, at.p[1] + 21, 5.4, [0.24, 4.3, 0.3]);
      }
      const front = s < middle ? -1 : 1, normal = at.t.map((v) => -v);
      const p = at.p.map((v, i) => v + normal[i] * 5.4 + at.n[i] * 21);
      mounts.push({ id: hall.id + '-gate-' + front, s, p, b: [...at.b], n: [...at.n], t: normal, w: 15, h: 7, frame: 'halo' });
    }
    for (let j = 0; j < Math.floor(length / 20); j++) {
      if (j % 4 === 2) continue;
      const at = track.frameAt(hall.s0 + (j + 0.5) * 20), a = { x: at.p[0], z: at.p[2], yaw };
      local(a, 'roof', 'open-roof-gallery', 0, at.p[1] + height - 2, 0, [width + 8, 2, j % 3 ? 6 : 16]);
    }
  }
  const towers = [...(layout.towers || [])];
  for (const a of layout.architecture?.cellblocks || []) for (const side of [-1, 1]) {
    const p = transform(a, side * (a.w / 2 - 13), 0, -a.d / 2 + 13);
    towers.push({ x: p[0], z: p[2], radiusM: 12, baseY: a.baseY, topY: a.topY + (side < 0 ? 6 : 15), guard: true });
  }
  for (const t of towers) {
    drums.push({ ...t, sides: 8 });
    const face = t.radiusM * Math.cos(Math.PI / 8), levels = Math.floor((t.topY - t.baseY - 9) / 11);
    for (let side = 0; side < 8; side++) {
      const yaw = (side + .5) * Math.PI / 4, nx = Math.sin(yaw), nz = Math.cos(yaw);
      const a = { x: t.x + nx * face, z: t.z + nz * face, yaw };
      for (let floor = 0; floor < levels; floor++) for (const j of t.guard ? [0] : [-1, 1]) {
        const y = t.baseY + 10 + floor * 11;
        local(a, 'dark', 'tower-cell-window', j * 8, y, 0.25, [6.3, 5.5, 0.35]);
        for (const k of [-2, 0, 2]) local(a, 'iron', 'tower-bars', j * 8 + k, y, 0.5, [0.28, 5.9, 0.35]);
        local(a, 'cap', 'tower-sill', j * 8, y - 3.1, 0.6, [7, 0.6, 1.4]);
      }
      box('stone', 'tower-buttress', [t.x + nx * (face - 1), (t.baseY + t.topY) / 2, t.z + nz * (face - 1)], [4, t.topY - t.baseY, 5], yaw);
      box('stone', 'broken-tower-crown', [t.x + nx * (face - 2), t.topY + (side % 3), t.z + nz * (face - 2)], [face * (t.guard ? .36 : .8), 4 + side % 3 * 2, 4], yaw);
    }
    const spiral = layout.landmarks.find((a) => a.id === 'tower-spiral');
    if (spiral && !t.guard) for (const [i, share] of [.12, .26, .4, .54, .68, .82].entries()) {
      const s = spiral.s0 + (spiral.s1 - spiral.s0) * share, f = track.frameAt(s);
      const angle = Math.atan2(f.p[0] - t.x, f.p[2] - t.z), yaw = (Math.round(angle / (Math.PI / 4) - .5) + .5) * Math.PI / 4;
      const normal = [Math.sin(yaw), 0, Math.cos(yaw)], p = [t.x + normal[0] * (face + .7), f.p[1] + 9, t.z + normal[2] * (face + .7)];
      box('dark', 'tower-media-tablet', p.map((v, k) => v - normal[k] * .3), [15, 7, .5], yaw);
      mounts.push({ id: 'tower-tablet-' + i, s, p, b: [Math.cos(yaw), 0, -Math.sin(yaw)], n: [0, 1, 0], t: normal, w: 14, h: 6, frame: 'billboard' });
    }
  }
  // Collapsed wings are disconnected masonry fragments, with large open roof holes.
  for (const a of layout.architecture?.ruins || []) {
    for (let j = 0; j < 13; j++) {
      const x = (j / 12 - .5) * a.w, height = 5 + (j * 7 % 17);
      local(a, 'stone', 'collapsed-wall', x, a.baseY + height / 2, -a.d / 2, [8, height, 6], 0, (j % 3 - 1) * .15);
      local(a, 'cap', 'prison-rubble', x + 2, a.baseY + 2, -a.d / 2 + 9 + j % 3 * 5, [6, 4, 5], j * .73, .3);
    }
  }
  const groupIndex = new Map();
  const details = keepDetails(track, layout, mounts); boxes.push(...details.boxes); mounts.push(...details.mounts);
  const climb = details.interiors.find((a) => a.id === 'interior-spiral');
  for (const m of mounts) if (m.id.startsWith('tower-tablet-') && climb && m.s > climb.s0 && m.s < climb.s1) {
    const f = track.frameAt(m.s), tower = layout.towers[0], out = Math.sign(f.b[0] * (f.p[0] - tower.x) + f.b[2] * (f.p[2] - tower.z));
    m.p = [f.p[0] - f.b[0] * out * (f.w / 2 + 1.6), f.p[1] + 7, f.p[2] - f.b[2] * out * (f.w / 2 + 1.6)];
    const length = Math.hypot(f.b[0], f.b[2]);
    m.t = [f.b[0] * out / length, 0, f.b[2] * out / length]; m.b = [m.t[2], 0, -m.t[0]]; m.n = [0, 1, 0]; m.standoff = .15;
    box('stone', 'interior-media-tablet', m.p.map((v, i) => v - m.t[i] * .7), [15, 7, 1.1], Math.atan2(m.t[0], m.t[2]));
  }
  for (const m of mounts) { const i = groupIndex.get(m.frame) || 0; m.phone = i % 4 < 2; groupIndex.set(m.frame, i + 1); }
  return { boxes, drums, mounts, buildings, interiors: details.interiors, missingBricks };
}
