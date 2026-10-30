// tests/sim-tunnel-bots.test.mjs - Tunnel Vision (lane B 2026-10-30): bots fire it, the ride inside the whole sim, the net mirror.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fakeTrack, place, makeFeel, recorder, acts, DT } from './sim-fake.mjs';
import { S } from '../sim/state.js';
import { createItems } from '../sim/items.js';
import { makeRace } from './sim-racers-harness.mjs';
import { createNetItems } from '../net/items.js';

const RACE = { phase: 'racing' };

test('a bot at the back fires it through the ITEMS API; the ride runs in racers.step and lands on solid road', () => {
  const R = makeRace({ seed: 5, items: createItems, feelOver: { 'ai.itemUseChancePerS': 50, 'items.boxRespawnS': 999 } });
  R.run(14);
  const last = R.standings.order[R.standings.order.length - 1], bot = R.racers.list[last];
  assert.ok(bot.meta.isBot, 'a bot is last');
  bot.item.id = 'tunnel'; bot.item.rolling = false;
  const startP = R.standings.progressOf(last);
  R.run(3, () => R.items.tunnel.riding(last));
  assert.ok(R.items.tunnel.riding(last), 'the bot pressed it');
  R.run(16, () => !R.items.tunnel.riding(last));
  assert.ok(!R.items.tunnel.riding(last));
  const ev = (n) => R.events(n).map((e) => e.payload).filter((p) => p.racer === last);
  assert.equal(ev('tunnel-start').length, 1);
  assert.equal(ev('tunnel-end').length, 1);
  assert.ok(R.standings.progressOf(last) - startP > 100, 'it caught up');
  assert.ok(R.standings.placeOf(last) >= R.feel.get('items.tunnelStopPlace'), 'never the lead');
  const falls = bot.falls;
  R.run(3);
  assert.equal(bot.falls, falls, 'handed back on solid road');
});

test('online: a local ride shows effect tunnel in its record, a remote rider launches this kart once', () => {
  const track = fakeTrack(), feel = makeFeel(), rec = recorder(), sent = [];
  const list = [100, 3000, 3100].map((s, i) => ({ i, kind: i ? 'remote' : 'player', meta: { seat: i, isBot: false },
    st: place(track, { s }), input: acts({ item: false, back: false }), item: {}, mods: {}, fxv: {}, finished: false }));
  const items = createNetItems(track, feel, rec, { send: (m) => sent.push(m) });
  const std = { order: [2, 1, 0], progressOf: (i) => list[i].st[S.s], placeOf: (i) => 3 - i, gapToLeader: () => 3000 };
  const tick = () => { for (const r of list) if (items.tunnel.riding(r.i)) items.tunnel.step(r, DT); items.update(DT, list, std, RACE); };
  tick();
  list[0].item.id = 'tunnel'; list[0].item.rolling = false; list[0].input.item = true; tick(); list[0].input.item = false;
  assert.equal(sent.at(-1).t, 'fire'); assert.equal(sent.at(-1).mode, 'ride'); assert.equal(sent.at(-1).item, 'tunnel');
  assert.ok(items.tunnel.riding(0));
  tick();
  assert.equal(list[0].fxv.effect, 'tunnel');
  // a hit verdict while riding lands nothing
  items.onHit({ sid: 9, victim: 0, by: 1, item: 'pendulum', verdict: 'hit', durationS: 1.5, tick: 0 });
  tick();
  assert.equal(list[0].fxv.effect, 'tunnel');
  // a remote rider passing this kart: the victim's client applies the launch, once
  const u = createNetItems(track, feel, recorder(), {});
  const v = [100, 101].map((s, i) => ({ i, kind: i ? 'remote' : 'player', meta: { seat: i, isBot: false },
    st: place(track, { s }), input: acts({ item: false, back: false }), item: {}, mods: {}, fxv: {}, finished: false }));
  v[1].fxv.effect = 'tunnel';
  const r2 = recorder();
  const w = createNetItems(track, feel, r2, {});
  for (let k = 0; k < 5; k++) w.update(DT, v, std, RACE);
  const rams = r2.all('tunnel-ram').map((e) => e.payload);
  assert.equal(rams.length, 1);
  assert.equal(rams[0].attacker, 1); assert.equal(rams[0].victim, 0);
  assert.equal(r2.all('tunnel-start').length, 1);
  assert.equal(v[0].st[S.airborne], 1);
  assert.ok(u);
});
