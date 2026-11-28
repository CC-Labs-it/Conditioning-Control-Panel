/* Extracted from backroom/stations/breakout/reform.js and feedback.js.
 * Keep the real station unchanged; the small cabinet uses the same face contact and impact shape. */
export function rotatedBrickContact(ball, brick) {
  const co = Math.cos(brick.angle || 0), si = Math.sin(brick.angle || 0);
  const dx = ball.x - brick.x - brick.w / 2, dy = ball.y - brick.y - brick.h / 2;
  const x = dx * co + dy * si, y = -dx * si + dy * co, hx = brick.w / 2, hy = brick.h / 2;
  const qx = Math.max(-hx, Math.min(hx, x)), qy = Math.max(-hy, Math.min(hy, y));
  let nx = x - qx, ny = y - qy, d = Math.hypot(nx, ny), depth = ball.r - d;
  if (depth <= 0) return null;
  if (d > 1e-8) { nx /= d; ny /= d; }
  else if (hx - Math.abs(x) < hy - Math.abs(y)) { nx = x < 0 ? -1 : 1; ny = 0; depth = ball.r + hx - Math.abs(x); }
  else { nx = 0; ny = y < 0 ? -1 : 1; depth = ball.r + hy - Math.abs(y); }
  return { nx: nx * co - ny * si, ny: nx * si + ny * co, depth };
}

export function squashScale(t) {
  const u = 1 - Math.max(0, Math.min(1, t || 0));
  const k = u < .4 ? 1 - u / .4 : -.3 * Math.sin((u - .4) / .6 * Math.PI);
  return { along: 1 - .38 * k, across: 1 + .24 * k };
}

const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
/** Station game.js paddle response: tip offset steers up to 60 degrees, motion adds at most 8. */
export function paddleBounce(offset, velocity, speed) {
  const angle = clamp(clamp(offset, -1, 1) * Math.PI / 3 + clamp(velocity / 210, -1, 1) * 8 * Math.PI / 180, -Math.PI / 3, Math.PI / 3);
  return { vx: Math.sin(angle) * speed, vy: -Math.cos(angle) * speed };
}
