import { PRIZES, prizeFor, prizePay } from '../../../sim/dispenser-rewards.js';
import { dispenserEffects } from './dispenser-effects.js';
import { dispenserState, dispenserStep, dispense, catchPrize, CATCH_MS } from '../../../sim/arcade.js';
import { createDispenserArt } from './dispenser-art.js';
import { sfx } from '../basics/fx.js';
import { autopilot } from './autopilot.js';  // prestige hook: Autopilot (idle_6)

const CSS = `
.prize-machine{position:relative;width:164px;height:260px;color:var(--ink);font:600 12px/1.2 var(--font,system-ui);text-align:center;user-select:none}
.prize-machine canvas{width:164px;height:260px;display:block;pointer-events:none}
.prize-crank,.prize-bubble{position:absolute;border:0;background:transparent;cursor:pointer;padding:0;touch-action:manipulation}
.prize-crank{left:42px;top:139px;width:80px;height:48px}
.prize-bubble{left:59px;top:183px;width:46px;height:46px}
.prize-crank:focus-visible,.prize-bubble:focus-visible{outline:2px solid var(--ink);border-radius:50%}
.prize-crank:disabled,.prize-bubble:disabled{cursor:default}.prize-bubble[hidden]{display:none}
.prize-note{position:absolute;bottom:0;left:0;right:0;min-height:18px;font-variant-numeric:tabular-nums}
.prize-catch{position:absolute;top:236px;height:3px;left:30px;width:104px;background:var(--niche);transform-origin:left;border-radius:3px}
`;

export function mountDispenser(ctx) {
  ctx.css(CSS);
  const effects = dispenserEffects(ctx);
  const dock = ctx.dock({ title: 'Prize Dispenser', edge: 'left', width: 164, reserveHeight: 270, onMin(hidden) { if(hidden) effects.clear(); } });
  const root = ctx.el('section', '', null, dock); root.dataset.dockSurface = ''; root.className = 'prize-machine'; root.dataset.arcade = 'dispenser';
  const add = (tag, cls, text, host = root) => { const el = ctx.el(tag, '', text, host); el.className = cls; return el; };
  const canvas = add('canvas', 'prize-art'); canvas.width = 328; canvas.height = 520;
  const crank = add('button', 'prize-crank'); crank.type = 'button'; crank.setAttribute('aria-label', 'Turn crank');
  const bubble = add('button', 'prize-bubble'); bubble.type = 'button'; bubble.setAttribute('aria-label', 'Catch prize bubble');
  const meter = add('div', 'prize-catch');
  const note = add('div', 'prize-note'); note.setAttribute('role', 'status');
  const s = dispenserState(ctx.store.get('machine')); ctx.store.set('machine', s);
  const art = createDispenserArt(ctx, canvas);
  let clock = ctx.state.clock, message = '', last = '';
  const enabled = (id) => ctx.has(id) && ctx.switches.isOn(id);
  const specialty = () => ctx.state.store.journey?.specs?.spec_dispenser;
  const cooldown = () => (enabled('dispenser_faster') ? 15000 : enabled('dispenser_fast') ? 22000 : 30000) * (specialty() === 'frequent' ? .8 : 1);
  let previousCooldown = cooldown();
  const auto = autopilot(ctx, { dueMs: 30000 });  // prestige hook: Autopilot (idle_6), a crank left unturned
  const MEAN = Object.values(PRIZES).reduce((m, p) => m + p.weight * p.multiplier, 0) / Object.values(PRIZES).reduce((m, p) => m + p.weight, 0);
  const refillUpgrade = () => {
    root.title = specialty() === 'jackpot'
      ? 'Red: Flashes 46%, 1x. Cyan: Subliminals 45%, 1x. Purple: Mind Drain 8%, 4x. Gold: Jackpot 1%, 30x.'
      : 'Red: Flashes 45%, 1x. Cyan: Subliminals 45%, 1x. Purple: Mind Drain 8%, 4x. Gold: Jackpot 2%, 10x.';
    const next = cooldown();
    if (next < previousCooldown) s.cooldown *= next / previousCooldown;
    previousCooldown = next;
    draw();
  };
  function draw() {
    root.dataset.phase = s.phase; root.dataset.prize = s.prize;
    bubble.setAttribute('aria-label', `Catch ${prizeFor(s.prize).label} prize`);
    dock.dock?.setReady(s.phase === 'ready' || s.phase === 'catch', s.phase === 'catch' ? 'Catch the prize' : 'Crank ready');
    crank.disabled = !art.ready || ctx.paused || s.phase !== 'ready';
    bubble.hidden = !['drop', 'catch', 'dissolve'].includes(s.phase);
    bubble.disabled = ctx.paused || s.phase !== 'catch';
    bubble.style.opacity = s.phase === 'dissolve' ? String(s.left / 650) : '1';
    art.draw(s);
    meter.hidden = s.phase !== 'catch'; meter.style.transform = `scaleX(${Math.max(0, s.left / CATCH_MS)})`;
    const text = s.phase === 'ready' ? 'Turn the crank' : s.phase === 'drop' ? 'Here it comes' : s.phase === 'catch' ? `Catch ${prizeFor(s.prize).label}` :
      s.phase === 'dissolve' ? 'Gone in a puff.' : `${message || 'Try the next one.'}  ${Math.ceil(s.cooldown / 1000)}s`;
    if (text !== last) { note.textContent = text; last = text; }
  }
  crank.addEventListener('click', () => {
    if (ctx.paused || !art.ready || !dispense(s, cooldown(), ctx.rng)) return;
    auto.reset();
    if (specialty() === 'jackpot' && s.prize === 'gold' && ctx.rng() < .5) s.prize = 'red';
    art.start();
    if (ctx.has('sound') && ctx.switches.isOn('sound')) ctx.audio.play('assets/arcade/dispenser/dispenser-crank.mp3', { bus: 'sfx', gain: .16 });
    message = '';


    draw();
  });
  bubble.addEventListener('click', () => {
    if (ctx.paused || !catchPrize(s)) return;
    const r = bubble.getBoundingClientRect(), rates = ctx.rates();
    const got = ctx.pay(prizePay(s.prize, rates) * (specialty() === 'jackpot' && s.prize === 'gold' ? 3 : 1), 'dispenser', { x: r.left + r.width / 2, y: r.top });
    art.catch();
    ctx.emit('sticker', { key: 'catch' });   // Sticker Book: Caught It
    if(dock.dock.visible) effects.start(s.prize,r);
    message = `${prizeFor(s.prize).label} +${ctx.fmt(got)} ${ctx.brand.currency}`;
    sfx(ctx, 'pop/Pop.mp3', .13);
    draw();
  });
  ctx.frame(() => {

    const now = ctx.state.clock, step = now - clock; dispenserStep(s, step); clock = now;
    if (auto.step(step, s.phase === 'ready')) {  // prestige hook: Autopilot (idle_6): half the average prize, the machine stays ready
      const r = root.getBoundingClientRect();
      ctx.pay(prizePay('red', ctx.rates()) * MEAN * auto.share, 'dispenser', { x: r.left + r.width / 2, y: r.top });  // the number flies to the counter
    }
    effects.draw(); draw();
  });
  ctx.on('pause', draw); ctx.on('resume', draw); ctx.on('suspend', draw);
  ctx.on('buy', refillUpgrade); ctx.on('switch', refillUpgrade);
  refillUpgrade();
  ctx.cleanup(() => dock.dock?.setReady(false));
  return {};
}
