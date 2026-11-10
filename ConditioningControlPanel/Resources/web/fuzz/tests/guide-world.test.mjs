import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { BY_ID, FORKS, FORK_OF_LANE } from '../data/items.js';
import * as E from '../sim/economy.js';
import { islandState, layoutWorld, SHOP_GOODS, worldStates } from '../ui/guide-world-model.js';
import { depthName, openWater, placeDecor } from '../ui/guide-sea.js';

const ROOT = fileURLToPath(new URL('..', import.meta.url));

/** Play a run the cheap way: buy the cheapest open row, take the first (or a chosen) option at each fork. */
function play(n, choose = {}) {
  const s = E.createRun();
  s.fuzz = 1e15;
  for (let i = 0; i < n; i++) {
    const o = E.openItems(s).find((it) => it.kind !== 'end');
    if (!o) break;
    if (o.kind === 'fork') E.choose(s, o.id, choose[o.id] || Object.keys(FORKS[o.id].opts)[0], null, 0);
    else E.buy(s, o.id);
  }
  return s;
}

test('every placed node sits on the map, inside its island, with no two stamps on the same spot', () => {
  const w = layoutWorld();
  const seen = new Set();
  const islands = Object.fromEntries(w.islands.map((i) => [i.id, i]));
  for (const n of w.nodes) {
    assert.ok(n.x > 0 && n.x < w.W && n.y > 0 && n.y < w.H, `${n.id} on the map`);
    const key = `${Math.round(n.x)},${Math.round(n.y)}`;
    assert.ok(!seen.has(key), `${n.id} has a spot of its own`);
    seen.add(key);
    const i = islands[n.lane];
    if (i) assert.ok(n.x >= i.x && n.x <= i.x + i.w && n.y >= i.y && n.y <= i.y + i.h, `${n.id} inside ${i.id}`);
  }
  assert.ok(w.islands.every((i) => i.x >= 100), 'nothing hangs off the left edge');
});

test('every fork option is an island with a plank, and every route row of the catalog is placed', () => {
  const w = layoutWorld();
  const ids = new Set(w.nodes.map((n) => n.id));
  for (const [fid, f] of Object.entries(FORKS)) {
    const post = w.forks.find((p) => p.id === fid);
    assert.ok(post, fid);
    for (const lane of Object.keys(f.opts)) {
      assert.ok(post.planks.some((p) => p.lane === lane), `${fid} plank ${lane}`);
      assert.ok(w.islands.some((i) => i.id === lane), `${lane} island`);
    }
  }
  for (const it of Object.values(BY_ID)) if (it.group === 'run' && FORK_OF_LANE[it.lane]) assert.ok(ids.has(it.id), `${it.id} placed`);
});

test('every picture the map draws exists: shop goods and row stamps', () => {
  const w = layoutWorld();
  for (const g of SHOP_GOODS) assert.ok(existsSync(ROOT + g.pic), g.pic);
  for (const i of w.islands) for (const l of i.ladders) assert.ok(existsSync(`${ROOT}assets/clipart/${l.shape}.webp`), l.shape);
});

test('a fresh run: start owned, the first row on offer and tagged next, every route open, nothing shut', () => {
  const s = E.createRun();
  const v = worldStates(s);
  assert.equal(v.states.start, 'own');
  assert.equal(v.here, 'start');
  assert.equal(v.states.stray, 'offer');
  assert.equal(v.next, 'stray');
  /* Only the endings retired from the shop read as shut on a fresh run (sim/economy.js OFF_SHELF; Drop is the road's end). */
  const retired = layoutWorld().nodes.filter((n) => n.id !== 'drop' && E.OFF_SHELF.has(n.id)).map((n) => n.id);
  assert.ok(retired.length >= 5, retired.join());
  assert.deepEqual(Object.keys(v.states).filter((id) => v.states[id] === 'off').sort(), retired.sort());
  for (const i of layoutWorld().islands) assert.notEqual(islandState(s, i), 'shut', i.id);
});

test('fate cards: the first seed shuts every other set, and a theme set waits for its theme', () => {
  const s = E.createRun();
  E.grant(s, 'f5'); s.picks.f5 = 'trance';
  let v = worldStates(s);
  assert.equal(v.states.ft_bubble, 'off', 'Bimbodoll needs the Bimbo theme');
  assert.notEqual(v.states.ft_window, 'off');
  E.grant(s, 'ft_depth');
  v = worldStates(s);
  for (const id of ['ft_window', 'go_tx', 'go_end', 'outside', 'ft_ticket']) assert.equal(v.states[id], 'off', id);
  assert.notEqual(v.states.ft_cursor, 'off');
  assert.ok(layoutWorld().islands.some((i) => i.id === 'fate'), 'the fate pocket is on the map');
});

