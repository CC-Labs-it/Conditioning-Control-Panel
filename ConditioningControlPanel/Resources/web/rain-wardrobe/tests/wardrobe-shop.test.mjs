import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createShop, indexItems, listenToHost, lookKeys, sameLook, STR } from '../wardrobe-shop.js';

const items = indexItems(JSON.parse(readFileSync(new URL('../items.json', import.meta.url), 'utf8')));
const names = { 'dress:midnight-cocktail': 'Midnight cocktail', 'accessories:midnight-ribbon': 'Midnight ribbon', 'accessories:hair-bow': 'Rose ribbon' };
const nameOf = key => names[key] || '';

const look = (over = {}, accessories = ['hoops-original']) => ({ rev: 1, items: { dress: 'satin-original', hair: 'original-blonde', skin: 'original', ...over, accessories }, colors: {} });

function shopWith(init, extra = {}) {
  const posted = [];
  const shop = createShop({ items, nameOf, post: m => posted.push(m), ...extra });
  if (init) shop.handle({ type: 'init', look: look(), grants: [], level: 1, achievements: [], ...init });
  return { shop, posted };
}

/** A fake WPF webview: `deliver(m)` is a host frame. */
function fakeWindow() {
  const subs = [];
  return { win: { chrome: { webview: { addEventListener: (t, f) => subs.push(f) } } }, deliver: m => subs.forEach(f => f({ data: m })) };
}

test('items.json indexes every row under slot:pageId', () => {
  assert.equal(items.size, 79);
  assert.equal(items.get('dress:midnight-cocktail').grant, 'cos.rain.dress.midnight-cocktail');
  assert.deepEqual(lookKeys(look({ legwear: 'none' })), ['dress:satin-original', 'hair:original-blonde', 'skin:original', 'accessories:hoops-original']);
});

test('badge table: wearing, owned, free, level, achievement, price, counter fallback', () => {
  const { shop } = shopWith({ grants: ['cos.rain.dress.rose-flare'], level: 12, prices: { 'dress:midnight-cocktail': 730, 'cos.rain.suit.hazmat-yellow': 2540 } });
  shop.setDraft(look());
  assert.deepEqual(shop.badge('dress', 'satin-original'), { kind: 'wearing', text: STR.wearing, owned: true }, 'free and worn');
  assert.equal(shop.badge('skin', 'warm'), null, 'free and not worn: nothing');
  assert.equal(shop.badge('dress', 'rose-flare').text, 'OWNED');
  assert.equal(shop.badge('accessories', 'velvet-choker').text, 'OWNED', 'Lv 10 at level 12 stays stamped');
  assert.equal(shop.badge('accessories', 'midnight-ribbon').text, 'LOCKED Lv 30');
  assert.equal(shop.badge('accessories', 'hair-bow').text, 'LOCKED: first achievement');
  assert.equal(shop.badge('dress', 'midnight-cocktail').text, '730 Sparkles', 'price by item id');
  assert.equal(shop.badge('dress', 'hazmat-yellow').text, '2540 Sparkles', 'price by grant');
  assert.equal(shop.badge('dress', 'teal-tulip').text, 'At the Prize Counter', 'no price: the counter line');
  shop.setDraft(look({ dress: 'rose-flare' }));
  assert.equal(shop.badge('dress', 'rose-flare').text, 'WEARING');
  shop.setDraft(look({ dress: 'midnight-cocktail' }));
  assert.equal(shop.badge('dress', 'midnight-cocktail').kind, 'price', 'trying on keeps the price stamp');
});

test('try-on count and the first blocker decide Apply', () => {
  const { shop } = shopWith({ prices: {} });
  let st = shop.setDraft(shop.applied);
  assert.deepEqual([st.count, st.canApply, st.canDiscard, st.blocker], [0, false, false, null], 'nothing changed');
  st = shop.setDraft(look({ skin: 'warm' }));
  assert.deepEqual([st.count, st.wearable, st.canApply, st.canDiscard], [0, true, true, true], 'free change: Apply on');
  st = shop.setDraft(look({ dress: 'midnight-cocktail' }, ['hoops-original', 'midnight-ribbon']));
  assert.equal(st.count, 2);
  assert.equal(STR.ribbon.replace('{n}', st.count), 'TRY-ON: 2 not yours yet');
  assert.equal(st.canApply, false);
  assert.equal(st.blocker.text, 'Buy Midnight cocktail at the Prize Counter');
  assert.deepEqual([st.blocker.counter, st.blocker.tab], [true, 'rain-clothes']);
  st = shop.setDraft(look({}, ['midnight-ribbon']));
  assert.equal(st.blocker.text, 'Reach level 30 to wear Midnight ribbon');
  assert.equal(st.blocker.counter, false, 'a level lock has no counter door');
  st = shop.setDraft(look({}, ['hair-bow']));
  assert.equal(st.blocker.text, 'Earn a first achievement to wear Rose ribbon');
  shop.handle({ type: 'init', look: look(), grants: [], level: 1, achievements: ['first_session'] });
  assert.equal(shop.setDraft(look({}, ['hair-bow'])).canApply, true, 'any achievement unlocks it');
});

