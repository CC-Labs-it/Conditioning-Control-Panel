/* Brain Drain's local impact: one bounded canvas, driven only by the existing effect clock.
 * Free text, soft smoke and embers cover the road below the HUD. No media or GPU resources. */
export const SUBLIMINAL_PHRASES = Object.freeze(['LET GO', 'DRIFT DEEPER', 'FOLLOW THE FLOW', 'SINK IN', 'SOFTEN', 'KEEP DRIFTING', 'JUST FLOAT']);
const sat = v => Math.max(0, Math.min(1, v));
const ease = v => { const q = sat(v); return q * q * (3 - 2 * q); };
const rnd = n => { const x = Math.sin(n * 127.1 + 31.7) * 43758.5453; return x - Math.floor(x); };
const STACK = [[.43,.18,-.09], [.57,.29,.08], [.45,.40,-.065], [.56,.51,.10], [.42,.62,-.08], [.55,.73,.065], [.48,.84,-.105]];

export const subliminalPrimary = value => /^#[a-f\d]{6}$/i.test(value || '') ? value : /^#[a-f\d]{3}$/i.test(value || '') ? '#' + value.slice(1).split('').map(x => x + x).join('') : '#c9eceb';

/** Every puff and ember has a finite lifetime, gentle onset, outward drift and a fading tail. */
export function subliminalParticle(index, age, seed = 0, smoke = false) {
  const r = rnd(index * 3.7 + seed * 11), life = (smoke ? 2.8 : 2.1) + r * (smoke ? 1.8 : 2.3);
  const t = age - index * (smoke ? .045 : .008), q = sat(t / life);
  const alpha = t <= 0 || t >= life ? 0 : ease(t / .35) * (1 - q) ** 1.6;
  const direction = rnd(index * 7.1 + seed) > .5 ? 1 : -1;
  return { x: (rnd(index + seed * 2.1) - .5) * .8 + direction * t * .045,
    y: (rnd(index * 2.9 + seed) - .5) * .1 - Math.max(0, t) * (.035 + r * .025),
    size: smoke ? .055 + q * .16 : .0025 + r * .003, alpha, life };
}

/** Layout in fractions of the view. Seeded variation stays fixed throughout a hit and its OUT. */
export function subliminalCards(age, aspect = 16 / 9, seed = 0, gentle = false) {
  const count = gentle ? 4 : 7, a = aspect > 0 ? aspect : 16 / 9;
  return STACK.slice(0, count).map(([x, y, tilt], i) => {
    const enter = ease((age - i * .045) / (gentle ? .65 : .34)), phase = seed * 1.7 + i * 2.3;
    const motion = gentle ? 0 : Math.sin(age * 1.15 + phase);
    return { text: SUBLIMINAL_PHRASES[(i + Math.abs(Math.trunc(seed))) % SUBLIMINAL_PHRASES.length],
      x: x + (rnd(seed * 11 + i) - .5) * .025 + (1 - enter) * (i % 2 ? .055 : -.055) + motion * .008,
      y: y + (rnd(seed * 7 + i) - .5) * .012 + (1 - enter) * .035 + motion * .005,
      w: .95 * (gentle ? .78 : 1),
      h: .19 * (gentle ? .75 : 1),
      tilt: tilt + motion * .022, alpha: enter, scale: .84 + enter * .16, seed: seed * 13 + i };
  });
}

