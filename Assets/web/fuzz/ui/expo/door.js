import {expoAllowed} from '../edition.js';
import {assetURL} from './asset-base.js';
import {warmEntrance} from './entrance.js';

export {warmEntrance, clearEntrance} from './entrance.js';
export {createPortal} from './portal.js';
export {bindExpoMenus, bindExpoPreviewButton} from './controller-menu.js';
export {bindExpoSoundUnlock} from './sound-unlock.js';
// The pause card's Options view offers the look options outside a visit too (ui/options.js). Both files are plain.
export {createCameraPreferences} from './camera-preferences.js';
export {mountCameraOptions} from './camera-options.js';

// Called after the visitor accepts the transmission.
export function prewarm() {
  warmEntrance(true);
}

export function playSpit(ctx) {
  if (expoAllowed()) return ctx.audio.play(assetURL('audio/spit.ogg'), {bus:'sfx', gain:.55});
}

export async function openExpo(ctx, parent, options) {
  if (!expoAllowed()) throw new Error('The Expo transmission is unavailable.');
  let closed = false;
  let visit;
  ctx.cleanup(() => {
    closed = true;
    visit?.dispose();
  });
  const {createExpo} = await import('./scene.js');
  if (closed) throw new DOMException('Visit ended', 'AbortError');
  visit = createExpo(ctx, parent ?? ctx.layer('ending'), {onExit: () => visit.dispose(), ...options});
  return visit;
}