test('counter door posts open-counter with the blocker tab', () => {
  const { shop, posted } = shopWith({});
  shop.setDraft(look({ hair: 'copper-lob' }));
  assert.deepEqual(shop.openCounter(), { type: 'open-counter', tab: 'rain-hair' });
  shop.setDraft(look({}, ['midnight-ribbon']));
  assert.deepEqual(shop.openCounter(), { type: 'open-counter', tab: null }, 'no shop blocker: the counter front');
  assert.deepEqual(posted.filter(m => m.type === 'open-counter').map(m => m.tab), ['rain-hair', null]);
});

test('applyMode: draft frames, rain-apply, rain-applied commits, discard reverts, rain-owned re-badges', () => {
  const { shop, posted } = shopWith({ applyMode: true });
  shop.setDraft(look({ dress: 'rose-flare' }));
  assert.deepEqual(posted.at(-1), { type: 'rain-draft', look: look({ dress: 'rose-flare' }), unowned: ['dress:rose-flare'] });
  assert.equal(shop.apply(), false, 'not owned: no apply');
  assert.equal(shop.discard().items.dress, 'satin-original');
  shop.setDraft(look({ dress: 'rose-flare' }));
  assert.equal(shop.handle({ type: 'rain-owned', grants: ['cos.rain.dress.rose-flare'] }), 'owned');
  assert.equal(shop.badge('dress', 'rose-flare').text, 'WEARING', 're-badged in place');
  assert.equal(shop.apply(), true);
  assert.deepEqual(posted.at(-1), { type: 'rain-apply', look: look({ dress: 'rose-flare' }) });
  assert.equal(shop.status().applying, true);
  assert.equal(shop.status().canApply, false, 'one apply at a time');
  shop.handle({ type: 'rain-applied', lookHash: 'abc' });
  assert.deepEqual([shop.lookHash, shop.applied.items.dress, shop.status().dirty], ['abc', 'rose-flare', false]);
});

test('a failed apply (cancel, bake error, host clamp) frees the button and keeps the applied look', () => {
  const { shop } = shopWith({ applyMode: true, grants: ['cos.rain.dress.rose-flare'] });
  shop.setDraft(look({ dress: 'rose-flare' }));
  assert.equal(shop.apply(), true);
  assert.equal(shop.handle({ type: 'rain-applied', failed: true, reason: 'cancelled' }), 'apply-failed');
  assert.deepEqual([shop.applied.items.dress, shop.status().applying, shop.status().canApply], ['satin-original', false, true]);
});

test('without applyMode the page keeps the old autosave and posts no draft frames', () => {
  const { shop, posted } = shopWith({});
  assert.equal(shop.applyMode, false);
  shop.setDraft(look({ skin: 'warm' }));
  assert.equal(posted.length, 0);
  assert.equal(shop.apply(), true);
  assert.equal(shop.applied.items.skin, 'warm', 'saved at once, no rain-applied wait');
});

test('fallback without init: ?locked= decides, level and achievement rows keep their reason', () => {
  const locked = new Set(['dress:midnight-cocktail', 'accessories:midnight-ribbon']);
  const { shop, posted } = shopWith(null, { isLocked: (slot, id) => locked.has(`${slot}:${id}`) });
  assert.equal(shop.source, 'fallback');
  shop.setDraft(look());
  assert.equal(shop.badge('dress', 'midnight-cocktail').text, 'At the Prize Counter');
  assert.equal(shop.badge('accessories', 'midnight-ribbon').text, 'LOCKED Lv 30');
  assert.equal(shop.badge('dress', 'rose-flare').text, 'OWNED', 'not on the list: owned');
  const st = shop.setDraft(look({ dress: 'midnight-cocktail' }));
  assert.deepEqual([st.count, st.canApply], [1, false]);
  assert.equal(shop.handle({ type: 'rain-owned', grants: [] }), 'owned');
  assert.equal(shop.badge('dress', 'rose-flare').text, 'OWNED', 'a grants frame never wipes the fallback');
  assert.equal(posted.length, 0);
});

