/* Three open strings. Their bridge points stay anchored; plucks travel along
 * an individual string and wake a quieter harmonic on a neighbouring string. */
import { pearl, sparkle } from './kinetic-particles.js';

const TAU = 2 * Math.PI;
const clamp = (n, low, high) => Math.min(high, Math.max(low, n));
const fract = n => n - Math.floor(n);
const strings = [
  { position: -1, speed: 1.12, width: 1.8, edge: '#414669', metal: '#65cfcf', light: '#d4fff5' },
  { position:  0, speed: 0.92, width: 2.6, edge: '#644568', metal: '#efac76', light: '#fff2cc' },
  { position:  1, speed: 0.77, width: 1.7, edge: '#51436c', metal: '#ab8be1', light: '#f1e7ff' },
];

function plucks(events, w, time, energy) {
  const active = [[], [], []];
  for (const evt of events) {
    const age = time - evt.time;
    if (age < 0 || age > 2.75) continue;
    const primary = Math.min(2, Math.floor(evt.seed * 3));
    const sympathetic = (primary + (evt.seed < .5 ? 1 : 2)) % 3;
    for (const voice of [primary, sympathetic]) {
      const main = voice === primary;
      const dt = age - (main ? 0 : .11);
      if (dt < 0) continue;
      const pace = clamp(w * .69, 150, 920) * evt.speed * strings[voice].speed;
      const spread = clamp(w * .069, 23, 68) * (.8 + .48 * evt.seed);
      active[voice].push({
        x: evt.u * w, seed: evt.seed, main, dt, pace, spread,
        distance: Math.max(0, dt * pace),
        amplitude: evt.power * (main ? 1 : .23 + .13 * energy) * Math.exp(-1.50 * dt),
        sign: main ? evt.polarity : -evt.polarity,
      });
    }
  }
  return active;
}

function path(g, points) {
  g.beginPath();
  g.moveTo(points[0].x, points[0].y);
  for (let i = 1; i < points.length; i++) g.lineTo(points[i].x, points[i].y);
}

function diamond(g, x, y, length, height, fill) {
  g.beginPath();
  g.moveTo(x - length, y);
  g.lineTo(x, y - height);
  g.lineTo(x + length, y);
  g.lineTo(x, y + height);
  g.closePath();
  g.fillStyle = fill;
  g.fill();
}

