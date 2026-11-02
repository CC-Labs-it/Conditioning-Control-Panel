/* ui/palette.js - the run's colours: the theme picked, mixed with a second one. Pure at import.
 *
 * paletteOf(run) answers { a, b, soft, ink, spiral: [c1, c2], themes: [...] }. `ink` reads on `a`, on `b`, on any mix of the
 * two and on `soft` (tests/palette.test.mjs holds that for every pair). applyPalette writes the --niche variables and, from
 * the pick on, the accents every panel already reads (--line, --fill, --stamp...). Those accents are !important on purpose:
 * a route tone or a Page Colour pot changes the page and never takes the theme's accents back.
 *
 * The page itself (--page, --ink, --panel) stays ordinary, so Page Colour, Dark Mode and a theme's own dark room still work.
 * Event 'palette' fires when the colours change. Page Colour's own event is 'page-colour'. */

export const THEME_IDS = Object.freeze(['trance', 'bimbo', 'drone', 'sissy', 'goon', 'locked', 'censored', 'infection']);

export const THEME_PALETTES = Object.freeze({
  trance: Object.freeze({ a: '#a58bff', soft: '#e6e0ff', ink: '#15123a', spiral: Object.freeze(['#8f7bff', '#8fd0ff']) }),
  bimbo: Object.freeze({ a: '#ff4fa8', soft: '#ffe0f0', ink: '#3a0726', spiral: Object.freeze(['#ff2f92', '#ffd1e8']) }),
  drone: Object.freeze({ a: '#3ddc84', soft: '#d8f5e6', ink: '#07150e', spiral: Object.freeze(['#3dff8e', '#8d99a1']) }),
  sissy: Object.freeze({ a: '#d98ad6', soft: '#f8e8f5', ink: '#3d1730', spiral: Object.freeze(['#e58ab0', '#c9a8ef']) }),
  goon: Object.freeze({ a: '#f2a33c', soft: '#fbe9cf', ink: '#2a1707', spiral: Object.freeze(['#f2a33c', '#b5342a']) }),
  locked: Object.freeze({ a: '#86b4e0', soft: '#dfeaf5', ink: '#0f1c29', spiral: Object.freeze(['#9fb3c6', '#e3b341']) }),
  censored: Object.freeze({ a: '#ff5a4d', soft: '#f9e2dc', ink: '#141414', spiral: Object.freeze(['#c2261f', '#141414']) }),
  infection: Object.freeze({ a: '#a4d83a', soft: '#e8f6d4', ink: '#132a08', spiral: Object.freeze(['#7fc247', '#ff2f92']) }),
});

/** The page each theme wears. Accents are not here: they come from the palette. */
const READY = 'color-mix(in srgb,var(--mix) 34%,var(--panel))';
const page = (p, ink, soft, faint, panel, panelShort) => Object.freeze({ '--page': p, '--ink': ink, '--ink-soft': soft, '--ink-faint': faint, '--panel': panel, '--panel-short': panelShort, '--ready': READY });
export const THEME_PAGES = Object.freeze({
  trance: page('#15123a', '#dfe4ff', '#a9b0e6', '#7d84c4', '#221e58', '#1a1749'),
  bimbo: page('#ffcde6', '#8a0f5c', '#a01f70', '#b23f88', '#fff0f7', '#ffd9ec'),
  drone: page('#10161a', '#dfe6ea', '#9aa8b0', '#6c7a82', '#1a2226', '#141a1e'),
  sissy: page('#fbeef0', '#7a2f4b', '#8f4a66', '#a5617c', '#fffafb', '#f6e0e6'),
  goon: page('#16110d', '#f7e6cf', '#d9c4a8', '#a8947c', '#241c15', '#1b1510'),
  locked: page('#1d2935', '#e4ecf3', '#c3d0dc', '#93a6b8', '#263544', '#1f2c39'),
  censored: page('#efece4', '#141414', '#3d3a34', '#6b675d', '#fffdf6', '#e2ded3'),
  infection: page('#e6f1e8', '#18342a', '#2f5245', '#567a6b', '#f4faf5', '#c9d8cc'),
});

/** Before a pick: grey, then the Colour row's pink, then whichever pot the player pressed. */
const POTS = Object.freeze({ pink: '#ff69b4', violet: '#9a80ff', teal: '#2cc4b4', black: '#ff9ccf' });
const GREY = Object.freeze({ a: '#8a867d', soft: '#e4e1da', ink: '#000000' });
const HOUSE_SPIRAL = Object.freeze(['#ff4fa8', '#6a3cff']);

