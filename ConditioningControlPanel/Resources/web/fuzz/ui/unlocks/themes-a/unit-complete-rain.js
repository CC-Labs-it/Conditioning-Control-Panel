import { canvasDpr, frameRect, LITE } from '../../perf.js';

const glyphs = '01:|/<>+ｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾕﾖﾗﾘﾙﾚﾛ';
const ink = ['#0b5030', '#107147', '#17985c', '#24bf71', '#55e598', '#ceffe0'];
const glyph = (seed) => glyphs[Math.abs(Math.imul(seed, 48271) ^ (seed >>> 3)) % glyphs.length];
const clamp = value => Math.max(0, Math.min(1, value));
const smooth = value => { const u = clamp(value); return u * u * (3 - 2 * u); };

/* One small reusable light sprite. Stamping it avoids a shadow filter or gradient allocation per ember. */
function makeGlow(doc) {
  const sprite = doc.createElement('canvas');
  sprite.width = sprite.height = 40;
  const brush = sprite.getContext('2d');
  const glow = brush.createRadialGradient(20, 20, 0, 20, 20, 20);
  glow.addColorStop(0, '#efffdc');
  glow.addColorStop(0.13, '#9dffafed');
  glow.addColorStop(0.37, '#31ff877d');
  glow.addColorStop(1, '#00dc7000');
  brush.fillStyle = glow;
  brush.fillRect(0, 0, 40, 40);
  return sprite;
}

