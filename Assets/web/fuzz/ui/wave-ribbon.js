/* The shop swatch and the live Low Hum ribbon share this painter. Coordinates
 * on a ribbon are its length and its signed distance from the centreline. */
import { FIELD_BY_ID, FIELD_WIDTH, FIELD_HEIGHT, FIELD_COLUMNS, FIELD_FRAMES, FIELD_FPS } from '../assets/prize-wheel/fields.js';
import { drawKineticWave, isKineticWave, KINETIC_STUDIES } from './kinetic-waves.js';
export { addWavePress } from './kinetic-waves.js';

const textures = new WeakMap();
export function waveFields(doc) {
  if (!textures.has(doc)) {
    const img = new doc.defaultView.Image();
    img.src = new URL('../assets/prize-wheel/fields.webp',import.meta.url).href;
    textures.set(doc,img);
  }
  return textures.get(doc);
}

export const WAVE_STYLES = Object.freeze([
  { id: 'classic', name: 'Classic', price: 0, description: 'A clean sine wave. Press to send a ripple in both directions.' },
  { id: 'aurora', name: 'Velvet Aurora', price: 1200, description: 'A pleated light curtain. Each press fans its folds open.' },
  { id: 'loom', name: 'Phase Loom', price: 2800, description: 'Two iridescent strands weave over and under. A press sends a tight twist down the braid.' },
  { id: 'mercury', name: 'Mercury Tide', price: 4800, description: 'Liquid chrome with moving reflections. A press sends two silver beads racing apart.' },
  { id: 'mycelium', name: 'Neon Mycelium', price: 7500, description: 'A branching mesh of neon filaments. A press lights up forks on both sides of the button.' },
  { id: 'prism', name: 'Spectral Shear', price: 12000, description: 'A fractured prism. A press separates its color channels, then pulls them back together.' },
]);
/* Every ribbon the Button Shop sells: the textured ones and the kinetic studies (owner 2026-11-07: all buyable). */
export const SHOP_WAVES = Object.freeze([...WAVE_STYLES, ...KINETIC_STUDIES]);
const names = new Set(SHOP_WAVES.map(s => s.id));
export function ownedWave(bag, id) {
  return id === 'classic' || (names.has(id) && bag?.waveStyles?.[id] === true);
}
export function chosenWave(bag) {
  return ownedWave(bag, bag?.waveStyle) ? bag.waveStyle : 'classic';
}

const tau = Math.PI * 2;
const sin = Math.sin, cos = Math.cos;
function trace(g, points, height, reverse = false) {
  for (let i = reverse ? points.length - 1 : 0; reverse ? i >= 0 : i < points.length; reverse ? i-- : i++) {
    const p = points[i];
    g.lineTo(p.x, p.y + p.h * height(p));
  }
}
const flat = () => 0;
function thread(g, points, height, color, width = 1) {
  g.beginPath();
  trace(g, points, height);
  g.strokeStyle = color; g.lineWidth = width; g.stroke();
}
function ribbon(g, points, top, bottom, color) {
  g.beginPath(); trace(g, points, top); trace(g, points, bottom, true);
  g.closePath(); g.fillStyle = color; g.fill();
}
function jewel(g, x, y, radius, color) {
  g.beginPath(); g.moveTo(x, y - radius); g.lineTo(x + radius * .34, y);
  g.lineTo(x, y + radius); g.lineTo(x - radius * .34, y); g.closePath();
  g.fillStyle = color; g.fill();
}