test('a picked fork shuts the other routes and everything that needs them', () => {
  const s = play(400, { f1: 'eyes' });
  assert.equal(s.picks.f1, 'eyes');
  const w = layoutWorld();
  const v = worldStates(s, w);
  assert.equal(islandState(s, w.islands.find((i) => i.id === 'eyes')), 'picked');
  assert.equal(islandState(s, w.islands.find((i) => i.id === 'ears')), 'shut');
  for (const n of w.nodes) if (n.lane === 'ears') assert.equal(v.states[n.id], 'off', n.id);
  assert.ok(Object.values(v.states).includes('own'));
  assert.notEqual(v.here, 'start', 'the pin follows the last buy');
  assert.equal(v.states[v.here], 'own');
});

test('a row cheaper than the last buy and still open reads as walked past', () => {
  const s = play(80);
  const placed = new Set(layoutWorld().nodes.map((n) => n.id));
  const fresh = () => E.openItems(s).filter((it) => it.kind !== 'fork' && it.kind !== 'end' && !E.has(s, it.id) && placed.has(it.id)).sort((x, y) => x.cost - y.cost);
  const open = fresh();
  assert.ok(open.length > 1);
  const dear = open[open.length - 1];
  E.buy(s, dear.id);
  const v = worldStates(s);
  const walked = fresh().filter((it) => it.cost < dear.cost);
  assert.ok(walked.length, 'something cheaper is still open');
  for (const it of walked) assert.equal(v.states[it.id], 'skip', it.id);
  for (const [id, st] of Object.entries(v.states)) if (st === 'skip') assert.ok(BY_ID[id].cost < dear.cost, id);
});

test('shop goods read ownership from the shops themselves', () => {
  const s = E.createRun();
  s.fuzz = 0;
  let v = worldStates(s);
  assert.equal(v.states.shop_face_round, 'own');
  assert.equal(v.states.shop_wave_classic, 'own');
  assert.equal(v.states.shop_cur_system, 'own');
  assert.equal(v.states['shop_face_human-check'], 'later');
  s.fuzz = 1e9;
  s.store.bubble = { waveStyles: { aurora: true } };
  v = worldStates(s);
  assert.equal(v.states['shop_face_human-check'], 'later', 'the Button Shop needs its door, the Bubble Button');
  s.owned.bubble = 1;
  v = worldStates(s);
  assert.equal(v.states['shop_face_human-check'], 'offer');
  assert.equal(v.states.shop_wave_aurora, 'own');
  assert.equal(v.states.shop_wave_loom, 'offer', 'an owned ribbon keeps its part of the shop open');
  assert.equal(v.states.shop_wave_siri9, 'later', 'kinetic ribbons wait for Glossy Icons');
  assert.equal(v.states.shop_face_emi, 'later', 'EMI waits for Pick a Theme');
  assert.equal(v.states.shop_fam_aurora, 'later', 'a trail family needs Stardust first');
  s.owned.glossy = 1; s.owned.trail = 1;
  v = worldStates(s);
  assert.equal(v.states.shop_wave_siri9, 'offer');
  assert.equal(v.states.shop_fam_aurora, 'later', 'trail families open with Starburst');
  s.owned.starburst = 1;
  assert.equal(worldStates(s).states.shop_fam_aurora, 'offer');
});

test('the sea: open water clears every island and the road; decor and the depth names fit the map', () => {
  const w = layoutWorld();
  for (const s of openWater(w)) {
    assert.ok(Math.abs(s.x - w.CX) >= 150, 'clear of the road');
    for (const i of w.islands) assert.ok(!(s.x > i.x && s.x < i.x + i.w && s.y > i.y && s.y < i.y + i.h), 'not on an island');
  }
  const decor = placeDecor(w);
  assert.ok(decor.some((d) => d.kind === 'whirl'));
  assert.ok(decor.filter((d) => d.kind === 'bubbles').every((d) => d.d >= 0.5), 'bubbles only in the deep');
  assert.ok(decor.filter((d) => d.kind === 'boat').every((d) => d.d <= 0.22), 'boats only in the shallows');
  assert.equal(depthName(0), 'Shallows');
  assert.equal(depthName(0.999), 'Gone');
});
