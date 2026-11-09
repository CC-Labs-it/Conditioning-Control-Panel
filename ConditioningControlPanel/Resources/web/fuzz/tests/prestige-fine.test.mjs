/* Fine Print lane (ui/unlocks/prestige/fine.js): rule filtering, the Stardust factor the chip shows, and each rule
 * applied through a stub ctx (ctx.prestige comes from the Core lane; stubbed here). */
import test from 'node:test';
import assert from 'node:assert/strict';
import fineLane, { activeRules, contractFactor, factorText, speedrunLimitMs, struckRows, TOY_IDS, COPY } from '../ui/unlocks/prestige/fine.js';
import { FINE_RULES, T_RUN } from '../data/prestige.js';
import { BY_ID } from '../data/items.js';
import { createPrestige, fineMult, prestigeView } from '../sim/prestige.js';

const entry = fineLane.fine;

function fakeEl(tag = 'div') {
  const listeners = {};
  const el = {
    tag, children: [], dataset: {}, attrs: {}, className: '', textContent: '', parent: null,
    classList: { set: new Set(), add(c) { this.set.add(c); }, remove(c) { this.set.delete(c); } },
    addEventListener(t, fn) { (listeners[t] ||= []).push(fn); },
    removeEventListener(t, fn) { listeners[t] = (listeners[t] || []).filter((f) => f !== fn); },
    fire(t, ev = {}) { for (const fn of [...(listeners[t] || [])]) fn(ev); },
    listeners,
    setAttribute(k, v) { this.attrs[k] = v; },
    remove() { if (el.parent) el.parent.children = el.parent.children.filter((c) => c !== el); },
    closest(sel) { return sel === '#btn' && el.id === 'btn' ? el : null; },
  };
  return el;
}

function fakeCtx({ rules = [], stars = [], depth = 0, owned = [], clock = 0 } = {}) {
  const starSet = new Set(stars);
  const handlers = {}, mods = [], timers = [], cleanups = [], toasts = [], emitted = [], switched = [];
  const btn = fakeEl('button'); btn.id = 'btn';
  const speaker = fakeEl('button');
  const win = fakeEl('window'); win.event = undefined;
  const doc = { defaultView: win, documentElement: fakeEl('html'), getElementById: (id) => (id === 'audio-mute' ? speaker : null) };
  btn.ownerDocument = doc;
  const off = new Set();
  const state = { clock, ended: null, owned: Object.fromEntries(owned.map((id) => [id, 1])) };
  const pictures = { source: 'own', next: async () => ({ url: 'x' }), rewardBatch: () => [1], stillsForDisplay: () => [1] };
  const audio = { muted: 0, muteFor(ms) { this.muted = ms; } };
  const ended = [];
  const ctx = {
    prestige: { depth, has: (id) => starSet.has(id), level: () => stars.length, next: { rules, words: [], keep: [] }, dust: 0 },
    state, gentle: false, reduced: false, pictures, audio,
    button: { el: btn },
    has: (id) => !!state.owned[id],
    switches: { isOn: (id) => !off.has(id), set(id, v) { if (v) off.delete(id); else off.add(id); switched.push([id, v]); return true; } },
    on(ev, fn) { (handlers[ev] ||= []).push(fn); },
    emit(ev, data) { emitted.push([ev, data]); },
    mod(m) { mods.push(m); },
    every(ms, fn) { timers.push({ ms, fn, every: true }); },
    after(ms, fn) { timers.push({ ms, fn, every: false }); },
    cleanup(fn) { cleanups.push(fn); },
    css() {}, toast(t) { toasts.push(t); }, float() {}, anim() {},
    el(tag, _css, text, parent) { const e = fakeEl(tag); if (text) e.textContent = text; if (parent && typeof parent === 'object') { e.parent = parent; parent.children.push(e); } return e; },
    endRun(id) { ended.push(id); },
  };
  const fire = (ev, data) => { for (const fn of handlers[ev] || []) fn(data); };
  const runAfters = () => { for (const t of timers.filter((x) => !x.every)) t.fn(); };
  const runEvery = () => { for (const t of timers.filter((x) => x.every)) t.fn(); };
  return { ctx, mods, toasts, emitted, switched, off, state, pictures, audio, speaker, btn, win, doc, fire, runAfters, runEvery, ended, cleanups };
}

test('activeRules keeps known rules whose star is owned, in table order', () => {
  const p = { next: { rules: ['speedrun', 'nope', 'silent', 'silent', 'blank'] }, has: (id) => id !== 'fine_3' };
  assert.deepEqual(activeRules(p), ['silent', 'speedrun']);
  assert.deepEqual(activeRules({}), []);
  assert.deepEqual(activeRules(null), []);
});

