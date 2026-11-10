/* ui/unlocks/fate/outside/art.js - the Go Outside cards' drawings: waxy crayon on paper cutouts, chunky dark ink, the same
 * hand as the Mystery Box (ui/unlocks/basics/box-fx.js). Static pieces are baked once into SVG pictures (the filters cost
 * nothing after the first paint); pieces whose colour follows the clock (clouds, hill, sun, moon) are plain inline shapes
 * without filters, cheap to move and recolour. Nothing here touches the DOM at import time. */

export const INK = '#2a2130';
export const PAPER = '#fffdf5';

const FILTERS = (() => {
  const mask = (k, b) => `<feColorMatrix in="n" type="matrix" values="0 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 ${k} ${b}" result="m"/>`;
  const wob = (seed, f, s) => `<feTurbulence type="fractalNoise" baseFrequency="${f}" numOctaves="2" seed="${seed}" result="w"/><feDisplacementMap in="SourceGraphic" in2="w" scale="${s}" xChannelSelector="R" yChannelSelector="G" result="s"/>`;
  return `<filter id="cut" x="-20%" y="-20%" width="140%" height="140%"><feTurbulence type="turbulence" baseFrequency="0.04" numOctaves="1" seed="9" result="w"/><feDisplacementMap in="SourceGraphic" in2="w" scale="3.5" xChannelSelector="R" yChannelSelector="G"/></filter>`
    + `<filter id="wax" x="-20%" y="-20%" width="140%" height="140%">${wob(3, 0.05, 2.2)}<feTurbulence type="fractalNoise" baseFrequency="0.08 1.1" numOctaves="2" seed="6" result="n"/>${mask(-3.2, 2.6)}<feComposite in="s" in2="m" operator="in"/></filter>`
    + `<filter id="line" x="-20%" y="-20%" width="140%" height="140%">${wob(12, 0.06, 2)}<feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="1" seed="5" result="n"/>${mask(-2.4, 2.2)}<feComposite in="s" in2="m" operator="in"/></filter>`;
})();

const svg = (vb, body) => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${vb}"><defs>${FILTERS}</defs>${body}</svg>`;
export const url = (s) => `data:image/svg+xml;charset=utf-8,${encodeURIComponent(s)}`;
const cutout = (d, w = 12) => `<g filter="url(#cut)"><path d="${d}" fill="#d8cfb8" stroke="#d8cfb8" stroke-width="${w + 1.2}" stroke-linejoin="round"/><path d="${d}" fill="${PAPER}" stroke="${PAPER}" stroke-width="${w}" stroke-linejoin="round"/></g>`;
const inked = (d, w = 3.4) => `<g filter="url(#line)"><path d="${d}" fill="none" stroke="${INK}" stroke-width="${w}" stroke-linejoin="round" stroke-linecap="round"/><path d="${d}" transform="translate(.7 -.5)" fill="none" stroke="${INK}" stroke-width="${(w * 0.45).toFixed(2)}" stroke-linejoin="round" stroke-linecap="round" opacity=".55"/></g>`;
const wax = (d, fill, extra = '') => `<g filter="url(#wax)"><path d="${d}" fill="${fill}" ${extra}/></g>`;

/* ---- A Window: 160 x 140, the pane is the hole at PANE ---- */
export const WIN_W = 160, WIN_H = 140;
export const PANE = Object.freeze({ x: 15, y: 13, w: 130, h: 98 });

