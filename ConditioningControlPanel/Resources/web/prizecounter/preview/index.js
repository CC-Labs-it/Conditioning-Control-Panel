/* preview/index.js - fills the HUD preview box (hud.previewSlot) for the focused item (lane B3-W5).
 *
 *   const pv = await createPreview(hud.previewSlot, { base, motion, reducedMotion, gentle, log });
 *   pv.focus(itemId | null); pv.dispose();
 *
 * Which preview: preview/catalog.js over assets/preview/index.json. When: preview/loader.js (rests 140 ms, drops
 * stale loads, disposes on leave). How: preview/stage.js for Rain, karts, the trail and chess (three.js, made on the
 * first 3D item), preview/pictures.js for frames, avatars, EMI desk sheets and paints. The slot shows as soon as an
 * item with a preview takes the focus, so a phone's framing does not jump when the art lands. */
import { previewSpec, STAGE_KINDS } from './catalog.js';
import { createPreviewLoader } from './loader.js';
import { fadeMs } from './frame.js';
import { openPicture } from './pictures.js';

export async function createPreview(slot, { base = './assets/', motion = 'full', reducedMotion = false, gentle = false,
  log = () => {}, fetchJson } = {}) {
  const get = fetchJson || ((f) => fetch(base + f).then((r) => (r.ok ? r.json() : null)));
  const [index, manifest] = await Promise.all([get('preview/index.json'), get('manifest.json')]);
  const doc = slot.ownerDocument;
  const box = doc.createElement('div');
  box.className = 'pc-pv-box';
  slot.append(box);
  slot.hidden = true;
  const fade = fadeMs({ motion, gentle });
  box.style.setProperty('--pc-pv-in', `${fade.in}ms`);
  box.style.setProperty('--pc-pv-out', `${fade.out}ms`);

  let stage = null, staging = null;
  const stageFor = () => (staging ||= import('./stage.js').then((m) => (stage = m.createStage(box, { base, motion, reducedMotion, gentle }))));
  const loader = createPreviewLoader({
    resolve: (id) => previewSpec(id, index, manifest),
    open: async (spec) => (STAGE_KINDS.has(spec.kind) ? (await stageFor()).open(spec) : openPicture(box, spec, { base, motion, gentle })),
    release: (kind) => { if (stage && STAGE_KINDS.has(kind)) stage.release(kind); },
    empty: (none) => { slot.hidden = none; },
    outMs: fade.out,
    log,
  });
  const onResize = () => { if (stage) stage.resize(); };
  const win = doc.defaultView;
  if (win) win.addEventListener('resize', onResize);

  return {
    focus: (id) => loader.focus(id),
    get id() { return loader.id; },
    dispose() {
      loader.dispose();
      if (win) win.removeEventListener('resize', onResize);
      if (staging) staging.then((s) => s.dispose(), () => {});
      box.remove();
    },
  };
}
