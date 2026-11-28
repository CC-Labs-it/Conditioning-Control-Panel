/*
 * Chroma Cycle choreography: Chris Gannon, https://codepen.io/chrisgannon/pen/vYaGJgM
 * Copyright (c) Chris Gannon
 * iOS 9 SiriWave curve model: https://github.com/kopiro/siriwave
 * Copyright (c) 2020 Flavio Maria De Stefano
 *
 * MIT License
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */
const inks = ['#27aee8', '#fa247e', '#efbb22'];
const lights = ['#1252ae', '#ce3659', '#15c397'];
function ease(t) {
  return .5 - .5 * Math.cos(Math.PI * Math.max(0, Math.min(1, t)));
}

function tapFronts(scene, count) {
  const pitch = scene.w / count;
  const stepTime = Math.min(.075, pitch / (.84 * scene.w + 65));
  return scene.events.filter(hit => scene.time >= hit.time && scene.time - hit.time < 3.8).map(hit => ({
    age:scene.time - hit.time,
    origin:Math.min(count - 1, Math.floor(hit.u * count)),
    // A common speed preserves the spacing between successive presses all
    // the way to the edge. Quick taps make shorter, individually visible crests.
    stepTime,
    duration:Math.max(.058, Math.min(.092, (hit.interval ?? .36) * .87)) * (.96 + .08 * hit.seed),
    echoDelay:Math.max(.024, Math.min(.067, (hit.interval ?? .36) * .25)),
    strength:Math.min(1.08, .59 + .39 * hit.power + .09 * hit.seed),
    seed:hit.seed, polarity:hit.polarity, tapRate:hit.tapRate ?? 0
  }));
}

function localCrest(front, steps, localTime, count) {
  if (localTime < 0 || localTime >= front.duration) return 0;
  const phase = localTime / front.duration;
  // The contact is visible on the press frame. Each neighbour rises only
  // after the front arrives, and releases before the next close-spaced crest.
  const rise = steps === 0 ? 1 : ease(phase / .25);
  const fall = 1 - ease((phase - .28) / .72);
  return front.strength * rise * fall * Math.exp(-.46 * steps / count);
}

export function drawChroma(g, scene) {
  const { w, h, time, axis } = scene;
  const scale = Math.min(h / 100, 1.13);
  const lanes = Math.max(3, Math.round(w / (15 * scale)));
  const pitch = w / lanes, width = pitch * .45;
  const wavelength = Math.min(180 * scale, Math.max(85 * scale, w * .36));
  const reach = Math.min(h * .255, 23.5 * scale);
  const heightScale = 1.15, accentRange = .18;
  const axisRoom = Math.min(10,h*.09);
  const peakLimit = Math.max(.01, ((h/2 - axisRoom - 2*scale)/heightScale - 2.84*scale) /
    (reach*(1 + accentRange)));
  const fronts = tapFronts(scene, lanes);
  for (const front of fronts) {
    const pace = Math.max(0, front.tapRate - 1.8);
    const requested = front.strength * (.21 + .14*pace + .005*pace*pace);
    // Use the final half of the available height gradually. Even very fast
    // bursts can grow further, and a short shop swatch retains its full wave.
    const knee = peakLimit / 2;
    const excess = Math.max(0,requested - knee);
    front.strength = Math.min(requested,knee) + knee * excess/(knee + excess);
    front.phase = front.seed * 2 * Math.PI;
  }
  for (let lane = 0; lane < lanes; lane++) {
    const x = (lane + .5) * pitch, y = axis(x);
    const phase = 2 * Math.PI * x / wavelength - time * 2.1 + .26 * Math.sin(lane * 2.4);
    const levels = [0, 0, 0];
    for (const front of fronts) {
      const steps = Math.abs(front.origin - lane);
      // Stagger the handoff by less than a third of one lane. The pressed
      // bar still responds immediately and the crest travels in order.
      const sway = Math.sin(lane * 2.4 + front.phase);
      const stagger = steps === 0 ? 0 : .30 * sway;
      const local = (front.age - (steps + stagger) * front.stepTime) / (1 + .10 * sway);
      const lag = Math.min(front.echoDelay * 1.3, front.duration * .54);
      // A few bars reach farther on each wave. Using the press seed keeps
      // the accent on the same bar throughout its rise, release and echoes.
      const accent = Math.max(0, (Math.cos(lane * 1.73 + front.phase) - .80) * 5);
      const gain = 1 + accentRange * accent;
      for (let ink = 0; ink < inks.length; ink++) {
        levels[ink] = Math.max(levels[ink], gain * localCrest(front, steps, local - ink * lag, lanes));
      }
    }
    // The solid centre carries the entire live crest. Limit the overhang
    // of its delayed contours as the centre returns to the baseline.
    const body = levels[0];
    const idle = scale * (1.60 + 1.24 * (.5 + .5 * Math.cos(phase)));
    for (let ink = inks.length - 1; ink >= 0; ink--) {
      const echo = Math.min(levels[ink], body + .43);
      const crest = ink === 0 ? body : body + (echo - body) * (.56 + .05 * ink);
      const half = heightScale * (idle + reach * crest);
      const edgeGain = Math.min(crest,1.2);
      const breadth = width * (1 + .16 * edgeGain) + ink * scale * (1.20 + .85 * edgeGain);
      g.beginPath();
      g.roundRect(x - breadth / 2, y - half, breadth, half * 2, Math.min(2.1 * scale, half));
      g.fillStyle = inks[ink];
      g.globalAlpha = ink === 0 ? .94 : .14;
      g.fill();
      g.globalAlpha = ink === 0 ? 1 : .95;
      g.strokeStyle = ink === 0 ? '#167ba8' : inks[ink]; g.lineWidth = Math.max(.70, (1.1 + .24 * edgeGain) * scale);
      g.stroke();
    }
  }
}

