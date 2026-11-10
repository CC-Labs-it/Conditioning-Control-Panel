import { subscriberPlan } from '../ads.js';

const ASSETS = new URL('../../assets/', import.meta.url);

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
export function createStage(ctx, anchor, manifest, { onNewRun, hours = 0, onEvent = () => {}, now } = {}) {
  const doc = anchor.ownerDocument;
  const make = (tag, className, parent, text) => {
    const node = doc.createElement(tag);
    node.className = className;
    if (text != null) node.textContent = text;
    parent.append(node);
    return node;
  };
  const art = (parent, slot) => {
    const image = make('img', 'td-stage-art', parent);
    image.alt = '';
    image.hidden = true;
    image.onload = () => { image.hidden = false; };
    image.onerror = () => { image.hidden = true; };
    image.dataset.slot = slot;
    return image;
  };
  const stage = make('div', 'td-stage', anchor);
  const board = make('div', 'td-adboard', stage);
  art(board, 's-adboard');
  const subscriber = subscriberPlan(ctx.caps);
  const ad = subscriber ? null : ctx.ads?.next({ ccpOnly: true, illustrated: true });
  if (ad?.illustration) {
    const illustration = make('img', 'td-ad-illustration', board);
    illustration.alt = ad.caption || ad.headline || '';
    illustration.onerror = () => illustration.remove();
    illustration.dataset.url = new URL(ad.illustration.replace(/^assets\//, ''), ASSETS).href;
  }
  board.hidden = subscriber;
  const screen = make('section', 'td-screen', stage);
  make('small', 'td-screen-kicker', screen, 'A documentary');
  make('h1', '', screen, manifest.banners.find(item => item.id === 'bn-title')?.title || 'A Natural History of the Tab');
  const pitch = make('p', 'td-pitch', screen, subscriber ? '' : ad?.headline || 'Conditioning Control Panel');
  const sub = make('p', 'td-pitch-sub', screen, subscriber ? '' : ad?.sub || '');
  const controls = make('div', 'td-stage-controls', screen);
  controls.hidden = true;
  const adButton = make('button', 'td-stage-ad', controls, 'AD');
  adButton.type = 'button';
  adButton.hidden = subscriber;
  const next = make('button', 'td-stage-new', controls, 'New run');
  next.type = 'button';
  const status = make('p', 'td-stage-status', screen, '');
  status.setAttribute('role', 'status');
  const booth = make('div', 'td-booth-window', stage);
  const boothImage = art(booth, 's-booth-a');
  make('span', '', booth, 'Recording booth');
  const clockFace = make('div', 'td-clock-face', stage);
  art(clockFace, 's-clock');
  clockFace.hidden = true;
  make('span', '', clockFace, 'Time spent in an ending with nothing in it');
  const counter = make('time', '', clockFace, '5:00');
  const clock = createStageClock(now, Math.max(0, Number(hours) || 0) * 3600000);
  let tickId = null, disposed = false, arrived = false, explicitPause = false;
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
  next.addEventListener('click', () => { if (!ctx.paused && arrived) { onEvent('stage-action', { id: 'new-run' }); onNewRun?.(); } });
  next.addEventListener('pointerenter', () => { if (arrived && !ctx.paused) onEvent('stage-action', { id: 'hover-new-run' }); });
  function arrive() {
    if (arrived) return;
    arrived = true;
    stage.dataset.arrived = 'true';
    for (const image of stage.querySelectorAll('img')) {
      image.src = image.dataset.url || new URL(`documentary/art/${image.dataset.slot}.webp`, ASSETS).href;
    }
    if (subscriber) onEvent('stage-action', { id: 'subscriber' });
  }
  return {
    arrive,
    beat(id, data = {}) {
      if (disposed) return;
      if (id === 'stage.pitch') { arrive(); pitch.hidden = sub.hidden = subscriber; }
      if (id === 'stage.buttons') {
        arrive(); controls.hidden = false; clock.start(); schedule();
      }
      if (id === 'stage.timer') tick();
      if (id === 'stage.booth' && ['a', 'b', 'c'].includes(data.state)) {
        boothImage.src = new URL(`documentary/art/s-booth-${data.state}.webp`, ASSETS).href;
        booth.dataset.state = data.state;
      }
      if (id === 'stage.caption') status.textContent = String(data.text || '');
    },
    pause(on) { explicitPause = Boolean(on); clock.pause(explicitPause); schedule(); },
    visibility: schedule,
    dispose() { disposed = true; clearInterval(tickId); stage.remove(); },
  };
}