let frameCache = null;
/** The wooden frame, mullions and sill as one picture. The pane is a hole: the sky shows through it. */
export function windowFrameUrl() {
  if (frameCache) return frameCache;
  const { x, y, w, h } = PANE;
  const outer = `M5 4 L155 3 L156 116 L4 117 Z`;
  const hole = `M${x} ${y} L${x + w} ${y + 0.5} L${x + w - 0.5} ${y + h} L${x + 0.5} ${y + h - 0.4} Z`;
  const sill = `M-2 113 L162 111 L160 128 L0 129 Z`;
  const midX = x + w / 2, midY = y + h / 2;
  const barV = `M${midX - 3} ${y} L${midX + 3} ${y} L${midX + 3.4} ${y + h} L${midX - 2.6} ${y + h} Z`;
  const barH = `M${x} ${midY - 3} L${x + w} ${midY - 2.4} L${x + w} ${midY + 3.2} L${x} ${midY + 3} Z`;
  /* grain strokes on the frame and the sill */
  const grain = [`M9 20 Q8 60 10 104`, `M151 22 Q152 62 150 106`, `M24 8 Q80 6 138 8`, `M10 121 Q80 119 152 121`].join(' ');
  /* a hook for the latch */
  const latch = `M${midX + 6} ${midY + 7} q4 1 4 5`;
  frameCache = url(svg('-6 -6 172 152',
    wax(`${outer} ${hole}`, '#c99b6d', 'fill-rule="evenodd"')
    + wax(barV, '#c99b6d') + wax(barH, '#c99b6d')
    + `<path d="${barV}" fill="#5a3a22" opacity=".12"/>`
    + wax(sill, '#b4835a') + `<path d="${sill}" fill="#3a2414" opacity=".14"/>`
    + `<g opacity=".35">${wax(grain, 'none', `stroke="#6b4426" stroke-width="1.6" stroke-linecap="round"`)}</g>`
    /* the glass: a faint sheen in two corners, so it reads as a window and not a picture */
    + `<path d="M${x + 6} ${y + 26} L${x + 22} ${y + 6}" stroke="#fff" stroke-width="3" stroke-linecap="round" opacity=".5"/>`
    + `<path d="M${midX + 10} ${midY + 30} L${midX + 22} ${midY + 16}" stroke="#fff" stroke-width="2.4" stroke-linecap="round" opacity=".38"/>`
    + inked(`${outer} ${hole}`, 3.4) + inked(barV, 2.4) + inked(barH, 2.4) + inked(sill, 3.2)
    + inked(latch, 2),
  ));
  return frameCache;
}

let backCache = null;
/** The paper cutout behind the whole window (drawn under the sky, so the pane stays clear). Same box as the frame. */
export function windowBackUrl() {
  backCache ||= url(svg('-6 -6 172 152', cutout('M5 4 L155 3 L156 111 L162 111 L160 128 L0 129 L-2 113 L4 113 Z', 11)));
  return backCache;
}

/* ---- inline shapes the clock recolours ---- */
const NS = 'http://www.w3.org/2000/svg';
function svgEl(doc, tag, attrs = {}) {
  const e = doc.createElementNS(NS, tag);
  for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, v);
  return e;
}

/** A crayon cloud, 60 x 26: a lumpy fill with a soft ink edge. Returns {el, setColour(c)}. */
export function cloud(doc, scale = 1) {
  const s = svgEl(doc, 'svg', { viewBox: '0 0 60 26', width: String(60 * scale), height: String(26 * scale) });
  s.style.cssText = 'position:absolute;left:0;top:0;overflow:visible;';
  const d = 'M6 22 Q-1 21 3 15 Q4 9 11 11 Q13 3 22 5 Q28 -1 35 4 Q42 1 45 8 Q54 7 55 14 Q60 19 53 22 Z';
  const body = svgEl(doc, 'path', { d, fill: '#fff', stroke: 'rgba(42,33,48,.28)', 'stroke-width': '1.6', 'stroke-linejoin': 'round' });
  const shade = svgEl(doc, 'path', { d: 'M8 21 Q20 17 30 20 Q42 17 52 21', fill: 'none', stroke: 'rgba(42,33,48,.12)', 'stroke-width': '2.4', 'stroke-linecap': 'round' });
  s.append(body, shade);
  return { el: s, setColour(c) { body.setAttribute('fill', c); } };
}