export function drawSiri(g, scene) {
  const { w, h, time, axis, energy } = scene;
  const samples = Math.max(2, Math.ceil(w / 2.25)), step = w / samples;
  const spacing = Math.min(w, Math.max(115, Math.min(224, w * .45)));
  const scale = Math.min(h * .34, 36);
  g.beginPath();
  for (let i = 0; i <= samples; i++) {
    const x = i * step;
    if (i === 0) g.moveTo(x, axis(x)); else g.lineTo(x, axis(x));
  }
  g.lineWidth = .9; g.strokeStyle = '#826f9666'; g.stroke();
  g.globalCompositeOperation = 'lighter';
  g.globalAlpha = .76;
  for (let channel = 0; channel < lights.length; channel++) {
    const values = new Float32Array(samples + 1);
    function addLobe(center, width, phase, amplitude) {
      const first = Math.max(0, Math.floor((center - width * 3.5) / step));
      const last = Math.min(samples, Math.ceil((center + width * 3.5) / step));
      for (let i = first; i <= last; i++) {
        const q = (i * step - center) / width;
        const attenuation = 4 / (4 + q * q);
        values[i] += amplitude * Math.abs(Math.sin(q - phase)) * attenuation ** 4;
      }
    }
    for (let cell = -1; cell <= Math.ceil(w / spacing); cell++) {
      const seed = cell * 1.73 + channel * 2.04;
      const phase = time * (.94 + .20 * channel) + seed;
      const center = spacing * (cell + .5 + .18 * Math.sin(phase * .78) + .085 * (channel - 1));
      addLobe(center, spacing * (.22 + .045 * Math.cos(phase + seed)), phase,
        .49 + .23 * Math.sin(phase * .72 + channel));
    }
    for (const hit of scene.events) {
      const age = time - hit.time;
      if (age < 0 || age >= 3.8) continue;
      const main = Math.min(2, Math.floor(hit.seed * 3));
      const attack = Math.min(1, .28 + age * 9);
      const strength = hit.power * attack * Math.exp(-age * 1.27) * Math.min(1, (3.8-age)*2);
      const phase = 1.1 + hit.seed * .65 + age * (3.9 + .45 * channel);
      const width = spacing * (.105 + .075 * hit.seed + .035 * energy);
      for (const dir of [-1, 1]) {
        const center = hit.u * w + dir * age * (w * .40 + 38) * hit.speed;
        addLobe(center, width, phase + (channel-main)*.44,
          strength * (channel === main ? 1.28 : .10));
      }
    }
    // Draw one closed, mirrored colour field. Additive overlap creates
    // the brighter colours at the crossing of two or three voices.
    g.beginPath();
    for (const side of [-1, 1]) {
      for (let k = 0; k <= samples; k++) {
        const i = side === -1 ? k : samples - k, x = i * step;
        const y = axis(x) + side * scale * Math.tanh(values[i] * 1.23);
        if (side === -1 && k === 0) g.moveTo(x, y); else g.lineTo(x, y);
      }
    }
    g.closePath(); g.fillStyle = lights[channel]; g.fill();
  }
}

// Each ray starts inside its own convex frame. Express the exit point as
// a fraction of the segment between neighbouring centres so overlaps need
// no connector, even when the frames tilt in different directions.
function frameExit(cell, dx, dy) {
  const p = cell.outline;
  for (let k = 0; k < 4; k++) {
    const a = p[k], b = p[(k + 1) % 4];
    const ex = b.x - a.x, ey = b.y - a.y, cross = dx * ey - dy * ex;
    if (Math.abs(cross) < .00001) continue;
    const ax = a.x - cell.x, ay = a.y - cell.y;
    const along = (ax * dy - ay * dx) / cross;
    const exit = (ax * ey - ay * ex) / cross;
    if (along >= -.00001 && along <= 1.00001 && exit >= 0) return exit;
  }
  return 0;
}

