/* Original prison fittings and enclosed circulation, sharing the fortress instance batches. */
export function keepDetails(track, layout, existingMounts = []) {
  const boxes = [], mounts = [], interiors = [];
  const find = (id) => layout.landmarks.find((a) => a.id === id);
  const put = (f, material, name, x, y, z, size, tilt = 0, turn = 0) => {
    const p = [f.p[0] + f.b[0] * x - f.t[0] * z, f.p[1] + y, f.p[2] + f.b[2] * x - f.t[2] * z];
    boxes.push({ material, name, p, size, yaw: Math.atan2(-f.t[0], -f.t[2]) + turn, tilt });
  };
  function ring(f, x, y, z, radius, turn = 0) {
    for (let k = 0; k < 8; k++) {
      const a = (k + .5) * Math.PI / 4;
      put(f, 'iron', 'forged-chain-link', x + Math.cos(a) * radius * Math.cos(turn), y + Math.sin(a) * radius,
        z - Math.cos(a) * radius * Math.sin(turn), [radius * .82, .18, .2], a + Math.PI / 2, turn);
    }
  }
  function lock(f, side, z = 0) {
    const x = side * (f.w / 2 + 2.1);
    put(f, 'cap', 'iron-lock-body', x, 4.3, z, [.8, 2.4, 2.1]);
    ring(f, x, 6, z, .85, Math.PI / 2);
    put(f, 'dark', 'lock-keyway', x - side * .43, 4.4, z, [.06, .9, .25]);
    for (let j = 0; j < 5; j++) ring(f, x, 7.6 + j * .8, z, .48, j % 2 ? 0 : Math.PI / 2);
    for (const dz of [-2.3, 2.3]) { ring(f, x, 2.2, z + dz, .8, Math.PI / 2); ring(f, x, 3.6, z + dz, .4, Math.PI / 2); }
  }
  function vault(a, spiral = false) {
    interiors.push({ id: a.id, s0: a.s0, s1: a.s1, clearanceM: 11 });
    const count = Math.ceil((a.s1 - a.s0) / (spiral ? 3 : 5)), step = (a.s1 - a.s0) / count;
    for (let j = 0; j < count; j++) {
      const f = track.frameAt(a.s0 + (j + .5) * step), r = f.w / 2 + 3.6;
      for (const side of [-1, 1]) {
        // Both walls close the gallery, including the recesses between the tower's eight facets.
        const tower = layout.towers[0], outward = Math.sign(f.b[0] * (f.p[0] - tower.x) + f.b[2] * (f.p[2] - tower.z));
        put(f, 'stone', 'interior-cell-wall', side * (f.w / 2 + 3.5), 5.6, 0, [3, 12.4, step * (spiral ? 1.3 : 1) + .4]);
        if (spiral && side === outward) {
          put(f, 'cap', 'winding-stair-ledge', side * (f.w / 2 + 1.8), .3, 0, [2, 1.2, step - .12]);
          if (j % 4 === 0) put(f, 'iron', 'stair-wall-brace', side * (f.w / 2 + 1.8), 5, 0, [.35, 9, .4]);
        }
      }
      for (let k = 0; k < 12; k++) {
        const angle = (k + .5) / 12 * Math.PI, x = r * Math.cos(angle), y = 11.7 + r * .34 * Math.sin(angle);
        const dx = -r * Math.sin(angle), dy = r * .34 * Math.cos(angle);
        put(f, j % 5 === 0 ? 'cap' : 'roof', 'barrel-vault', x, y, 0, [Math.hypot(dx, dy) * Math.PI / 12 + .25, 1.4, step * (spiral ? 1.3 : 1) + .4], Math.atan2(dy, dx));
      }
      if (j % (spiral ? 8 : 4) === 2) {
        for (const side of [-1, 1]) {
          if (spiral && side !== Math.sign(f.b[0] * (f.p[0] - layout.towers[0].x) + f.b[2] * (f.p[2] - layout.towers[0].z))) continue;
          put(f, 'dark', 'barred-cell-alcove', side * (f.w / 2 + 1.92), 5, 0, [.15, 7, 5]);
          for (const z of [-2, -1, 0, 1, 2]) put(f, 'iron', 'cell-door-bars', side * (f.w / 2 + 1.72), 5, z, [.2, 7.2, .2]);
          lock(f, side, 4.4);
          put(f, 'glow', 'prison-lamp', side * (f.w / 2 + 1.6), 9.5, -4, [.5, 1.3, .6]);
          put(f, 'iron', 'lamp-bracket', side * (f.w / 2 + 2.1), 8.6, -4, [1.4, .25, 1]);
        }
      }
    }
  }
  const gallery = find('cell-gallery'), spiral = find('tower-spiral');
  if (gallery) vault({ ...gallery, s0: gallery.s0 + 10, s1: gallery.s1 - 10 });
  if (spiral) vault({ id: 'interior-spiral', s0: spiral.s0 + (spiral.s1 - spiral.s0) * .32, s1: spiral.s0 + (spiral.s1 - spiral.s0) * .62 }, true);
  // Paired tablet piers face the road near the bridge and the long platform crossing.
  for (const [id, share] of [['bridge-approach', .4], ['drawbridge-landing', .55], ['stones-approach', .55], ['stones-exit', .55],
    ['stones', .15], ['stones', .37], ['stones', .63], ['stones', .85]]) {
    const a = find(id); if (!a) continue;
    const s = a.s0 + (a.s1 - a.s0) * share, f = track.frameAt(s);
    for (const side of [-1, 1]) {
      const x = side * (f.w / 2 + 4), p = [f.p[0] + f.b[0] * x, f.p[1] + 7, f.p[2] + f.b[2] * x];
      const ground = Math.min(-25, ...layout.towers.map((t) => t.baseY));
      put(f, 'stone', 'crossing-tablet-pier', x, (ground + 13 - f.p[1]) / 2, 0, [3.5, f.p[1] + 13 - ground, 13]);
      const normal = f.b.map((v) => -side * v);
      mounts.push({ id: 'crossing-' + id + '-' + share + '-' + side, s, p: p.map((v, i) => v + normal[i] * 1.9), b: f.t.map((v) => -side * v), n: [0, 1, 0], t: normal,
        w: 11, h: 7, frame: 'billboard', standoff: .15 });
      if (id !== 'stones') lock(f, side, 12);
    }
  }
  // Locks, wall chains and cage frames repeat at architectural thresholds, not on the racing surface.
  for (const a of layout.architecture.halls) for (const s of [a.s0 + 12, a.s1 - 12]) {
    const f = track.frameAt(s);
    for (const side of [-1, 1]) {
      lock(f, side);
      const x = side * (f.w / 2 + 4.5);
      for (const y of [4, 9]) put(f, 'iron', 'hanging-cage-frame', x, y, 5, [3, .35, 3]);
      for (const dx of [-1.4, 0, 1.4]) for (const z of [3.6, 6.4]) put(f, 'iron', 'hanging-cage-bars', x + dx, 6.5, z, [.16, 5, .16]);
    }
  }
  const weatherStart = boxes.length;
  // Sparse climbing plants and dropped stone make the lower walls visibly weathered.
  for (const id of ['outer-gate', 'cell-gallery', 'chain-hall', 'high-gate', 'tower-spiral']) {
    const a = find(id); if (!a) continue;
    for (const share of [.1, .31, .57, .86]) {
      const f = track.frameAt(a.s0 + (a.s1 - a.s0) * share);
      for (const side of [-1, 1]) {
        const x = side * (f.w / 2 + 1.75), height = 8 + Math.round(share * 8);
        for (let j = 0; j < height; j++) {
          const z = Math.sin(j * .71 + side) * .7;
          put(f, 'dark', 'climbing-vine-stem', x, j + .5, z, [.13, 1.3, .16], Math.sin(j) * .1, Math.PI / 2);
          for (const leaf of [-1, 1]) {
            put(f, j % 7 === 2 ? 'iron' : 'roof', 'climbing-vine-leaf', x - side * .16, j + .6, z + leaf * .7,
              [.85, 1.45, .13], leaf * .66, Math.PI / 2);
          }
        }
        // Small chips sit on the stone shoulder, never on the kart ribbon or a crossing gap.
        for (let j = 0; j < 3; j++) put(f, j % 2 ? 'cap' : 'stone', 'fallen-masonry-chip', side * (f.w / 2 + 1.65), .25,
          3 + j * 1.8, [.7, .5 + j * .15, .8], .17 * side, j * .73);
        if (id !== 'tower-spiral') for (let j = 0; j < 10; j++) {
          const u = j / 9;
          ring(f, x, 8 - Math.sin(u * Math.PI) * 2.6, 5 + u * 7, .38, j % 2 ? 0 : Math.PI / 2);
        }
      }
    }
  }
  // These additions may approach a display border, but must never cross its visible rectangle.
  const weather = boxes.splice(weatherStart), screens = [...existingMounts, ...mounts];
  for (const b of weather) {
    const radius = Math.hypot(...b.size) / 2;
    if (screens.some((m) => Math.hypot(...b.p.map((v, i) => v - m.p[i])) < Math.hypot(m.w, m.h) / 2 + radius + 2)) continue;
    boxes.push(b);
  }
  return { boxes, mounts, interiors };
}
