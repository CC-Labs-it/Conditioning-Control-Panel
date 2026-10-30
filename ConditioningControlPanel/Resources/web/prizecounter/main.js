/* main.js - the page entry. mountPrizeCounter(host, { root }) boots the host seam, loads the state and wires the
 * model to the 3D scene (scene/index.js, lane B3-W2) and the HUD (hud/index.js, lane B3-W4). If the scene cannot load,
 * the HUD draws a plain list of the shelves and the page still buys. index.html mounts it on the real host, dev.html
 * on the mock. preview/index.js (lane B3-W5) fills the HUD's preview box for the focused item. */

import { createShop, windowShelves } from './state/shop.js';
import { makeT } from './state/strings.js';
import { createHud } from './hud/index.js';
import { asHost } from './host.js';
import { createPressGuard, keyName } from './state/press-guard.js';

const mintIdem = () => Array.from(crypto.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, '0')).join('');
const KEYS = { ArrowLeft: 'left', ArrowRight: 'right', ArrowUp: 'up', ArrowDown: 'down', q: 'tabPrev', e: 'tabNext',
  Enter: 'select', ' ': 'select', Escape: 'back', Backspace: 'back' };

export async function mountPrizeCounter(hostIn, { root = document.body } = {}) {
  const host = typeof hostIn.ready === 'function' && hostIn.hosted !== undefined ? hostIn : asHost(hostIn);
  const init = (await host.ready()) || {};
  const t = makeT(init.lexicon), motion = init.motion || 'full';
  root.classList.add('pc');
  root.dataset.motion = motion;
  root.replaceChildren();
  const canvas = document.createElement('canvas');
  canvas.className = 'pc-canvas';
  root.append(canvas);

  const shop = createShop({ request: host.request, newIdem: mintIdem, tab: init.tab || null, wears: init.wears });
  // Stray keys never spend (desk bug 2026-10-30): Space, Enter and pad A confirm only as a deliberate press and
  // release after the counter has held focus a while (state/press-guard.js). Mounting counts as gaining focus.
  const guard = createPressGuard({ now: () => performance.now(), focused: document.hasFocus() });
  const onFocus = () => guard.focus(true), onBlur = () => guard.focus(false);
  window.addEventListener('focus', onFocus);
  window.addEventListener('blur', onBlur);
  let scene = null;
  try {
    const m = await import('./scene/index.js');
    scene = await m.createScene(canvas, { base: './assets/', motion, guard });
  } catch (e) { host.log(`prizecounter: scene unavailable, plain list (${(e && e.message) || e})`); }
  if (scene) scene.fx = await import('./fx/index.js').then((m) => m.installFx(scene, init)).catch((e) => host.log(`prizecounter: no juice (${(e && e.message) || e})`));
  canvas.hidden = !scene;

  const hud = createHud(root, { t, send: (i) => intent(i), goTab, signIn: () => host.signIn(), list: !scene, gentle: !!init.gentle, guard,
    motion: motion === 'full' && init.reducedMotion ? 'less' : motion });
  // The preview box (lane B3-W5): the focused item's model or picture, loaded when the focus rests, freed when it leaves.
  const preview = await import('./preview/index.js')
    .then((m) => m.createPreview(hud.previewSlot, { base: './assets/', motion, reducedMotion: !!init.reducedMotion, gentle: !!init.gentle, log: host.log }))
    .catch((e) => { host.log(`prizecounter: no previews (${(e && e.message) || e})`); return null; });
  const tier = () => (shop.state() ? shop.state().tier : 0);
  const paint = () => {
    const f = shop.focus();
    if (preview) preview.focus(shop.status() === 'ready' && f && f.row ? f.row.id : null);
    hud.paint({ status: shop.status(), sp: shop.sp(), week: shop.state() && shop.state().week, tier: tier(),
      tabs: shop.tabs(), tabId: shop.tabId(), shelves: shop.shelves(), focus: shop.focus(), card: shop.card() });
    if (scene) scene.setInsets(hud.insets());   // a phone frames the shelf between the tabs and the preview strip
  };
  const itemPos = () => { const f = shop.focus(); return f && scene ? scene.screenPos(f.bay, f.slot) : null; };

  function apply(ch) {
    if (!ch) return;
    if (scene && (ch.tab || ch.effects || ch.status)) scene.setWallet(shop.sp(), tier());
    // showTab builds the shelves first; focus again once they hang, or the camera never leaves the wide shot.
    // Nothing focused (just opened): the wide shot, or the first bay on a portrait view.
    if (ch.tab && scene) scene.showTab(ch.tab.id, ch.tab.shelves).then(() => {
      const f = shop.focus();
      if (f) scene.focus(f.bay, f.slot); else if (canvas.clientWidth < canvas.clientHeight) scene.frameBay(0);
    });
    if (ch.focus && scene) scene.focus(ch.focus.bay, ch.focus.slot);
    if (ch.owned && scene && !ch.tab) scene.stamp?.(ch.owned);
    for (const e of ch.effects || []) if (e.type === 'wallet') { host.wallet(e.sp); hud.receipt(e.sp, itemPos()); }
    if (ch.close) host.close(ch.close.reason);
    if (ch.signIn) host.signIn();
    if (ch.wear) host.wear(ch.wear.itemId);
    if (ch.status) {
      const out = ch.status === 'signed_out';
      root.classList.toggle('pc-signed-out', out);
      // Signed out: the art hangs as a window display (no prices) behind the sign-in button.
      // A portrait view frames one bay of it; the whole counter would be a strip across the middle.
      if (out && scene) scene.showTab('window', windowShelves(scene.items))
        .then(() => { if (canvas.clientWidth < canvas.clientHeight) scene.frameBay(0); }).catch((e) => host.log(`prizecounter: no window display (${(e && e.message) || e})`));
    }
    if (ch.buy) ch.buy.then(apply);
    paint();
  }
  // A key's select from the shelves never reaches an open card: the card takes keys only on its own buttons.
  function intent(i) { if (i && i.type === 'select' && i.key && shop.card()) return; apply(shop.intent(i)); }
  // A tab stamp jumps straight there: walk the model, then draw only where it lands.
  function goTab(id) { let ch = null, n = 8; while (shop.tabId() !== id && n--) ch = shop.intent({ type: 'tabNext' }); apply(ch); }
  const keyOf = (e) => KEYS[e.key] || KEYS[e.key.toLowerCase()];
  const onKey = (e) => {
    const k = keyOf(e);
    if (!k || (e.target && e.target.tagName === 'BUTTON' && k === 'select')) return;
    e.preventDefault();
    if (k === 'select') guard.down(keyName(e), e.repeat); else intent({ type: k });
  };
  const onKeyUp = (e) => {
    if (keyOf(e) !== 'select') return;
    if (guard.up(keyName(e)) && !(e.target && e.target.tagName === 'BUTTON')) intent({ type: 'select', key: true });
  };
  const onResize = () => { if (scene) { scene.resize(); scene.setInsets(hud.insets()); } };
  if (scene) scene.onIntent(intent); else { window.addEventListener('keydown', onKey); window.addEventListener('keyup', onKeyUp); }
  // The host's answer to an in-place Wear: a refusal replaces the card's "Wearing it now." (fix wave F1).
  if (typeof host.onWorn === 'function') host.onWorn((m) => apply(shop.worn(m && m.id, !!(m && m.ok === true))));
  window.addEventListener('resize', onResize);
  paint();
  apply(await shop.load());

  return {
    shop, scene, hud, intent,
    dispose() {
      window.removeEventListener('keydown', onKey); window.removeEventListener('keyup', onKeyUp);
      window.removeEventListener('focus', onFocus); window.removeEventListener('blur', onBlur);
      window.removeEventListener('resize', onResize); hud.dispose(); if (preview) preview.dispose(); if (scene) scene.dispose(); root.replaceChildren(); },
  };
}