export function createSubliminalBurst({ parent = globalThis.document?.body, doc = globalThis.document, primary = '#c9eceb' } = {}) {
  const canvas = doc?.createElement('canvas'), ctx = canvas?.getContext('2d');
  const resolvePrimary = () => { try { return subliminalPrimary(typeof primary === 'function' ? primary() : primary); } catch { return '#c9eceb'; } };
  let colour = resolvePrimary();
  const stats = { active: false, cards: 0, particles: 0, smoke: 0, clock: 0, seed: 0, primary: colour };
  let age = 0, seed = null, params = null, effectStrength = 1, drawAge = -Infinity, disposed = false;
  if (!canvas || !ctx || !parent) return { update() {}, dispose() {}, get stats() { return { ...stats }; } };
  // Three tiny offscreen sprites give the smoke soft scalloped wisps without per-frame gradients.
  const puffs = Array.from({ length: 3 }, () => {
    const puff = doc.createElement('canvas'); puff.width = puff.height = 96;
    return puff;
  });
  function paintPuffs() { const rgb = [1, 3, 5].map(i => parseInt(colour.slice(i, i + 2), 16));
    puffs.forEach((puff, i) => {
    const g = puff.getContext('2d');
    g.clearRect(0, 0, 96, 96);
    for (let j = 0; j < 3; j++) {
      const x = 48 + Math.cos(i + j * 2.1) * 17, y = 48 + Math.sin(i * 2 + j * 1.9) * 14;
      const gradient = g.createRadialGradient(x, y, 1, x, y, 29);
      gradient.addColorStop(0, `rgba(${rgb.join(',')},.28)`);
      gradient.addColorStop(.4, `rgba(${rgb.map(v => Math.min(255, v + 35)).join(',')},.13)`);
      gradient.addColorStop(1, `rgba(${rgb.join(',')},0)`);
      g.fillStyle = gradient; g.beginPath(); g.arc(x, y, 29, 0, Math.PI * 2); g.fill();
    }
    });
  }
  paintPuffs();
  canvas.className = 'subliminal-burst'; canvas.setAttribute('aria-hidden', 'true');
  Object.assign(canvas.style, { position: 'fixed', zIndex: '2', pointerEvents: 'none', left: '0', top: '0', width: '100vw', height: '100vh', display: 'none' });
  parent.appendChild(canvas);
  function clear() {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    canvas.style.display = 'none'; age = 0; seed = null; params = null; drawAge = -Infinity;
    Object.assign(stats, { active: false, cards: 0, particles: 0, smoke: 0, clock: 0, seed: 0 });
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
    Object.assign(stats, { active: true, clock: age, seed: seed || 0, cards: gentle ? 4 : 7 });
    if (!resized && age - drawAge < 1 / 20) return;
    drawAge = age;
    const nextColour = current ? resolvePrimary() : colour;
    if (nextColour !== colour) { colour = nextColour; paintPuffs(); }
    stats.primary = colour;
    stats.particles = 0; stats.smoke = 0;
    ctx.clearRect(0, 0, w, h);
    for (const card of subliminalCards(age, a, seed || 0, gentle)) {
      ctx.save();
      ctx.translate(card.x * w, card.y * h); ctx.rotate(card.tilt); ctx.scale(card.scale, card.scale);
      const cw = Math.min(card.w * w, (Math.min(card.x, 1 - card.x) * 2 - .055) * w), ch = card.h * h;
      const smokeCount = gentle ? 3 : 6;
      for (let j = 0; j < smokeCount; j++) {
        const puff = subliminalParticle(j, age, card.seed, true);
        if (puff.alpha <= .001) continue;
        ctx.globalAlpha = card.alpha * puff.alpha * (gentle ? .45 : .8);
        const radius = puff.size * h;
        ctx.drawImage(puffs[j % puffs.length], puff.x * cw - radius, puff.y * h - radius, radius * 2.8, radius * 1.8);
        stats.smoke++;
      }
      ctx.globalAlpha = card.alpha;
      let fontSize = ch;
      ctx.font = `900 ${fontSize}px system-ui, sans-serif`;
      const measured = ctx.measureText(card.text).width;
      fontSize *= Math.min(1, Math.max(1, cw - Math.abs(Math.sin(card.tilt)) * fontSize) / Math.max(1, measured));
      ctx.font = `900 ${fontSize}px system-ui, sans-serif`;
      ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.lineJoin = 'round';
      ctx.globalAlpha = card.alpha * .22; ctx.lineWidth = fontSize * .2; ctx.strokeStyle = colour; ctx.strokeText(card.text, 0, 1);
      ctx.globalAlpha = card.alpha; ctx.lineWidth = Math.max(4, fontSize * .115); ctx.strokeStyle = '#10131d'; ctx.strokeText(card.text, 0, 1);
      ctx.lineWidth = Math.max(1, fontSize * .025); ctx.strokeStyle = '#edfffc'; ctx.strokeText(card.text, 0, 1);
      ctx.fillStyle = colour; ctx.fillText(card.text, 0, 1);
      const sparks = gentle ? 6 : 14;
      for (let j = 0; j < sparks; j++) {
        const spark = subliminalParticle(j, age, card.seed);
        if (spark.alpha <= .001) continue;
        const sx = spark.x * cw, sy = spark.y * h, r = Math.max(1.5, spark.size * h);
        ctx.globalAlpha = card.alpha * spark.alpha;
        ctx.fillStyle = j % 3 ? colour : '#edfffc';
        ctx.beginPath(); ctx.moveTo(sx, sy - r); ctx.lineTo(sx + r * .45, sy); ctx.lineTo(sx, sy + r); ctx.lineTo(sx - r * .45, sy); ctx.closePath(); ctx.fill();
        stats.particles++;
      }
      ctx.restore();
    }
  }
  return { update, get stats() { return { ...stats }; }, dispose() { if (disposed) return; clear(); disposed = true; canvas.remove(); } };
}