// Draw the wheel's moving material in the ribbon's own coordinates. Mirrored
// repeats join at the same edge pixel; each patch bends with the centreline.
function material(g, points, t, id, top, bottom, opacity) {
  const img = waveFields(g.canvas.ownerDocument);
  if (!img.complete || !img.naturalWidth) return;
  const cell = FIELD_BY_ID.get(id);
  const frame = Math.floor(t * FIELD_FPS) % FIELD_FRAMES;
  const sx = (frame % FIELD_COLUMNS) * FIELD_WIDTH, sy = Math.floor(frame / FIELD_COLUMNS) * FIELD_HEIGHT + cell.top;
  const period = 112;
  g.save();
  g.beginPath(); trace(g,points,top); trace(g,points,bottom,true); g.closePath(); g.clip();
  g.globalAlpha *= opacity;
  for (let i = 0; i < points.length-1; i+=2) {
    const a = points[i], b = points[Math.min(i+2,points.length-1)];
    const dx = b.x-a.x, centre = (a.x+b.x)/2, slope = (b.y-a.y)/dx;
    const height = Math.max(1,(a.h+b.h)*1.30);
    const phase = (centre/period + .06 * sin(t + centre*.006) + .07*a.kick) % 2;
    const u = 1-Math.abs(1-(phase+2)%2);
    const sampleWidth = Math.min(FIELD_WIDTH, FIELD_WIDTH*(dx+1)/period);
    const sourceX = sx + u*(FIELD_WIDTH-sampleWidth);
    g.save();
    g.transform(1,slope,0,1,a.x,a.y);
    if ((phase+2)%2 >= 1) { g.translate(dx,0); g.scale(-1,1); }
    g.drawImage(img,sourceX,sy,sampleWidth,cell.height,-.35,-height,dx+.7,height*2);
    g.restore();
  }
  g.restore();
}

function velvet(g, points, t) {
  const fold = p => .54 * sin(p.d * .027 - t * 1.7 + p.kick) + .23 * sin(p.d * .048 + t);
  const colors = ['#46316f', '#663fa4', '#b77cd7', '#f7b9c9', '#a9ead5', '#59cdbd', '#397f99'];
  for (let i = 0; i < colors.length; i++) {
    const edge = (p, n) => 1.04 - n * .30 + fold(p) * (.2 + .10 * n);
    ribbon(g, points, p => edge(p, i), p => edge(p, i + 1) - .02, colors[i]);
  }
  material(g,points,t,'small',p=>-.83+fold(p)*.74,p=>.67+fold(p)*.31,.82);
  g.lineWidth = 1.15;
  for (let i = 1; i < points.length - 1; i++) {
    const p = points[i], next = points[i + 1];
    const light = (.5 + .5 * cos(p.d * .19 - t * 2 + fold(p) * 3)) ** 2;
    g.strokeStyle = 'rgba(227,255,230,' + (.09 + light * .42) + ')';
    g.beginPath(); g.moveTo(p.x, p.y + p.h * .8); g.quadraticCurveTo(next.x, p.y, next.x + 1.5, next.y + next.h * (-.9 + fold(next) * .7)); g.stroke();
    if (i % 8 === 0) jewel(g, p.x, p.y + p.h * sin(p.d * 1.3 + t), 1.2 + light + p.power * 2, '#fff4c5');
  }
}

function loom(g, points, t) {
  const phase = p => p.d * .060 - t * 2.4 + p.kick * .66;
  function strand(segment,side) {
    const centre = p => .67 * side * sin(phase(p));
    const width = p => .14 + .15 * (1 + side * cos(phase(p)));
    ribbon(g,segment,p=>centre(p)-width(p),p=>centre(p)+width(p),side>0?'#933888':'#277d93');
    ribbon(g,segment,p=>centre(p)-width(p)*.83,p=>centre(p)+width(p)*.35,side>0?'#f88dcc':'#62e7da');
    thread(g,segment,p=>centre(p)-width(p)*.54,side>0?'#ffe9ca':'#dafff4',1.25);
  }
  for (let i = 1; i < points.length; i += 2) {
    const a = points[i-1], b = points[i];
    g.beginPath(); g.moveTo(a.x,a.y + a.h * .65 * sin(phase(a))); g.lineTo(b.x,b.y - b.h * .65 * sin(phase(b)));
    g.strokeStyle = '#806aa3'; g.lineWidth = .85; g.stroke();
  }
  strand(points,-1); strand(points,1);
  // Reprint the near strand over the crossing. Switch depth at maximum
  // separation, where the endpoints of the painted spans are hidden.
  let start = 0;
  for (let i = 1; i <= points.length; i++) {
    if (i < points.length && (cos(phase(points[i])) >= 0) === (cos(phase(points[start])) >= 0)) continue;
    if (cos(phase(points[start])) < 0) strand(points.slice(start, Math.min(i+1,points.length)),-1);
    start = i;
  }
}

