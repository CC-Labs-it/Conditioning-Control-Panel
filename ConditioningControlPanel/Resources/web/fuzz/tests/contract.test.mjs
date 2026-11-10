// The engine contract: what a registry entry's mount gets, and that everything it makes dies with it.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createEngine } from '../ui/engine.js';
import { CTX_MEMBERS } from '../ui/context.js';
import { allowedLink } from '../ui/links.js';
import { GROUPS, REGISTRY } from '../ui/unlocks/index.js';
import { mergeGroups } from '../ui/unlocks/merge.js';
import { BY_ID } from '../data/items.js';
import * as E from '../sim/economy.js';
import { createLife } from '../sim/save.js';

class FakeEl {
  constructor(tag) { this.tag = tag; this.children = []; this.style = { cssText: '', setProperty() {}, getPropertyValue: () => '', removeProperty() {} }; this.parent = null; this.dataset = {}; this.classList = { add() {}, remove() {} }; }
  appendChild(c) { c.parent = this; this.children.push(c); return c; }
  remove() { if (this.parent) this.parent.children = this.parent.children.filter((c) => c !== this); this.parent = null; }
  animate() { const a = { cancelled: false, cancel() { this.cancelled = true; }, pause() {}, play() {}, finished: new Promise(() => {}) }; FakeEl.anims.push(a); return a; }
}
FakeEl.anims = [];

function fakeUi() {
  const layers = {};
  const doc = { createElement: (t) => new FakeEl(t), head: new FakeEl('head'), documentElement: new FakeEl('html') };
  const button = new FakeEl('button');
  return {
    doc, world: new FakeEl('world'), layers,
    layer: (n) => (layers[n] ||= new FakeEl(n)),
    bounds: () => ({ w: 800, h: 600 }),
    gentle: () => false, reduced: () => false,
    buttonFor: () => ({ el: button, skin() {}, press() {}, melt() {}, swap() {}, lockFor() {} }),
    say() {}, toast() {}, float() {}, draw() {},
    modal: () => ({ close() {} }),
  };
}

function setup(registry, id = 'stray') {
  const run = E.createRun();
  E.grant(run, id);
  const ui = fakeUi();
  const engine = createEngine({ run, life: createLife(), ui, registry });
  return { run, ui, engine };
}

test('a mount gets a ctx with every documented member', () => {
  let got = null;
  const { engine } = setup({ stray: { mount(ctx) { got = ctx; return {}; } } });
  engine.mount('stray');
  for (const m of CTX_MEMBERS) assert.ok(m in got, `ctx.${m} is missing`);
  assert.equal(got.item, BY_ID.stray);
  assert.equal(got.count, 1);
  assert.equal(got.has('stray'), true);
  assert.equal(got.pick('f1'), null);
  assert.equal(got.fmt(1234), '1,234');
  assert.equal(typeof got.rng(), 'number');
  for (const b of ['el', 'skin', 'press', 'melt', 'swap', 'lockFor']) assert.ok(got.button[b] !== undefined, `button.${b}`);
  for (const a of ['play', 'loop']) assert.equal(typeof got.audio[a], 'function');
  assert.equal(typeof got.pictures.next, 'function');
  assert.equal(typeof got.ads.next, 'function');
  assert.equal(typeof got.store.get, 'function');
  assert.equal(typeof got.store.set, 'function');
});

test('ENGINE.md documents every member', () => {
  const doc = readFileSync(new URL('../ENGINE.md', import.meta.url), 'utf8');
  for (const m of CTX_MEMBERS) assert.ok(doc.includes(`ctx.${m}`), `ENGINE.md does not mention ctx.${m}`);
});

test('ctx.state reads live and refuses writes', () => {
  let ctx;
  const { run, engine } = setup({ stray: { mount(c) { ctx = c; return {}; } } });
  engine.mount('stray');
  E.lump(run, 7);
  assert.equal(ctx.state.fuzz, 7);
  assert.equal(ctx.state.order.includes('stray'), true);
  assert.throws(() => { ctx.state.fuzz = 1e9; });
  assert.throws(() => { ctx.state.owned.drop = 1; });
  assert.equal(run.fuzz, 7);
});

test('the store survives a remount and lands in the save', () => {
  let ctx;
  const { run, engine } = setup({ stray: { mount(c) { ctx = c; return {}; } } });
  engine.mount('stray');
  ctx.store.set('x', 5);
  engine.mount('stray');
  assert.equal(ctx.store.get('x'), 5);
  assert.deepEqual(run.store.stray, { x: 5 });
});

