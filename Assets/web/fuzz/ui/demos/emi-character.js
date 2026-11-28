import { emiTileSprite } from '../emi-tile-sprite.js';

const poses = { happy:'body-idle', glee:'body-pet', annoyed:'body-sad', rage:'body-shock', sleep:'body-idle' };

// Keep the demo's gestures and pet sequence. The screen always carries her normal face.
export function companionDemo(kit, x, y, scale = 1, mood = 'happy') {
  const el = kit.add(x, y, `width:36px;height:36px;transform-origin:0 0;transform:scale(${scale})`);
  const setMood = name => { el.innerHTML = emiTileSprite(poses[name] || 'body-idle', 0, 0, 36); };
  setMood(mood);
  return { el, mood: setMood };
}

export function portraitDemo(kit, x, y, size) {
  return kit.add(x, y, `width:${size}px;height:${size}px`, emiTileSprite('body-idle', 0, 0, size));
}
