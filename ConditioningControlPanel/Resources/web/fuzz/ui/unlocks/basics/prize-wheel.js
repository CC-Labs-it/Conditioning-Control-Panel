import { SECTORS, SPIN_MS, WHEEL_WAIT, earnsRespin, restoreWheelSpin, rollWheel, sectorsFor, wheelAngle, wheelBase, wheelRank, wheelRequest, wheelTravel } from '../../../sim/prize-wheel.js';
import { createPrizeWheelView } from './prize-wheel-view.js';
import { prizeMult, wheelDraw } from '../prestige/boosts.js';  // prestige hooks (Renewal Sky, boosts lane)
import { createWheelMedia } from './prize-wheel-media.js';
import { bindWheelDrag } from './prize-wheel-drag.js';
import { autopilot, wheelMean } from '../arcade/autopilot.js';  // prestige hook: Autopilot (idle_6)
import { sfx } from './fx.js';

const firstAngle = wheelAngle(-(SECTORS[0].start + SECTORS[0].end) / 2);

export function mountPrizeWheel(ctx) {
  let gesture;
  const dock = ctx.dock({ width: 272, onMin: hidden => { if (hidden) gesture?.cancel(); } });
  const hostDocument = dock.ownerDocument;
  /* Journey ranks, read live (a rank can be bought while the wheel is open): 1 Respin, 2 Big Wedge. */
  const face = () => sectorsFor(wheelRank(ctx.state) >= 2);
  const sectorsOf = spinRound => sectorsFor(spinRound?.big === true);
  const view = createPrizeWheelView(ctx, dock, face());
  const media = createWheelMedia(ctx, dock, view);
  const { spinButton, result, baseLabel } = view;
  let round = restoreWheelSpin(ctx.store.get('spin'));
  const oldWait = Number(ctx.store.get('wait'));
  let wait = Number.isFinite(oldWait) ? Math.min(WHEEL_WAIT, Math.max(0, oldWait)) : 0;
  const oldAngle = ctx.store.get('angle');
  let angle = Number.isFinite(oldAngle) ? wheelAngle(oldAngle) : firstAngle;
  let available = wheelBase(ctx.state, ctx.rates());
  let previousWait = -1, wasVisible = false, wasGentle = false;
  let holding = false, handTurn = 0, handSpeed = 0, releaseTurn = 0;
  const auto = autopilot(ctx, { dueMs: WHEEL_WAIT * 1000 });  // prestige hook: Autopilot (idle_6), a spin left untaken
  const last = ctx.store.get('receipt');
  const resultText = (id, amount) => id === 'nothing' && !amount ? 'Nothing' : '+' + ctx.brand.money(Math.round(amount));
  result.textContent = last && SECTORS.some(s => s.id === last.id) && Number.isFinite(last.amount)
    ? resultText(last.id, last.amount) : 'Top prize 10×';

  function controls() {
    const seconds = Math.ceil(wait);
    spinButton.disabled = holding || !!round || seconds > 0;
    view.grip.toggleAttribute('data-ready', !round && seconds === 0);
    spinButton.textContent = round ? 'Spinning…' : seconds > 0 ? `Spin in ${seconds}s` : 'Spin';
    dock.dock?.setReady(!spinButton.disabled, 'Free spin ready');
    if (!round) { view.setSectors(face()); available = wheelBase(ctx.state, ctx.rates()); }
    const caption = '1× = ' + ctx.brand.money(round?.base ?? available);
    if (baseLabel.textContent !== caption) baseLabel.textContent = caption;
    previousWait = seconds;
  }

  function pose() {
    if (dock.dock?.visible === false || hostDocument.hidden) { wasVisible = false; return; }
    const gentle = ctx.gentle || ctx.reduced;
    const progress = round ? Math.min(1, round.elapsed / round.duration) : 1;
    const travel = round && !gentle ? wheelTravel(round, progress) : null;
    view.setPose({
      angle: round ? round.from + (travel?.degrees ?? 0) : angle + handTurn,
      sweep: round ? releaseTurn + (travel?.degrees ?? 0) : handTurn,
      velocity: gentle ? 0 : (travel?.velocity ?? handSpeed),
    });
    wasVisible = true;
    wasGentle = gentle;
  }

  function finish() {
    const done = round, { id, end, base } = done;
    const sector = sectorsOf(done).find(s => s.id === id);
    round = null;
    ctx.store.set('spin', null);
    angle = wheelAngle(end);
    ctx.store.set('angle', angle);
    if (earnsRespin(done, wheelRank(ctx.state))) {
      // Respin: Nothing hands the spin straight back. The cooldown already runs, any activity bank waits for the respin.
      result.textContent = 'Respin';
      sfx(ctx, 'sfx/rank_settle.mp3', 0.4);
      spin(undefined, true);
      return;
    }
    let amount = 0;
    if (sector.mult > 0 || ctx.state.store.journey?.banks?.wheel) {
      const anchor = dock.dock?.visible === false ? dock.dock.chip : spinButton;
      const r = anchor?.getBoundingClientRect();
      const at = r ? { x: r.left + r.width / 2, y: r.top } : null;
      amount = ctx.pay(sector.mult > 0 ? wheelRequest(ctx.state, base * sector.mult) * prizeMult(ctx.prestige) : 0, 'wheel', at);  // prestige hook: Lucky/Charm
    }
    ctx.store.set('receipt', { id, amount });
    ctx.emit('sticker', { key: 'wheel' });   // Sticker Book: Round and Round
    result.textContent = resultText(id, amount);
    sfx(ctx, sector.mult >= 2 ? 'sfx/pb_fanfare.mp3' : 'sfx/rank_settle.mp3', 0.5);
    controls();
    pose();
    media.landed(sector);
  }

  /* Autopilot (idle_6): a free spin left untaken for a whole cooldown pays half the wheel's average prize, uses the spin. */
  function autoSpin() {
    const r = (dock.dock?.visible === false ? dock.dock.chip : spinButton)?.getBoundingClientRect();
    const at = r ? { x: r.left + r.width / 2, y: r.top } : null;
    const base = wheelBase(ctx.state, ctx.rates());
    const amount = ctx.pay(wheelRequest(ctx.state, base * wheelMean(face())) * prizeMult(ctx.prestige) * auto.share, 'wheel', at);
    wait = WHEEL_WAIT;
    ctx.store.set('wait', wait);
    ctx.store.set('receipt', { id: 'auto', amount });
    result.textContent = 'Autopilot +' + ctx.brand.money(Math.round(amount));
    controls();
  }

  function spin(omega, respin = false) {
    if (ctx.paused || (wait > 0 && !respin) || round || holding) return;
    auto.reset();
    const sectors = face();
    view.setSectors(sectors);
    // Lock the amount the player saw before drawing their outcome.
    const plan = rollWheel(wheelDraw(ctx.prestige, ctx.rng, ctx.store, sectors), angle, omega, sectors);  // prestige hook: Second Spin, No Nothing, Jackpot (luck_4/7/10)
    round = { ...plan, base: available, elapsed: 0, duration: ctx.gentle || ctx.reduced ? 400 : (plan.duration ?? SPIN_MS), ...(respin ? { respin: true } : {}) };
    if (!respin) wait = WHEEL_WAIT;
    ctx.store.set('spin', round);
    ctx.store.set('wait', wait);
    controls();
    pose();
  }

  if (round) {
    ctx.store.set('spin', round);
  }
  spinButton.addEventListener('click', () => { if (!holding) { releaseTurn = 0; spin(); } });
  gesture = bindWheelDrag(view.grip,{
    ready: () => !ctx.paused && !round && wait === 0 && dock.dock?.visible !== false && !hostDocument.hidden,
    move(turn, speed) {
      const starting = !holding;
      holding = true; handTurn = turn; handSpeed = speed;
      if (starting) controls();
      pose();
    },
    release(omega) {
      if (omega !== null) angle = wheelAngle(angle + handTurn);
      releaseTurn = handTurn;
      holding = false; handTurn = handSpeed = 0;
      if (omega !== null) spin(omega);
      else { controls(); pose(); }
    },
  });
  ctx.on('pause', gesture.cancel);
  ctx.on('suspend', gesture.cancel);
  ctx.on('ending', gesture.cancel);
  ctx.cleanup(gesture.dispose);
  if (view.dispose) ctx.cleanup(() => view.dispose());
  controls();
  pose();
  ctx.every(1000, () => { if (!round && wait === 0) controls(); });
  return {
    show() { dock.dock?.setVisible(true); dock.dock?.bringToFront(); pose(); },
    tick(dt) {
      const visible = dock.dock?.visible !== false && !hostDocument.hidden;
      const changedField = visible && !ctx.gentle && !ctx.reduced && view.advanceFields(dt);
      if (holding) handSpeed = Math.abs(handSpeed) < .0001 ? 0 : handSpeed * Math.exp(-dt * 10);
      if (wait > 0) {
        wait = Math.max(0, wait - dt);
        ctx.store.set('wait', wait);
        if (Math.ceil(wait) !== previousWait) controls();
      }
      if (round) {
        round.elapsed += dt * 1000;
        if (round.elapsed >= round.duration) finish();
        else pose();
      } else if (!holding && wait === 0 && auto.step(dt * 1000, true)) autoSpin();
      else if (holding || changedField || visible !== wasVisible || wasGentle !== (ctx.gentle || ctx.reduced)) pose();
    },
  };
}
