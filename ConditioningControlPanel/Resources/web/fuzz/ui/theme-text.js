/* ui/theme-text.js - themed copy (owner 2026-11-10). Pure: no DOM, and never the run RNG.
 *
 * Before Pick a Theme a list reads plain. After it, about THEME_SHARE of the picks come from the picked theme's own lines
 * (data/content/theme-text.js) and the rest stay plain; a Second Theme joins the theme side. The consumer keeps drawing
 * its plain line exactly as before (same ctx.rng calls), and themed() decides with Math.random whether a theme line takes
 * its place, so RNG sequences never move. Themes are read on every call: a pick mid-run takes effect at once. */
import { THEME_TEXT } from '../data/content/theme-text.js';

export const THEME_SHARE = 0.6;

/** The run's themes: the picked one, then a live Second Theme, as keys of THEME_TEXT (as the bouncing copies read them). */
export function runThemes(run) {
  const first = run?.picks?.f5 || null;
  const second = (run?.owned?.taste2 || 0) > 0 ? run?.store?.taste2?.theme || null : null;
  return [first, second].filter((t, i, all) => t && Object.hasOwn(THEME_TEXT, t) && all.indexOf(t) === i);
}

/** Every theme line for a key. `sub` picks one entry per theme: a Golden prize key, or a Falling Words index. */
export function themeLines(themes, key, sub) {
  return themes.flatMap((t) => {
    const v = THEME_TEXT[t]?.[key];
    const x = sub === undefined ? v : v?.[sub];
    return Array.isArray(x) ? x : typeof x === 'string' ? [x] : [];
  });
}

/** The line to show: the consumer's plain pick, or (THEME_SHARE of the time, once a theme is picked) a theme line.
 * `fits` filters theme lines the same way the consumer filtered its plain list. */
export function themed(themes, key, plain, { sub, fits, rand = Math.random } = {}) {
  const lines = fits ? themeLines(themes, key, sub).filter(fits) : themeLines(themes, key, sub);
  if (!lines.length || !(rand() < THEME_SHARE)) return plain;
  return lines[Math.floor(rand() * lines.length)];
}

/** themed() for an unlock: wraps a draw function (a deck, a pick) and reads the run's themes on every draw. */
export const themedDraw = (ctx, key, draw, opts) => () => themed(runThemes(ctx.state), key, draw(), opts);
