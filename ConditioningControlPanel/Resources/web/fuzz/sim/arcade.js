// Small saved machines. Callers supply elapsed active time and the game's RNG.
const clamp = (n, max, fallback = 0) => Number.isFinite(n) ? Math.max(0, Math.min(max, n)) : fallback;
export const COIN_MS = 45000;
export const SPIN_MS = 2200;
export const CATCH_MS = 2500;
export const DROP_MS = 700;

export function slotState(saved) {
  const s = saved && typeof saved === 'object' ? saved : {};
  const pending = s.pending && Array.isArray(s.pending.reels) && s.pending.reels.length === 3
    && s.pending.reels.every((n) => Number.isInteger(n) && n >= 0 && n < 4) ? s.pending : null;
  return {
    coins: Math.floor(clamp(s.coins, 3, 1)), elapsed: clamp(s.elapsed, COIN_MS - 1),
    bag: Array.isArray(s.bag) && s.bag.length <= 6 && s.bag.every((v) => typeof v === 'boolean')
      && s.bag.filter(Boolean).length <= 2 && s.bag.filter((v) => !v).length <= 4 ? [...s.bag] : [],
    pending: pending ? { reels: [...pending.reels], left: clamp(pending.left, SPIN_MS), win: new Set(pending.reels).size === 1 } : null,
    last: Array.isArray(s.last) && s.last.length === 3 ? s.last.map((v) => Math.floor(clamp(v, 3))) : [1, 0, 2],
    result: typeof s.result === 'string' ? s.result.slice(0, 100) : 'One coin. Give it a spin.',
  };
}

export function slotStep(s, ms) {
  const dt = Math.max(0, Number.isFinite(ms) ? ms : 0);
  if (s.coins < 3) {
    s.elapsed += dt;
    while (s.elapsed >= COIN_MS && s.coins < 3) { s.elapsed -= COIN_MS; s.coins++; }
  }
  if (s.coins === 3) s.elapsed = 0;
  if (s.pending) s.pending.left = Math.max(0, s.pending.left - dt);
}

export function beginSpin(s, rng) {
  if (s.coins < 1 || s.pending) return false;
  if (!s.bag.length) {
    s.bag = [true, true, false, false, false, false];
    for (let i = s.bag.length - 1; i > 0; i--) {
      const j = Math.min(i, Math.floor(rng() * (i + 1)));
      [s.bag[i], s.bag[j]] = [s.bag[j], s.bag[i]];
    }
  }
  const win = s.bag.shift(), symbol = Math.min(3, Math.floor(rng() * 4));
  const reels = win ? [symbol, symbol, symbol] : [symbol, (symbol + 1) % 4, (symbol + 2) % 4];
  s.coins--;
  s.pending = { reels, win, left: SPIN_MS };
  s.result = 'Round and round.';
  return true;
}

export function finishSpin(s) {
  if (!s.pending || s.pending.left > 0) return null;
  const result = s.pending;
  s.last = [...result.reels];
  s.pending = null; // Consume before the UI pays. Remounting cannot replay a win.
  return { win: result.win, big: result.win && result.reels[0] === 0 };
}

export function dispenserState(saved) {
  const s = saved && typeof saved === 'object' ? saved : {};
  return {
    phase: ['ready', 'drop', 'catch', 'dissolve', 'cooldown'].includes(s.phase) ? s.phase : 'ready',
    left: clamp(s.left, 30000), cooldown: clamp(s.cooldown, 30000),
    caught: Math.floor(clamp(s.caught, 1e6)), missed: Math.floor(clamp(s.missed, 1e6)),
  };
}

export function dispense(s, cooldown = 30000) {
  if (s.phase !== 'ready') return false;
  s.phase = 'drop'; s.left = DROP_MS; s.cooldown = clamp(cooldown, 30000);
  return true;
}

export function dispenserStep(s, ms) {
  let dt = Math.max(0, Number.isFinite(ms) ? ms : 0);
  s.cooldown = Math.max(0, s.cooldown - dt);
  // Carry the remainder across phases, so a slow frame never widens the catch window.
  while (dt > 0 && s.phase !== 'ready') {
    if (s.phase === 'cooldown') { if (s.cooldown === 0) s.phase = 'ready'; break; }
    const used = Math.min(dt, s.left); s.left -= used; dt -= used;
    if (s.left > 0) break;
    if (s.phase === 'drop') { s.phase = 'catch'; s.left = CATCH_MS; }
    else if (s.phase === 'catch') { s.phase = 'dissolve'; s.left = 650; s.missed++; }
    else { s.phase = s.cooldown > 0 ? 'cooldown' : 'ready'; }
  }
}

export function catchPrize(s) {
  if (s.phase !== 'catch' || s.left <= 0) return false;
  s.caught++; s.phase = s.cooldown > 0 ? 'cooldown' : 'ready'; s.left = 0;
  return true;
}
