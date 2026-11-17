// Late game wave, lane SIM (owner 2026-11-10, handoff-1110/TC-LATE-GAME-BRIEF.md): game arrival times, the savour rule,
// named game upgrades, felt non-game ranks and the trunk show jumps.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as E from '../sim/economy.js';
import { buildPlan, createJourney } from '../sim/journey.js';
import { ACTIVITIES, GAMES, GAME_UPGRADES, GAME_RANK_MAX, SAVOUR_BUYS, TRUNK_SHOWS, rankName, rankDescription, setupJourney, trunkBoost } from '../sim/journey-effects.js';
import { BY_ID } from '../data/items.js';
import { allRoutes } from '../sim/route.js';
import { measureJourney } from '../dev/journey-timing.mjs';

const route = allRoutes()[0];
const run = measureJourney(route);
const minuteOf = (id) => run.paid.find((p) => p.id === id).at / 60;
const indexOf = (id) => run.paid.findIndex((p) => p.id === id) + 1;

test('games arrive slowly, in the owner order, and the run keeps its length', () => {
  // Even beat (owner 2026-11-10, handoff-1110/TC-EVEN-BEAT-BRIEF.md): Wheel ~4, Slots ~9, Pop-Up Question ~12, Race ~15,
  // Boxes ~19, Breakout ~22, Deep End ~30, Toy Box ~44, Dispenser ~56, each within about 2 minutes.
  const want = { gold: [1, 3], wheel: [2, 6], mini_slots: [7, 11], quiz: [10, 14], mini_race: [13, 17], boxes: [17, 21], mini_breakout: [20, 24.5], mini_deep: [28, 32], toy_box: [42, 46], dispenser: [54, 58] };
  for (const [id, [lo, hi]] of Object.entries(want)) {
    const m = minuteOf(id);
    assert.ok(m >= lo && m <= hi, `${id} arrives at minute ${m.toFixed(1)}, wanted ${lo}-${hi}`);
  }
  assert.ok(minuteOf('dispenser') > minuteOf('mini_slots'));
  assert.ok(run.minutes > 87 && run.minutes < 94, `${run.minutes} min`);
  assert.ok(run.max <= 60.5, `longest gap ${run.max} s`);
});

test('a game rank waits SAVOUR_BUYS purchases after its game, and games stop at four ranks', () => {
  for (const id of GAMES) {
    const first = indexOf('rank_' + id + '_1');
    if (first) assert.ok(first - indexOf(id) >= SAVOUR_BUYS, `${id}: first rank ${first - indexOf(id)} buys after it arrived`);
    assert.ok(!run.paid.some((p) => p.id === 'rank_' + id + '_' + (GAME_RANK_MAX + 1)), id);
  }
  assert.ok(ACTIVITIES.includes('mini_slots'));
});

test('the live shelf holds a game rank back until the game has been owned SAVOUR_BUYS purchases', () => {
  const s = E.createRun(), shop = createJourney(s);
  const offered = () => { const { goal, rows } = shop.shop(8); return [goal, ...rows].filter(Boolean); };
  let late = null, seen = null;
  for (let n = 0; n < 400 && !s.ended && seen === null; n++) {
    E.lump(s, 1e15);
    const list = offered();
    if (late !== null && list.some((it) => it.id === 'rank_wheel_1')) { seen = s.buys; break; }
    const it = list.find((x) => x.kind === 'fork') || list.find((x) => x.id !== 'drop' && x.kind !== 'end') || list[0];
    const res = it.kind === 'fork' ? shop.choose(it.id, route[it.id]) : shop.buy(it.id, it.options?.[0]?.[0]);
    assert.ok(res.ok, it.id);
    // Pretend the wheel arrived 20 purchases later than it did: its first upgrade must wait for that too.
    if (it.id === 'wheel') { late = s.buys + 20; s.store.journey.arrive.wheel = late; }
  }
  assert.ok(late !== null && seen !== null);
  assert.ok(seen - late >= SAVOUR_BUYS, `offered ${seen - late} buys after arrival`);
});

test('game rank 1 and 2 rows carry the contract names; later ranks keep the bonus text', () => {
  const rows = buildPlan(route).filter((e) => e.rank);
  const wheel1 = rows.find((e) => e.item.id === 'rank_wheel_1');
  assert.equal(wheel1.item.name, 'Prize Wheel: Respin');
  assert.equal(wheel1.item.what, GAME_UPGRADES.wheel[0].what);
  assert.equal(rankName('dispenser', 2, 'Prize Dispenser'), 'Prize Dispenser: Double Drop');
  assert.equal(rankName('mini_slots', 1, 'Mini Slots'), 'Mini Slots: Hold');
  assert.equal(rankName('wheel', 3, 'Prize Wheel'), 'Prize Wheel · Rank 4');
  assert.match(rankDescription('wheel', 3), /bonus/);
  assert.equal(rankName('stray', 1, 'Bouncing Button'), 'Bouncing Button · Rank 2');
  for (const [id, ups] of Object.entries(GAME_UPGRADES)) {
    assert.ok(BY_ID[id] && GAMES.includes(id), id);
    assert.equal(ups.length, 2);
    for (const u of ups) assert.ok(!/[–—!]/.test(u.name + u.what), `${id}: plain copy`);
  }
});

test('every non-game rank lifts a staying player about 3-6% when bought', () => {
  const lifts = run.paid.filter((p) => /^rank_/.test(p.id) && !GAMES.some((g) => p.id.startsWith('rank_' + g + '_'))).map((p) => p.lift * 100);
  assert.ok(lifts.length > 20);
  const mean = lifts.reduce((a, b) => a + b, 0) / lifts.length;
  assert.ok(mean > 3 && mean < 6, `mean lift ${mean}`);
  assert.ok(Math.min(...lifts) > 1.5, `smallest lift ${Math.min(...lifts)}`);
});

test('each trunk show multiplies every Journey income path by 1.5, the plain shop by nothing', () => {
  const s = E.createRun();
  E.grant(s, 'overlay');
  const plain = E.rates(s);
  assert.equal(trunkBoost(s), 1, 'no Journey, no jump');
  setupJourney(s);
  const before = { ...s, owned: { ...s.owned, overlay: 0 } };
  const a = E.rates(before), b = E.rates(s);
  for (const k of ['fps', 'click', 'manualClick', 'bounce']) assert.ok(Math.abs(b[k] / a[k] - 1.5) < 1e-9, k);
  E.grant(s, 'tunnel');
  assert.equal(trunkBoost(s), 1.5 ** TRUNK_SHOWS.length);
  assert.ok(plain.fps > 0);
  const show = run.paid.filter((p) => TRUNK_SHOWS.includes(p.id));
  assert.equal(show.length, 4);
  for (const p of show) assert.ok(p.lift > .45, `${p.id} lifts ${p.lift}`);
});

test('haptic rows and Friend Remote are never plan rows, so a web run reaches its own ending without them', () => {
  for (const r of allRoutes().filter((_, i) => i % 40 === 0)) {
    const ids = buildPlan(r).map((e) => e.item.id);
    for (const id of ['toy', 'buzz', 'remote']) assert.ok(!ids.includes(id), id);
  }
  assert.equal(run.s.ended.id, 'o_out');  // This route completes the Living Ad chain first.
  assert.ok(!run.paid.some((p) => ['toy', 'buzz', 'remote'].includes(p.id)));
});
