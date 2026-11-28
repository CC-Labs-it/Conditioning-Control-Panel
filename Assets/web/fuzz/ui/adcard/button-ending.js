import { createButtonTunnel } from './button-tunnel.js';
import { captureButtonScreen, createButtonFinale, BUTTON_FINALE_MS } from './button-finale.js';
import { createButtonSound } from './button-sound.js';
import { captureButtonBoard } from './button-board-capture.js';
import { captureButtonMedia } from './button-media.js';

const FREEZE_MS = 2400;
const FADE_MS = 2100;
const BLACK_HOLD_MS = 650;

export function isButtonFinaleReview(location = globalThis.location) {
  if (!location || !['localhost', '127.0.0.1'].includes(location.hostname)) return false;
  const query = new URLSearchParams(location.search);
  return query.has('buy') && query.get('buttonCheckpoint') === 'finale';
}

// The shop has already stopped its game clock. Freeze independently animated surfaces on that same frame.
function freezeBoard(doc) {
  for (const surface of doc.querySelectorAll('#world, #dock-strip')) {
    for (const animation of surface.getAnimations({ subtree: true })) {
      if (animation.playState === 'running') animation.pause();
    }
    for (const video of surface.querySelectorAll('video')) video.pause();
  }
}

export function buttonEndingTake(ctx, releaseWorld) {
  const doc = ctx.world.ownerDocument;
  const review = isButtonFinaleReview(doc.location);
  // The purchase closes the shelf before the take. Keep the same tiles visible until they enter the tunnel.
  ctx.css(`
    body:has([data-button-ending]) #shop[hidden]:has(.row){display:flex!important;pointer-events:none}
    body:has([data-button-ending]) :is(#cards,#tc-tips){visibility:hidden}
    body:has([data-button-ending]:not([data-button-ending="freeze"])) #game-controls > :not(#wake,#audio-controls){visibility:hidden}
    body:has([data-button-ending][data-board-covered]) :is(#world,#dock-strip){visibility:hidden}
  `);
  freezeBoard(doc);
  const root = ctx.el('div', 'position:fixed;inset:0;overflow:hidden;pointer-events:auto;isolation:isolate;z-index:84;touch-action:none;', null, 'ending');
  // Game effects can transform #world. The tunnel camera and control hitboxes need viewport coordinates.
  doc.body.insertBefore(root, doc.getElementById('cards'));
  root.dataset.buttonEnding = 'freeze';
  const screen = captureButtonScreen(ctx);
  const media = captureButtonMedia(ctx);
  const board = captureButtonBoard(ctx, media.members);
  screen.board = board;
  screen.mediaSurfaces = media.surfaces;

  let phase = 'freeze', elapsed = 0, captureStart = null, finished = false;
  let tunnel = null, finale = null, sound = null, black = null, offFrame = null;
  let resolve;
  const complete = new Promise(done => { resolve = done; });
  function enter(next) {
    phase = next;
    root.dataset.buttonEnding = next;
    elapsed = 0;
  }
  function beginFade() {
    if (phase !== 'finale') return;
    enter('black');
    black = ctx.el('div', 'position:absolute;inset:0;z-index:15;pointer-events:none;background:black;opacity:0', null, root);
  }
  function enterFinale() {
    enter('finale');
    root.dataset.boardCovered = '';
    sound.update(phase, 0, 1);
    tunnel?.dispose();
    tunnel = null;
    for (const picture of media.surfaces) picture.unclip?.();
    finale = createButtonFinale(ctx, root, screen, { onPress: hit => sound.press(hit), loop: review });
    if (review) {
      board.activate();
      media.activate();
      const recap = ctx.el('button', 'position:absolute;left:max(14px,env(safe-area-inset-left));bottom:max(14px,env(safe-area-inset-bottom));z-index:12;padding:9px 14px;border:1px solid #ffffff55;border-radius:22px;background:#151324d9;color:#f3e1ef;cursor:pointer;font:500 13px/1.2 system-ui', 'Show recap', root);
      recap.type = 'button';
      recap.dataset.buttonReviewControls = '';
      recap.addEventListener('click', () => { recap.remove(); beginFade(); });
    }
    finale.update(0);
  }
  function finish() {
    if (finished) return;
    finished = true;
    offFrame?.();
    tunnel?.dispose();
    finale?.destroy();
    media.dispose();
    board.dispose();
    sound?.stop();
    resolve();
  }
  ctx.cleanup(finish);
  ctx.on('pause', () => { root.inert = true; });
  ctx.on('resume', () => { root.inert = ctx.paused; });
  ctx.on('suspend', event => {
    root.inert = ctx.paused;
    if (event.on && event.reason === 'panic') {
      root.hidden = true;
      finish();
    }
  });
  try {
    sound = createButtonSound(ctx);
    if (review) enterFinale();
    else {
      tunnel = createButtonTunnel(ctx, root, [...board.surfaces, ...media.surfaces]);
      sound.update('freeze', 0, 0);
    }
    let previous = ctx.activeMs;
    function advance() {
      if (finished) return;
      const now = ctx.activeMs;
      const dt = (now - previous) / 1000;
      previous = now;
      elapsed += dt * 1000;
      if (phase === 'freeze') {
        sound.update('freeze', elapsed, 0);
        if (elapsed >= FREEZE_MS) {
          enter('tunnel');
          board.activate();
          media.activate();
        }
        return;
      }
      if (phase === 'tunnel' || phase === 'capture') {
        const state = tunnel.update(dt);
        if (!root.hasAttribute('data-board-covered') && elapsed >= 4000) root.dataset.boardCovered = '';
        if (state.capture > 0 && captureStart === null) {
          captureStart = elapsed;
          phase = 'capture';
          root.dataset.buttonEnding = phase;
        }
        sound.update(phase, elapsed - (captureStart ?? 0), state.progress);
        if (state.done) enterFinale();
        return;
      }
      if (phase === 'finale') {
        sound.update(phase, elapsed, 1);
        finale.update(review ? elapsed : Math.min(elapsed, BUTTON_FINALE_MS));
        if (!review && elapsed >= BUTTON_FINALE_MS) beginFade();
        return;
      }
      sound.update('black', elapsed, 1);
      black.style.opacity = String(Math.min(1, elapsed / FADE_MS));
      if (elapsed >= FADE_MS + BLACK_HOLD_MS) {
        finish();
        releaseWorld();
      }
    }
    offFrame = ctx.frame(() => {
      try { advance(); }
      catch (error) {
        finish();
        throw error;
      }
    });
  } catch (error) {
    finish();
    throw error;
  }
  return complete;
}