test('timers, frames and ticks run on the game clock and stop on pause, suspend and end', () => {
  const log = { every: 0, after: 0, frame: 0, tick: 0 };
  const { engine } = setup({
    stray: { mount(c) { c.every(100, () => log.every++); c.after(250, () => log.after++); c.frame(() => log.frame++); return { tick() { log.tick++; } }; } },
  });
  engine.mount('stray');
  engine.step(100); engine.step(100); engine.step(100);
  assert.deepEqual(log, { every: 3, after: 1, frame: 3, tick: 3 });
  engine.setPaused(true);
  engine.step(1000);
  assert.deepEqual(log, { every: 3, after: 1, frame: 3, tick: 3 });
  engine.setPaused(false);
  engine.setSuspended(true);
  engine.step(1000);
  assert.deepEqual(log, { every: 3, after: 1, frame: 3, tick: 3 });
});

test('off tears down elements, timers, listeners, modifiers and animations', () => {
  let offCalled = 0, heard = 0, fired = 0;
  const { engine, ui } = setup({
    stray: {
      mount(c) {
        c.el('div', '', 'x', 'over');
        c.anim(c.el('div'), [], 100);
        c.every(10, () => fired++);
        c.on('tap', () => heard++);
        c.mod({ click: (v) => v + 5 });
        return { off() { offCalled++; } };
      },
    },
  });
  engine.mount('stray');
  assert.equal(engine.rates().click, 6);
  assert.equal(ui.layers.over.children.length, 1);
  engine.unmount('stray');
  assert.equal(offCalled, 1);
  assert.equal(ui.layers.over.children.length, 0);
  assert.equal(engine.rates().click, 1);
  engine.emit('tap', {});
  engine.step(100);
  assert.equal(heard, 0);
  assert.equal(fired, 0);
  assert.ok(FakeEl.anims.at(-1).cancelled);
});

test('events reach listeners; a wall hit pays through the sim', () => {
  const events = [];
  const { run, engine } = setup({ stray: { mount(c) { c.on('wall', (e) => events.push(e.corner)); c.on('buy', (e) => events.push(e.item.id)); return {}; } } });
  engine.mount('stray');
  const before = run.fuzz;
  engine.wallHit({ corner: true });
  assert.equal(run.fuzz, before + 2);
  E.lump(run, 100);
  engine.buy('rounded');
  assert.deepEqual(events, [true, 'rounded']);
});

test('a stream dies with its unlock and at the end; suspend holds audio before anyone hears of it', () => {
  const log = [];
  const stream = () => ({ el: {}, play() {}, pause() {}, setGain() {}, stop(ms) { log.push(`stop ${ms}`); }, playing: true });
  const audio = { stream, hold(v) { log.push(`hold ${v}`); }, stopStreams() { log.push('stopStreams'); }, setQuiet() {} };
  const run = E.createRun();
  E.grant(run, 'stray');
  const engine = createEngine({ run, life: createLife(), ui: fakeUi(), audio, registry: {
    stray: { mount(c) { c.audio.stream('a.mp3'); c.on('suspend', (e) => log.push(`heard ${e.on} ${e.reason}`)); return {}; } },
  } });
  engine.mount('stray');
  engine.setSuspended(true, 'panic');
  assert.deepEqual(log, ['hold true', 'heard true panic']);
  log.length = 0;
  engine.setSuspended(false);
  engine.unmount('stray');
  assert.deepEqual(log, ['hold false', 'heard false undefined', 'stop 300']);
  log.length = 0;
  engine.end('drop', { restored: true });
  assert.equal(log[0], 'stopStreams');
});

test('ending notifies video owners before waiting for the visual fade', async () => {
  let finishFade, heard = false;
  const { engine, ui } = setup({ stray: { mount(c) { c.on('ending', () => { heard = true; }); return {}; } } });
  engine.mount('stray');
  ui.ending = () => new Promise((resolve) => { finishFade = resolve; });
  const ending = engine.end('drop');
  assert.equal(heard, true);
  assert.equal(engine.scopes.has('stray'), true);
  finishFade(); await ending;
  assert.equal(engine.scopes.has('stray'), false);
});

test('with no kit a stream is a harmless stand-in', () => {
  let s;
  const { engine } = setup({ stray: { mount(c) { s = c.audio.stream('a.mp3'); return {}; } } });
  engine.mount('stray');
  assert.equal(s.el, null);
  s.play(); s.pause(); s.setGain(1); s.stop(100);
  assert.equal(s.playing, false);
});

