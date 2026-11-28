/* ui/unlocks/fate/outside/sky.js - A Window's sky by the player's REAL local time of day (pure, no DOM; tests/fate-outside.test.mjs).
 * Hours are local and fractional (13.5 = half past one). Night, dawn, day and dusk blend through a ring of colour stops, so a
 * window left open watches the evening come in a minute at a time. The sun rises at 6 and sets at 19; the moon has the rest. */

export const SUNRISE = 6;
export const SUNSET = 19;

/** The phase a window names: night from 20:30, dawn from 5, day from 7:30, dusk from 17:30. */
export function phaseAt(h) {
  const x = wrap(h);
  if (x >= 20.5 || x < 5) return 'night';
  if (x < 7.5) return 'dawn';
  if (x < 17.5) return 'day';
  return 'dusk';
}

/* Colour stops round the clock: sky top, sky bottom (horizon), hill, cloud, star strength. The last stop wraps to the first. */
const STOPS = [
  { h: 0, top: '#0b1030', bottom: '#1f2756', hill: '#1c2b2a', cloud: '#3a4270', stars: 1 },
  { h: 4.6, top: '#0b1030', bottom: '#1f2756', hill: '#1c2b2a', cloud: '#3a4270', stars: 1 },
  { h: 5.6, top: '#3b3f7c', bottom: '#f0a07c', hill: '#3a4a3a', cloud: '#e9b4b0', stars: 0.35 },
  { h: 6.6, top: '#80b3e6', bottom: '#ffd3a6', hill: '#5f8a4c', cloud: '#fff1e6', stars: 0 },
  { h: 8, top: '#5aa6e8', bottom: '#c2e4fa', hill: '#6fa552', cloud: '#ffffff', stars: 0 },
  { h: 16.6, top: '#5aa6e8', bottom: '#c2e4fa', hill: '#6fa552', cloud: '#ffffff', stars: 0 },
  { h: 18, top: '#4c5da0', bottom: '#ff9f6c', hill: '#4f6e40', cloud: '#ffc8b0', stars: 0 },
  { h: 19.3, top: '#292b63', bottom: '#b45a7a', hill: '#2c3b30', cloud: '#7d5f86', stars: 0.45 },
  { h: 20.6, top: '#0b1030', bottom: '#1f2756', hill: '#1c2b2a', cloud: '#3a4270', stars: 1 },
  { h: 24, top: '#0b1030', bottom: '#1f2756', hill: '#1c2b2a', cloud: '#3a4270', stars: 1 },
];

function wrap(h) {
  const n = Number(h);
  if (!Number.isFinite(n)) return 12;
  return ((n % 24) + 24) % 24;
}

const hex = (c) => [1, 3, 5].map((i) => parseInt(c.slice(i, i + 2), 16));
const toHex = (rgb) => `#${rgb.map((v) => Math.round(Math.max(0, Math.min(255, v))).toString(16).padStart(2, '0')).join('')}`;
export function mix(a, b, t) {
  const A = hex(a), B = hex(b), k = Math.max(0, Math.min(1, t));
  return toHex(A.map((v, i) => v + (B[i] - v) * k));
}

/** Where a body on an arc from `rise` to `set` sits at hour h: {up, x, y} with x, y in 0..1 of the pane (y 0 = top). */
function arc(h, rise, set) {
  const span = ((set - rise) % 24 + 24) % 24;
  const since = ((h - rise) % 24 + 24) % 24;
  if (since > span) return { up: false, x: 0, y: 1 };
  const t = since / span;
  return { up: true, x: 0.12 + 0.76 * t, y: 0.86 - 0.66 * Math.sin(Math.PI * t) };
}

/** Everything the window draws at local hour h. */
export function skyAt(h) {
  const x = wrap(h);
  let i = 0;
  while (i < STOPS.length - 2 && STOPS[i + 1].h <= x) i++;
  const a = STOPS[i], b = STOPS[i + 1];
  const t = b.h > a.h ? (x - a.h) / (b.h - a.h) : 0;
  return {
    hour: x,
    phase: phaseAt(x),
    top: mix(a.top, b.top, t),
    bottom: mix(a.bottom, b.bottom, t),
    hill: mix(a.hill, b.hill, t),
    cloud: mix(a.cloud, b.cloud, t),
    stars: a.stars + (b.stars - a.stars) * t,
    sun: arc(x, SUNRISE, SUNSET),
    moon: arc(x, SUNSET + 0.5, SUNRISE),
  };
}

/** Local fractional hour of a Date. */
export const hourOf = (d = new Date()) => d.getHours() + d.getMinutes() / 60 + d.getSeconds() / 3600;
