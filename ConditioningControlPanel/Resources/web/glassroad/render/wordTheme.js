/* One lettering identity from the road bubble to the popped word. Fonts are bundled; words and
 * song timing belong to the chart and are never replaced by a theme. No extra render passes. */
import { bubbleFace } from './wordBubblesMath.js';

const profile = (id, faces, stops, ink, halo, accent, echo, motif, tracking, glitter, stars) => Object.freeze({
  id, faces: Object.freeze(faces), fill: Object.freeze(stops.map((c, i) => Object.freeze([i / (stops.length - 1), c]))),
  ink, halo, accent, echo, motif, tracking, glitter, stars,
});

export const WORD_THEMES = Object.freeze({
  threshold: profile('threshold', ['editorial', 'display', 'rounded'],
    ['#fff7ff', '#ffc8ee', '#f963c5', '#885ff1', '#a8f8ef'], '#24122f', '#f963c5', '#f963c5', '#a8f8ef', 'prism', 0, 22, 3),
  keep: profile('keep', ['silk', 'display', 'editorial'],
    ['#fff6dc', '#f4c48b', '#e995b9', '#a72c73', '#ffe4a8'], '#290e25', '#e995b9', '#ed3e9d', '#f4c48b', 'engraved', 0.015, 12, 2),
  mainframe: profile('mainframe', ['mono', 'display', 'mono'],
    ['#f1ffe8', '#a4f870', '#4fda48', '#217c3b', '#b3ff78'], '#061309', '#52e256', '#52e256', '#c1df8a', 'terminal', 0.025, 0, 0),
  boudoir: profile('boudoir', ['silk', 'editorial', 'rounded'],
    ['#fffaf0', '#f7dae5', '#e9a2c4', '#a66caa', '#f6e3fc'], '#482338', '#e9a2c4', '#e9a2c4', '#dac1f1', 'satin', 0.005, 28, 4),
  goonworks: profile('goonworks', ['rounded', 'display', 'rounded'],
    ['#fffaff', '#ffb5e2', '#f52a9e', '#b90069', '#fff3ad'], '#25051b', '#f52a9e', '#f52a9e', '#fff3ad', 'comic', -0.025, 8, 2),
});

export function wordTheme(track = null, pal = null) {
  const id = typeof track === 'string' ? track : track?.theme || track?.id || pal?.theme;
  return WORD_THEMES[id] || WORD_THEMES.threshold;
}

export function wordFace(theme, row = 0) {
  const f = bubbleFace(row), kind = row === 5 ? 2 : row === 3 ? 1 : 0;
  return { face: theme.faces[kind], weight: theme.id === 'keep' || theme.id === 'boudoir' ? 700 : 1000,
    upper: f.upper || theme.id === 'mainframe' };
}

/** CSS colours for the rush. Preserve the level's identity even if its primary is very dark. */
export function wordAccent(theme, pal = null) {
  const col = pal?.primaryHex?.();
  if (!/^#[0-9a-f]{6}$/i.test(col || '')) return theme.accent;
  const r = parseInt(col.slice(1, 3), 16), g = parseInt(col.slice(3, 5), 16), b = parseInt(col.slice(5, 7), 16);
  if (theme.id === 'mainframe') {
    const green = Math.max(144, g);
    return '#' + [Math.min(r, green * 0.72), green, Math.min(b, green * 0.55)].map((x) => Math.round(x).toString(16).padStart(2, '0')).join('');
  }
  return (r * 0.299 + g * 0.587 + b * 0.114) > 95 ? col : theme.accent;
}

/** Static letter finish. The sole animated highlight is the existing sweep on the song-safe clock. */
export function wordFinish(theme) {
  if (theme.motif === 'terminal') return 'repeating-linear-gradient(0deg,transparent 0 .055em,rgba(3,18,5,.3) .06em .078em)';
  if (theme.motif === 'comic') return 'radial-gradient(circle,rgba(68,0,40,.3) 0 .017em,transparent .023em)';
  if (theme.motif === 'engraved') return 'repeating-linear-gradient(118deg,transparent 0 .1em,rgba(255,246,210,.3) .105em .12em)';
  if (theme.motif === 'satin') return 'linear-gradient(125deg,transparent 15%,rgba(255,255,255,.5) 32%,transparent 38%,rgba(255,255,255,.3) 70%,transparent 82%)';
  return 'linear-gradient(115deg,rgba(255,255,255,.4),transparent 25%,rgba(255,255,255,.25) 55%,transparent 70%)';
}
