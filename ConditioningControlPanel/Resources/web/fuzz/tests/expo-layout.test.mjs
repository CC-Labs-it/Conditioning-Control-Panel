import test from 'node:test';
import assert from 'node:assert/strict';
import { ROOMS, START, canWalk, roomAt } from '../ui/expo/layout.js';

const worldPoint = (room, x, z) => ({
  x: room.x + x * Math.cos(room.angle) + z * Math.sin(room.angle),
  z: room.z - x * Math.sin(room.angle) + z * Math.cos(room.angle),
});
function walk(from, to) {
  const steps = Math.ceil(Math.hypot(to.x - from.x, to.z - from.z) / .1);
  for (let i = 0; i <= steps; i++) {
    const f = steps ? i / steps : 0;
    const x = from.x + (to.x - from.x) * f, z = from.z + (to.z - from.z) * f;
    assert.ok(canWalk(x, z), `blocked at ${x.toFixed(2)},${z.toFixed(2)}`);
  }
}

test('the plaza and all six halls are connected by continuous player-width paths', () => {
  assert.deepEqual(ROOMS.map(room => room.id), ['first', 'hoof', 'habitat', 'twelve', 'dome', 'shop']);
  const hub = { x: 0, z: 0 };
  assert.ok(canWalk(START.x, START.z));
  walk(START, hub);
  for (const room of ROOMS) {
    assert.equal(roomAt(room.x, room.z), room.id);
    walk(hub, room);
    walk(room, hub);
  }
});

test('the secret corridor reaches its far room and remains freely reversible', () => {
  const hub = { x: 0, z: 0 }, farRoom = { x: 0, z: -123 };
  walk(hub, farRoom);
  assert.equal(roomAt(farRoom.x, farRoom.z), 'contract');
  walk(farRoom, hub);
});

test('the dome doorway connects to the gift shop through the hub', () => {
  const dome = ROOMS.find(room => room.id === 'dome');
  const shop = ROOMS.find(room => room.id === 'shop');
  const threshold = worldPoint(dome, 0, -13);
  walk(dome, threshold);
  walk(threshold, { x: 0, z: 0 });
  walk({ x: 0, z: 0 }, shop);
});

test('collision uses the player radius at each hall wall, including the curved dome', () => {
  for (const room of ROOMS) {
    const halfWidth = room.id === 'dome' ? 13 : room.width / 2;
    const nearWall = worldPoint(room, halfWidth - .1, 0);
    const safe = worldPoint(room, halfWidth - .5, 0);
    assert.ok(canWalk(nearWall.x, nearWall.z, 0), `${room.id}: the point is on the floor`);
    assert.equal(canWalk(nearWall.x, nearWall.z), false, `${room.id}: the body cannot clip the wall`);
    assert.ok(canWalk(safe.x, safe.z), `${room.id}: room to turn beside the wall`);
  }
});

test('outside ground and invalid coordinates cannot become walkable', () => {
  for (const [x, z] of [[100, 100], [-100, -100], [0, -130], [NaN, 0], [0, Infinity]]) {
    assert.equal(canWalk(x, z), false, `${x},${z}`);
  }
});