function paintString(g, scene, interval, voice, strokes, scale) {
  const [left, right] = interval;
  const span = right - left;
  if (span < 1) return;
  const { axis, h, time, energy } = scene;
  const wire = strings[voice];
  // An odd number of bays leaves the middle open for the usual button press.
  const bays = Math.max(1, 2 * Math.round((span / (126*scale) - 1) / 2) + 1);
  const count = clamp(Math.ceil(span / 3.5), 12, 440);
  const points = [];
  for (let i = 0; i <= count; i++) {
    const r = i / count, x = left + r * span;
    const fundamental = Math.sin(Math.PI * r * bays);
    const pin = Math.abs(fundamental);
    const spread = 15.2*scale*fundamental*(1+.075*Math.sin(time*.54-r*Math.PI));
    // The two outer voices meet at fixed bridge points. A gold octave
    // continues through each opening and gives the two halves a shared rhythm.
    const slack = scale * pin * (voice === 1 ? 4.4 : .8) * Math.sin(TAU*r*bays + .38*Math.sin(time*.65));
    let pull = 0, slide = 0, hot = 0;
    for (const hit of strokes) {
      const dx = x - hit.x;
      const q = (Math.abs(dx) - hit.distance) / hit.spread;
      if (Math.abs(q) > 3.1) continue;
      const envelope = Math.exp(-q * q * 1.7) * hit.amplitude;
      const note = q * (1.35 + hit.seed * .85) + hit.dt * (.95 + .36 * voice);
      const overtone = energy * .24 * Math.sin(note * 2 + hit.seed * Math.PI);
      pull += envelope * (Math.cos(note) + overtone) * hit.sign;
      slide += envelope * Math.sin(note + .5 + hit.seed * 4) * Math.sign(dx || 1);
      hot += envelope;
    }
    // The endpoints and bridge nodes bear the load. Fast taps bend the wire
    // locally; the axis, bridge positions and the other strings stay put.
    const freedom = clamp(pin * 1.70, 0, 1);
    const px = x + freedom * Math.tanh(slide) * scale * (4.0 + 3.1 * energy);
    const rest = axis(x) + wire.position * spread + slack;
    const py = clamp(rest + freedom * Math.tanh(pull * 1.65) * scale * (14 + 10 * energy), 2, h - 2);
    points.push({x: px, y: py, heat: clamp(hot, 0, 1)});
  }
  function at(x) {
    const spot = clamp((x - left) / span * count, 0, count);
    const i = Math.min(count - 1, Math.floor(spot)), f = spot - i, a = points[i], b = points[i + 1];
    return { x: a.x + (b.x - a.x) * f, y: a.y + (b.y - a.y) * f, tilt: Math.atan2(b.y - a.y, b.x - a.x) };
  }
  path(g, points);
  g.strokeStyle = wire.edge; g.lineWidth = wire.width * scale + 1.25; g.stroke();
  g.strokeStyle = wire.metal; g.lineWidth = Math.max(.8, wire.width * scale); g.stroke();
  g.beginPath();
  for (let i = 1; i < points.length; i++) {
    if (points[i].heat < .15) continue;
    g.moveTo(points[i-1].x,points[i-1].y); g.lineTo(points[i].x,points[i].y);
  }
  g.lineWidth = Math.max(.85,1.3*scale); g.strokeStyle = wire.light; g.stroke();

  // A small travelling node on each string gives the eye a pitch to follow
  // at rest. Nodes stay well apart and their moving reflections are short.
  const nodeSpacing = Math.max(132, (208 + 18 * voice) * scale);
  for (let x = left + fract(time * (.075 + .009 * voice) + .17 + voice * .31) * nodeSpacing;
       x < right; x += nodeSpacing) {
    const p = at(x);
    g.save(); g.translate(p.x,p.y); g.rotate(p.tilt);
    diamond(g,0,0,(6+.7*voice)*scale,3.3*scale,wire.edge);
    diamond(g,-.3*scale,-.25*scale,4.5*scale,2.05*scale,wire.light);
    g.restore();
    for (let n = 1; n <= 6; n++) {
      const u = x - n * 4.2 * scale;
      if (u < left) break;
      const q = at(u);
      const lift = 4.4 * scale * Math.sin(Math.PI * n / 7) * Math.cos(time * 2 + voice);
      const radius = scale * (1.45 - .10 * n);
      for (const side of [-1, 1]) {
        pearl(g, q.x, q.y + side * lift, radius, wire.edge);
        pearl(g, q.x, q.y + side * lift, radius * .57, wire.light);
      }
    }
  }
  const sounding = strokes.filter(hit => hit.main && hit.dt <= 1.95);
  for (let i = 0; i < sounding.length; i++) {
    const hit = sounding[i];
    for (const dir of [-1, 1]) {
      const x = hit.x + dir * hit.distance;
      if (x <= left || x >= right) continue;
      const p = at(x);
      const flare = Math.min(1, hit.amplitude * 1.7);
      g.save(); g.translate(p.x,p.y); g.rotate(p.tilt);
      g.globalAlpha = flare;
      const stretch = 1+energy*.38;
      g.globalAlpha = flare*.28;
      diamond(g,0,0,13*scale*stretch,5.4*scale,wire.metal);
      g.globalAlpha = flare;
      diamond(g,0,0,8*scale*stretch,3.6*scale,wire.light);
      g.restore();
      // A pluck carries a pair of pearl garlands down the sounding string.
      // The pearls take their height from the wire itself even at a bridge.
      const newer = sounding.length - 1 - i;
      const strandAlpha = Math.max(0, 1 - .45 * newer);
      if (!strandAlpha) continue;
      for (let n = 1; n <= 7; n++) {
        const f = n / 8;
        const sampleX = x - dir * f * (41 + 37 * energy) * scale;
        if (sampleX <= left || sampleX >= right) continue;
        const q = at(sampleX);
        const height = Math.sin(Math.PI * f) * (5 + 11 * energy) * scale;
        const twist = f * TAU + hit.seed * Math.PI + hit.dt * 4;
        g.globalAlpha = strandAlpha * Math.min(1, flare * 1.40) * (1 - .45 * f);
        for (const side of [-1, 1]) {
          const py = q.y + side * height * Math.sin(twist);
          const r = (1.25 + .85 * (1 - f)) * scale;
          if ((n + side) % 4 === 0) {
            sparkle(g, q.x, py, r * 2.6, wire.edge, q.tilt);
            sparkle(g, q.x, py, r * 1.6, wire.light, q.tilt);
          } else {
            pearl(g, q.x, py, r, wire.edge);
            pearl(g, q.x, py, r * .66, wire.metal);
          }
        }
      }
      g.globalAlpha = 1;
    }
  }
}

export function drawHarp(g, scene) {
  const scale = clamp(scene.h / 92, .43, 1);
  const strokes = plucks(scene.events, scene.w, scene.time, scene.energy);
  g.save(); g.lineJoin = 'round'; g.lineCap = 'round';
  for (const range of scene.ranges) {
    for (const voice of [2, 0, 1]) paintString(g,scene,range,voice,strokes[voice],scale);
  }
  g.restore();
}