/** Second Theme keeps its id in its own store; the store is in the save, so this holds across a reload. */
export const secondTheme = (run) => {
  const id = run?.off?.taste2 ? null : run?.store?.taste2?.theme;
  return THEME_PALETTES[id] && id !== run?.picks?.f5 ? id : null;
};

const hex = (s) => [1, 3, 5].map((i) => parseInt(s.slice(i, i + 2), 16));
export function mixHex(x, y, t = 0.5) {
  const a = hex(x), b = hex(y);
  return `#${a.map((v, i) => Math.round(v + (b[i] - v) * t).toString(16).padStart(2, '0')).join('')}`;
}

const cache = new Map();
function remember(key, make) {
  if (!cache.has(key)) cache.set(key, Object.freeze(make()));
  return cache.get(key);
}

export function paletteOf(run) {
  const first = run?.picks?.f5;
  const t = THEME_PALETTES[first];
  if (t) {
    const second = secondTheme(run);
    const u = second ? THEME_PALETTES[second] : t;
    return remember(`${first}+${second || ''}`, () => ({
      a: t.a, b: u.a, soft: mixHex(t.soft, u.soft), ink: t.ink,
      spiral: Object.freeze(second ? [t.spiral[0], u.spiral[0]] : [...t.spiral]),
      themes: Object.freeze(second ? [first, second] : [first]),
    }));
  }
  const pot = run?.store?.paint?.name;
  const a = POTS[pot] || (run?.owned?.colour ? POTS.pink : null);
  return remember(`none+${a || ''}`, () => (a
    ? { a, b: a, soft: mixHex(a, '#ffffff', 0.8), ink: '#1a0d16', spiral: HOUSE_SPIRAL, themes: Object.freeze([]) }
    : { ...GREY, b: GREY.a, spiral: HOUSE_SPIRAL, themes: Object.freeze([]) }));
}

/** Every theme's hue as a class: a theme lane's tile wears its own, everything else wears the mix. */
const HUES = THEME_IDS.map((id) => `.hue-${id}{--hue-a:${THEME_PALETTES[id].a};--hue-b:${THEME_PALETTES[id].a};}`).join('\n');
export const hueClass = (lane) => (THEME_PALETTES[lane] ? `hue-${lane}` : '');

/* From the pick on. These read --niche, so a second theme reaches every panel by itself. */
const ACCENTS = `:root{
--accent:var(--niche);--accent-2:var(--niche-2);--accent-soft:color-mix(in srgb,var(--niche-2) 62%,#fff);--accent-deep:color-mix(in srgb,var(--mix) 62%,var(--ink));--niche-wash:var(--niche-wash-now,16%);
--btn:var(--mix)!important;--btn-ink:var(--niche-ink)!important;
--btn-edge:color-mix(in srgb,var(--mix) 66%,#000)!important;
--btn-fill:linear-gradient(165deg,color-mix(in srgb,var(--niche) 76%,#fff),var(--niche) 46%,color-mix(in srgb,var(--niche-2) 90%,#000));
--line:color-mix(in srgb,var(--mix) 62%,var(--panel))!important;
--fill:color-mix(in srgb,var(--mix) 48%,var(--panel))!important;
--stamp:var(--niche-text)!important;
--toast-bg:linear-gradient(90deg,var(--niche),var(--niche-2));--toast-ink:var(--niche-ink);
}`;

export function paletteCss(p) {
  const base = `:root{--niche:${p.a};--niche-2:${p.b};--niche-soft:${p.soft};--niche-ink:${p.ink};}`;
  return [base, HUES, p.themes.length ? ACCENTS : ''].join('\n');
}

/** Writes the colours. Answers whether they changed. */
export function applyPalette(doc, run) {
  const p = paletteOf(run);
  let s = doc.getElementById('fz-palette');
  if (!s) {
    s = doc.createElement('style');
    s.id = 'fz-palette';
    doc.head.appendChild(s);
  }
  const css = paletteCss(p);
  if (s.textContent === css) return false;
  s.textContent = css;
  doc.body.classList.toggle('niched', p.themes.length > 0);
  doc.body.classList.toggle('mixed', p.themes.length > 1);
  if (p.themes[0]) doc.body.dataset.theme = p.themes[0];
  return true;
}

/** Keeps the page in step with the run. Colours change before the unlock that caused it mounts, so its mount reads them. */
export function bindPalette(engine, doc) {
  const sync = () => { if (applyPalette(doc, engine.run)) engine.emit('palette', paletteOf(engine.run)); };
  applyPalette(doc, engine.run);
  doc.defaultView?.setTimeout(() => doc.documentElement.classList.add('palette-live'), 100);
  for (const event of ['fork', 'taste2', 'buy', 'page-colour', 'switch']) engine.subscribe(event, sync);
}
