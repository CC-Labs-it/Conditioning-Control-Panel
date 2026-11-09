/* Daily Daze's real cabinet and foreground hardware are baked from the Backroom
 * GLB. All moving pieces use the same projected centre and physical bulb positions. */
import { machine } from '../../../assets/prize-wheel/geometry.js';
import { FIELD_BY_ID, FIELD_WIDTH, FIELD_HEIGHT, FIELD_COLUMNS, FIELD_FRAMES, FIELD_FPS } from '../../../assets/prize-wheel/fields.js';
const TAU = 2 * Math.PI;
const radians = degrees => degrees * Math.PI / 180;
const art = name => new URL('../../../assets/prize-wheel/' + name + '.webp', import.meta.url).href;
const CHASE = [[255, 88, 163], [175, 98, 255], [89, 189, 255], [92, 241, 205], [255, 209, 92], [255, 118, 100]];
const padding = 24;
const css = `
  .dk-b:has(> .tc-prize-wheel) { overflow:visible; }
  .tc-prize-wheel { position:relative; width:100%; container-type:inline-size; color:#fff2d1; text-align:center; }
  .tc-wheel-stage { position:relative; width:100%; aspect-ratio:272/324; isolation:isolate; }
  .tc-wheel-stage > img, .tc-wheel-stage > canvas { position:absolute; inset:0; width:100%; height:100%; pointer-events:none; }
  .tc-wheel-input { position:absolute; inset:0; width:100%; height:100%; pointer-events:none; touch-action:none; }
  .tc-wheel-grip { pointer-events:stroke; touch-action:none; }
  .tc-wheel-grip[data-ready] { cursor:grab; }
  .tc-wheel-grip[data-held] { cursor:grabbing; }
  .tc-wheel-stage > .tc-wheel-aura { inset:-7.4074% -8.8235%; width:117.647%; height:114.815%; }
  .tc-wheel-title, .tc-wheel-result { position:absolute; left:50%; transform:translate(-50%,-50%); display:grid; place-items:center;
    margin:0; border:0; color:#fff3da; font:800 clamp(8px,3.65cqw,11px)/1 system-ui,sans-serif; white-space:nowrap; pointer-events:none; }
  .tc-wheel-title { top:6.75%; letter-spacing:.1em; text-shadow:0 0 3px #ff66ba; }
  .tc-wheel-result { top:81.36%; min-width:25%; padding:2px 4px; border-radius:2px; background:#27162c; font-size:clamp(8px,3.5cqw,11px); }
  .tc-prize-wheel .tc-wheel-spin { position:absolute; left:50%; top:91.4%; transform:translate(-50%,-50%); min-height:26px;
    width:47%; padding:3px 4px; border:1px solid #4e204b; border-radius:5px;
    background:linear-gradient(#ffefbf,#ffc66b 62%,#f5a562); color:#38213c;
    box-shadow:inset 0 0 0 1px #fff2ba,0 2px 0 #6b3250; font:800 clamp(10px,4.8cqw,14px)/1.2 system-ui,sans-serif; cursor:pointer; }
  .tc-prize-wheel .tc-wheel-spin:disabled { background:linear-gradient(#f2c1dc,#d58ab7); color:#441c3e; box-shadow:inset 0 0 0 1px #f4cfde,0 2px 0 #6b3250; cursor:default; }
  .tc-prize-wheel .tc-wheel-spin:focus-visible { outline:3px solid #5ef1e0; outline-offset:2px; }
  .tc-prize-wheel .tc-wheel-spin::before { content:''; position:absolute; inset:-7px 0; }
  .tc-wheel-base { display:table; margin:-1.8% auto 0; position:relative; padding:2px 8px 3px; border:1px solid #a75883; border-radius:0 0 6px 6px;
    background:#332037; color:#ffebcd; font:650 clamp(10px,4cqw,12px)/1.18 system-ui,sans-serif; font-variant-numeric:tabular-nums; }
  .tc-wheel-base:empty { display:none; }
  html[data-bubble-type] .tc-prize-wheel :is(.tc-wheel-title,.tc-wheel-result,.tc-wheel-spin,.tc-wheel-base) {
    font-family:var(--game-bubble-font,system-ui,sans-serif); font-weight:700;
  }
  html[data-bubble-type] .tc-wheel-title {
    color:var(--game-bubble-fill,#fff3da); -webkit-text-stroke:1.1px var(--game-bubble-edge,#38213c); paint-order:stroke fill;
  }
  html[data-bubble-type] .tc-wheel-result { color:var(--game-bubble-alt,#fff3da); }
`;
function arcSlice(g, radius, start, end) {
  g.beginPath();
  g.moveTo(0, 0);
  g.arc(0, 0, radius, start, end);
  g.closePath();
}
function paintPhoto(g, photo, radius, mid, half) {
  const w = photo.naturalWidth || photo.width, h = photo.naturalHeight || photo.height;
  if (!(w > 0 && h > 0)) return;
  const outer = radius * .93, inner = radius * .34, spread = half - .095;
  g.save();
  g.rotate(mid + Math.PI / 2);
  const from = -Math.PI / 2 - spread, to = -Math.PI / 2 + spread;
  g.beginPath(); g.arc(0, 0, outer, from, to);
  g.arc(0, 0, inner, to, from, true); g.closePath();
  g.strokeStyle = '#efcf92'; g.lineWidth = 1.7; g.stroke(); g.clip();
  const width = 2 * outer * Math.sin(spread);
  const height = outer - inner * Math.cos(spread);
  const ratio = Math.max(width / w, height / h);
  const sw = width / ratio, sh = height / ratio;
  g.drawImage(photo, (w-sw)/2, (h-sh)*.4, sw, sh, -width/2, -outer, width, height);
  g.restore();
}