test('contractFactor multiplies rules, Hush Money lifts Silent, Full Contract is x5 flat', () => {
  assert.equal(contractFactor([]), 1);
  assert.equal(contractFactor(['silent']), 1.2);
  assert.equal(contractFactor(['silent'], (id) => id === 'fine_6'), 1.5);
  assert.ok(Math.abs(contractFactor(['silent', 'no_hands']) - 1.8) < 1e-9);
  const all = FINE_RULES.map((r) => r.id);
  assert.equal(contractFactor(all, (id) => id === 'fine_10'), 5);
  assert.ok(contractFactor(all) > 5);
  assert.equal(factorText(1.2), 'x1.2');
  assert.equal(factorText(2.34), 'x2.3');
  assert.equal(factorText(5), 'x5');
});

test('speedrun limit is half the planned run, clamped to the table', () => {
  assert.equal(speedrunLimitMs(0), 45 * 60000);
  assert.equal(speedrunLimitMs(3), T_RUN[3] * 30000);
  assert.equal(speedrunLimitMs(10), T_RUN[T_RUN.length - 1] * 30000);
  assert.equal(speedrunLimitMs(undefined), 45 * 60000);
  assert.equal(speedrunLimitMs(9, 45), 45 * 30000, 'the stamp minutes win');
});

test('the chip factor matches the Core lane fineMult for every rule set', () => {
  const all = FINE_RULES.map((r) => r.id);
  for (const stars of [['fine_1', 'fine_2', 'fine_3', 'fine_4', 'fine_5', 'fine_7', 'fine_8', 'fine_9'], ['fine_1', 'fine_2', 'fine_3', 'fine_4', 'fine_5', 'fine_6', 'fine_7', 'fine_8', 'fine_9', 'fine_10']]) {
    const p = createPrestige();
    for (const s of stars) p.own[s] = 1;
    for (let mask = 1; mask < 64; mask += 1) {
      const rules = all.filter((_, i) => mask & (1 << i));
      if (rules.length > 3 && !p.own.fine_10) continue;
      const mine = contractFactor(rules, (id) => !!p.own[id]);
      assert.ok(Math.abs(mine - fineMult(rules, p)) < 1e-9, rules.join(','));
    }
  }
});

test('the run stamp wins over next.rules (after a renew, next is the following run)', () => {
  const p = createPrestige();
  p.own.fine_1 = 1; p.own.fine_7 = 1; p.own.fine_4 = 1;
  p.next.rules = ['silent'];
  const view = prestigeView(() => p, () => ({ rules: ['speedrun'], depth: 0, minutes: 90 }));
  assert.deepEqual(activeRules(view), ['speedrun']);
  assert.deepEqual(activeRules(prestigeView(() => p)), ['silent']);
});

test('every struck row exists in the items table', () => {
  for (const id of [...TOY_IDS, 'taste2']) assert.ok(BY_ID[id], id);
  assert.deepEqual([...struckRows(['one_theme'])], ['taste2']);
  assert.equal(struckRows(['no_toys']).size, TOY_IDS.length);
});

test('no rules: mounts nothing', () => {
  const f = fakeCtx({ rules: [], stars: ['fine_1'] });
  assert.deepEqual(entry.mount(f.ctx), {});
  assert.equal(f.mods.length, 0);
});

test('copy has no exclamation marks or dashes', () => {
  const lines = Object.values(COPY).map((v) => (typeof v === 'function' ? v(1.2) : v));
  for (const l of lines) assert.doesNotMatch(l, /[!–—]/);
});

test('Silent renews the mute and marks the speaker; cleanup unmarks it', () => {
  const f = fakeCtx({ rules: ['silent'], stars: ['fine_1'] });
  entry.mount(f.ctx);
  assert.equal(f.audio.muted, 15000);
  assert.equal(f.speaker.dataset.fineRule, 'silent');
  f.speaker.fire('click');
  assert.equal(f.toasts.at(-1), COPY.silent);
  for (const c of f.cleanups) c();
  assert.equal(f.speaker.dataset.fineRule, undefined);
});

test('Silent prefers a run-long kit hook when Core offers one', () => {
  const f = fakeCtx({ rules: ['silent'], stars: ['fine_1'] });
  let held = null;
  f.ctx.audio.silence = (v) => { held = v; };
  entry.mount(f.ctx);
  assert.equal(held, true);
  assert.equal(f.audio.muted, 0);
});

test('No Hands: a press on #btn pays nothing, anything else still pays', () => {
  const f = fakeCtx({ rules: ['no_hands'], stars: ['fine_2'] });
  entry.mount(f.ctx);
  const click = f.mods.find((m) => m.click).click;
  assert.equal(f.mods.find((m) => m.click).timed, true);
  assert.equal(click(10), 10, 'no event: auto taps and display rates pay');
  const press = { isTrusted: true, target: f.btn };
  f.win.listeners.pointerdown[0](press);           // capture mark
  f.win.event = press;
  assert.equal(click(10), 0);
  f.fire('tap', { auto: false, x: 1, y: 2 });
  assert.equal(f.toasts.at(-1), COPY.noHands);
  f.win.listeners.pointerdown[1](press);           // bubble clear
  f.win.event = undefined;
  assert.equal(click(10), 10);
  const copyPress = { isTrusted: true, target: fakeEl('div') };
  f.win.listeners.pointerdown[0](copyPress);
  f.win.event = copyPress;
  assert.equal(click(10), 10, 'a bouncing copy still pays');
  const scripted = { isTrusted: false, target: f.btn };
  f.win.listeners.pointerdown[0](scripted);
  f.win.event = scripted;
  assert.equal(click(10), 10);
});

