/* Brain Drain's local impact: one bounded canvas, driven only by the existing effect clock.
 * Text and sparks cover the road below the HUD. No images, media, filters or new graphics context. */
export const SUBLIMINAL_PHRASES = Object.freeze(['LET GO', 'DRIFT DEEPER', 'FOLLOW THE FLOW', 'SINK IN', 'SOFTEN', 'KEEP DRIFTING', 'JUST FLOAT']);
const sat = v => Math.max(0, Math.min(1, v));
const ease = v => { const q = sat(v); return q * q * (3 - 2 * q); };
const rnd = n => { const x = Math.sin(n * 127.1 + 31.7) * 43758.5453; return x - Math.floor(x); };
const STACK = [[.39,.23,-.14], [.61,.32,.11], [.34,.43,-.10], [.64,.54,.16], [.43,.64,-.07], [.59,.75,.08], [.48,.48,-.19]];

/** Layout in fractions of the view. Seeded variation stays fixed throughout a hit and its OUT. */
export function subliminalCards(age, aspect = 16 / 9, seed = 0, gentle = false) {
  const count = gentle ? 4 : 7, a = aspect > 0 ? aspect : 16 / 9;
  return STACK.slice(0, count).map(([x, y, tilt], i) => {
    const enter = ease((age - i * .045) / (gentle ? .65 : .34)), phase = seed * 1.7 + i * 2.3;
    const motion = gentle ? 0 : Math.sin(age * 1.15 + phase);
    return { text: SUBLIMINAL_PHRASES[(i + Math.abs(Math.trunc(seed))) % SUBLIMINAL_PHRASES.length],
      x: x + (rnd(seed * 11 + i) - .5) * .06 + (1 - enter) * (i % 2 ? .09 : -.09) + motion * .012,
      y: y + (rnd(seed * 7 + i) - .5) * .035 + (1 - enter) * .055 + motion * .009,
      w: Math.min(.84, .64 + .03 * Math.sin(i * 2.1)) * (gentle ? .7 : 1),
      h: Math.min(.16, .22 / a) * (gentle ? .75 : 1),
      tilt: tilt + motion * .022, alpha: enter, scale: .84 + enter * .16, seed: seed * 13 + i };
  });
}

export function createSubliminalBurst({ parent = globalThis.document?.body, doc = globalThis.document } = {}) {
  const canvas = doc?.createElement('canvas'), ctx = canvas?.getContext('2d');
  const stats = { active: false, cards: 0, particles: 0, clock: 0, seed: 0 };
  let age = 0, seed = null, params = null, effectStrength = 1, drawAge = -Infinity, disposed = false;
  if (!canvas || !ctx || !parent) return { update() {}, dispose() {}, get stats() { return { ...stats }; } };
  canvas.className = 'subliminal-burst'; canvas.setAttribute('aria-hidden', 'true');
  Object.assign(canvas.style, { position: 'fixed', zIndex: '2', pointerEvents: 'none', left: '0', top: '0', width: '100vw', height: '100vh', display: 'none' });
  parent.appendChild(canvas);
  function clear() {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    canvas.style.display = 'none'; age = 0; seed = null; params = null; drawAge = -Infinity;
    Object.assign(stats, { active: false, cards: 0, particles: 0, clock: 0, seed: 0 });
  }
  function update(screen, dt, aspect = 16 / 9) {
    if (disposed) return;
    const current = screen?.kind === 'brainDrain';
    const amount = sat(current ? screen.k : screen?.prevKind === 'brainDrain' ? screen.prevK : 0);
    if (!(amount > .001)) { if (stats.active) clear(); return; }
    if (current) {
      params = { ...screen.params };
      effectStrength = screen.strength > 0 ? Math.min(1, screen.strength) : 1;
      if (params.seed !== seed) { age = 0; drawAge = -Infinity; seed = params.seed; }
    }
    const p = params || screen.params || {}, gentle = !!p.gentle;
    age += Math.max(0, dt || 0) * (gentle ? .5 : 1);
    const a = Number.isFinite(aspect) && aspect > 0 ? aspect : 16 / 9;
    const viewW = Math.max(1, parent.clientWidth || doc.defaultView?.innerWidth || 1280);
    const scale = Math.min(1, 1280 / viewW, 1280 / (viewW / a));
    const w = Math.max(1, Math.round(viewW * scale)), h = Math.max(1, Math.round(viewW / a * scale));
    const resized = canvas.width !== w || canvas.height !== h;
    if (resized) { canvas.width = w; canvas.height = h; }
    Object.assign(canvas.style, { display: 'block', opacity: String(amount * effectStrength * (gentle ? .5 : 1)) });
    Object.assign(stats, { active: true, clock: age, seed: seed || 0, cards: gentle ? 4 : 7, particles: gentle ? 12 : 35 });
    if (!resized && age - drawAge < 1 / 20) return;
    drawAge = age;
    ctx.clearRect(0, 0, w, h);
    for (const card of subliminalCards(age, a, seed || 0, gentle)) {
      ctx.save();
      ctx.translate(card.x * w, card.y * h); ctx.rotate(card.tilt); ctx.scale(card.scale, card.scale);
      const cw = card.w * w, ch = card.h * h;
      ctx.globalAlpha = card.alpha;
      ctx.fillStyle = '#170c24'; ctx.fillRect(-cw / 2 + 6, -ch / 2 + 8, cw, ch);
      ctx.fillStyle = '#ed9ad8'; ctx.fillRect(-cw / 2, -ch / 2, cw, ch);
      ctx.strokeStyle = '#21102c'; ctx.lineWidth = Math.max(3, h * .006); ctx.strokeRect(-cw / 2, -ch / 2, cw, ch);
      ctx.strokeStyle = '#fff0fc'; ctx.lineWidth = 2; ctx.strokeRect(-cw / 2 + 6, -ch / 2 + 6, cw - 12, ch - 12);
      ctx.font = `900 ${Math.min(ch * .61, cw / (card.text.length * .65))}px system-ui, sans-serif`;
      ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.lineJoin = 'round';
      ctx.lineWidth = Math.max(3, ch * .075); ctx.strokeStyle = '#291033'; ctx.strokeText(card.text, 0, 1);
      ctx.fillStyle = '#fff5ff'; ctx.fillText(card.text, 0, 1);
      const sparks = gentle ? 3 : 5;
      for (let j = 0; j < sparks; j++) {
        const ph = rnd(card.seed * 3 + j) * Math.PI * 2 + age * (gentle ? .12 : .32);
        const drift = .5 + .5 * Math.sin(age * .6 + j * 1.7);
        const sx = Math.cos(ph) * (cw * .52 + drift * ch * .3), sy = Math.sin(ph) * (ch * .7 + drift * ch * .45);
        const r = Math.max(2, ch * (.035 + rnd(j + card.seed) * .025));
        ctx.globalAlpha = card.alpha * (.45 + .3 * drift);
        ctx.fillStyle = j % 2 ? '#fff5c8' : '#ffdcfa';
        ctx.beginPath(); ctx.moveTo(sx, sy - r); ctx.lineTo(sx + r * .45, sy); ctx.lineTo(sx, sy + r); ctx.lineTo(sx - r * .45, sy); ctx.closePath(); ctx.fill();
      }
      ctx.restore();
    }
  }
  return { update, get stats() { return { ...stats }; }, dispose() { if (disposed) return; clear(); disposed = true; canvas.remove(); } };
}