function framePoints(cell, level) {
  const depth = 1 - .82 ** level;
  const r = cell.radius * (1 - depth);
  // Looking into the turning stack, the small interior squares swim
  // toward the far side of the opening, then drift back across it.
  const cx = cell.x + depth * cell.radius * .36 * cell.rock;
  const cy = cell.y + depth * cell.radius * .27 * cell.nod;
  const facing = cell.angle + level * cell.twist;
  const points = [];
  for (let k = 0; k < 4; k++) {
    const a = facing + Math.PI / 4 + k * Math.PI / 2;
    const u = r * Math.cos(a), v = r * Math.sin(a);
    points.push({x:cx + u * cell.foreshorten + v * .29 * cell.rock,
      y:cy + v * (.95 - .15 * cell.nod ** 2) + u * .17 * cell.nod});
  }
  return points;
}

// Swimming pyramid by Misha Tsankashvili inspired the nested square outlines.
export function drawPyramids(g, scene) {
  const { w, h, time, axis } = scene;
  const scale = Math.min(h / 106, 1.15);
  const count = Math.max(2, Math.round(w / (67 * scale)));
  const pitch = w / count, levels = 10;
  const fronts = tapFronts(scene, count);
  const cells = [];
  for (let i = -1; i <= count; i++) {
    const x = (i + .5) * pitch;
    let rotation = 0, swell = 0, recoil = 0;
    for (const front of fronts) {
      const steps = Math.abs(i - front.origin);
      const local = front.age - steps * front.stepTime;
      if (local < 0) continue;
      swell = Math.max(swell, localCrest(front, steps, local, count));
      // Complete a quarter-turn per tap. Once settled, the fourfold square
      // has the same silhouette when an expired impulse leaves the buffer.
      rotation += ease(local / (.50 + front.seed * .18)) * Math.PI / 2;
      const fade = front.strength * Math.exp(-3.2 * local) * Math.max(0, Math.min(1, (3.8 - front.age) * 2));
      recoil += fade * Math.sin(local * (9 + front.seed * 2.4)) * front.polarity;
    }
    const wave = time * 2.35 - i * .79;
    const kick = Math.tanh(recoil * 1.35);
    const rock = .66 * Math.sin(wave) + .47 * kick;
    const nod = .62 * Math.cos(wave) - .42 * kick;
    const y = axis(x) + scale * (3.5 * Math.sin(wave - .45) + 2.8 * kick - 7 * swell);
    const idleRadius = Math.min(h * .24, pitch * .47);
    const peakRadius = Math.min(h * .387, pitch * .81);
    const radius = idleRadius + (peakRadius - idleRadius) * swell;
    const cell = {x, y, radius, swell, i, rock, nod,
      angle:time * .66 + i * .24 + rotation + .32 * rock,
      twist:.20 + .14 * Math.sin(wave - .55) + .095 * swell,
      foreshorten:Math.cos(.88 * rock),
      hue:157 + 6 * Math.sin(wave - .35) + 14 * swell + 3 * kick};
    cell.outline = framePoints(cell, 0);
    cells.push(cell);
  }
  g.beginPath();
  for (let i = 1; i < cells.length; i++) {
    const a = cells[i-1], b = cells[i];
    const dx = b.x - a.x, dy = b.y - a.y;
    const from = frameExit(a, dx, dy), to = 1 - frameExit(b, -dx, -dy);
    if (to <= from) continue;
    g.moveTo(a.x + from * dx, a.y + from * dy);
    g.lineTo(a.x + to * dx, a.y + to * dy);
  }
  g.lineWidth = 2.35 * scale; g.strokeStyle = '#147a72'; g.stroke();
  g.lineWidth = 1.05 * scale; g.strokeStyle = '#52ffc7'; g.stroke();
  g.lineJoin = 'miter';
  for (const cell of cells) {
    const {swell, i, hue} = cell;
    const edge = 'hsl(' + hue + ' 63% 24%)';
    for (let j = 0; j < levels; j++) {
      const points = j === 0 ? cell.outline : framePoints(cell, j);
      const pulse = .5 + .5 * Math.cos(j * .56 - time * 2.1 + i * .31 - swell * 2);
      const weight = scale * (.80 + (1.65 + .48 * swell) * pulse) * (1 - .043*j);
      g.beginPath();
      g.moveTo(points[0].x, points[0].y);
      for (let k = 1; k < points.length; k++) g.lineTo(points[k].x, points[k].y);
      g.closePath();
      g.lineWidth = weight + .68 * scale; g.strokeStyle = edge; g.stroke();
      g.lineWidth = weight; g.strokeStyle = 'hsl(' + (hue + j * .65) + ' 83% ' + (54 + 6 * pulse) + '%)'; g.stroke();
    }
  }
}