test('host frames arrive through the webview; calm follows gentle and motion', () => {
  const { shop } = shopWith(null);
  const w = fakeWindow();
  const seen = [];
  assert.equal(listenToHost(w.win, shop, kind => seen.push(kind)), true);
  assert.equal(listenToHost({}, shop, () => {}), false, 'no webview: nothing to listen to');
  w.deliver({ type: 'init', look: look(), grants: [], level: 3, achievements: [], gentle: true, applyMode: true });
  w.deliver({ type: 'something-else' });
  w.deliver({ type: 'rain-applied', lookHash: 'h' });
  assert.deepEqual(seen, ['init', 'applied']);
  assert.deepEqual([shop.source, shop.applyMode, shop.calm], ['init', true, true]);
  shop.handle({ type: 'init', motion: 'off' });
  assert.equal(shop.calm, true);
  shop.handle({ type: 'init', motion: 'full' });
  assert.equal(shop.calm, false);
});

test('sameLook ignores accessory order; copy carries no em-dash or exclamation mark', () => {
  assert.equal(sameLook(look({}, ['a', 'b']), look({}, ['b', 'a'])), true);
  assert.equal(sameLook(look(), { ...look(), colors: { fabric: '#000000' } }), false);
  for (const text of Object.values(STR)) assert.doesNotMatch(text, new RegExp('[\\u2013\\u2014!]'));
});

test('paintShop fills the ribbon and the bar by the ids index.html carries', async () => {
  const { paintShop } = await import('../wardrobe-shop.js');
  const html = readFileSync(new URL('../index.html', import.meta.url), 'utf8');
  const ids = ['tryonRibbon', 'lookNowLabel', 'lookNow', 'lookBlocker', 'lookBlockerText', 'openCounter', 'discardLook', 'applyLook'];
  for (const id of ids) assert.match(html, new RegExp(`id="${id}"`));
  const el = () => { const cls = new Set(), attrs = {}; return { textContent: '', hidden: false, disabled: false, classList: { toggle: (c, on) => (on ? cls.add(c) : cls.delete(c)), has: c => cls.has(c) }, setAttribute: (k, v) => { attrs[k] = v; }, attrs }; };
  const nodes = Object.fromEntries(ids.map(id => [id, el()]));
  const doc = { getElementById: id => nodes[id] || null, documentElement: { toggleAttribute() {} } };
  const { shop } = shopWith({});
  shop.setDraft(look({ dress: 'midnight-cocktail' }));
  paintShop(doc, shop);
  assert.equal(nodes.tryonRibbon.textContent, 'TRY-ON: 1 not yours yet');
  assert.equal(nodes.tryonRibbon.classList.has('on'), true);
  assert.equal(nodes.lookNow.textContent.split(', ')[0], 'Midnight cocktail');
  assert.equal(nodes.lookBlockerText.textContent, 'Buy Midnight cocktail at the Prize Counter');
  assert.deepEqual([nodes.openCounter.hidden, nodes.applyLook.disabled, nodes.discardLook.disabled], [false, true, false]);
});

test('counter Wear door: init.tryOn and rain-tryon hand one known piece over once; unknown keys and bundled looks never', () => {
  const { shop } = shopWith({ tryOn: 'dress:midnight-cocktail' });
  assert.deepEqual(shop.takeTryOn(), { slot: 'dress', id: 'midnight-cocktail' });
  assert.equal(shop.takeTryOn(), null, 'taken once');
  assert.equal(shop.handle({ type: 'rain-tryon', key: 'accessories:hair-bow' }), 'tryon');
  assert.deepEqual(shop.takeTryOn(), { slot: 'accessories', id: 'hair-bow' });
  shop.handle({ type: 'rain-tryon', key: 'dress:not-a-dress' });
  assert.equal(shop.takeTryOn(), null);
  shop.handle({ type: 'rain-tryon', key: 'looks:anything' });
  assert.equal(shop.takeTryOn(), null);
  assert.equal(shop.handle({ type: 'rain-tryon', key: 7 }), null);
});

test('counter Wear door: a try-on that lands before items.json waits for it', () => {
  const shop = createShop({ items: new Map() });
  shop.handle({ type: 'init', look: look(), grants: [], tryOn: 'hair:rose-waves' });
  assert.equal(shop.takeTryOn(), null);
  shop.setItems(items);
  assert.deepEqual(shop.takeTryOn(), { slot: 'hair', id: 'rose-waves' });
});
