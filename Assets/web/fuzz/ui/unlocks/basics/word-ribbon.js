const FADE_MS = 950;
const smoothstep = value => {
  const t = Math.max(0, Math.min(1, value));
  return t * t * (3 - 2 * t);
};

// A word advances in distance along the particle emission path. All words
// share that path, including its corners, while their spacing staggers turns.
export function createWordRibbon(count) {
  const links = Array.from({length: count}, () => ({x: 0, y: 0, width: 44, depth: 0, opacity: 0}));
  const path = [];
  let lastAt = 0, simulatedAt = 0, stroke = 0, detached = false;
  let progress = 0, speed = 0;
  let inputX, inputY;

  function layout(widths) {
    let depth = 6;
    for (let i = 0; i < links.length; i++) {
      if (i) depth += (widths[i-1] + widths[i]) * .5 + 16;
      Object.assign(links[i], {width: widths[i], depth});
    }
  }
  layout(links.map(link => link.width));

  function clear() {
    path.length = 0;
    progress = speed = 0;
    detached = false;
    for (const link of links) link.opacity = 0;
  }

  // A critically damped playhead keeps moving during a pause. Its only
  // destination is the newest particle on the trail, so it cannot overshoot.
  function coast(now) {
    if (!path.length || now <= simulatedAt) return;
    const dt = (now - simulatedAt) / 1000;
    const target = path[path.length-1].distance;
    const gap = progress - target;
    const rate = 6.0;
    const impulse = speed + rate * gap;
    const decay = Math.exp(-rate * dt);
    progress = target + (gap + impulse * dt) * decay;
    speed = (speed - rate * impulse * dt) * decay;
    simulatedAt = now;
  }

  function move(x, y, now) {
    const last = path[path.length-1];
    if (last && !detached && inputX === x && inputY === y) return;
    inputX = x;
    inputY = y;
    if (!last || detached || now - lastAt >= FADE_MS) {
      clear();
      stroke++;
      path.push({x, y, at: now, distance: 0});
      lastAt = simulatedAt = now;
      return;
    }
    const step = Math.hypot(x - last.x, y - last.y);
    if (step < 4) return;
    coast(now);
    const distance = last.distance + step;
    path.push({x, y, at: now, distance});
    // Bound storage even while the word overlay is switched off.
    if (path.length > 768) path.splice(0, 128);
    lastAt = now;
  }

  function sample(distance) {
    if (path.length === 1 || distance <= path[0].distance) return path[0];
    let lo = 0, hi = path.length - 1;
    while (hi - lo > 1) {
      const mid = (lo + hi) >>> 1;
      if (path[mid].distance <= distance) lo = mid;
      else hi = mid;
    }
    const a = path[lo], b = path[hi];
    const f = Math.min(1, (distance - a.distance) / (b.distance - a.distance));
    return {x: a.x + (b.x-a.x)*f, y: a.y + (b.y-a.y)*f, at: a.at + (b.at-a.at)*f};
  }

  function advance(now) {
    if (!path.length) return;
    coast(now);
    const fade = 1 - smoothstep((now-lastAt) / FADE_MS);
    const oldestNeeded = progress - links[links.length-1].depth - 14;
    let discard = 0;
    while (discard + 2 < path.length && path[discard+1].distance < oldestNeeded) discard++;
    if (discard) path.splice(0, discard);
    for (let i = 0; i < links.length; i++) {
      const link = links[i];
      const distance = progress - link.depth;
      const point = sample(distance);
      // Average a few pixels on either side to round folds without lateral swing.
      const before = sample(distance - 10), after = sample(distance + 10);
      link.x = (before.x + 2*point.x + after.x) / 4;
      link.y = (before.y + 2*point.y + after.y) / 4;
      const reveal = smoothstep((distance + 4) / 22);
      const recent = 1 - smoothstep((lastAt - point.at - 560) / 530);
      let opacity = .88 * fade * reveal * recent * (1 - .055*i);
      // A folded or very short trail has room for fewer readable words.
      for (let j = 0; j < i && opacity > .006; j++) {
        const other = links[j];
        const spacing = Math.max(Math.abs(link.x-other.x)/((link.width+other.width)*.5+4),
                                 Math.abs(link.y-other.y)/18);
        opacity *= 1 - smoothstep(other.opacity/.25) * (1-smoothstep((spacing-.32)/.68));
      }
      link.opacity = opacity;
    }
  }
  return {links, layout, move, advance, clear, leave() { detached = true; },
    alive: now => path.length > 0 && now-lastAt < FADE_MS, get stroke() { return stroke; }};
}
