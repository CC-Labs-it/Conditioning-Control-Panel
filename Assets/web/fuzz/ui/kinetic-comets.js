import { passingBeat, pearl, sparkle } from './kinetic-particles.js';

const TAU = 2 * Math.PI;
const colors = [
  { tail: '#983984', body: '#df3d81', light: '#ffae80', core: '#fff1bb' },
  { tail: '#554493', body: '#8b55ca', light: '#f09ad4', core: '#fff1e4' },
];

// One long comet tail is a tapered brushstroke. The other colours and
// its beads follow the very same curve, so the train turns as a single body.
function brush(g, point, length, width, range, color) {
  const sections = 25;
  g.beginPath();
  for (const edge of [1, -1]) {
    for (let i = 0; i <= sections; i++) {
      const t = edge === 1 ? i / sections : 1 - i / sections;
      const p = point(-length * (1 - t));
      const taper = Math.pow(t, .84) * Math.pow(Math.max(0, 1 - t), .43) * 2;
      const offset = width * taper * (edge === 1 ? range[0] : range[1]);
      const x = p.x - p.sin * offset, y = p.y + p.cos * offset;
      if (edge === 1 && i === 0) g.moveTo(x, y);
      else g.lineTo(x, y);
    }
  }
  g.closePath();
  g.fillStyle = color;
  g.fill();
}

export function drawComets(g, scene) {
  const { w, h, axis, time, energy } = scene;
  const scale = Math.min(1.13, h / 103, Math.max(.74, scene.detail));
  const spacing = Math.min(155 * scale, Math.max(78 * scale, w / 3.4));
  const drift = time * .27;
  const first = Math.floor(-drift) - 1;
  const last = Math.ceil(w / spacing - drift) + 1;
  for (let i = first; i <= last; i++) {
    const travelX = (i + drift + .27) * spacing;
    const beat = passingBeat(scene, travelX);
    const headX = travelX + spacing * (.29 * beat.turn + .15 * beat.swell);
    const parity = ((i % 2) + 2) % 2;
    const ink = colors[parity];
    const phase = i * 1.86 - time * 1.72 + beat.turn * 1.08;
    const amplitude = Math.min(h * .28, scale * (15 + 12 * beat.swell + 4 * energy));
    function point(offset) {
      const theta = phase + offset * TAU;
      const x = headX + offset * spacing;
      const y = axis(x) + amplitude * Math.sin(theta);
      const slope = axis(x + .5) - axis(x - .5) + amplitude * TAU * Math.cos(theta) / spacing;
      const cos = 1 / Math.hypot(1, slope);
      return { x, y, cos, sin: slope * cos };
    }
    const tail = .67 + .18 * beat.swell;
    const width = scale * (4.4 + 2.8 * beat.swell);
    const surface = g.createLinearGradient(headX - tail * spacing, 0, headX, 0);
    surface.addColorStop(0, ink.tail);
    surface.addColorStop(.42, ink.body);
    surface.addColorStop(.74, ink.light);
    surface.addColorStop(1, ink.core);
    brush(g, point, tail, width, [-.97, 1], ink.tail);
    brush(g, point, tail * .91, width, [-.78, .65], surface);
    brush(g, point, tail * .56, width, [-.61, .10], ink.light);
    // The beads spiral round the brushstroke in matched pairs. A wave of
    // extra satellites unfurls along both flanks when the player taps.
    for (let j = 0; j < 11; j++) {
      const f = (j + .55) / 11;
      const offset = -tail * (.19 + .98 * f);
      const p = point(offset);
      const orbit = Math.sin(f * 4.1 - time * 2.8 + i * 1.2);
      const gap = scale * (4 + 5.6 * f + 10 * beat.swell) * Math.sin(Math.PI * f);
      const side = j % 2 ? 1 : -1;
      const d = side * (gap + scale) * (.70 + .30 * orbit);
      const x = p.x - d * p.sin, y = p.y + d * p.cos;
      const size = scale * (1.1 + 1.0 * (1 - f) + .8 * beat.swell);
      if (j % 4 === 1) sparkle(g, x, y, size * 2.5, ink.tail, .20 * orbit);
      else pearl(g, x, y, size, j % 3 === 0 ? ink.body : ink.tail);
      if (j % 3 === 0) pearl(g, x, y, size * .55, ink.core);
      if (beat.swell > .015) {
        g.globalAlpha = beat.swell * .87;
        const d2 = -side * (gap * 1.55 + 2 * scale);
        pearl(g, p.x - d2 * p.sin, p.y + d2 * p.cos, size * .85, ink.body);
        g.globalAlpha = 1;
      }
    }
    const head = point(0);
    const r = scale * (8.1 + 4.2 * beat.swell);
    const tilt = .25 + head.sin * .53;
    sparkle(g, head.x, head.y, r * 1.13, ink.tail, tilt);
    sparkle(g, head.x, head.y, r * .78, ink.core, tilt);
    const lead = point(.068);
    pearl(g, lead.x, lead.y, scale * 1.55, ink.body);
  }
}
