import { rotatedBrickContact, paddleBounce } from './contact.js';

export const WIDTH = 280, HEIGHT = 196, PADDLE_Y = 176, PADDLE_W = 62;
const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
const finite = (v, fallback) => Number.isFinite(v) ? v : fallback;

export function makeBoard(board) {
  const bricks = [];
  for (let row = 0; row < 4; row++) for (let col = 0; col < 7; col++) {
    if (board % 3 === 1 && row < 2 && (col + row) % 3 === 0) continue;
    if (board % 3 === 2 && row === 0 && (col === 0 || col === 6)) continue;
    bricks.push({ x: 12 + col * 37, y: 22 + row * 17, w: 34, h: 13, row, alive: true });
  }
  return bricks;
}

export function createBreakout(saved = {}) {
  if (!saved || typeof saved !== 'object') saved = {};
  const board = Math.floor(clamp(finite(saved.board, 0), 0, 100000));
  const s = { board, bricks: makeBoard(board), paddle: clamp(finite(saved.paddle, 140), 31, 249),
    ball: { x: 140, y: 150, vx: 38, vy: -112, r: 3.6 }, wait: clamp(finite(saved.wait, 0), 0, 1.2),
    manualReturns: Math.max(0, finite(saved.manualReturns, 0)), returns: Math.max(0, finite(saved.returns, 0)),
    time: Math.max(0, finite(saved.time, 0)), cleared: Math.max(0, finite(saved.cleared, 0)) };
  if (saved.ball) {
    s.ball.x = clamp(finite(saved.ball.x, 140), 4, 276); s.ball.y = clamp(finite(saved.ball.y, 150), 4, 194);
    const vx = finite(saved.ball.vx, 38), vy = finite(saved.ball.vy, -112), speed = Math.hypot(vx, vy);
    if (speed >= 80 && speed <= 200) { s.ball.vx = vx; s.ball.vy = vy; }
  }
  if (Array.isArray(saved.alive) && saved.alive.length === s.bricks.length) s.bricks.forEach((b, i) => { b.alive = saved.alive[i] !== false; });
  if (s.wait > 0) s.bricks.forEach((b) => { b.alive = false; });
  // A corrupt empty board is dealt again, never cashed again.
  if (!s.wait && !s.bricks.some((b) => b.alive)) s.bricks = makeBoard(board);
  return s;
}

export function snapshot(s) {
  return { board: s.board, alive: s.bricks.map((b) => b.alive), paddle: s.paddle, ball: { ...s.ball },
    wait: s.wait, manualReturns: s.manualReturns, returns: s.returns, time: s.time, cleared: s.cleared };
}

export function rewardSeconds(manualReturns, returns) {
  return 8 + 4 * clamp(manualReturns / Math.max(1, returns), 0, 1);
}

function launch(s) {
  const speed = 118 + Math.min(22, s.board * 2);
  s.ball = { x: s.paddle, y: 164, vx: (s.board % 2 ? -1 : 1) * 40, vy: -Math.sqrt(speed * speed - 1600), r: 3.6 };
}

function landingX(ball) {
  const span = WIDTH - ball.r * 2;
  const t = ball.vy > 0 ? Math.max(0, (PADDLE_Y - 7 - ball.y) / ball.vy) : 0;
  let x = ((ball.x - ball.r + ball.vx * t) % (span * 2) + span * 2) % (span * 2);
  if (x > span) x = span * 2 - x;
  return x + ball.r;
}

/** Fixed small steps keep the station's circle/face collision reliable on a narrow cabinet. */
export function stepBreakout(s, dt, input = {}) {
  const events = [];
  let left = clamp(finite(dt, 0), 0, .15);
  while (left > 1e-6) {
    const step = Math.min(left, 1 / 120); left -= step; s.time += step;
    if (s.wait > 0) {
      s.wait = Math.max(0, s.wait - step);
      if (!s.wait) { s.bricks = makeBoard(s.board); s.returns = s.manualReturns = 0; launch(s); events.push({ type: 'deal' }); }
      continue;
    }
    const b = s.ball, oldPaddle = s.paddle;
    const want = input.manual ? finite(input.x, s.paddle) : landingX(b);
    s.paddle += clamp(want - s.paddle, -480 * step, 480 * step);
    s.paddle = clamp(s.paddle, PADDLE_W / 2, WIDTH - PADDLE_W / 2);
    const previousY = b.y;
    b.x += b.vx * step; b.y += b.vy * step;
    if (b.x < b.r || b.x > WIDTH - b.r) { b.x = clamp(b.x, b.r, WIDTH - b.r); b.vx *= -1; events.push({ type: 'wall', x: b.x, y: b.y }); }
    if (b.y < b.r) { b.y = b.r; b.vy = Math.abs(b.vy); events.push({ type: 'wall', x: b.x, y: b.y }); }
    if (b.vy > 0 && previousY + b.r <= PADDLE_Y - 4 && b.y + b.r >= PADDLE_Y - 4 && Math.abs(b.x - s.paddle) <= PADDLE_W / 2 + b.r) {
      const speed = Math.hypot(b.vx, b.vy);
      Object.assign(b, paddleBounce((b.x - s.paddle) / (PADDLE_W / 2), (s.paddle - oldPaddle) / step, speed));
      b.y = PADDLE_Y - 4 - b.r;
      if (!input.manual) {
        // Like the station's cleanup help, aim once at the paddle, never bend a ball in flight.
        const target = s.bricks.filter((br) => br.alive).sort((a, c) => Math.abs(a.x + a.w / 2 - b.x) - Math.abs(c.x + c.w / 2 - b.x))[0];
        if (target) {
          const angle = clamp(Math.atan2(target.x + target.w / 2 - b.x, b.y - target.y - target.h / 2), -Math.PI / 3, Math.PI / 3);
          b.vx = Math.sin(angle) * speed; b.vy = -Math.cos(angle) * speed;
        }
      }
      s.returns++; if (input.manual) s.manualReturns++;
      events.push({ type: 'paddle', x: b.x, y: b.y, manual: !!input.manual });
    }
    for (const brick of s.bricks) {
      if (!brick.alive) continue;
      const hit = rotatedBrickContact(b, brick);
      if (!hit) continue;
      b.x += hit.nx * (hit.depth + .01); b.y += hit.ny * (hit.depth + .01);
      const dot = b.vx * hit.nx + b.vy * hit.ny;
      if (dot >= 0) continue;
      b.vx -= 2 * dot * hit.nx; b.vy -= 2 * dot * hit.ny;
      brick.alive = false;
      events.push({ type: 'brick', x: brick.x + brick.w / 2, y: brick.y + brick.h / 2, row: brick.row });
      if (!s.bricks.some((br) => br.alive)) {
        const seconds = rewardSeconds(s.manualReturns, s.returns);
        s.board++; s.cleared++; s.wait = 1.2;
        events.push({ type: 'clear', board: s.board, seconds, manual: s.manualReturns > 0 });
      }
      break;
    }
    if (b.y > HEIGHT + b.r) { launch(s); events.push({ type: 'miss' }); }
  }
  return events;
}