/** The hill along the pane's bottom with one round tree. Returns {el, setColour(c)}. */
export function hill(doc, w, h) {
  const s = svgEl(doc, 'svg', { viewBox: `0 0 ${w} ${h}`, width: String(w), height: String(h), preserveAspectRatio: 'none' });
  s.style.cssText = 'position:absolute;left:0;bottom:0;';
  const far = svgEl(doc, 'path', { d: `M0 ${h * 0.52} Q${w * 0.3} ${h * 0.18} ${w * 0.62} ${h * 0.46} Q${w * 0.82} ${h * 0.6} ${w} ${h * 0.38} L${w} ${h} L0 ${h} Z`, fill: '#5f8a4c', opacity: '.65' });
  const near = svgEl(doc, 'path', { d: `M0 ${h * 0.78} Q${w * 0.4} ${h * 0.48} ${w * 0.7} ${h * 0.72} Q${w * 0.88} ${h * 0.84} ${w} ${h * 0.7} L${w} ${h} L0 ${h} Z`, fill: '#6fa552', stroke: 'rgba(42,33,48,.35)', 'stroke-width': '1.4' });
  const trunk = svgEl(doc, 'path', { d: `M${w * 0.25} ${h * 0.64} L${w * 0.25} ${h * 0.4}`, stroke: 'rgba(42,33,48,.7)', 'stroke-width': '2.2', 'stroke-linecap': 'round' });
  const crown = svgEl(doc, 'circle', { cx: String(w * 0.25), cy: String(h * 0.33), r: String(h * 0.14), fill: '#4f7e3c', stroke: 'rgba(42,33,48,.45)', 'stroke-width': '1.4' });
  s.append(far, near, trunk, crown);
  return {
    el: s,
    setColour(c, dark) { near.setAttribute('fill', c); far.setAttribute('fill', c); crown.setAttribute('fill', dark); },
  };
}

/** A sun with a crayon ring (r 11) and a moon crescent (r 9): {sun, moon}. */
export function bodies(doc) {
  const sun = svgEl(doc, 'svg', { viewBox: '-14 -14 28 28', width: '28', height: '28' });
  sun.style.cssText = 'position:absolute;left:0;top:0;overflow:visible;';
  sun.append(
    svgEl(doc, 'circle', { r: '12.5', fill: '#ffe9a0', opacity: '.55' }),
    svgEl(doc, 'circle', { r: '9', fill: '#ffd34d', stroke: 'rgba(160,90,20,.6)', 'stroke-width': '1.6', 'stroke-dasharray': '7 2.2' }),
  );
  const moon = svgEl(doc, 'svg', { viewBox: '-12 -12 24 24', width: '24', height: '24' });
  moon.style.cssText = 'position:absolute;left:0;top:0;overflow:visible;';
  moon.append(svgEl(doc, 'path', { d: 'M3 -9 A9 9 0 1 0 8 6 A7.2 7.2 0 1 1 3 -9 Z', fill: '#f4f0d8', stroke: 'rgba(42,33,48,.35)', 'stroke-width': '1.2' }));
  return { sun, moon };
}

/** A small four-point star, size px. */
export function star(doc, size = 6) {
  const s = svgEl(doc, 'svg', { viewBox: '-5 -5 10 10', width: String(size), height: String(size) });
  s.style.cssText = 'position:absolute;left:0;top:0;';
  s.append(svgEl(doc, 'path', { d: 'M0 -5 L1.2 -1.2 L5 0 L1.2 1.2 L0 5 L-1.2 1.2 L-5 0 L-1.2 -1.2 Z', fill: '#fff8d8' }));
  return s;
}

/* ---- Fresh Air: what the breeze carries ---- */
const flyCache = new Map();
/** A leaf or a scrap of paper, about 26 px. kind: 0 green leaf, 1 yellow leaf, 2 paper scrap. */
export function flyerUrl(kind = 0) {
  if (flyCache.has(kind)) return flyCache.get(kind);
  let body;
  if (kind === 2) {
    const d = 'M6 8 L30 4 L33 24 L9 29 Z';
    body = cutout(d, 3) + wax(d, '#fbf6e8') + `<path d="M11 12 L27 9 M12 17 L29 14 M13 22 L24 20" stroke="#8ea6c8" stroke-width="1.4" stroke-linecap="round" opacity=".7"/>` + inked(d, 2);
  } else {
    const fill = kind === 1 ? '#d9c24a' : '#7fb25a';
    const d = 'M4 30 Q4 12 20 5 Q34 0 34 2 Q35 18 22 28 Q12 34 4 30 Z';
    const vein = 'M5 29 Q16 18 31 4 M14 21 L13 13 M20 16 L25 18 M24 11 L22 6';
    body = cutout(d, 3) + wax(d, fill) + `<path d="${d}" fill="#1d2a12" opacity=".12"/>` + inked(d, 2.2) + inked(vein, 1.3);
  }
  const u = url(svg('-2 -2 40 38', body));
  flyCache.set(kind, u);
  return u;
}
