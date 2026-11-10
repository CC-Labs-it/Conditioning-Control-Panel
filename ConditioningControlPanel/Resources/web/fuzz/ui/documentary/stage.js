import { subscriberPlan } from '../ads.js';

export function createStageClock(now = () => performance.now(), advancedMs = 0) {
  let started = null, pausedAt = null, pausedMs = 0;
  return {
    start() { if (started === null) started = now(); },
    pause(on) {
      if (on && pausedAt === null) pausedAt = now();
      if (!on && pausedAt !== null) { if (started !== null) pausedMs += now() - Math.max(started, pausedAt); pausedAt = null; }
    },
    elapsed() { return started === null ? 0 : Math.max(0, (pausedAt ?? now()) - started - pausedMs + advancedMs); },
    get started() { return started !== null; },
  };
}

/** The only wall-clock scheduler in the scene. It never asks for a rendered frame. */
export function createStage(ctx, anchor, manifest, { onNewRun, controlsAnchor = anchor, hours = 0, onEvent = () => {}, now } = {}) {
  const doc = anchor.ownerDocument;
  const make = (tag, className, parent, text) => {
    const node = doc.createElement(tag);
    node.className = className;
    if (text != null) node.textContent = text;
    parent.append(node);
    return node;
  };
  const stage = make('div', 'td-stage', anchor);
  const subscriber = subscriberPlan(ctx.caps);
  const ad = subscriber ? null : ctx.ads?.next({ ccpOnly: true });
  const controls = make('div', 'td-wall-controls td-stage-controls', controlsAnchor);
  controls.hidden = true;
  const adButton = make('button', 'td-stage-ad', controls, 'AD');
  adButton.type = 'button';
  adButton.setAttribute('aria-label', 'Open the Conditioning Control Panel advert');
  adButton.hidden = subscriber;
  const next = make('button', 'td-stage-new', controls, 'New run');
  next.type = 'button';
  const status = make('p', 'td-stage-status', stage, '');
  status.setAttribute('role', 'status');
  const clockFace = make('div', 'td-clock-face', stage);
  clockFace.hidden = true;
  make('span', '', clockFace, 'Time spent in an ending with nothing in it');
  const counter = make('time', '', clockFace, '5:00');
  const clock = createStageClock(now, Math.max(0, Number(hours) || 0) * 3600000);
  let tickId = null, disposed = false, arrived = false, explicitPause = false, leaving = false;
  function tick() {
    if (disposed || doc.hidden || explicitPause || ctx.paused) return;
    const elapsed = clock.elapsed();
    clockFace.hidden = elapsed < 300000;
    if (clockFace.hidden) return;
    const seconds = Math.floor(elapsed / 1000);
    const h = Math.floor(seconds / 3600);
    const m = Math.floor(seconds / 60) % 60;
    const s = String(seconds % 60).padStart(2, '0');
    counter.textContent = h ? `${h}:${String(m).padStart(2, '0')}:${s}` : `${m}:${s}`;
  }
  function schedule() {
    clearInterval(tickId);
    tickId = null;
    if (!disposed && clock.started && !doc.hidden && !explicitPause) {
      tick();
      tickId = setInterval(tick, 1000);
    }
  }
  adButton.addEventListener('click', () => {
    if (ctx.paused || !ad?.url || !arrived) return;
    const opened = ctx.openAdLink(ad.url);
    onEvent('stage-action', { id: 'ad', opened });
  });
  next.addEventListener('click', async () => {
    if (ctx.paused || !arrived || leaving) return;
    leaving = true;
    next.disabled = true;
    onEvent('stage-action', { id: 'new-run' });
    try { await onNewRun?.(); }
    catch (error) { console.error('Museum exit failed', error); }
    finally { leaving = false; next.disabled = false; }
  });
  next.addEventListener('pointerenter', () => { if (arrived && !ctx.paused) onEvent('stage-action', { id: 'hover-new-run' }); });
  function arrive() {
    if (arrived) return;
    arrived = true;
    stage.dataset.arrived = 'true';
    if (subscriber) onEvent('stage-action', { id: 'subscriber' });
  }
  return {
    arrive,
    beat(id, data = {}) {
      if (disposed) return;
      if (id === 'stage.pitch') arrive();
      if (id === 'stage.buttons') {
        arrive(); controls.hidden = false; clock.start(); schedule();
      }
      if (id === 'stage.timer') tick();
      if (id === 'stage.caption') status.textContent = String(data.text || '');
    },
    pause(on) { explicitPause = Boolean(on); clock.pause(explicitPause); schedule(); },
    visibility: schedule,
    dispose() { disposed = true; clearInterval(tickId); controls.remove(); stage.remove(); },
  };
}
