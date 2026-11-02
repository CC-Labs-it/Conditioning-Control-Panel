// The engine contract: what a registry entry's mount gets, and that everything it makes dies with it.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createEngine } from '../ui/engine.js';
import { CTX_MEMBERS } from '../ui/context.js';
import { GROUPS, REGISTRY } from '../ui/unlocks/index.js';
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
        c.mod({ click: (v) => v + 100 });
        return { off() { offCalled++; } };
      },
    },
  });
  engine.mount('stray');
  assert.equal(engine.rates().click, 101);
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

test('the registry merges five independent group files, no id claimed twice, every id real', () => {
  assert.deepEqual(Object.keys(GROUPS).sort(), ['basics', 'extras', 'routes', 'themes-a', 'themes-b']);
  const seen = new Set();
  for (const [name, group] of Object.entries(GROUPS)) {
    for (const [id, entry] of Object.entries(group)) {
      assert.ok(BY_ID[id], `${name} registers ${id}, which is not an item`);
      assert.ok(!seen.has(id), `${id} is registered twice`);
      seen.add(id);
      assert.equal(typeof entry.mount, 'function', id);
    }
  }
  assert.equal(Object.keys(REGISTRY).length, seen.size);
});