function drawPrizeLabel(g, sector, mid, hasPhoto, family) {
  const jackpot = sector.id === 'jackpot';
  const narrow = sector.weight <= 8;
  const word = sector.effect === 'words' ? sector.trigger : '';
  // The 4% wedge has enough length for a single line even at the pointer.
  // The other word slices can fit a second line while keeping the prize larger.
  const single = jackpot && word;
  const stacked = word && !single;
  const distance = machine.faceRadius * (single ? .59 : hasPhoto ? .49 : narrow ? .65 : sector.id === 'nothing' ? .59 : .65);
  g.save();
  g.translate(Math.cos(mid) * distance, Math.sin(mid) * distance);
  if (narrow) g.rotate(mid + (Math.cos(mid) < 0 ? Math.PI : 0));
  const mainFont = single ? `800 10.5px ${family}` : `800 ${narrow ? 12 : 14}px ${family}`;
  const wordFont = `800 ${narrow ? 8 : 9}px system-ui,sans-serif`;
  g.font = mainFont;
  const prizeWidth = g.measureText(sector.label).width;
  g.font = wordFont;
  const triggerWidth = word ? g.measureText(word).width : 0;
  const width = single ? 52 : Math.max(prizeWidth, triggerWidth) + (narrow ? 5 : 8);
  const height = single ? 11.7 : stacked ? (narrow ? 20.5 : 27) : 18;
  g.beginPath();
  g.roundRect(-width/2, -height/2, width, height, height/2);
  g.fillStyle = jackpot ? '#4c2814' : '#261a32';
  g.fill();
  g.lineWidth = .8;
  g.strokeStyle = jackpot ? '#ffdf82' : '#f4d4d181';
  g.stroke();
  g.font = mainFont;
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  g.fillStyle = jackpot ? '#ffe596' : '#fff7e7';
  g.fillText(sector.label, single ? -15.7 : 0, stacked ? -4.1 : .4, single ? 16.7 : width-3);
  if (word) {
    g.font = wordFont;
    g.fillStyle = jackpot ? '#fff4cd' : '#f8cce7';
    g.fillText(word, single ? 9 : 0, stacked ? (narrow ? 5.3 : 7) : .4, single ? 29.5 : width-4);
  }
  g.restore();
}