function mercury(g, points, t) {
  const top = p => -1 + .13 * sin(p.d * .031 + t);
  const reflection = p => -.35 + .29 * sin(p.d * .022 + t * 1.6 + .8 * sin(p.d * .057 - t)) + .25 * p.kick;
  ribbon(g, points, top, () => 1, '#2c2743');
  ribbon(g, points, p => top(p) + .10, p => reflection(p) - .04, '#9ebbd0');
  ribbon(g, points, p => top(p) + .13, p => top(p) + .24 + .11 * sin(p.d * .045 - t), '#595e80');
  ribbon(g, points, p => top(p) + .18, p => reflection(p) - .24, '#edfafb');
  ribbon(g, points, p => reflection(p) + .08, p => .71 + .19 * sin(p.d * .036 - t), '#655c8b');
  material(g,points,t,'large',p=>reflection(p)+.03,p=>.70+.15*sin(p.d*.036-t),.78);
  ribbon(g, points, p=>reflection(p)+.30, p=>reflection(p)+.45+.12*sin(p.d*.028+t*1.3),'#9b9cb8');
  thread(g, points, p => reflection(p) + .28, '#f7bbc8', 1.45);
  thread(g, points, p => .78 + .13 * sin(p.d * .02 + t), '#aee9fa', 1.7);
  for (let i = 2; i < points.length; i += 2) {
    const p = points[i];
    if (p.power < .17) continue;
    const radius = (.5 + p.power ** 2 * 3.4);
    g.fillStyle = '#292d47'; g.beginPath(); g.ellipse(p.x, p.y - .2 * p.h, radius * 1.3, radius, -.25, 0, tau); g.fill();
    g.fillStyle = '#f0ffff'; g.beginPath(); g.ellipse(p.x, p.y - .2 * p.h - .3 * radius, radius * .85, radius * .37, -.32, 0, tau); g.fill();
  }
}

function mycelium(g, points, t) {
  const stem = p => .22 * sin(p.d * .023 - t * .7);
  const fork = p => stem(p) + .60 * Math.max(0, sin(p.d * .024 + t * .45)) ** 2;
  ribbon(g,points,() => -.77,() => .77,'#263b52');
  material(g,points,t,'double',() => -.74,() => .74,.22);
  ribbon(g,points,p => stem(p)-.18,p => stem(p)+.14,'#55bfae');
  thread(g,points,fork,'#bcabcb',1.25);
  // Light travels inside the main filament without adding new branches.
  for (let i = 1; i < points.length; i++) {
    const a = points[i-1], b = points[i];
    if (b.power < .04) continue;
    g.beginPath(); g.moveTo(a.x,a.y+a.h*stem(a)); g.lineTo(b.x,b.y+b.h*stem(b));
    g.strokeStyle = 'rgba(228,255,218,' + Math.min(.9,b.power) + ')'; g.lineWidth = 2.5; g.stroke();
  }
}

