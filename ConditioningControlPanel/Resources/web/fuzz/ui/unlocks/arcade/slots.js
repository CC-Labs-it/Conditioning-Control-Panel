import { slotState, slotStep, beginSpin, finishSpin, COIN_MS, SPIN_MS } from '../../../sim/arcade.js';
import { iconMarkup } from '../../icon-art.js';
import { sfx } from '../basics/fx.js';
import { drawSpiral, normalizeParams } from '../../../../arcademy/engine/loom/loomSpiral.js';

const CSS = `
.mini-slot{padding:6px 7px 7px;background:#442a42;border:3px solid #241c27;border-radius:16px 16px 10px 10px;box-shadow:inset 0 0 0 3px #d5b879,0 4px 0 #241c27;color:#fff0cc;position:relative;overflow:hidden;--hue-a:var(--niche);--hue-b:#a6bfd3;font:600 12px/1.3 var(--font,system-ui)}
.mini-slot-reels{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:4px;background:#1d1721;padding:5px;border:2px solid #d5b879;border-radius:8px}
.mini-slot-window{height:78px;overflow:hidden;background:#fff3db;border-radius:4px;position:relative}
.mini-slot-strip{will-change:transform}.mini-slot-symbol{height:78px;display:grid;place-items:center;overflow:hidden;box-sizing:border-box;padding:5px}
.mini-slot-symbol svg{width:100%;max-width:65px;height:65px}.mini-slot-symbol canvas{width:62px;height:62px;border-radius:50%}.mini-slot-symbol img{width:100%;height:68px;object-fit:cover;border-radius:3px}
.mini-slot-result{min-height:26px;display:grid;place-items:center;text-align:center;padding:3px 0 1px;font-size:12px;font-variant-numeric:tabular-nums}
.mini-slot-bottom{display:flex;align-items:center;justify-content:space-between;gap:8px}
.mini-slot-spin{min-height:44px;padding:8px 15px;border:2px solid #2b1e28;border-bottom-width:5px;border-radius:10px;background:#ffcd6e;color:#342234;font:800 14px var(--font,system-ui);cursor:pointer}
.mini-slot-spin:active{translate:0 2px;border-bottom-width:3px}.mini-slot-spin:disabled{opacity:.6;cursor:default}
.mini-slot-coins{letter-spacing:4px;font-size:20px;color:#ffd577}.mini-slot-next{font-size:10px;letter-spacing:0;min-height:14px;color:#e0c9db}
.mini-slot[data-win=true] .mini-slot-reels{border-color:#fff0a3;box-shadow:0 0 0 3px #e6b451}.mini-slot-odds{font-size:10px;text-align:center;opacity:.75;margin-top:3px}
`;