export function makeRain(ctx, stage, pieces) {
  const wash = ctx.el('div', 'position:absolute;inset:0;z-index:4;pointer-events:none;background:radial-gradient(ellipse at 48% 46%,#061c12,#010705 88%);opacity:0', null, ctx.world);
  const canvas = ctx.el('canvas', 'position:absolute;inset:0;width:100%;height:100%;pointer-events:none', null, stage);
  canvas.dataset.unitRain = '';
  canvas.setAttribute('aria-hidden', 'true');
  const g = canvas.getContext('2d'), glow = g && makeGlow(canvas.ownerDocument);
  const rng = ctx.rng;
  const pool = Array.from({ length: LITE ? 144 : 384 }, () => ({ start: -Infinity, life: 0 }));
  let columns = [], masks = [], width = 0, height = 0, lastDraw = -1, dpr = 1, next = 0;

  function resize(w, h) {
    if (w === width && h === height) return;
    width = w; height = h; dpr = canvasDpr(1.5);
    canvas.width = Math.max(1, Math.round(width * dpr));
    canvas.height = Math.max(1, Math.round(height * dpr));
    const count = Math.max(10, Math.min(72, Math.ceil(w / (LITE ? 38 : 29))));
    columns = Array.from({ length: count }, (_, i) => ({
      x: (i + 0.15 + rng() * 0.35) * w / count,
      rate: 75 + rng() * 130, size: 22 + 2 * Math.floor(rng() * 5),
      start: h * (0.12 + rng() * 0.80), trail: 5 + Math.floor(rng() * 7),
      // Space the first streams across the screen, including a narrow phone in Gentle.
      density: i % 4 === 1 ? 0 : 0.12 + rng() * 0.88, seed: i * 231 + 193,
    }));
    // Rain sits behind the frozen toys. The pieces they shed can cross in front of their neighbours.
    masks = pieces.map(({el}) => ({ el, rect: frameRect(el) }));
  }

  /* Emit from just the strip the laser removed since the last frame. Crossing numbered samples
     distributes the same amount of material at 30 or 144 Hz. */
  function evaporate(rect, from, to, time, letters = null, reverse = false) {
    if (!g || !columns.length || to <= from) return;
    const left = Math.max(0, rect.left), right = Math.min(width, rect.right);
    if (left >= right) return;
    const area = Math.sqrt((right - left) * Math.min(height, rect.height));
    const total = letters ? (LITE ? 110 : 180) : Math.ceil(Math.min(LITE ? 46 : 98, Math.max(12, area * (LITE ? 0.48 : 0.72))));
    const first = Math.floor(clamp(from) * total), end = Math.floor(clamp(to) * total);
    for (let i = first; i < end; i++) {
      const strip = (i + .5) / total, u = (i * .618034 + rng() * .13) % 1;
      const y = rect.top + rect.height * (letters ? u : 1 - strip);
      if (y < -4 || y > height + 4) continue;
      const p = pool[next]; next = (next + 1) % pool.length;
      const x = letters ? (reverse ? 1 - strip : strip) : u;
      p.x = left + (right - left) * x; p.y = y;
      p.letter = letters && i % 4 === 0 ? letters[Math.min(letters.length - 1, Math.floor(x * letters.length))] : '';
      p.start = time; p.life = 0.63 + rng() * 0.56;
      p.vx = (u - 0.5) * 68 + (rng() - 0.5) * 122;
      p.lift = 26 + rng() * 68;
      p.depth = (rng() - 0.38) * 170;
      p.gravity = 135 + rng() * 80;
      p.size = (LITE ? 1.8 : 2.0) + rng() ** 2 * 5.3;
      p.turn = rng() * Math.PI * 2;
      p.spin = (rng() < 0.5 ? -1 : 1) * (4 + rng() * 9);
      p.seed = Math.floor(rng() * 32000);
      p.kind = i % 5;
      const target = Math.max(0, Math.min(columns.length - 1, Math.floor((p.x + p.vx * 0.3) * columns.length / width)));
      p.columnX = columns[target].x;
    }
  }

  function drawRain(time, progress, soft, visibility) {
    if (visibility <= 0) return;
    const density = (0.22 + progress * 0.75) * (soft ? 0.40 : 1);
    for (const c of columns) {
      if (c.density > density) continue;
      g.font = c.size + 'px Consolas,monospace';
      const distance = c.start + time * c.rate * (soft ? 0.30 : 1), cycle = height + c.trail * c.size;
      const head = distance % cycle, step = Math.floor(distance / c.size);
      for (let j = c.trail - 1; j >= 0; j--) {
        const y = head - j * c.size;
        if (y < 0 || y > height + c.size) continue;
        const value = 1 - j / c.trail;
        g.globalAlpha = Math.min(1, time) * value * (soft ? 0.47 : 0.84) * visibility;
        g.fillStyle = ink[j === 0 ? 5 : Math.floor(value * 4)];
        g.fillText(glyph(c.seed + step - j), c.x, y);
      }
    }
    for (const { el, rect } of masks) {
      if (!el.hasAttribute('data-unit-scanned')) g.clearRect(rect.left, rect.top, rect.width, rect.height);
    }
  }

  function drawDebris(time) {
    const focal = Math.max(480, Math.min(950, height));
    g.font = 'bold 12px Consolas,monospace';
    for (const p of pool) {
      const age = time - p.start;
      if (age >= p.life) continue;
      const u = age / p.life, drift = 1 - Math.exp(-3.9 * age);
      const z = p.depth * Math.sin(Math.PI * u);
      const scale = focal / (focal - z);
      const settle = smooth((u - 0.50) / 0.38);
      const flightX = width * 0.5 + (p.x + p.vx * drift - width * 0.5) * scale;
      const x = flightX + (p.columnX - flightX) * settle;
      const y = height * 0.5 + (p.y - p.lift * drift + p.gravity * age * age - height * 0.5) * scale;
      const alpha = clamp(age / 0.024) * (1 - smooth((u - 0.68) / 0.32));
      const size = p.size * scale;
      const spin = p.turn + age * p.spin, face = Math.cos(spin);
      if (p.kind === 0 || p.kind === 3) {
        const halo = size * (p.kind === 0 ? 7.0 : 4.4);
        g.globalAlpha = alpha * (1 - settle) * 0.68;
        g.drawImage(glow, x - halo / 2, y - halo / 2, halo, halo);
      }
      g.globalAlpha = alpha * (1 - settle);
      if (p.letter) {
        g.fillStyle = p.depth > 0 ? '#e1ffe0' : '#64db97';
        g.fillText(p.letter, x, y);
      } else if (p.kind < 2) {
        // A narrow glowing streak follows the ember's projected trajectory.
        g.strokeStyle = p.depth > 0 ? '#dcffca' : '#5bff9c';
        g.lineWidth = Math.max(0.7, size * 0.33);
        g.beginPath();
        g.moveTo(x, y);
        g.lineTo(x - p.vx * (1 - drift) * 0.038, y + (p.lift * (1 - drift) * 3.9 - 2 * p.gravity * age) * 0.033);
        g.stroke();
      } else {
        // Two facets share a spine. The near face catches the light as the fragment tumbles toward the lens.
        const a = Math.sin(spin * 0.38), b = Math.cos(spin * 0.38);
        const dx = b * size, dy = a * size, fx = -a * size * face * 0.65, fy = b * size * face * 0.65;
        g.fillStyle = face > 0 ? '#baffa0' : '#27c47f';
        g.beginPath(); g.moveTo(x - dx, y - dy); g.lineTo(x + fx, y + fy); g.lineTo(x + dx, y + dy); g.fill();
        g.fillStyle = face > 0 ? '#45d987' : '#107653';
        g.beginPath(); g.moveTo(x - dx, y - dy); g.lineTo(x - fx * 0.6, y - fy * 0.6); g.lineTo(x + dx, y + dy); g.fill();
      }
      if (settle > 0 && p.kind >= 2) {
        g.globalAlpha = alpha * settle * 0.82;
        g.fillStyle = p.depth > 0 ? '#9dfbc2' : '#2bde87';
        g.fillText(glyph(p.seed + Math.floor(age * 5)), x, y);
      }
    }
  }

  function draw(time, progress, soft, w, h, visibility = 1) {
    if (!g || time - lastDraw < 1 / (soft ? 20 : LITE ? 30 : 55)) return;
    lastDraw = time; resize(w, h);
    wash.style.opacity = ((0.38 + progress * 0.60) * Math.min(1, time / 0.65)).toFixed(3);
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    g.clearRect(0, 0, w, h);
    drawRain(time, progress, soft, visibility);
    if (soft) {
      for (const p of pool) p.start = -Infinity;
    } else drawDebris(time);
    g.globalAlpha = 1;
  }

  function clear() { wash.remove(); canvas.remove(); }
  ctx.cleanup(clear);
  return { canvas, evaporate, draw, clear };
}
