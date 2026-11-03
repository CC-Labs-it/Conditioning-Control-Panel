import { createBreakout, stepBreakout, snapshot, WIDTH, HEIGHT, PADDLE_Y, PADDLE_W } from './breakout-core/game.js';
import { squashScale } from './breakout-core/contact.js';
import { sfx } from '../basics/fx.js';

export function mountBreakout(ctx) {
  const host = ctx.dock({ title: 'Mini Breakout', icon: '▰' });
  const root = ctx.el('div', 'width:100%;max-width:280px;margin:auto;pointer-events:auto;', null, host);
  root.dataset.arcade = 'mini_breakout';
  const header = ctx.el('div', 'display:flex;justify-content:space-between;gap:6px;font-size:11px;padding:0 2px 5px;color:var(--ink);', null, root);
  const board = ctx.el('span', 'font-weight:700;', '', header);
  const mode = ctx.el('span', 'opacity:.7;', '', header);
  const canvas = ctx.el('canvas', 'display:block;width:100%;aspect-ratio:280/196;border:1px solid var(--niche);border-radius:9px;background:#15151e;touch-action:pan-y;', null, root);
  canvas.width = WIDTH * 2; canvas.height = HEIGHT * 2; canvas.tabIndex = 0;
  canvas.setAttribute('aria-label', 'Mini Breakout. Hover to play, or use the Take control button and arrow keys.');
  const footer = ctx.el('div', 'display:flex;align-items:center;justify-content:space-between;gap:5px;margin-top:5px;', null, root);
  const receipt = ctx.el('span', 'font-size:11px;min-height:16px;color:var(--niche-text);', 'Clears pay. Playing pays more.', footer);
  const toggle = ctx.el('button', 'flex:none;min-height:30px;padding:3px 7px;border:1px solid var(--line);border-radius:5px;background:var(--panel);color:var(--ink);font:inherit;font-size:11px;', 'Take control', footer);
  toggle.type = 'button';
  const g = canvas.getContext('2d'), sim = createBreakout(ctx.store.get('game'));
  const particles = [], trail = [];
  let hovered = false, touchControl = false, aim = sim.paddle, visible = true, lastPaint = ctx.state.clock, squash = 0, glow = 0, dead = false;
  let colours = ['#f48db8', '#bb9cf5', '#89dce4', '#9de7c5'];
  const calm = () => ctx.gentle || ctx.reduced;
  const manual = () => !calm() && (hovered || touchControl);
  const save = () => ctx.store.set('game', snapshot(sim));
  const palette = () => { const p = ctx.palette(); if (p.themes.length) colours = [p.a, p.b, p.a, p.b]; };
  palette(); ctx.on('palette', palette);
  function controls() {
    const playing = manual();
    mode.textContent = calm() ? 'Gentle autoplay' : playing ? 'Your paddle' : 'Autoplay';
    toggle.textContent = touchControl ? 'Autoplay' : 'Take control';
    toggle.disabled = calm();
    toggle.setAttribute('aria-pressed', String(touchControl));
    canvas.style.touchAction = touchControl ? 'none' : 'pan-y';
  }
  function point(e) {
    const r = canvas.getBoundingClientRect();
    if (r.width) aim = Math.max(PADDLE_W / 2, Math.min(WIDTH - PADDLE_W / 2, (e.clientX - r.left) * WIDTH / r.width));
  }
  canvas.addEventListener('pointerenter', (e) => { if (ctx.paused || e.pointerType !== 'mouse') return; hovered = true; point(e); controls(); });
  canvas.addEventListener('pointerleave', (e) => { if (e.pointerType === 'mouse') { hovered = false; controls(); } });
  canvas.addEventListener('pointermove', (e) => { if (!ctx.paused && manual()) point(e); });
  canvas.addEventListener('pointerdown', (e) => { if (ctx.paused || !manual()) return; point(e); if (e.pointerType === 'touch') { e.preventDefault(); canvas.setPointerCapture?.(e.pointerId); } });
  canvas.addEventListener('keydown', (e) => {
    if (ctx.paused || calm() || !['ArrowLeft', 'ArrowRight'].includes(e.key)) return;
    e.preventDefault(); touchControl = true;
    aim = Math.max(PADDLE_W / 2, Math.min(WIDTH - PADDLE_W / 2, aim + (e.key === 'ArrowLeft' ? -20 : 20)));
    controls();
  });
  toggle.addEventListener('click', () => { if (ctx.paused || calm()) return; touchControl = !touchControl; hovered = false; aim = sim.paddle; controls(); if (touchControl) canvas.focus({ preventScroll: true }); });
  function burst(x, y, row, n = 8) {
    if (calm()) return;
    for (let i = 0; i < n && particles.length < 72; i++) {
      const a = i * 2.39996 + sim.time, speed = 22 + i % 5 * 11;
      particles.push({ x, y, vx: Math.cos(a) * speed, vy: Math.sin(a) * speed - 10, life: .65, row });
    }
  }
  function pay(event) {
    // Store the advanced board before crediting. A remount cannot repeat this clearance.
    save();
    const rates = ctx.rates(), amount = ctx.pay(Math.max(2, rates.fps, rates.click * 2) * event.seconds, 'mini_breakout');
    receipt.textContent = `+${ctx.fmt(amount)} ${ctx.brand.currencyPlural}${event.manual ? ' · played' : ''}`;
    burst(WIDTH / 2, 75, sim.board % 4, 40); glow = .8;
    sfx(ctx, 'sfx/rank_settle.mp3', .18);
  }
  function draw() {
    g.setTransform(2, 0, 0, 2, 0, 0); g.clearRect(0, 0, WIDTH, HEIGHT);
    g.fillStyle = '#15151e'; g.fillRect(0, 0, WIDTH, HEIGHT);
    g.strokeStyle = '#ffffff08'; g.lineWidth = 1;
    for (let x = 10; x < WIDTH; x += 18) { g.beginPath(); g.moveTo(x, 0); g.lineTo(x, HEIGHT); g.stroke(); }
    for (const b of sim.bricks) if (b.alive) {
      g.fillStyle = colours[b.row % 4]; g.fillRect(b.x, b.y, b.w, b.h);
      g.fillStyle = '#ffffff55'; g.fillRect(b.x + 1, b.y + 1, b.w - 2, 2);
      g.fillStyle = '#00000035'; g.fillRect(b.x, b.y + b.h - 2, b.w, 2);
    }
    if (!calm()) {
      trail.forEach((p, i) => { g.fillStyle = colours[sim.board % 4]; g.globalAlpha = i / trail.length * .22; g.beginPath(); g.arc(p.x, p.y, 2.5 * i / trail.length, 0, Math.PI * 2); g.fill(); });
      for (const p of particles) { g.globalAlpha = p.life / .65; g.fillStyle = colours[p.row % 4]; g.fillRect(p.x, p.y, 3, 3); }
    }
    g.globalAlpha = 1;
    const px = calm() ? WIDTH / 2 : sim.paddle;
    g.fillStyle = '#ffffff24'; g.fillRect(px - PADDLE_W / 2 - 3, PADDLE_Y - 2, PADDLE_W + 6, 9);
    g.fillStyle = colours[0]; g.fillRect(px - PADDLE_W / 2, PADDLE_Y - 4, PADDLE_W, 6);
    g.fillStyle = '#ffffffaa'; g.fillRect(px - PADDLE_W / 2 + 2, PADDLE_Y - 4, PADDLE_W - 4, 1);
    if (!sim.wait) {
      const b = sim.ball, shape = calm() ? { along: 1, across: 1 } : squashScale(squash);
      g.save(); g.translate(calm() ? WIDTH / 2 : b.x, calm() ? 150 : b.y); g.scale(shape.across, shape.along);
      g.fillStyle = '#ffffff'; g.beginPath(); g.arc(0, 0, b.r, 0, Math.PI * 2); g.fill(); g.restore();
    } else {
      g.fillStyle = colours[0]; g.font = '700 19px system-ui'; g.textAlign = 'center'; g.fillText('Board cleared', WIDTH / 2, 106);
    }
    if (glow > 0 && !calm()) { g.globalAlpha = glow * .12; g.fillStyle = colours[0]; g.fillRect(0, 0, WIDTH, HEIGHT); g.globalAlpha = 1; }
    board.textContent = `Board ${sim.board + (sim.wait ? 0 : 1)}`;
  }
  ctx.every(400, () => { visible = !!canvas.getClientRects().length; controls(); });
  ctx.every(1000, save);
  ctx.on('pause', save); ctx.on('suspend', save);
  ctx.frame((dt) => {
    if (dead || ctx.paused) return;
    const events = stepBreakout(sim, dt, { manual: manual(), x: aim });
    for (const e of events) {
      if (e.type === 'brick') { burst(e.x, e.y, e.row); squash = 1; }
      if (e.type === 'paddle') squash = 1;
      if (e.type === 'clear') pay(e);
      if (['brick', 'miss', 'deal'].includes(e.type)) save();
    }
    squash = Math.max(0, squash - dt * 10); glow = Math.max(0, glow - dt);
    for (const p of particles) { p.life -= dt; p.x += p.vx * dt; p.y += p.vy * dt; p.vy += 90 * dt; }
    for (let i = particles.length - 1; i >= 0; i--) if (particles[i].life <= 0) particles.splice(i, 1);
    // The paint cap uses real run time, so a speed upgrade cannot turn Gentle into a fast flicker.
    if (ctx.state.clock - lastPaint < (calm() ? 400 : 1000 / 30)) return;
    lastPaint = ctx.state.clock;
    if (calm()) { trail.length = 0; particles.length = 0; }
    else { trail.push({ x: sim.ball.x, y: sim.ball.y }); if (trail.length > 8) trail.shift(); }
    if (visible) draw();
  });
  controls(); draw();
  ctx.cleanup(() => { dead = true; save(); particles.length = trail.length = 0; });
  return {};
}
