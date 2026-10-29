import test from 'node:test';
import assert from 'node:assert/strict';
import { createNetItems } from '../net/items.js';
import { createRacers } from '../sim/racers.js';
import { S, BOOST } from '../sim/state.js';
import { fakeTrack, makeFeel, recorder, place, DT } from './sim-fake.mjs';

function setup() {
  const track = fakeTrack(), feel = makeFeel(), events = recorder();
  const racers = createRacers(track, feel, events, { net: [{ seat: 0, kind: 'human', local: true }] });
  const player = racers.player; player.st.set(place(track, { s: 100 }));
  const standings = { order: [0], placeOf: () => 1, gapToLeader: () => 0 };
  let connected = false;
  const sent = [];
  const items = createNetItems(track, feel, events, { send: message => {
    if (!connected) return false;
    sent.push(message); return true;
  } });
  return { track, events, player, items, sent, connect: () => { connected = true; },
    tick: () => items.update(DT, racers.list, standings, { phase: 'racing' }) };
}

test('a disconnected fire keeps the held item, its shot id and its local effect', () => {
  const r = setup();
  r.player.item.id = 'surge'; r.player.input.item = true;
  r.tick();
  assert.equal(r.player.item.id, 'surge');
  assert.equal(r.player.st[S.boostKind], BOOST.NONE);
  assert.equal(r.events.all('item-fire').length, 0);
  r.connect(); r.tick();
  assert.equal(r.sent[0].id, 1, 'an unsent attempt consumes no shot id');
  assert.equal(r.player.item.id, null);
  assert.notEqual(r.player.st[S.boostKind], BOOST.NONE);
  assert.equal(r.events.all('item-fire').length, 1);
});

test('an unsent pickup leaves its box visible and immediately available after reconnect', () => {
  const r = setup();
  // Pickups are constructed once, so use a new instance with this fixture's box.
  r.track.items = [{ kind: 'box', s: 100, x: 0 }];
  let connected = false;
  const sent = [], items = createNetItems(r.track, makeFeel(), r.events, {
    send: m => { if (!connected) return false; sent.push(m); return true; },
  });
  const tick = () => items.update(DT, [r.player], { placeOf: () => 1, gapToLeader: () => 0 }, { phase: 'racing' });
  tick();
  assert.equal(items.view().boxes[0].alive, true);
  assert.equal(items.state(0).pendT, 0);
  connected = true; tick();
  assert.equal(sent.length, 1);
  assert.equal(sent[0].t, 'pickup');
  assert.equal(items.view().boxes[0].alive, false);
  assert.ok(items.state(0).pendT > 0);
});
