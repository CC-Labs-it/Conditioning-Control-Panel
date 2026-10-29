/* main.js - the page entry. mountPrizeCounter(host, { root }) boots the host seam, loads the state and wires the
 * model to the 3D scene (scene/index.js, lane B3-W2). If the scene cannot load, a plain list of the shelves stands in
 * and the page still buys. index.html mounts it on the real host, dev.html on the mock. */

import { createShop } from './state/shop.js';
import { makeT, tabKey, itemName } from './state/strings.js';
import { asHost } from './host.js';

const mintIdem = () => Array.from(crypto.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, '0')).join('');
const KEYS = { ArrowLeft: 'left', ArrowRight: 'right', ArrowUp: 'up', ArrowDown: 'down', q: 'tabPrev', e: 'tabNext',
  Enter: 'select', ' ': 'select', Escape: 'back', Backspace: 'back' };
const el = (tag, cls, text) => { const n = document.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; };

export async function mountPrizeCounter(hostIn, { root = document.body } = {}) {
  const host = typeof hostIn.ready === 'function' && hostIn.hosted !== undefined ? hostIn : asHost(hostIn);
  const init = (await host.ready()) || {};
  const t = makeT(init.lexicon), motion = init.motion || 'full';
  root.classList.add('pc');
  root.replaceChildren();
  const canvas = el('canvas', 'pc-canvas'), hud = el('div', 'pc-hud');
  const wallet = el('p', 'pc-wallet'), tabs = el('nav', 'pc-tabs'), list = el('div', 'pc-list'), card = el('div', 'pc-card'), status = el('p', 'pc-status');
  hud.append(wallet, tabs, list, card, status);
  root.append(canvas, hud);

  const shop = createShop({ request: host.request, newIdem: mintIdem, tab: init.tab || null });
  let scene = null;
  try {
    const m = await import('./scene/index.js');
    scene = await m.createScene(canvas, { base: './assets/', motion });
  } catch (e) { host.log(`prizecounter: scene unavailable, plain list (${(e && e.message) || e})`); }
  canvas.hidden = !scene;

  function paint() {
    const c = shop.card(), f = shop.focus();
    wallet.textContent = t('shop_wallet', shop.sp());
    status.textContent = { loading: t('shop_loading'), offline: t('shop_offline'), signed_out: t('shop_signed_out') }[shop.status()] || '';
    tabs.replaceChildren(...shop.tabs().map((id) => {
      const b = el('button', 'pc-tab', t(tabKey(id)));
      if (id === shop.tabId()) b.setAttribute('aria-current', 'true');
      b.onclick = () => { let n = 8; while (shop.tabId() !== id && n--) intent({ type: 'tabNext' }); };
      return b;
    }));
    list.hidden = !!scene;
    if (!scene) list.replaceChildren(...shop.shelves().map((s, bay) => {
      const ul = el('ul', 'pc-shelf');
      s.rows.forEach((r, slot) => {
        if (!r) return;
        const b = el('button', 'pc-item', `${itemName(t, r.id)} - ${r.owned ? t('shop_owned') : t('shop_price', r.priceSp)}`);
        if (f && f.bay === bay && f.slot === slot) b.classList.add('pc-focus');
        b.onclick = () => intent({ type: 'pick', bay, slot });
        const li = el('li');
        li.append(b);
        ul.append(li);
      });
      return ul;
    }));
    card.hidden = !c;
    if (!c) return;
    const line = { buy: t('shop_price', c.priceSp), owned: t('shop_owned'), prime: t(c.reasonKey), short: t('shop_short', c.short),
      soon: t('shop_soon'), gone: t('shop_gone') }[c.face];
    card.replaceChildren(el('h2', null, itemName(t, c.itemId)), el('p', `pc-face pc-${c.face}`, line));
    if (c.noteKey) card.append(el('p', 'pc-note', t(c.noteKey)));
    const label = { buy: 'shop_buy', wear: 'shop_wear', prime: 'shop_prime' }[c.action];
    if (label) { const b = el('button', 'pc-act', t(label)); b.disabled = c.pending; b.onclick = () => intent({ type: 'select' }); card.append(b); }
    const k = el('button', 'pc-back', t('shop_keep_looking'));
    k.onclick = () => intent({ type: 'back' });
    card.append(k);
  }

  function apply(ch) {
    if (!ch) return;
    if (ch.tab && scene) scene.showTab(ch.tab.id, ch.tab.shelves);
    if (ch.focus && scene) scene.focus(ch.focus.bay, ch.focus.slot);
    if (ch.owned && scene && !ch.tab) scene.stamp?.(ch.owned);
    for (const e of ch.effects || []) if (e.type === 'wallet') host.wallet(e.sp);
    if (ch.close) host.close(ch.close.reason);
    if (ch.buy) ch.buy.then(apply);
    paint();
  }
  const intent = (i) => apply(shop.intent(i));
  const onKey = (e) => { const k = KEYS[e.key] || KEYS[e.key.toLowerCase()]; if (k) { e.preventDefault(); intent({ type: k }); } };
  const onResize = () => scene && scene.resize();
  if (scene) scene.onIntent(intent); else window.addEventListener('keydown', onKey);
  window.addEventListener('resize', onResize);
  paint();
  apply(await shop.load());

  return {
    shop, scene, intent,
    dispose() { window.removeEventListener('keydown', onKey); window.removeEventListener('resize', onResize); if (scene) scene.dispose(); root.replaceChildren(); },
  };
}