function prism(g, points, t) {
  const shear = p => .81 + .40 * sin(p.d * .018 - t * 1.5) + p.power * .27;
  for (const [sign,color] of [[-1,'#ee7eacbd'],[1,'#54bcf0c7']]) {
    thread(g,points,p=>sign*(.9*shear(p)+.25*cos(p.d*.054-t*1.3)+.12*p.power),color,1.2);
  }
  for (const [shift, color] of [[1.05,'#3a2f81'],[.80,'#5365e3'],[.50,'#2fd6e1'],[.15,'#7e5dd9'],[-.22,'#e96fcf'],[-.62,'#ffb77d']]) {
    ribbon(g, points, p => shift * shear(p) - .22, p => shift * shear(p) + .22, color);
  }
  material(g,points,t,'jackpot',p=>-.59*shear(p),p=>-.12*shear(p),.75);
  for (let i = 0; i < points.length - 3; i += 3) {
    const a = points[i], b = points[i + 3], sign = i % 2 === 0 ? 1 : -1;
    const gleam = .5 + .5 * sin(a.d * .045 - t * 2 + a.kick);
    g.beginPath(); g.moveTo(a.x, a.y - a.h * .59 * shear(a)); g.lineTo(b.x,b.y + b.h * .66 * shear(b));
    g.lineTo(b.x,b.y + sign * b.h * .14); g.closePath();
    g.fillStyle='rgba(249,250,255,' + (.12 + .42 * gleam) + ')'; g.fill();
  }
  thread(g, points, p => .18 * sin(p.d * .037 + t), '#ffedfd', .9);
}

const painters = { aurora: velvet, loom, mercury, mycelium, prism };
export function drawWave(g, w, h, {style, time, hits, wide = 1, split = false, colors = ['#8e59eb','#4ea7df'], preview = false}) {
  if (isKineticWave(style)) {
    drawKineticWave(g,w,h,{style,time,hits,wide,split,preview});
    return;
  }
  g.clearRect(0, 0, w, h);
  const amplitude = style === 'classic'
    ? (preview ? h * .19 : 60 * (.6 - .4 * cos(time * .8)) * wide)
    : (preview ? h * .115 : 25 * (.84 - .16 * cos(time * .6)) * (1 + .4 * (wide - 1)));
  const materialWidth = preview ? Math.min(16, h * .19) : Math.min(20, 13 + w / 180);
  const ranges = split ? [[0,215/480,.05,3.1],[265/480,1,.042,-2.6]] : [[0,1,.03,1.6]];
  g.lineJoin = 'round'; g.lineCap = 'round';
  for (const [part, [first, last, frequency, speed]] of ranges.entries()) {
    const steps = Math.max(30, Math.min(260, Math.ceil(w * (last-first) / 4)));
    const points = [];
    for (let i = 0; i <= steps; i++) {
      const u = first + (last - first) * i / steps, x = u * w;
      const edge = Math.min(1, Math.min(u - first, last - u) * 480 / 50);
      let power = 0, kick = 0;
      for (const hit of hits) {
        const age = time - hit.time;
        if (age < 0 || age > 1.35) continue;
        const front = (Math.abs(u - hit.u) - age * .72 * (hit.speed ?? 1)) / .075;
        const envelope = Math.exp(-front * front) * (1 - age / 1.35) * (hit.power ?? 1);
        power += envelope; kick += envelope * cos(front * 2.2) * (hit.polarity ?? 1);
      }
      power = Math.min(1.2, power); kick = Math.max(-1.2, Math.min(1.2, kick));
      const base = sin(u * 480 * frequency - time * speed) * amplitude;
      const y = h / 2 + edge * (base + kick * 2);
      const breadth = materialWidth * (.83 + .2 * sin(x * .025 - time * 1.2)) * (.38 + .62 * edge);
      points.push({x, y, d:x, h:breadth * (1 + power * .10), power, kick});
    }
    if (painters[style]) {
      // Keep a visible cross-section where a tall sine turns steep on phones.
      // Limit it near crests so the ribbon stays inside the canvas.
      for (let i = 0; i < points.length; i++) {
        const p = points[i], left=points[Math.max(0,i-1)], right=points[Math.min(points.length-1,i+1)];
        const slope = (right.y-left.y)/Math.max(1,right.x-left.x);
        p.h = Math.min(p.h * Math.min(2.7,Math.hypot(1,slope)), Math.max(1,Math.min(p.y,h-p.y)-1) * .57);
      }
      painters[style](g, points, time);
    } else thread(g, points, flat, colors[part], Math.max(2, Math.min(w / 250, 4)));
  }
}
