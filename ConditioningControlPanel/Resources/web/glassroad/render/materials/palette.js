/* render/materials/palette.js - section palette uniforms shared by road, rails and sky.
 * Two palettes (from, to) and a mix; col(i) is the crossfaded colour i as a TSL node. The from side
 * may itself be a weighted blend ([[id, w], ...]): a gate mid-fade starts from what was on screen.
 */
import { Color } from 'three/webgpu';
import { uniform, mix } from 'three/tsl';
import { PALETTES, FALLBACK_PALETTE } from '../look.js';

export function createPalette() {
  const a = [0, 1, 2].map(() => uniform(new Color()));
  const b = [0, 1, 2].map(() => uniform(new Color()));
  const t = uniform(1);
  const tmp = new Color();
  let fromKey = null, toName = null;
  const pal = (name) => PALETTES[name] || PALETTES[FALLBACK_PALETTE];
  const load = (us, list) => {
    for (let i = 0; i < 3; i++) {
      us[i].value.setRGB(0, 0, 0);
      for (const [name, w] of list) us[i].value.add(tmp.set(pal(name)[i]).multiplyScalar(w));
    }
  };
  return {
    t,
    col: (i) => mix(a[i], b[i], t),
    /** from: a palette id or [[id, weight], ...] (weights sum to 1); mixT 0 = from, 1 = to. */
    set(from, to, mixT) {
      const list = Array.isArray(from) ? from : [[from, 1]];
      const key = list.map(([n, w]) => n + ':' + w.toFixed(3)).join('|');
      if (key !== fromKey) { load(a, list); fromKey = key; }
      if (to !== toName) { load(b, [[to, 1]]); toName = to; }
      t.value = mixT;
    },
  };
}