test('ctx.speed scales every unlock clock, never income, and ends with its holder', () => {
  const log = { every: 0, sec: 0 };
  let ctx, seenNow = 0, release;
  const run = E.createRun();
  E.grant(run, 'ticker');
  E.grant(run, 'rounded');
  const engine = createEngine({ run, life: createLife(), ui: fakeUi(), registry: {
    stray: { mount(c) { ctx = c; c.every(100, () => log.every++); c.frame((dt, now) => { log.sec += dt; seenNow = now; }); c.mod({ fps: (v, _s, now) => { seenNow = now; return v; } }); return {}; } },
    rounded: { mount(c) { release = c.speed(0.5); return {}; } },
  } });
  engine.mount('stray');
  engine.mount('rounded');
  const anim = ctx.anim(ctx.el('div'), [], 100);
  assert.equal(anim.playbackRate, 0.5);
  const fps = engine.rates().fps;
  const before = run.fuzz;
  for (let i = 0; i < 10; i++) engine.step(100);
  assert.equal(log.every, 5, 'half the timer fires');
  assert.equal(Math.round(log.sec * 100), 50, 'half the frame time');
  assert.equal(Math.round(run.fuzz - before), fps, 'a full second of income');
  assert.equal(run.clock, 1000, 'the sim clock is real time');
  assert.equal(engine.real, 1000);
  assert.equal(engine.now, 500);
  engine.rates();
  assert.equal(seenNow, 500, 'a modifier reads the same clock as a frame');
  release();
  assert.equal(anim.playbackRate, 1);
  engine.step(100);
  assert.equal(engine.now, 600);
  ctx.speed(0.001);
  assert.equal(engine.speedFactor(), 0.1, 'never a stopped clock');
  ctx.speed(NaN);
  assert.equal(engine.speedFactor(), 0.1);
  engine.unmount('stray');
  assert.equal(engine.speedFactor(), 1, 'restored on off');
});

test('ctx.parts hands over the shop and the button wrap, and cannot be reassigned', () => {
  let ctx;
  const { ui, engine } = setup({ stray: { mount(c) { ctx = c; return {}; } } });
  ui.parts = { shop: new FakeEl('shop'), buttonWrap: new FakeEl('wrap'), counter: new FakeEl('counter'), stage: new FakeEl('stage') };
  engine.mount('stray');
  assert.equal(ctx.parts.shop, ui.parts.shop);
  assert.equal(ctx.parts.buttonWrap, ui.parts.buttonWrap);
  assert.equal(ctx.parts.counter, ui.parts.counter);
  assert.equal(ctx.parts.stage, ui.parts.stage);
  assert.throws(() => { ctx.parts.shop = null; });
});

