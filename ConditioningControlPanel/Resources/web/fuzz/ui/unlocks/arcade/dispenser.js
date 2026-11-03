import { dispenserState, dispenserStep, dispense, catchPrize, CATCH_MS, DROP_MS } from '../../../sim/arcade.js';

const CSS = `
.prize-machine{position:relative;height:256px;overflow:hidden;color:var(--ink);font:600 12px/1.2 var(--font,system-ui);text-align:center;user-select:none}
.prize-globe{position:absolute;top:4px;left:calc(50% - 65px);width:130px;height:105px;border:3px solid #352835;border-radius:65px 65px 30px 30px;background:#b5e5e4;box-shadow:inset 7px 7px 0 #ffffff6b;overflow:hidden}
.prize-marble{position:absolute;width:29px;height:29px;border:2px solid #352835;border-radius:50%;background:var(--niche);box-shadow:inset 5px 4px 0 #ffffff50}
.prize-base{position:absolute;top:91px;left:calc(50% - 72px);width:144px;height:67px;background:var(--niche);border:3px solid #352835;border-radius:12px 12px 25px 25px;box-sizing:border-box}
.prize-crank{position:absolute;top:102px;left:calc(50% - 23px);width:46px;height:46px;border:3px solid #352835;border-radius:50%;background:#ffe1a2;color:#352835;cursor:pointer;font-size:28px;line-height:1;padding:0}
.prize-crank:disabled{cursor:default}.prize-crank::before{content:'';display:block;width:34px;height:9px;margin:auto;border:2px solid #352835;border-radius:8px;background:#fff1cc;transform:rotate(-30deg)}
.prize-chute{position:absolute;left:calc(50% - 18px);top:149px;width:36px;height:17px;background:#352835;border-radius:0 0 12px 12px}
.prize-pad{position:absolute;top:211px;left:calc(50% - 68px);width:136px;height:16px;border:2px solid #352835;border-radius:50%;background:#deb88b}
.prize-bubble{position:absolute;top:169px;left:calc(50% - 25px);width:50px;height:50px;border:2px solid #352835;border-radius:50%;background:radial-gradient(circle at 30% 25%,#fff 0 7%,#fff7 9%,#b4e3edab 35%,#ad83cebb);box-shadow:inset -4px -5px 0 #a279bb70;cursor:pointer;color:#352835;font-size:25px;padding:0;touch-action:manipulation}
.prize-bubble[hidden]{display:none}.prize-bubble::before{content:'✦';display:block}.prize-note{position:absolute;bottom:0;left:4px;right:4px;min-height:25px;display:grid;place-items:center;font-variant-numeric:tabular-nums}
.prize-catch{position:absolute;top:228px;height:3px;left:calc(50% - 52px);width:104px;background:var(--niche);transform-origin:left;border-radius:3px}
`;

export function mountDispenser(ctx) {
  ctx.css(CSS);
  const dock = ctx.dock({ title: 'Prize Dispenser', edge: 'left' });
  const root = ctx.el('section', '', null, dock); root.className = 'prize-machine'; root.dataset.arcade = 'dispenser';
  const add = (tag, cls, text, host = root) => { const el = ctx.el(tag, '', text, host); el.className = cls; return el; };
  const globe = add('div', 'prize-globe');
  for (let i = 0; i < 9; i++) {
    const m = add('i', 'prize-marble', null, globe);
    m.style.left = `${6 + (i % 3) * 38 + (Math.floor(i / 3) % 2) * 5}px`; m.style.top = `${15 + Math.floor(i / 3) * 26}px`;
    m.style.background = ['var(--niche)', '#ffe1a2', '#ceadc9'][i % 3];
  }
  add('div', 'prize-base'); add('div', 'prize-chute'); add('div', 'prize-pad');
  const crank = add('button', 'prize-crank'); crank.type = 'button'; crank.setAttribute('aria-label', 'Turn crank');
  const bubble = add('button', 'prize-bubble'); bubble.type = 'button'; bubble.setAttribute('aria-label', 'Catch prize bubble');
  const meter = add('div', 'prize-catch');
  const note = add('div', 'prize-note'); note.setAttribute('role', 'status');
  const s = dispenserState(ctx.store.get('machine')); ctx.store.set('machine', s);
  let clock = ctx.state.clock, message = '', last = '', crankAnimation = null;
  const enabled = (id) => ctx.has(id) && ctx.switches.isOn(id);
  const cooldown = () => enabled('dispenser_faster') ? 15000 : enabled('dispenser_fast') ? 22000 : 30000;
  let previousCooldown = cooldown();
  const refillUpgrade = () => {
    const next = cooldown();
    if (next < previousCooldown) s.cooldown *= next / previousCooldown;
    previousCooldown = next;
    draw();
  };
  function draw() {
    root.dataset.phase = s.phase;
    crank.disabled = ctx.paused || s.phase !== 'ready';
    bubble.hidden = !['drop', 'catch', 'dissolve'].includes(s.phase);
    bubble.disabled = ctx.paused || s.phase !== 'catch';
    bubble.style.opacity = s.phase === 'dissolve' ? String(s.left / 650) : '1';
    const y = s.phase === 'drop' && !ctx.gentle ? -45 * (s.left / DROP_MS) : 0;
    bubble.style.transform = `translateY(${y}px) scale(${s.phase === 'dissolve' && !ctx.gentle ? 1 + (1 - s.left / 650) * .2 : 1})`;
    meter.hidden = s.phase !== 'catch'; meter.style.transform = `scaleX(${Math.max(0, s.left / CATCH_MS)})`;
    const text = s.phase === 'ready' ? 'Turn the crank' : s.phase === 'drop' ? 'Here it comes' : s.phase === 'catch' ? 'Catch it' :
      s.phase === 'dissolve' ? 'Gone in a puff.' : `${message || 'Try the next one.'}  ${Math.ceil(s.cooldown / 1000)}s`;
    if (text !== last) { note.textContent = text; last = text; }
  }
  crank.addEventListener('click', () => {
    if (ctx.paused || !dispense(s, cooldown())) return;
    message = '';
    crankAnimation?.cancel();
    if (!ctx.gentle) crankAnimation = ctx.anim(crank, [{ rotate: '0deg' }, { rotate: '180deg' }], { duration: 480 });
    draw();
  });
  bubble.addEventListener('click', () => {
    if (ctx.paused || !catchPrize(s)) return;
    const r = bubble.getBoundingClientRect(), rates = ctx.rates();
    const got = ctx.pay(Math.max(rates.fps * 12, rates.click * 24), 'dispenser', { x: r.left + r.width / 2, y: r.top });
    message = `+${ctx.fmt(got)} ${ctx.brand.currency}`;
    ctx.audio.play('assets/audio/pop/Pop.mp3', { bus: 'sfx', gain: .13 });
    draw();
  });
  ctx.frame(() => {
    if (ctx.gentle && crankAnimation) { crankAnimation.cancel(); crankAnimation = null; }
    const now = ctx.state.clock; dispenserStep(s, now - clock); clock = now; draw();
  });
  ctx.on('pause', draw); ctx.on('resume', draw); ctx.on('suspend', draw);
  ctx.on('buy', refillUpgrade); ctx.on('switch', refillUpgrade);
  draw(); return {};
}
