import { S } from './state.js';

const local = (p, f, axis) => p.reduce((v, n, i) => v + (n - f.p[i]) * f[axis][i], 0);
// Physical plane crossings prevent a road above/below, shortcut, rescue or reverse pass firing the gate.
export function createPhraseGateContact(gates) {
  let previous = null, cooldown = 0, count = 0;
  const visited = new Map();
  function reset() { previous = null; cooldown = 0; count = 0; visited.clear(); }
  function update(st, dt, { paused = false, racing = true } = {}) {
    if (paused) return null;
    if (!racing) { reset(); return null; }
    cooldown = Math.max(0, cooldown - Math.max(0, dt));
    const p = [st[S.posX], st[S.posY], st[S.posZ]], prev = previous;
    previous = p;
    if (st[S.respawnPhase]) { previous = null; return null; }
    if (!prev || Math.hypot(...p.map((v, i) => v - prev[i])) > 20) return null;
    for (const gate of gates) {
      if (Number.isFinite(gate.s) && Math.abs(st[S.s] - gate.s) > 25) continue;
      const f = gate.frame, a = local(prev, f, 't'), b = local(p, f, 't');
      if (a >= 0 || b < 0 || b <= a || visited.get(gate.id) === st[S.lap]) continue;
      const u = -a / (b - a), hit = p.map((v, i) => prev[i] + (v - prev[i]) * u);
      if (Math.abs(local(hit, f, 'b')) > f.w / 2 || local(hit, f, 'n') < -.6 || local(hit, f, 'n') > 6) continue;
      visited.set(gate.id, st[S.lap]);
      if (cooldown > 0) continue;
      cooldown = 8; count++; return gate;
    }
    return null;
  }
  return { update, reset, get count() { return count; } };
}