test('the ending layer is in the page, above the end fade', () => {
  const html = readFileSync(new URL('../index.html', import.meta.url), 'utf8');
  const css = readFileSync(new URL('../styles.css', import.meta.url), 'utf8');
  const app = readFileSync(new URL('../ui/app.js', import.meta.url), 'utf8');
  assert.ok(html.includes('id="l-ending"'));
  assert.ok(app.includes("ending: 'l-ending'"));
  const z = (re) => Number(css.match(re)[1]);
  assert.ok(z(/#l-ending \{ z-index: (\d+)/) > z(/\.endfade \{[^}]*z-index: (\d+)/));
});

test('a link is cclabs.app, www, app, the one Discord invite, or nothing', () => {
  for (const good of ['https://cclabs.app', 'https://cclabs.app/', 'https://cclabs.app/ccp.html?x=1#y', 'HTTPS://CCLABS.APP/a',
    'https://www.cclabs.app/', 'https://app.cclabs.app', 'https://app.cclabs.app/tc', 'https://discord.gg/YxVAMt4qaZ']) assert.ok(allowedLink(good), good);
  for (const bad of ['http://cclabs.app', 'http://app.cclabs.app', 'https://tc.cclabs.app', 'https://evil.cclabs.app', 'https://cclabs.app.example.com', 'https://app.cclabs.app.example.com',
    'https://cclabs.app@example.com', 'https://x:y@cclabs.app', 'https://cclabs.app:8443', 'https://app.cclabs.app:444/', 'javascript:alert(1)', 'data:text/html,x', '//cclabs.app', '/a', '', null, undefined,
    'https://example.com/https://cclabs.app', 'https://discord.gg/other', 'https://discord.gg/YxVAMt4qaZ/x', 'https://discord.gg/YxVAMt4qaZ?x=1', 'http://discord.gg/YxVAMt4qaZ',
    'https://discord.gg.example.com/YxVAMt4qaZ', 'https://discord.com/invite/YxVAMt4qaZ']) assert.equal(allowedLink(bad), null, String(bad));
});

test('ctx.openLink opens only inside a press; hosted goes down the wire, unhosted is a new tab', () => {
  let ctx;
  const sent = [], opened = [];
  let pressing = false;
  const run = E.createRun();
  E.grant(run, 'stray');
  const make = (isHosted) => {
    const ui = fakeUi();
    ui.inPress = () => pressing;
    ui.win = { open: (...a) => opened.push(a) };
    const engine = createEngine({ run, life: createLife(), ui, host: { isHosted, send: (t, f) => sent.push([t, f]) }, registry: { stray: { mount(c) { ctx = c; return {}; } } } });
    engine.mount('stray');
  };
  make(false);
  assert.equal(ctx.openLink('https://cclabs.app/x'), false, 'no press, no tab');
  pressing = true;
  assert.equal(ctx.openLink('https://example.com'), false);
  assert.equal(ctx.openLink('https://cclabs.app/x'), true);
  assert.deepEqual(opened, [['https://cclabs.app/x', '_blank', 'noopener']]);
  assert.deepEqual(sent, []);
  make(true);
  assert.equal(ctx.openLink('https://cclabs.app/y'), true);
  assert.deepEqual(sent, [['open-url', { url: 'https://cclabs.app/y' }]]);
  assert.equal(opened.length, 1);
  assert.equal(ctx.openLink('https://example.com'), false);
  assert.equal(sent.length, 1);
});

test('ctx.spend never goes below zero, answers what it took, and is kept apart from what was earned', () => {
  let ctx;
  const { run, engine } = setup({ stray: { mount(c) { ctx = c; return {}; } } });
  engine.mount('stray');
  E.lump(run, 10);
  const total = run.total;
  assert.equal(ctx.spend(4, 'fine'), 4);
  assert.equal(ctx.spend(100, 'fine'), 6);
  assert.equal(run.fuzz, 0);
  assert.equal(run.total, total);
  assert.deepEqual(run.spent, { fine: 10 });
  assert.equal(ctx.spend(5), 0);
  for (const bad of [-5, NaN, Infinity]) { E.lump(run, 1); const f = run.fuzz; assert.ok(ctx.spend(bad) <= f); assert.ok(run.fuzz >= 0); }
});

test('ctx.guardBuy refuses a capped number of times, then the row goes through', () => {
  const asked = [];
  const toasts = [];
  const { run, ui, engine } = setup({ stray: { mount(c) { c.guardBuy((it) => { asked.push(it.id); return { refuse: 'Not in {cur}' }; }); return {}; } } });
  ui.toast = (t) => toasts.push(t);
  engine.mount('stray');
  E.lump(run, 1000);
  const money = run.fuzz;
  for (let i = 0; i < 3; i++) {
    const r = engine.buy('rounded');
    assert.equal(r.ok, false);
    assert.equal(r.reason, 'guarded');
  }
  assert.equal(run.fuzz, money, 'a refusal costs nothing');
  assert.equal(toasts.length, 3);
  assert.ok(!toasts[0].includes('{cur}'));
  assert.equal(engine.buy('rounded').ok, true);
  assert.equal(asked.length, 3, 'the guard is not even asked the fourth time');
});

test('a stacking row gets its own count of refusals per copy', () => {
  const { run, engine } = setup({ stray: { mount(c) { c.guardBuy(() => ({ refuse: 'no' })); return {}; } } });
  engine.mount('stray');
  E.lump(run, 1000);
  for (let copy = 0; copy < 2; copy++) {
    for (let i = 0; i < 3; i++) assert.equal(engine.buy('stray').reason, 'guarded');
    assert.equal(engine.buy('stray').ok, true);
  }
});

test('a guard never sees a fork or an ending, nor a buy that would fail anyway', () => {
  const asked = [];
  const { run, engine } = setup({ stray: { mount(c) { c.guardBuy((it) => { asked.push(it.id); return { refuse: 'no' }; }); return {}; } } }, 'tunnel');
  engine.mount('stray');
  assert.equal(engine.buy('rounded').reason, 'short');
  assert.deepEqual(asked, []);
  E.lump(run, 1e8);
  E.grant(run, 'ft_door');   // tunnel granted Spiral Overlay with A Window: Go Outside is this run's ending
  assert.equal(engine.buy('outside').ok, true);
  assert.deepEqual(asked, []);
  const fresh = setup({ stray: { mount(c) { c.guardBuy(() => ({ refuse: 'no' })); return {}; } } });
  fresh.engine.mount('stray');
  E.grant(fresh.run, 'fpsv');
  E.lump(fresh.run, 1000);
  assert.equal(fresh.engine.choose('f1', 'eyes').ok, true);
});

test('a throwing guard lets the row through, and a guard ends with its unlock', () => {
  const realError = console.error;
  console.error = () => {};
  try {
    const a = setup({ stray: { mount(c) { c.guardBuy(() => { throw new Error('boom'); }); return {}; } } });
    a.engine.mount('stray');
    E.lump(a.run, 1000);
    assert.equal(a.engine.buy('rounded').ok, true);
  } finally { console.error = realError; }
  const b = setup({ stray: { mount(c) { c.guardBuy(() => ({ refuse: 'no' })); return {}; } } });
  b.engine.mount('stray');
  b.engine.unmount('stray');
  E.lump(b.run, 1000);
  assert.equal(b.engine.buy('rounded').ok, true);
});

test('a stacking row stays mounted and hears grow; a row without grow remounts', () => {
  let mounts = 0, grown = null;
  const { run, engine } = setup({ stray: { mount() { mounts++; return { grow(n) { grown = n; } }; } } });
  engine.mount('stray');
  E.lump(run, 100);
  engine.buy('stray');
  assert.equal(mounts, 1);
  assert.equal(grown, 2);
});

test('a throwing unlock is switched off and the game carries on', () => {
  const realError = console.error;
  console.error = () => {};
  try {
    let ticks = 0;
    const { engine } = setup({ stray: { mount() { return { tick() { ticks++; throw new Error('boom'); } }; } } });
    engine.mount('stray');
    for (let i = 0; i < 10; i++) engine.step(16);
    assert.equal(ticks, 3);
    assert.equal(engine.scopes.has('stray'), false);
    const bad = setup({ stray: { mount() { throw new Error('boom'); } } });
    assert.equal(bad.engine.mount('stray'), true);
    bad.engine.step(16);
  } finally { console.error = realError; }
});

test('an id with no entry still sells, pays and draws nothing', () => {
  const { run, engine } = setup({});
  E.lump(run, 1000);
  assert.equal(engine.mount('ticker'), false);
  assert.ok(engine.buy('rounded').ok);
  assert.equal(engine.rates().click, 2);
});

test('a fork row can have a handler in several groups: all mount, one failing does not stop the others', () => {
  const realError = console.error;
  console.error = () => {};
  try {
    const log = [];
    const merged = mergeGroups([
      { f5: { mount(c) { log.push(`a ${c.pick('f5')}`); return { tick() { log.push('a tick'); }, off() { log.push('a off'); } }; } } },
      { f5: { mount() { throw new Error('boom'); } } },
      { f5: { mount() { log.push('c'); return { tick() { throw new Error('boom'); }, off() { log.push('c off'); } }; } } },
    ]);
    const run = E.createRun();
    E.grant(run, 'f5');
    const engine = createEngine({ run, life: createLife(), ui: fakeUi(), registry: merged });
    engine.mount('f5');
    assert.deepEqual(log, [`a ${run.picks.f5}`, 'c']);
    log.length = 0;
    for (let i = 0; i < 5; i++) engine.step(16);
    assert.equal(log.filter((l) => l === 'a tick').length, 5, 'a throwing tick beside it does not stop this one');
    log.length = 0;
    engine.unmount('f5');
    assert.deepEqual(log, ['a off', 'c off']);
  } finally { console.error = realError; }
});

test('a single fork handler and a plain row pass through untouched', () => {
  const one = { mount() { return {}; } }, row = { mount() { return {}; } };
  const merged = mergeGroups([{ f5: one, stray: row }, {}]);
  assert.equal(merged.f5, one);
  assert.equal(merged.stray, row);
});

test('the registry merges independent group files, no id claimed twice, every id real', () => {
  assert.deepEqual(Object.keys(GROUPS).sort(), ['arcade', 'basics', 'edges', 'extras', 'fate-button', 'fate-museum', 'fate-outside', 'fate-themes', 'meta', 'routes', 'spiral', 'themes-a', 'themes-b']);
  const seen = new Set();
  for (const [name, group] of Object.entries(GROUPS)) {
    for (const [id, entry] of Object.entries(group)) {
      assert.ok(BY_ID[id], `${name} registers ${id}, which is not an item`);
      assert.ok(!seen.has(id) || BY_ID[id].kind === 'fork', `${id} is registered twice`);
      seen.add(id);
      assert.equal(typeof entry.mount, 'function', id);
    }
  }
  assert.equal(Object.keys(REGISTRY).length, seen.size);
});

test('an ad press is worth double on a Broke day, and any other lump is not', () => {
  const { engine, run } = setup({ stray: { mount() { return {}; } } });
  run.mood = 'm_broke';
  const plain = engine.pay(10, 'x');
  const ad = engine.pay(10, 'ad');
  assert.equal(ad, plain * 2);
});
