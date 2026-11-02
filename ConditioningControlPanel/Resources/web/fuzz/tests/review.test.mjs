// Defects the review lane found: each test holds one fix.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cardUp } from '../ui/unlocks/extras/kit.js';
import { bankOf, collect, levelSeconds, xpNeed } from '../sim/levels.js';
import { createEngine } from '../ui/engine.js';
import { REGISTRY } from '../ui/unlocks/index.js';
import { fresh } from '../ui/unlocks/basics/fx.js';
import * as E from '../sim/economy.js';
import { createLife, parse, serialize } from '../sim/save.js';

test('a closed Guide or Control Panel sheet is not a card up', () => {
  const was = globalThis.document;
  const host = { children: [{ hidden: true }] };
  globalThis.document = { getElementById: () => host };
  try {
    assert.equal(cardUp(), false, 'only a hidden sheet');
    host.children = [{ hidden: true }, { hidden: false }];
    assert.equal(cardUp(), true, 'a real card over it');
    host.children = [];
    assert.equal(cardUp(), false, 'an empty host');
  } finally { globalThis.document = was; }
});

test('Levels: a huge bank answers at once and the sums match the slow way', () => {
  for (const xp of [1e308, 1e18, Number.MAX_SAFE_INTEGER]) {
    const b = bankOf(1, xp);
    assert.ok(Number.isFinite(b.banked) && b.left >= 0 && b.left < b.need, `bank of ${xp}`);
    assert.ok(Number.isFinite(collect(1, xp).seconds));
  }
  for (const [lvl, xp] of [[1, 0], [1, 34.9], [1, 35], [1, 500], [3, 2000], [12, 181], [40, 12345.5]]) {
    let banked = 0, left = xp, seconds = 0;
    while (left >= xpNeed(lvl + banked)) { left -= xpNeed(lvl + banked); seconds += levelSeconds(lvl + banked); banked += 1; }
    const now = bankOf(lvl, xp), got = collect(lvl, xp);
    assert.equal(now.banked, banked, `banked at ${lvl}, ${xp}`);
    assert.ok(Math.abs(now.left - left) < 1e-6, `left at ${lvl}, ${xp}`);
    assert.equal(got.seconds, seconds, `seconds at ${lvl}, ${xp}`);
    assert.equal(got.lvl, lvl + banked);
  }
});

class El {
  constructor() { this.children = []; this.style = { setProperty() {}, getPropertyValue: () => '', removeProperty() {} }; this.dataset = {}; this.classList = { add() {}, remove() {}, toggle() {}, contains: () => false }; }
  appendChild(c) { this.children.push(c); return c; }
  remove() {}
  animate() { return { cancel() {}, pause() {}, play() {}, finished: new Promise(() => {}) }; }
  addEventListener() {}
  get ownerDocument() { return doc; }
  getBoundingClientRect() { return { left: 0, top: 0, width: 10, height: 10, right: 10, bottom: 10 }; }
}
const doc = { createElement: () => new El(), head: new El(), documentElement: new El(), querySelectorAll: () => [], getElementById: () => null };
doc.head.querySelectorAll = () => [];
doc.head.insertBefore = (a) => a;
function engineFor(registry, ids) {
  const run = E.createRun();
  for (const id of ids) E.grant(run, id);
  const layers = {}, button = new El();
  const ui = { doc, world: new El(), layer: (n) => (layers[n] ||= new El()), bounds: () => ({ w: 800, h: 600 }), gentle: () => false, reduced: () => false,
    buttonFor: () => ({ el: button, skin() {}, press() {}, melt() {}, swap() {}, lockFor() {} }), say() {}, toast() {}, float() {}, draw() {}, modal: () => ({ close() {} }),
    dock: { add: () => ({ root: new El(), body: new El(), side: 'left', remove() {} }), isRail: () => true } };
  return { run, engine: createEngine({ run, life: createLife(), ui, registry }) };
}

test('a reload from a save is not an arrival: Levels keeps its XP, the wheel its wait', () => {
  const seen = [];
  const { engine: a } = engineFor({ stray: { mount(ctx) { seen.push(fresh(ctx)); return {}; } } }, ['stray']);
  a.mountAll();
  a.mountAll(true);
  assert.deepEqual(seen, [true, false], 'a new buy arrives, a reload does not');

  const { run, engine } = engineFor({ levels: REGISTRY.levels }, ['levels']);
  run.store.levels = { lvl: 1, xp: 500 };
  engine.mountAll(true);
  engine.step(1000);
  assert.ok(run.store.levels.xp >= 500, `xp after a reload is ${run.store.levels.xp}`);
});

test('a save whose store holds something other than an object loads clean', () => {
  const run = E.createRun();
  const raw = JSON.parse(serialize(run, createLife()));
  raw.run.store = { quiz: 'nope', levels: { lvl: 2 }, wheel: [1], gold: null };
  raw.life.store = { z_x: 3, z_y: { a: 1 } };
  const got = parse(JSON.stringify(raw));
  assert.deepEqual(got.run.store, { levels: { lvl: 2 } });
  assert.deepEqual(got.life.store, { z_y: { a: 1 } });
});
