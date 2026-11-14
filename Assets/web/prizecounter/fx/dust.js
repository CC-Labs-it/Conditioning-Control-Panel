/* fx/dust.js - soft paper-tone puffs where a shelf lands (lane B3-W3). Plain alpha sprites (never additive glow),
 * pooled, each one grows, drifts out and fades to nothing. */
export const DUST = { per: 5, life: 0.75, spread: [0.5, 1.4], rise: [0.15, 0.6], size: [0.14, 0.5], alpha: 0.45, drag: 3.2, colour: '#cdc1ae' };

export function createDust(T, parent, rng = Math.random) {
  const c = document.createElement('canvas');
  c.width = c.height = 64;
  const g = c.getContext('2d'), grad = g.createRadialGradient(32, 32, 0, 32, 32, 32);
  grad.addColorStop(0, DUST.colour); grad.addColorStop(0.45, DUST.colour + 'aa'); grad.addColorStop(1, DUST.colour + '00');
  g.fillStyle = grad; g.fillRect(0, 0, 64, 64);
  const tex = new T.CanvasTexture(c);
  tex.colorSpace = T.SRGBColorSpace;
  const pool = [], live = [];

  function sprite() {
    const s = pool.pop() || new T.Sprite(new T.SpriteMaterial({ map: tex, transparent: true, depthWrite: false, toneMapped: false, opacity: 0 }));
    s.renderOrder = 3; s.visible = true; parent.add(s);
    return s;
  }
  // points: [{ at: Vector3, side: -1 | 0 | 1 }]; amp: 1 full, 0.5 less (half the puffs, half the spread).
  function puff(points, amp) {
    for (const { at, side } of points) {
      const n = Math.max(1, Math.round(DUST.per * amp * (side === 0 ? 0.6 : 1)));
      for (let i = 0; i < n; i++) {
        const s = sprite(), dir = side || (rng() < 0.5 ? -1 : 1);
        s.position.set(at.x + dir * 0.05, at.y + 0.02, at.z + 0.3);
        s.userData.v = new T.Vector3(dir * (DUST.spread[0] + rng() * (DUST.spread[1] - DUST.spread[0])) * amp,
          (DUST.rise[0] + rng() * (DUST.rise[1] - DUST.rise[0])) * amp, (rng() - 0.3) * 0.4 * amp);
        s.userData.age = -i * 0.02;
        s.userData.s0 = DUST.size[0] * (0.7 + rng() * 0.6);
        live.push(s);
      }
    }
  }
  function step(dt) {
    for (let i = live.length - 1; i >= 0; i--) {
      const s = live[i], u = s.userData;
      u.age += dt;
      const k = Math.max(0, u.age) / DUST.life;
      if (k >= 1) { s.visible = false; s.removeFromParent(); live.splice(i, 1); pool.push(s); continue; }
      if (u.age > 0) { s.position.addScaledVector(u.v, dt); u.v.multiplyScalar(Math.exp(-DUST.drag * dt)); }
      const grow = 1 - (1 - k) * (1 - k);
      s.scale.setScalar(u.s0 + (DUST.size[1] - u.s0) * grow);
      s.material.opacity = u.age > 0 ? DUST.alpha * Math.min(1, k * 8) * (1 - k) ** 1.5 : 0;   // a quick in, a long out
    }
  }
  return { puff, step, get count() { return live.length; } };
}
