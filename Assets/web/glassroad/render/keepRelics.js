/* Colossal broken fittings suspended around the prison. Shared fortress instance batches. */
export function keepRelics(track, layout, mounts = []) {
  const boxes = [], road = [];
  for (let s = 0; s < track.lengthM; s += 3) road.push(track.frameAt(s));
  function assemble(f, x, y, z, tilt, scale, kind) {
    const parts = [], yaw = Math.atan2(-f.t[0], -f.t[2]);
    const put = (material, name, p, size, angle = 0) => {
      const lx = x + scale * (p[0] * Math.cos(tilt) - p[1] * Math.sin(tilt));
      const ly = y + scale * (p[0] * Math.sin(tilt) + p[1] * Math.cos(tilt)), lz = z + p[2] * scale;
      parts.push({ material, name, p: [f.p[0] + f.b[0] * lx - f.t[0] * lz, f.p[1] + ly, f.p[2] + f.b[2] * lx - f.t[2] * lz],
        size: size.map(v => v * scale), yaw, tilt: tilt + angle });
    };
    function link(cx, cy, cz, radius, name) {
      for (let k = 0; k < 12; k++) {
        const a = (k + .5) * Math.PI / 6;
        put('iron', name, [cx + Math.cos(a) * radius, cy + Math.sin(a) * radius, cz], [radius * .55, .8, 1], a + Math.PI / 2);
      }
    }
    if (kind === 'lock') {
      // An inverted lock leans into the wall, its open shackle forming the drive-through arch.
      put('iron', 'colossal-leaning-lock-body', [0, 16, 0], [24, 14, 5]);
      put('cap', 'colossal-lock-inset', [0, 16, 2.6], [21, 11, .35]);
      put('dark', 'colossal-lock-keyhole', [0, 17.5, 2.85], [3, 3, .2], Math.PI / 4);
      put('dark', 'colossal-lock-keyway', [0, 14, 2.85], [1.7, 5, .2]);
      for (const dx of [-9, 9]) for (const dy of [12, 20]) put('iron', 'colossal-lock-rivet', [dx, dy, 2.9], [1, 1, .4], Math.PI / 4);
      for (const dx of [-11, 11]) put('cap', 'colossal-lock-shackle', [dx, 3, 0], [2.4, 14, 3]);
      for (let k = 0; k < 10; k++) {
        const a = Math.PI + (k + .5) * Math.PI / 10;
        put('cap', 'colossal-lock-shackle', [Math.cos(a) * 11, -4 + Math.sin(a) * 7, 0], [3.7, 2.4, 3], Math.atan2(7 * Math.cos(a), -11 * Math.sin(a)));
      }
    } else {
      link(0, 0, 0, 4, 'floating-broken-chain'); link(6.2, 0, 0, 4, 'floating-broken-chain');
      for (let i = 0; i < 5; i++) put(i % 2 ? 'cap' : 'stone', 'floating-masonry-fragment', [i * 3 - 6, -8 - i % 3 * 3, i % 2 * 4], [2 + i % 3, 2, 3], i * .37);
    }
    return parts;
  }
  // Conservative oriented box versus road envelopes includes every stacked level and camera headroom.
  function clear(part) {
    const c = Math.cos(part.yaw), s = Math.sin(part.yaw), ct = Math.cos(part.tilt), st = Math.sin(part.tilt);
    const local = p => { const dx = p[0] - part.p[0], dy = p[1] - part.p[1], dz = p[2] - part.p[2], x = dx * c - dz * s;
      return [x * ct + dy * st, -x * st + dy * ct, dx * s + dz * c]; };
    const radius = Math.hypot(...part.size) / 2;
    if (mounts.some(m => Math.hypot(...m.p.map((v,i) => v - part.p[i])) < Math.hypot(m.w, m.h) / 2 + radius + 3)) return false;
    for (const f of road) {
      if (Math.hypot(f.p[0] - part.p[0], f.p[2] - part.p[2]) > radius + f.w / 2 + 8) continue;
      for (let x = -f.w / 2 - 2; x <= f.w / 2 + 2; x += 2) for (const h of [-2, 0, 3, 6.5, 9]) {
        const q = local(f.p.map((v,i) => v + f.b[i] * x + f.n[i] * h));
        if (q.every((v,i) => Math.abs(v) < part.size[i] / 2 + 2)) return false;
      }
    }
    return true;
  }
  for (const [name, share] of [['forecourt', .35], ['outer-exit', .5], ['crown-walk', .45], ['west-descent', .5], ['moat-return', .55]]) {
    const a = layout.landmarks.find(a => a.id === name); if (!a) continue;
    const f = track.frameAt(a.s0 + (a.s1 - a.s0) * share);
    const scale = Math.max(1.8, (f.w + 10) / 19), tilt = .13;
    const lock = assemble(f, 3, 16 + scale * 11, 0, tilt, scale, 'lock');
    if (lock.every(clear)) boxes.push(...lock);
  }
  for (let i = 0; i < 28; i++) {
    const f = track.frameAt((i + .4) * track.lengthM / 28), side = i % 2 ? 1 : -1;
    const parts = assemble(f, side * (f.w / 2 + 25 + i % 3 * 9), 17 + i % 4 * 9, 0, side * .25, 1.3 + i % 3 * .3, 'chain');
    if (parts.every(clear)) boxes.push(...parts);
  }
  return boxes;
}