export function mountSlots(ctx) {
  ctx.css(CSS);
  const dock = ctx.dock({ title: 'Mini Slots', edge: 'right' });
  const root = ctx.el('section', '', null, dock); root.className = 'mini-slot'; root.dataset.arcade = 'mini_slots';
  const add = (tag, cls, text, host = root) => { const el = ctx.el(tag, '', text, host); el.className = cls; return el; };
  const reels = add('div', 'mini-slot-reels'); reels.setAttribute('aria-label', 'Three reels');
  const windows = Array.from({ length: 3 }, () => add('div', 'mini-slot-window', null, reels));
  const result = add('div', 'mini-slot-result'); result.setAttribute('role', 'status');
  const bottom = add('div', 'mini-slot-bottom');
  const wallet = add('div', '', null, bottom), coins = add('div', 'mini-slot-coins', null, wallet), next = add('div', 'mini-slot-next', null, wallet);
  const spin = add('button', 'mini-slot-spin', 'Spin', bottom); spin.type = 'button';
  add('div', 'mini-slot-odds', '2 wins in every 6 spins');
  const s = slotState(ctx.store.get('machine')); ctx.store.set('machine', s);
  let alive = true, picture = null, pictureEpoch = 0, clock = ctx.state.clock, rolling = false, gentle = ctx.gentle, animations = [];

  function symbol(n, parent) {
    const cell = add('div', 'mini-slot-symbol', null, parent);
    if (n === 0) {
      const canvas = ctx.el('canvas', '', null, cell); canvas.width = canvas.height = 72;
      drawSpiral(canvas.getContext('2d'), 72, normalizeParams({ arms: 4, turns: 2, colors: [ctx.palette().a], bg: '#fff3db' }), 0);
    } else if (n === 3 && picture) {
      const img = ctx.el('img', '', null, cell); img.alt = 'Picture'; img.src = picture.url;
      img.addEventListener('error', () => { img.remove(); cell.innerHTML = art('frame'); }, { once: true });
    } else cell.innerHTML = art(n === 1 ? 'heart' : n === 2 ? 'crown' : 'frame');
  }
  function art(shape) { return `<svg viewBox="0 0 64 64" aria-hidden="true">${iconMarkup({ shape })}</svg>`; }
  const stopAnimations = () => { for (const a of animations) a.cancel(); animations = []; };
  function showReels(values, animate = false) {
    stopAnimations();
    windows.forEach((win, index) => {
      win.replaceChildren(); const strip = add('div', 'mini-slot-strip', null, win);
      if (animate && !ctx.gentle) {
        const count = 9 + index * 2;
        for (let i = 0; i < count; i++) symbol((i + index) % 4, strip);
        symbol(values[index], strip);
        animations.push(ctx.anim(strip, [{ transform: 'translateY(0)' }, { transform: `translateY(${-count * 78}px)` }],
          { duration: Math.max(250, (s.pending?.left ?? SPIN_MS) - (2 - index) * 180), easing: 'cubic-bezier(.12,.72,.2,1)', fill: 'forwards' }));
      } else symbol(values[index], strip);
    });
  }
  function draw() {
    root.dataset.spinning = String(!!s.pending);
    coins.textContent = '●'.repeat(s.coins) + '○'.repeat(3 - s.coins);
    coins.setAttribute('aria-label', `${s.coins} of 3 coins`);
    next.textContent = s.coins === 3 ? 'Tray full' : `Next coin ${Math.ceil((COIN_MS - s.elapsed) / 1000)}s`;
    spin.disabled = !!s.pending || !s.coins || ctx.paused;
    spin.textContent = s.pending ? 'Spinning' : 'Spin';
    result.textContent = s.result;
  }
  function startVisual() { rolling = true; showReels(s.pending.reels, true); }
  spin.addEventListener('click', () => {
    if (ctx.paused || !beginSpin(s, ctx.rng)) return;
    root.dataset.win = 'false'; startVisual(); draw();
  });
  ctx.frame(() => {
    const now = ctx.state.clock; slotStep(s, Math.max(0, now - clock)); clock = now;
    if (gentle !== ctx.gentle) { gentle = ctx.gentle; if (s.pending) showReels(s.last); }
    const completed = finishSpin(s);
    if (completed) {
      rolling = false; showReels(s.last); root.dataset.win = String(completed.win);
      if (completed.win) {
        const rates = ctx.rates(), bounds = reels.getBoundingClientRect();
        const got = ctx.pay(Math.max(rates.fps * (completed.big ? 90 : 45), rates.click * (completed.big ? 160 : 80)), 'mini_slots',
          { x: bounds.left + bounds.width / 2, y: bounds.top + bounds.height / 2 });
        s.result = `${completed.big ? 'Spiral sweep' : 'Lovely little win'} +${ctx.fmt(got)} ${ctx.brand.currency}`;
        sfx(ctx, 'sfx/chime1.mp3', .22);
        if (!ctx.gentle) ctx.anim(reels, [{ transform: 'scale(1)' }, { transform: 'scale(1.05)', offset: .3 }, { transform: 'scale(1)' }], { duration: 450 });
      } else s.result = 'No match. The next coin is on its way.';
    }
    if (s.pending && !rolling) startVisual();
    draw();
  });
  async function loadPicture() {
    const epoch = ++pictureEpoch;
    const found = await ctx.pictures.next({});
    if (!alive || epoch !== pictureEpoch || !found?.url) return;
    picture = found;
    root.title = found.credit === 'Scrolller' ? 'Pictures via Scrolller' : '';
    if (!s.pending) showReels(s.last);
  }
  ctx.on('pictures', () => { picture = null; showReels(s.last); loadPicture().catch(() => {}); });
  ctx.on('pause', draw); ctx.on('resume', draw); ctx.on('suspend', draw);
  loadPicture().catch(() => {}); showReels(s.last); if (s.pending) startVisual(); draw();
  return { off() { alive = false; stopAnimations(); } };
}