test('No Hands never stalls a run: an auto tap a second until a copy or per-second pay', () => {
  const f = fakeCtx({ rules: ['no_hands'], stars: ['fine_2'] });
  let presses = 0, fps = 0;
  f.ctx.button.press = () => { presses += 1; };
  f.ctx.rates = () => ({ fps });
  entry.mount(f.ctx);
  f.runEvery();
  assert.equal(presses, 1);
  fps = 3;
  f.runEvery();
  assert.equal(presses, 1);
  fps = 0; f.state.owned.stray = 1;
  f.runEvery();
  assert.equal(presses, 1);
});

test('Blank: the feed answers nothing until the rule goes', async () => {
  const f = fakeCtx({ rules: ['blank'], stars: ['fine_3'] });
  entry.mount(f.ctx);
  assert.equal(await f.pictures.next({}), null);
  assert.deepEqual(f.pictures.rewardBatch(3), []);
  assert.deepEqual(f.pictures.stillsForDisplay(3), []);
  assert.ok(f.doc.documentElement.classList.set.has('fp-blank'));
  assert.ok(f.emitted.some(([e]) => e === 'pictures'));
  for (const c of f.cleanups) c();
  assert.deepEqual(await f.pictures.next({}), { url: 'x' });
  assert.ok(!f.doc.documentElement.classList.set.has('fp-blank'));
});

test('No Toys: toys are free, never refused, and shut when bought or switched back on', () => {
  const f = fakeCtx({ rules: ['no_toys'], stars: ['fine_5'], owned: ['wheel'] });
  entry.mount(f.ctx);
  const price = f.mods.find((m) => m.price).price;
  assert.equal(price(900, null, 0, BY_ID.toy_box), 0);
  assert.equal(price(900, null, 0, BY_ID.taste2), 900);
  f.runAfters();
  assert.ok(f.off.has('wheel'), 'an owned toy (keepsake, Lifetime Plan) is shut at start');
  f.state.owned.toy_box = 1;
  f.fire('buy', { item: BY_ID.toy_box });
  f.runAfters();
  assert.ok(f.off.has('toy_box'));
  assert.equal(f.toasts.at(-1), COPY.toyStruck);
  f.off.delete('toy_box');
  f.fire('switch', { id: 'toy_box', on: true });
  f.runAfters();
  assert.ok(f.off.has('toy_box'));
  assert.equal(f.toasts.at(-1), COPY.stillStruck);
});

test('One Theme: Second Theme is free and struck, toys untouched', () => {
  const f = fakeCtx({ rules: ['one_theme'], stars: ['fine_8'] });
  entry.mount(f.ctx);
  const price = f.mods.find((m) => m.price).price;
  assert.equal(price(45000, null, 0, BY_ID.taste2), 0);
  assert.equal(price(2500, null, 0, BY_ID.toy_box), 2500);
  f.state.owned.taste2 = 1;
  f.fire('buy', { item: BY_ID.taste2 });
  f.runAfters();
  assert.ok(f.off.has('taste2'));
  assert.equal(f.toasts.at(-1), COPY.themeStruck);
});

test('Speedrun ends the run through the engine at half time, once', () => {
  const f = fakeCtx({ rules: ['speedrun'], stars: ['fine_7'], depth: 2 });
  entry.mount(f.ctx);
  const limit = speedrunLimitMs(2);
  f.state.clock = limit - 30000;
  f.runEvery();
  assert.equal(f.toasts.at(-1), COPY.oneMinute);
  assert.deepEqual(f.ended, []);
  f.state.clock = limit;
  f.runEvery();
  f.runEvery();
  assert.deepEqual(f.ended, ['drop']);
});

test('the chip lists the rules and the factor', () => {
  const f = fakeCtx({ rules: ['silent', 'blank'], stars: ['fine_1', 'fine_3', 'fine_4'] });
  const overs = [];
  const el = f.ctx.el;
  f.ctx.el = (tag, css, text, parent) => { const e = el(tag, css, text, parent); if (parent === 'over') overs.push(e); return e; };
  entry.mount(f.ctx);
  assert.equal(overs.length, 1);
  const face = overs[0].children[0];
  assert.equal(face.children[1].textContent, factorText(1.2 * 1.3));
  face.fire('click');
  const card = overs[0].children[1];
  assert.ok(card, 'a tap opens the card');
  assert.equal(card.children[1].children.length, 2);
  face.fire('click');
  assert.equal(overs[0].children.length, 1, 'a second tap closes it');
});