function drawPrizes(g, slices, fields, frame, pictures, angle, labelFamily) {
  const radius = machine.faceRadius;
  g.save();
  g.translate(...machine.center);
  for (const s of slices) {
    const start = radians(s.start) + angle - Math.PI / 2;
    const end = radians(s.end) + angle - Math.PI / 2;
    const mid = (start + end) / 2;
    const cell = FIELD_BY_ID.get(s.id);
    const photo = s.effect === 'picture' ? pictures.get(s.id) : null;
    g.save();
    arcSlice(g, radius, start, end);
    g.fillStyle = cell.color[1];
    g.fill();
    g.clip();
    if (fields.complete && fields.naturalWidth) {
      const half = (end-start)/2;
      const left = Math.cos(half)*machine.hubRadius*.93;
      const halfHeight = radius * Math.sin(half) + 1;
      const sx = (frame % FIELD_COLUMNS) * FIELD_WIDTH;
      const sy = Math.floor(frame / FIELD_COLUMNS) * FIELD_HEIGHT + cell.top;
      g.save();
      g.rotate(mid);
      g.drawImage(fields, sx, sy, FIELD_WIDTH, cell.height, left, -halfHeight, radius-left+1, 2*halfHeight);
      g.restore();
    }
    const shine = g.createRadialGradient(0, 0, 24, 0, 0, radius);
    shine.addColorStop(0, '#2515346b');
    shine.addColorStop(.46, '#ffffff00');
    shine.addColorStop(.89, '#ffffff10');
    shine.addColorStop(1, '#32142665');
    g.fillStyle = shine;
    g.fillRect(-radius, -radius, radius * 2, radius * 2);
    if (photo) paintPhoto(g, photo, radius, mid, (end-start)/2);
    g.restore();
    arcSlice(g, radius, start, end);
    g.lineWidth = 1.15;
    g.strokeStyle = '#644553';
    g.stroke();
    g.beginPath();
    g.moveTo(Math.cos(start) * 22, Math.sin(start) * 22);
    g.lineTo(Math.cos(start) * radius, Math.sin(start) * radius);
    g.strokeStyle = '#f4d396';
    g.lineWidth = .7;
    g.stroke();
    drawPrizeLabel(g, s, mid, !!photo, labelFamily);
  }
  g.beginPath();
  g.arc(0, 0, radius, 0, TAU);
  g.lineWidth = 1.5;
  g.strokeStyle = '#f7c879';
  g.stroke();
  g.restore();
}
function drawHub(g, turn) {
  const r = machine.hubRadius;
  g.save();
  g.translate(...machine.center);
  g.beginPath();
  g.arc(0, 0, r, 0, TAU);
  g.fillStyle = '#281833';
  g.fill();
  g.lineWidth = 2;
  g.strokeStyle = '#f4cf8f';
  g.stroke();
  g.beginPath();
  g.arc(0, 0, r - 1, 0, TAU);
  g.clip();
  for (const [i, color] of ['#ff76bc', '#a988fa', '#ffe490'].entries()) {
    g.beginPath();
    for (let k = 0; k <= 48; k++) {
      const f = k / 48,
        a = turn + i * TAU / 3 + 6.6 * Math.pow(f, .86);
      g.lineTo(Math.cos(a) * f * r, Math.sin(a) * f * r);
    }
    for (let k = 48; k >= 0; k--) {
      const f = k / 48,
        a = turn + i * TAU / 3 + 6.6 * Math.pow(f, .86) + 1.44;
      g.lineTo(Math.cos(a) * f * r, Math.sin(a) * f * r);
    }
    g.closePath();
    g.fillStyle = color;
    g.fill();
  }
  g.restore();
}
function chaseColor(position) {
  const t = ((position % CHASE.length) + CHASE.length) % CHASE.length;
  const index = Math.floor(t);
  const next = CHASE[(index + 1) % CHASE.length];
  return CHASE[index].map((v, i) => v + (next[i] - v) * (t - index));
}
function drawLamps(g, phase, energy) {
  const count = machine.bulbs.length;
  machine.bulbs.forEach(([x, y], i) => {
    const position = i / count * CHASE.length;
    const rest = chaseColor(position),
      chase = chaseColor(position + phase * .44);
    const rgb = rest.map((v, j) => Math.round(v + (chase[j] - v) * energy)).join(',');
    const pulse = (.5 + .5 * Math.cos(i / count * TAU * 3 - phase * 2.3)) ** 2;
    const heat = .38 + energy * (.27 + .35 * pulse);
    const glow = g.createRadialGradient(x, y, 2, x, y, 7 + energy * 3);
    glow.addColorStop(0, `rgba(${rgb},${heat * .85})`);
    glow.addColorStop(.58, `rgba(${rgb},${heat * .29})`);
    glow.addColorStop(1, `rgba(${rgb},0)`);
    g.fillStyle = glow;
    g.fillRect(x - 11, y - 11, 22, 22);
    const bulb = g.createRadialGradient(x - 1.7, y - 2, 0, x, y, 5.3);
    bulb.addColorStop(0, '#fff8de');
    bulb.addColorStop(.26, `rgba(${rgb},.97)`);
    bulb.addColorStop(.68, `rgba(${rgb},.91)`);
    bulb.addColorStop(1, `rgba(${rgb},.12)`);
    g.fillStyle = bulb;
    g.beginPath();
    g.arc(x, y, 5.45, 0, TAU);
    g.fill();
  });
}
function drawAura(g, phase, energy, direction) {
  if (energy <= 0) return;
  g.save();
  g.translate(machine.center[0] + padding, machine.center[1] + padding);
  const radius = machine.faceRadius;
  for (const [i, c] of ['255,103,188', '152,104,248', '92,232,231', '255,189,103', '194,120,239', '114,223,243'].entries()) {
    const gradient = g.createRadialGradient(0, 0, radius, 0, 0, radius * 1.57);
    gradient.addColorStop(0, `rgba(${c},0)`);
    gradient.addColorStop(.42, `rgba(${c},${.84 * energy})`);
    gradient.addColorStop(1, `rgba(${c},0)`);
    g.fillStyle = gradient;
    g.beginPath();
    for (let side = 0; side < 2; side++) for (let k = 0; k <= 52; k++) {
      const f = (side ? 52 - k : k) / 52;
      // The outer tip trails the rotor in either direction.
      const a = phase * .72 + i * TAU / 6 - direction * 2.05 * f + side * .14 * Math.sin(f * Math.PI);
      const r = radius * (1.08 + .5 * f);
      g.lineTo(Math.cos(a) * r, Math.sin(a) * r);
    }
    g.closePath();
    g.fill();
    for (let j = 0; j < 3; j++) {
      const a = phase * .56 + i * TAU / 6 - direction * j * .13;
      const r = radius * (1.25 + .12 * Math.sin(i + phase + j * .8));
      const x = Math.cos(a) * r,
        y = Math.sin(a) * r;
      const size = (1.1 + j * .55) * energy;
      g.fillStyle = `rgba(${c},${.9 * energy})`;
      g.beginPath();
      g.moveTo(x, y - size * 1.8);
      g.quadraticCurveTo(x, y, x + size * 1.4, y);
      g.quadraticCurveTo(x, y, x, y + size * 1.8);
      g.quadraticCurveTo(x, y, x - size * 1.4, y);
      g.quadraticCurveTo(x, y, x, y - size * 1.8);
      g.fill();
    }
  }
  g.restore();
}
export function createPrizeWheelView(ctx, host, sectors) {
  ctx.css(css);
  const doc = host.ownerDocument;
  const root = doc.createElement('div');
  root.className = 'tc-prize-wheel';
  root.dataset.widget = 'prize-wheel';
  root.dataset.dockSurface = '';
  host.appendChild(root);
  const stage = doc.createElement('div');
  stage.className = 'tc-wheel-stage';
  root.appendChild(stage);
  function canvas(className, w, h) {
    const c = doc.createElement('canvas');
    c.className = className;
    c.width = w * 2;
    c.height = h * 2;
    c.setAttribute('aria-hidden', 'true');
    stage.appendChild(c);
    const g = c.getContext('2d');
    g.scale(2, 2);
    return g;
  }
  function layer(name) {
    const image = doc.createElement('img');
    image.src = art(name);
    image.alt = '';
    image.draggable = false;
    stage.appendChild(image);
    return image;
  }
  const aura = canvas('tc-wheel-aura', machine.width + padding * 2, machine.height + padding * 2);
  layer('cabinet');
  const face = canvas('tc-wheel-face', machine.width, machine.height);
  const hardware = canvas('tc-wheel-hardware', machine.width, machine.height);
  layer('trim');
  const ns = 'http://www.w3.org/2000/svg';
  const input = doc.createElementNS(ns,'svg');
  input.setAttribute('class','tc-wheel-input');
  input.setAttribute('viewBox',`0 0 ${machine.width} ${machine.height}`);
  input.setAttribute('aria-hidden','true');
  const grip = doc.createElementNS(ns,'circle');
  grip.setAttribute('class','tc-wheel-grip');
  grip.setAttribute('data-dock-interactive','');
  grip.setAttribute('cx',machine.center[0]); grip.setAttribute('cy',machine.center[1]);
  grip.setAttribute('r',(machine.faceRadius + machine.hubRadius)/2);
  grip.setAttribute('fill','none'); grip.setAttribute('stroke','transparent');
  grip.setAttribute('stroke-width',machine.faceRadius - machine.hubRadius);
  const hint = doc.createElementNS(ns,'title');
  hint.textContent = 'Drag the wheel and release to spin';
  grip.append(hint); input.append(grip); stage.append(input);
  const title = doc.createElement('span');
  title.className = 'tc-wheel-title';
  title.textContent = 'DAILY DAZE';
  const result = doc.createElement('output');
  result.className = 'tc-wheel-result';
  result.setAttribute('aria-live', 'polite');
  result.setAttribute('aria-atomic', 'true');
  const spinButton = doc.createElement('button');
  spinButton.className = 'tc-wheel-spin';
  spinButton.type = 'button';
  spinButton.setAttribute('aria-description', sectors.map(s =>
    [s.label,s.trigger].filter(Boolean).join(' ') + ' at ' + s.weight + '%').join(', '));
  const baseLabel = doc.createElement('small');
  baseLabel.className = 'tc-wheel-base';
  stage.append(title, result, spinButton);
  root.appendChild(baseLabel);
  const fields = new doc.defaultView.Image();
  let pictures = new Map(), fieldTime = 0, fieldFrame = 0, drawnFrame = -1;
  let pose = { angle:0, sweep:0, velocity:0 }, drawn = null;
  const family = () => doc.defaultView.getComputedStyle(title).fontFamily;
  let labelFamily = family();
  function setPose(next, force = false) {
    pose = next;
    const sameHardware = drawn && drawn.angle === next.angle && drawn.velocity === next.velocity && drawn.sweep === next.sweep;
    if (!force && sameHardware && fieldFrame === drawnFrame) return;
    const angle = radians(next.angle);
    face.clearRect(0, 0, machine.width, machine.height);
    drawPrizes(face, sectors, fields, fieldFrame, pictures, angle, labelFamily);
    drawnFrame = fieldFrame;
    if (sameHardware) return;
    drawn = next;
    // An interrupted spin also clears the full halo when motion is disabled.
    aura.clearRect(0, 0, machine.width + padding * 2, machine.height + padding * 2);
    hardware.clearRect(0, 0, machine.width, machine.height);
    const energy = Math.sqrt(Math.min(1, Math.abs(next.velocity) / .015));
    const phase = energy ? radians(next.sweep) * .42 : 0;
    drawAura(aura, phase, energy, Math.sign(next.velocity));
    drawHub(hardware, angle + .7 * Math.sin(phase) * energy);
    drawLamps(hardware, phase, energy);
    root.dataset.wheelAngle = String(next.angle);
  }
  // Reprint the labels immediately even when motion is held between frames.
  const fontObserver = new doc.defaultView.MutationObserver(() => {
    labelFamily = family();
    setPose(pose, true);
  });
  fontObserver.observe(doc.documentElement, {attributes:true, attributeFilter:['data-bubble-type']});
  fields.onload = () => setPose(pose, true);
  fields.src = art('fields');
  setPose(pose, true);
  return {
    setPose,
    advanceFields(dt) {
      fieldTime = (fieldTime + dt) % (FIELD_FRAMES / FIELD_FPS);
      const next = Math.floor(fieldTime * FIELD_FPS);
      if (fieldFrame === next) return false;
      fieldFrame = next;
      return true;
    },
    setPictures(mapping) {
      pictures = new Map(mapping);
      setPose(pose, true);
    },
    portrait(id) {
      const sector = sectors.find(item => item.id === id);
      if (!sector) return null;
      const box = stage.getBoundingClientRect();
      if (!box.width || !box.height) return null;
      const mid = radians((sector.start + sector.end)/2 + pose.angle - 90);
      const r = machine.faceRadius * .66;
      const sx = box.width/machine.width, sy = box.height/machine.height;
      return {
        x: box.left + (machine.center[0] + Math.cos(mid)*r)*sx,
        y: box.top + (machine.center[1] + Math.sin(mid)*r)*sy,
        w: Math.max(18, 2*r*Math.sin(radians((sector.end-sector.start)/2)))*sx,
        h: machine.faceRadius*.52*sy,
      };
    },
    spinButton,
    grip,
    result,
    baseLabel,
    dispose() {
      fontObserver.disconnect();
      fields.onload = null;
      pictures.clear();
    }
  };
}
