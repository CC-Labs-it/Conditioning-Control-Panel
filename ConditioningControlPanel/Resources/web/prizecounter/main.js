/* main.js - the page entry. mountPrizeCounter(host, { root }) boots the host seam, loads the state and wires the
 * model to the 3D scene (scene/index.js, lane B3-W2) and the HUD (hud/index.js, lane B3-W4). If the scene cannot load,
 * the HUD draws a plain list of the shelves and the page still buys. index.html mounts it on the real host, dev.html
 * on the mock. */

import { createShop } from './state/shop.js';
import { makeT } from './state/strings.js';
import { createHud } from './hud/index.js';
import { asHost } from './host.js';

const mintIdem = () => Array.from(crypto.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, '0')).join('');
const KEYS = { ArrowLeft: 'left', ArrowRight: 'right', ArrowUp: 'up', ArrowDown: 'down', q: 'tabPrev', e: 'tabNext',
  Enter: 'select', ' ': 'select', Escape: 'back', Backspace: 'back' };

export async function mountPrizeCounter(hostIn, { root = document.body } = {}) {
  const host = typeof hostIn.ready === 'function' && hostIn.hosted !== undefined ? hostIn : asHost(hostIn);
  const init = (await host.ready()) || {};
  const t = makeT(init.lexicon), motion = init.motion || 'full';
  root.classList.add('pc');
  root.replaceChildren();
  const canvas = document.createElement('canvas');
  canvas.className = 'pc-canvas';
  root.append(canvas);

  const shop = createShop({ request: host.request, newIdem: mintIdem, tab: init.tab || null });
  let scene = null;
  try {
    const m = await import('./scene/index.js');
    scene = await m.createScene(canvas, { base: './assets/', motion });
  } catch (e) { host.log(`prizecounter: scene unavailable, plain list (${(e && e.message) || e})`); }
  if (scene) scene.fx = await import('./fx/index.js').then((m) => m.installFx(scene, init)).catch((e) => host.log(`prizecounter: no juice (${(e && e.message) || e})`));
  canvas.hidden = !scene;

  const hud = createHud(root, { t, send: (i) => intent(i), goTab, list: !scene, gentle: !!init.gentle,
    motion: motion === 'full' && init.reducedMotion ? 'less' : motion });
  const tier = () => (shop.state() ? shop.state().tier : 0);
  const paint = () => hud.paint({ status: shop.status(), sp: shop.sp(), week: shop.state() && shop.state().week, tier: tier(),
    tabs: shop.tabs(), tabId: shop.tabId(), shelves: shop.shelves(), focus: shop.focus(), card: shop.card() });
  const itemPos = () => { const f = shop.focus(); return f && scene ? scene.screenPos(f.bay, f.slot) : null; };

  function apply(ch) {
    if (!ch) return;
    if (scene && (ch.tab || ch.effects || ch.status)) scene.setWallet(shop.sp(), tier());
    // showTab builds the shelves first; focus again once they hang, or the camera never leaves the wide shot.
    if (ch.tab && scene) scene.showTab(ch.tab.id, ch.tab.shelves).then(() => { const f = shop.focus(); if (f) scene.focus(f.bay, f.slot); });
    if (ch.focus && scene) scene.focus(ch.focus.bay, ch.focus.slot);
    if (ch.owned && scene && !ch.tab) scene.stamp?.(ch.owned);
    for (const e of ch.effects || []) if (e.type === 'wallet') { host.wallet(e.sp); hud.receipt(e.sp, itemPos()); }
    if (ch.close) host.close(ch.close.reason);
    if (ch.buy) ch.buy.then(apply);
    paint();
  }
  function intent(i) { apply(shop.intent(i)); }
  // A tab stamp jumps straight there: walk the model, then draw only where it lands.
  function goTab(id) { let ch = null, n = 8; while (shop.tabId() !== id && n--) ch = shop.intent({ type: 'tabNext' }); apply(ch); }
  const onKey = (e) => {
    const k = KEYS[e.key] || KEYS[e.key.toLowerCase()];
    if (k && !(e.target && e.target.tagName === 'BUTTON' && k === 'select')) { e.preventDefault(); intent({ type: k }); }
  };
  const onResize = () => scene && scene.resize();
  if (scene) scene.onIntent(intent); else window.addEventListener('keydown', onKey);
  window.addEventListener('resize', onResize);
  paint();
  apply(await shop.load());

  return {
    shop, scene, hud, intent,
    dispose() { window.removeEventListener('keydown', onKey); window.removeEventListener('resize', onResize); hud.dispose(); if (scene) scene.dispose(); root.replaceChildren(); },
  };
}
