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

/** A fake element that remembers classes, attributes and inline style. */
function meterEl() {
  const cls = new Set(), attrs = {};
  const fill = { style: { transition: '', transform: '' } };
  return { firstElementChild: fill, offsetWidth: 0, style: {}, attrs, fill, setAttribute: (k, v) => { attrs[k] = v; }, classList: { toggle: (c, on) => (on ? cls.add(c) : cls.delete(c)), contains: c => cls.has(c), has: c => cls.has(c) } };
}

test('bake progress: rain-baking counts on the Apply button, the hint shows, and it clears on rain-applied', async () => {
  const { paintShop, bakeLine } = await import('../wardrobe-shop.js');
  const { shop } = shopWith({ applyMode: true, grants: ['cos.rain.dress.rose-flare'] });
  shop.setDraft(look({ dress: 'rose-flare' }));
  assert.equal(shop.apply(), true);
  assert.equal(shop.status().bake, null, 'no meter until the host says it bakes (a cached look commits at once)');
  assert.equal(shop.handle({ type: 'rain-baking', done: 2, total: 17, shown: 3, fraction: 0.15 }), 'baking');
  assert.deepEqual(shop.status().bake, { shown: 3, total: 17, fraction: 0.15 });
  assert.equal(bakeLine(STR, shop.status().bake), 'Getting dressed: 3 of 17');

  const ids = ['tryonRibbon', 'lookNowLabel', 'lookNow', 'lookBlocker', 'lookBlockerText', 'openCounter', 'discardLook', 'applyLook'];
  const el = () => { const cls = new Set(); return { textContent: '', hidden: false, disabled: false, setAttribute() {}, classList: { toggle: (c, on) => (on ? cls.add(c) : cls.delete(c)), has: c => cls.has(c), contains: c => cls.has(c) } }; };
  const nodes = Object.fromEntries(ids.map(id => [id, el()]));
  nodes.bakeMeter = meterEl();
  const doc = { getElementById: id => nodes[id] || null, documentElement: { toggleAttribute() {} } };
  paintShop(doc, shop);
  assert.equal(nodes.applyLook.textContent, 'Getting dressed: 3 of 17');
  assert.equal(nodes.applyLook.disabled, true);
  assert.equal(nodes.lookBlockerText.textContent, STR.bakeHint);
  assert.equal(nodes.lookBlocker.classList.has('on'), true);
  assert.equal(nodes.bakeMeter.classList.contains('on'), true, 'IN: the meter fades up');
  assert.equal(nodes.bakeMeter.fill.style.transform, 'scaleX(0.15)');
  assert.equal(nodes.bakeMeter.attrs['aria-hidden'], 'false');

  shop.handle({ type: 'rain-applied', lookHash: 'h' });
  assert.deepEqual([shop.status().bake, shop.status().outcome, shop.status().applying], [null, 'applied', false]);
  paintShop(doc, shop);
  assert.equal(nodes.applyLook.textContent, STR.apply);
  assert.equal(nodes.bakeMeter.classList.contains('on'), false, 'OUT: the meter fades');
  assert.equal(nodes.bakeMeter.classList.contains('done'), true);
  assert.equal(nodes.bakeMeter.fill.style.transform, 'scaleX(1)', 'OUT: an applied bake fills to the end first');
});

test('bake progress: a fresh bake starts the meter empty; a failed bake fades without filling; bad frames are ignored', async () => {
  const { paintMeter } = await import('../wardrobe-shop.js');
  const { shop } = shopWith({ applyMode: true, grants: ['cos.rain.dress.rose-flare'] });
  assert.equal(shop.handle({ type: 'rain-baking', total: 0, shown: 1, fraction: 0 }), null);
  assert.equal(shop.handle({ type: 'rain-baking', total: 17, shown: 'x', fraction: 0 }), null);
  assert.deepEqual(shop.handle({ type: 'rain-baking', total: 17, shown: 40, fraction: 3 }) && shop.status().bake, { shown: 17, total: 17, fraction: 1 }, 'clamped');

  const meter = meterEl();
  meter.fill.style.transform = 'scaleX(1)';   // left over from the last applied bake
  paintMeter(meter, { bake: { shown: 1, total: 17, fraction: 0.02 } }, true);
  assert.equal(meter.fill.style.transform, 'scaleX(0.02)');
  assert.equal(meter.fill.style.transition, '', 'the reset runs with no transition, then hands it back');
  assert.equal(meter.classList.contains('calm'), true, 'Gentle / reduced motion lengthens the fades');

  shop.setDraft(look({ dress: 'rose-flare' }));
  shop.apply();
  shop.handle({ type: 'rain-baking', total: 17, shown: 5, fraction: 0.25 });
  assert.equal(shop.handle({ type: 'rain-applied', failed: true, reason: 'cancelled' }), 'apply-failed');
  const st = shop.status();
  assert.equal(st.outcome, 'failed');
  paintMeter(meter, { bake: { shown: 5, total: 17, fraction: 0.25 } });
  paintMeter(meter, st);
  assert.equal(meter.classList.contains('on'), false);
  assert.equal(meter.classList.contains('done'), false);
  assert.equal(meter.fill.style.transform, 'scaleX(0.25)', 'a cancel never claims a finished bake');
});

test('bake progress: init.strings localises known lines only', async () => {
  const { paintShop } = await import('../wardrobe-shop.js');
  const { shop } = shopWith({ strings: { baking: 'Cambio in corso: {n} di {total}', bakeHint: 'Tieni aperta questa finestra.', nope: 'x', apply: 7 } });
  assert.equal(shop.str.baking, 'Cambio in corso: {n} di {total}');
  assert.equal(shop.str.nope, undefined);
  assert.equal(shop.str.apply, STR.apply);
  shop.handle({ type: 'rain-baking', total: 17, shown: 4, fraction: 0.2 });
  const node = () => ({ textContent: '', hidden: false, disabled: false, setAttribute() {}, classList: { toggle() {}, has() {}, contains() { return false; } } });
  const nodes = { applyLook: node(), lookBlocker: node(), lookBlockerText: node(), openCounter: node() };
  paintShop({ getElementById: id => nodes[id] || null, documentElement: { toggleAttribute() {} } }, shop);
  assert.equal(nodes.applyLook.textContent, 'Cambio in corso: 4 di 17');
  assert.equal(nodes.lookBlockerText.textContent, 'Tieni aperta questa finestra.');
  const html = readFileSync(new URL('../index.html', import.meta.url), 'utf8');
  assert.match(html, /id="bakeMeter"/);
});
