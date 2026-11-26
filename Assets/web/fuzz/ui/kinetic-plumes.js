import { passingBeat, pearl, sparkle } from './kinetic-particles.js';

const shades = ['#405185', '#167985', '#2b968b', '#137e89', '#554f9a'];
const tips = ['#c5c4ff', '#89e3d0', '#cfedbb', '#8be1d1', '#cbb3f1'];

// Five enamel vanes overlap at one pin. Each one curves back towards its
// neighbours at the tip. The three pearl rings travel along those same vanes.
function fan(g, x, y, radius, pitch, angle, open, beat, time, index, scale) {
  g.save();
  g.translate(x, y);
  g.rotate(angle);
  const order = [0, 4, 1, 3, 2];
  for (const n of order) {
    const rank = (n - 2) / 2;
    const theta = rank * open;
    const sn = Math.sin(theta), cs = Math.cos(theta);
    const len = radius * (1 - .12 * rank * rank);
    const dx = sn * len, dy = -cs * len;
    const curl = rank * len * .16;
    const waist = Math.min(pitch * .12, len * (.19 + .028 * Math.cos(theta)));
    const midX = dx * .46 + cs * curl, midY = dy * .46 + sn * curl;
    g.beginPath();
    g.moveTo(0, 0);
    g.quadraticCurveTo(midX - cs * waist * 1.6, midY - sn * waist * 1.6, dx, dy);
    g.quadraticCurveTo(midX + cs * waist, midY + sn * waist, 0, 0);
    const enamel = g.createLinearGradient(0, 0, dx, dy);
    enamel.addColorStop(0, '#4a417d');
    enamel.addColorStop(.45, shades[n]);
    enamel.addColorStop(.85, tips[n]);
    enamel.addColorStop(1, '#fff2cb');
    g.fillStyle = enamel; g.fill();
    g.strokeStyle = shades[n]; g.lineWidth = Math.max(.65, 1.05 * scale); g.stroke();
    g.beginPath(); g.moveTo(dx * .14, dy * .14);
    g.quadraticCurveTo(midX, midY, dx * .88, dy * .88);
    g.strokeStyle = '#d3edc6'; g.lineWidth = Math.max(.55, .85 * scale); g.stroke();
    // The stacked turquoise eye stays attached to the blade as it unfolds.
    g.save();
    g.translate(dx * .70 + cs * curl * .36, dy * .70 + sn * curl * .36);
    g.rotate(theta);
    for (const [width, height, color] of [[.088,.15,'#e6bd72'],[.061,.112,'#257080'],[.032,.068,'#393b72']]) {
      g.beginPath();
      g.ellipse(0, 0, len * width, len * height, 0, 0, Math.PI * 2);
      g.fillStyle = color;
      g.fill();
    }
    g.restore();
    // Pearl rings climb in formation. The leading ring clears the feather
    // tips on a press and folds back into the following ring.
    for (let k = 0; k < 4; k++) {
      const clock = time * .50 + index * .12 + k * .25 + beat.turn * .28;
      const phase = clock - Math.floor(clock);
      const distance = .81 + phase * (.36 + .47 * beat.swell);
      const curlOut = curl * Math.sin(Math.min(1, distance) * Math.PI) * .56;
      const px = dx * distance + cs * curlOut, py = dy * distance + sn * curlOut;
      const alpha = Math.min(1, phase * 9, (1 - phase) * 9);
      g.globalAlpha = alpha;
      if (k % 2 === 0 && n % 2 === 0) {
        const r = scale * (2.6 + 2.4 * beat.swell);
        sparkle(g, px, py, r, shades[n], theta * .6);
        sparkle(g, px, py, r * .61, '#fff3c9', theta * .6);
      } else {
        pearl(g, px, py, scale * (1.35 + .87 * beat.swell), distance > 1 ? shades[n] : '#fff2c7');
      }
    }
    g.globalAlpha = 1;
  }
  pearl(g, 0, 0, 3.3 * scale, '#4b4779');
  pearl(g, -.4 * scale, -.4 * scale, 1.75 * scale, '#ffe8a0');
  g.restore();
}

export function drawPlumes(g, scene) {
  const { w, h, time, axis, energy } = scene;
  const scale = Math.min(1.12, h / 105, Math.max(.78, scene.detail));
  const count = Math.max(2, Math.round(w / (85 * scale)));
  const pitch = w / count;
  // Alternating fans face across the same curved line like a feather stitch.
  g.beginPath();
  for (let x = 0; x < w + 5; x += 5) {
    const y = axis(x);
    if (x === 0) g.moveTo(x, y); else g.lineTo(x, y);
  }
  g.strokeStyle = '#71628a'; g.lineWidth = Math.max(.75, scale * 1.3); g.stroke();
  for (let i = -1; i <= count; i++) {
    const x = (i + .5) * pitch;
    const beat = passingBeat(scene, x);
    const phase = time * 1.65 - i * .77;
    const sign = i % 2 === 0 ? 1 : -1;
    const open = 1.02 + .27 * Math.sin(phase) + .48 * beat.swell;
    const angle = (sign === 1 ? 0 : Math.PI) + sign *
      (.53 + .32 * Math.cos(phase) + .84 * beat.turn);
    const reach = Math.min(h * .265, pitch * .39) *
      (1.02 + .11 * Math.cos(phase) + .28 * beat.swell + .09 * energy);
    const y = axis(x);
    const space = Math.min(y, h - y) - 3 * scale;
    const radius = Math.min(reach, space / (1.15 + .49 * beat.swell));
    fan(g, x, y, radius, pitch, angle, open, beat, time, i, scale);
  }
}
