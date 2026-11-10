import { bimbodollTake, bimbodollAborted } from './bimbodoll.js';
import { prewarmBambiProjection } from './bambi-projection.js';
import { createBambiSky } from './bambi-sky.js';
import { createBambiAtmosphere } from './bambi-atmosphere.js';
import { createBubbleEntrance } from './bambi-entrance.js';
import { playBubbleFinale } from './bubble-finale.js';

export async function playBimbodoll(ctx, releaseWorld) {
  let active = true;
  let projection;
  function stopMedia() {
    if (!active) return;
    active = false;
    ctx.emit('ending-media', false);
    projection?.dispose();
  }
  ctx.cleanup(stopMedia);
  ctx.emit('ending-media', true);
  try {
    projection = prewarmBambiProjection(ctx);
    await bimbodollTake(ctx, () => projection.readyForPop);
    if (!active || bimbodollAborted()) return;
    releaseWorld();
    ctx.audio.muffleOthers?.(false);
    await enterBubble(ctx, projection, stopMedia);
  } finally {
    stopMedia();
  }
}

async function enterBubble(ctx, projection, stopMedia) {
  const stage = ctx.el('div', 'position:absolute;inset:0;overflow:hidden;background:#361126;pointer-events:auto;', null, 'ending');
  stage.className = 'bi-dome-stage';
  stage.setAttribute('role', 'group');
  stage.setAttribute('aria-label', 'Inside the Bambi bubble');
  ctx.css(`
    body:has(.bi-dome-stage) #cards, body:has(.bi-dome-stage) #fz-tools { visibility:hidden }
    body:has(.bi-dome-stage) #game-controls { gap:5px; }
    body:has(.bi-dome-stage) #game-controls > button,
    body:has(.bi-dome-stage) #audio-controls > button { color:#fff1f8; border:1px solid #ffdae450;
      border-radius:12px; background:#3f1c3d90; box-shadow:none; font:500 13px/1.2 system-ui,sans-serif; }
    body:has(.bi-dome-stage) #game-controls button:focus-visible { outline:2px solid #fff1f8; outline-offset:2px; }
    body:has(.bi-finale[data-black]):has(#pause[hidden]) #game-controls { opacity:0 }
  `);
  const entrance = createBubbleEntrance(ctx, stage);
  const motion = { gentle: ctx.gentle, reduced: ctx.reduced };
  projection.present(stage);
  const sky = createBambiSky(stage, { ...motion, projection });
  ctx.cleanup(sky.dispose);
  projection.updateNativeGifs(sky);
  const atmosphere = createBambiAtmosphere(stage, motion);
  ctx.cleanup(atmosphere.dispose);
  let seconds = 0;
  const stopFrames = ctx.frame(dt => {
    seconds += dt;
    projection.update(seconds);
    sky.update(dt);
    projection.updateNativeGifs(sky);
    atmosphere.update(dt, sky.camera);
    entrance.reveal();
  });

  function stopRoaming() {
    stopFrames();
    entrance.dispose();
    atmosphere.dispose();
    sky.dispose();
    stopMedia();
  }
  try {
    await playBubbleFinale(ctx, stage, { onBlack: stopRoaming });
  } finally {
    stopRoaming();
  }
}
