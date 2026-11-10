import { createBreakout, stepBreakout, snapshot, WIDTH, HEIGHT, PADDLE_Y, PADDLE_W, SHIELD_Y } from './breakout-core/game.js';
import { squashScale } from './breakout-core/contact.js';
import { bubbleInk } from './canvas-lettering.js';
import { breakoutAudio, createBrickPhoto, drawDrop, drawSpecialBrick } from './breakout-core/presentation.js';

export function mountBreakout(ctx) {
  const host = ctx.dock({ title: 'Mini Breakout', icon: '▰', width: 232, allowUnderStrip: true });
  host.dock?.setReady(false);
  host.style.overflow = 'clip';
  host.style.flexShrink = '0';
  const root = ctx.el('div', 'width:100%;border:1px solid var(--niche);border-radius:8px;overflow:clip;background:var(--panel);color:var(--ink);pointer-events:auto;', null, host);
  root.dataset.dockSurface = '';
  root.dataset.arcade = 'mini_breakout';
  const screen = ctx.el('div', 'position:relative;', null, root);
  const canvas = ctx.el('canvas', 'display:block;width:100%;aspect-ratio:280/196;background:#15151e;touch-action:none;', null, screen);
  // The top casing stays reachable when the footer sits below the viewport and the court owns the paddle.
  const rim = ctx.el('div', 'position:absolute;inset:0 0 auto;height:24px;border-top:4px solid var(--line);', null, screen);
  rim.dataset.dockSurface = '';
  rim.setAttribute('aria-hidden', 'true');
  rim.title = 'Move Mini Breakout. On touch, hold first.';
  canvas.dataset.dockSurface = '';
  canvas.width = WIDTH * 2; canvas.height = HEIGHT * 2; canvas.tabIndex = 0;
  canvas.setAttribute('aria-label', 'Mini Breakout. Hover to play. Drag the board to move it, or hold then drag on touch. Take control to play with touch or arrow keys. Drag the top edge to move while playing. Catch flames to pierce bricks and shields to save a missed ball. Riveted bricks take two hits.');
  const footer = ctx.el('div', 'display:flex;align-items:center;gap:5px;padding:3px 5px;border-top:1px solid var(--line);', null, root);
  const details = ctx.el('div', 'display:flex;flex:1;min-width:0;flex-wrap:wrap;align-items:baseline;gap:1px 5px;font-size:10px;line-height:1.2;overflow-wrap:anywhere;', null, footer);
  const board = ctx.el('span', 'font-weight:700;', '', details);
  const mode = ctx.el('span', 'color:var(--ink-soft);', '', details);
  const receipt = ctx.el('span', 'color:var(--niche-text);', '', details);
  const credit = ctx.el('small', 'flex-basis:100%;font:10px/1.2 system-ui;', '', details);
  credit.hidden = true;
  const toggle = ctx.el('button', 'flex:none;min-height:24px;padding:2px 5px;border:1px solid var(--line);border-radius:4px;background:var(--panel-short);color:var(--ink);font:inherit;font-size:10px;line-height:1.1;cursor:pointer;', 'Take control', footer);
  toggle.type = 'button';
  const g = canvas.getContext('2d'), sim = createBreakout(ctx.store.get('game'), ctx.state.store.journey?.specs?.spec_breakout);
  const doc=canvas.ownerDocument, audio=breakoutAudio(ctx), pictures=createBrickPhoto(ctx,canvas,credit);
  const particles = [], trail = [];
  let hovered = false, touchControl = false, aim = sim.paddle, visible = true, lastPaint = ctx.state.clock, squash = 0, glow = 0, dead = false, ending=false;
  let colours = ['#f48db8', '#bb9cf5', '#89dce4', '#9de7c5'];
  const calm = () => ctx.gentle || ctx.reduced;
  const manual = () => host.dock?.visible !== false && !calm() && (hovered || touchControl);
  const save = () => ctx.store.set('game', snapshot(sim));
  const palette = () => { const p = ctx.palette(); if (p.themes.length) colours = [p.a, p.b, p.a, p.b]; };
  palette(); ctx.on('palette', palette);
  function controls() {
    const playing = manual();
    mode.textContent = calm() ? 'Gentle autoplay' : playing ? 'Your paddle' : 'Autoplay';
    toggle.textContent = touchControl ? 'Autoplay' : 'Take control';
    toggle.disabled = calm();
    toggle.setAttribute('aria-pressed', String(touchControl));
    // Hover steers without a press. Only explicit paddle control claims a board drag.
    const ownsDrag = touchControl && !calm();
    canvas.toggleAttribute('data-dock-interactive', ownsDrag);
    canvas.style.cursor = ownsDrag ? 'crosshair' : '';
    rim.style.pointerEvents = ownsDrag ? 'auto' : 'none';
  }
  function point(e) {
    const r = canvas.getBoundingClientRect();
    if (r.width) aim = Math.max(PADDLE_W / 2, Math.min(WIDTH - PADDLE_W / 2, (e.clientX - r.left) * WIDTH / r.width));
  }
  canvas.addEventListener('pointerenter', (e) => { if (ctx.paused || e.pointerType !== 'mouse') return; hovered = true; point(e); controls(); });
  canvas.addEventListener('pointerleave', (e) => { if (e.pointerType === 'mouse') { hovered = false; controls(); } });
  canvas.addEventListener('pointermove', (e) => { if (!ctx.paused && manual()) point(e); });
  canvas.addEventListener('pointerdown', (e) => { if (ctx.paused || !touchControl || !manual()) return; point(e); if (e.pointerType === 'touch') { e.preventDefault(); canvas.setPointerCapture?.(e.pointerId); } });
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
      drawSpecialBrick(g,b);
      if (b.photo) pictures.draw(g,b);
    }
    if (!calm()) {
      trail.forEach((p, i) => { g.fillStyle = colours[sim.board % 4]; g.globalAlpha = i / trail.length * .22; g.beginPath(); g.arc(p.x, p.y, 2.5 * i / trail.length, 0, Math.PI * 2); g.fill(); });
      for (const p of particles) { g.globalAlpha = p.life / .65; g.fillStyle = colours[p.row % 4]; g.fillRect(p.x, p.y, 3, 3); }
    }
    g.globalAlpha = 1;
    if (sim.shield > 0) {
      g.fillStyle='#173f50'; g.fillRect(0,SHIELD_Y-2,WIDTH,5);
      g.strokeStyle='#80eff0'; g.lineWidth=1; g.beginPath(); g.moveTo(0,SHIELD_Y);
      for(let x=0;x<WIDTH;x+=12){g.lineTo(x+4,SHIELD_Y-1.5);g.lineTo(x+8,SHIELD_Y+1);g.lineTo(x+12,SHIELD_Y);}
      g.stroke();
    }
    for (const drop of sim.drops) drawDrop(g,drop);
    const px = calm() ? WIDTH / 2 : sim.paddle;
    g.fillStyle = '#ffffff24'; g.fillRect(px - PADDLE_W / 2 - 3, PADDLE_Y - 2, PADDLE_W + 6, 9);
    g.fillStyle = colours[0]; g.fillRect(px - PADDLE_W / 2, PADDLE_Y - 4, PADDLE_W, 6);
    g.fillStyle = '#ffffffaa'; g.fillRect(px - PADDLE_W / 2 + 2, PADDLE_Y - 4, PADDLE_W - 4, 1);
    if (!sim.wait) {
      const b = sim.ball, shape = calm() ? { along: 1, across: 1 } : squashScale(squash);
      g.save(); g.translate(calm() ? WIDTH / 2 : b.x, calm() ? 150 : b.y); g.scale(shape.across, shape.along);
      if (sim.fireball) {
        g.rotate(Math.atan2(b.vy,b.vx));
        g.fillStyle='#ff8a42'; g.beginPath(); g.moveTo(-12,-1); g.lineTo(-8,-4);
        g.lineTo(0,-4.5); g.quadraticCurveTo(8,0,0,4.5); g.lineTo(-6,4); g.closePath(); g.fill();
      }
      g.fillStyle = sim.fireball ? '#fff4b3' : '#ffffff'; g.beginPath(); g.arc(0, 0, b.r, 0, Math.PI * 2); g.fill(); g.restore();
    } else {
      const ink = bubbleInk(canvas);
      g.save();
      g.font = '700 19px ' + (ink?.font || 'system-ui');
      g.textAlign = 'center';
      if (ink) {
        g.lineJoin = 'round';
        g.strokeStyle = ink.edge;
        g.lineWidth = 2.5;
        g.strokeText('Board cleared', WIDTH / 2, 106);
      }
      g.fillStyle = ink?.fill || colours[0];
      g.fillText('Board cleared', WIDTH / 2, 106);
      g.restore();
    }
    if (glow > 0 && !calm()) { g.globalAlpha = glow * .12; g.fillStyle = colours[0]; g.fillRect(0, 0, WIDTH, HEIGHT); g.globalAlpha = 1; }
    const buffs=[];
    if (sim.fireball) buffs.push(`Fireball ${Math.ceil(sim.fireball)}s`);
    if (sim.shield) buffs.push(`Shield ${Math.ceil(sim.shield)}s`);
    board.textContent = [`Board ${sim.board + (sim.wait ? 0 : 1)}`, ...buffs].join(' · ');
  }
  const onScreen=()=>visible && host.dock?.visible !== false && !doc.hidden && !ctx.paused && !ending && !dead;
  function measure() {
    const r=canvas.getBoundingClientRect();
    visible=r.width>0 && r.height>0 && r.bottom>0 && r.right>0 && r.left<doc.defaultView.innerWidth && r.top<doc.defaultView.innerHeight;
    audio.audible(onScreen());
    if (!onScreen() && pictures.effects.active) pictures.effects.clear();
  }
  const observer=doc.defaultView.IntersectionObserver ? new doc.defaultView.IntersectionObserver(measure) : null;
  observer?.observe(canvas);
  doc.addEventListener('visibilitychange',measure);
  ctx.on('ending',()=>{ending=true;measure();});
  ctx.on('pause',measure); ctx.on('suspend',measure); ctx.on('resume',measure);
  ctx.on('pictures',draw);
  ctx.every(400, () => { measure(); controls(); });
  ctx.every(1000, save);
  ctx.on('pause', save); ctx.on('suspend', save);
  ctx.frame((dt) => {
    if (dead || ctx.paused || ending) return;
    const shown=onScreen();
    audio.audible(shown);
    if (shown && !sim.wait && sim.bricks.some(b=>b.alive && b.photo)) pictures.prepare(sim.board);
    const events = stepBreakout(sim, dt, { manual: manual(), x: aim, specialty: ctx.state.store.journey?.specs?.spec_breakout });
    for (const e of events) {
      if (shown) audio.cue(e);
      if (e.type === 'brick' || e.type === 'damage') { burst(e.x, e.y, e.row,e.type==='damage'?3:8); squash = 1; }
      if (e.type === 'brick' && e.photo) {
        credit.hidden=true;
        if (shown) pictures.hit(e);
      }
      if (e.type === 'catch' || e.type === 'save') burst(e.x,e.y,e.kind==='fireball'?0:2,12);
      if (e.type === 'paddle') squash = 1;
      if (e.type === 'clear') pay(e);
      if (['brick', 'damage', 'catch', 'save', 'miss', 'deal'].includes(e.type)) save();
    }
    if (pictures.effects.active) {
      if (shown && !calm()) pictures.effects.draw(); else pictures.effects.clear();
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
  controls(); measure(); draw();
  ctx.cleanup(() => { dead = true; observer?.disconnect(); doc.removeEventListener('visibilitychange',measure); save(); particles.length = trail.length = 0; });
  return {};
}
