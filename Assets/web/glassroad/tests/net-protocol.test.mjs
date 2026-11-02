// net/protocol.js against net/vectors.json (the bytes the room and the lobby are pinned to as well).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as P from '../net/protocol.js';
import { ITEM_IDS } from '../sim/itemTable.js';
import { createState, S } from '../sim/state.js';

const read = (p) => JSON.parse(readFileSync(new URL(p, import.meta.url), 'utf8'));
const V = read('../net/vectors.json');

const close = (a, b, path = '') => {
  if (typeof a === 'number' && typeof b === 'number') return assert.ok(Math.abs(a - b) < 1e-9, `${path}: ${a} vs ${b}`);
  if (Array.isArray(b)) { assert.equal(a.length, b.length, path); return b.forEach((x, i) => close(a[i], x, `${path}[${i}]`)); }
  if (b && typeof b === 'object') { assert.deepEqual(Object.keys(a).sort(), Object.keys(b).sort(), path); for (const k of Object.keys(b)) close(a[k], b[k], `${path}.${k}`); return; }
  assert.equal(a, b, path);
};
const one = (k) => { const u = new Uint8Array(P.KART_BYTES); P.packKart(new DataView(u.buffer), 0, k); return u; };

test('wire constants match the game and the art', () => {
  assert.deepEqual([...P.WIRE_ITEMS], ITEM_IDS);
  assert.equal(V.v, P.NET_V); assert.equal(V.kartBytes, 40); assert.equal(V.headerBytes, 8);
  const wardrobe = read('../art/models/emi-bot-wardrobe.json').outfits.map((o) => o.id);
  const vehicles = read('../art/models/emi-vehicle-variants.json').vehicles.map((o) => o.id);
  assert.deepEqual([...P.LOOK_OUTFITS], wardrobe);
  assert.deepEqual([...P.LOOK_VEHICLES], ['emi-teacup-kart', ...vehicles]);
  assert.equal(P.encodeFrame(P.BIN.BATCH, 0, Array.from({ length: 8 }, P.createKart)).length, 328);
});

test('kart records encode and decode to the vector bytes', () => {
  for (const c of V.karts) {
    const u = one(c.in);
    assert.equal(P.toHex(u), c.hex, c.name);
    const d = P.unpackKart(new DataView(P.fromHex(c.hex).buffer), 0);
    close(d, c.out, c.name);
    if (c.name !== 'zero quat') assert.equal(P.toHex(one(d)), c.hex, `${c.name} re-encodes the same`); // zero quat comes back as identity
  }
});

test('frames and malformed frames', () => {
  for (const f of V.frames) {
    const d = P.decodeFrame(P.fromHex(f.hex));
    close(d, f.out, f.name);
    assert.equal(P.toHex(P.encodeFrame(d.type, d.ms, d.karts)), f.hex, f.name);
  }
  for (const b of V.badFrames) assert.equal(P.decodeFrame(P.fromHex(b.hex)), null, b.name);
  assert.equal(P.decodeFrame(new ArrayBuffer(3)), null);
});

test('message tables agree with the vectors', () => {
  for (const m of V.messages) assert.equal(P.decodeMsg(m.text, m.dir) !== null, m.ok, m.text.slice(0, 120));
  assert.throws(() => P.encodeMsg({ t: 'emote', code: 'Free text' }));
  assert.equal(P.encodeMsg({ t: 'ready', on: false }), '{"t":"ready","on":false}');
});

test('looks, bot looks and seq order', () => {
  for (const c of V.botLooks) c.seats.forEach((l, s) => assert.deepEqual(P.botLook(s, c.seed), l));
  for (const c of V.normLooks) assert.deepEqual(P.normLook(c.in), c.out);
  for (const c of V.seqNewer) assert.equal(P.seqNewer(c.a, c.b), c.newer, `${c.a} vs ${c.b}`);
});

test('kartFromState carries a sim state through the wire', () => {
  const st = createState();
  st[S.s] = 321.5; st[S.lateral] = -1.25; st[S.height] = 0.4; st[S.lap] = 2; st[S.checkpoint] = 1; st[S.grounded] = 1;
  st[S.quatY] = Math.SQRT1_2; st[S.quatW] = Math.SQRT1_2; st[S.velX] = 20; st[S.speedPct] = 0.75;
  st[S.driftDir] = -1; st[S.driftStage] = 3; st[S.boostKind] = 6; st[S.respawnPhase] = 4; st[S.stalled] = 0.3;
  const k = P.kartFromState(P.createKart(), st, { tick: 77, seq: 3, seat: 5, paused: true, item: 'melt', effect: 'pink', pulses: P.PULSE.WORD, pops: 12 });
  const d = P.decodeFrame(P.encodeFrame(P.BIN.UPLOAD, 1, [k])).karts[0];
  assert.equal(d.seat, 5); assert.equal(d.paused, true); assert.equal(d.lap, 2); assert.equal(d.s, 321.5); assert.equal(d.x, -1.25);
  assert.equal(d.driftDir, -1); assert.equal(d.driftStage, 3); assert.equal(d.boostKind, 6); assert.equal(d.respawn, 4); assert.equal(d.stalled, true);
  assert.equal(d.item, 'melt'); assert.equal(d.effect, 'pink'); assert.equal(d.pulses, P.PULSE.WORD); assert.equal(d.pops, 12);
  assert.ok(Math.abs(d.q[1] - Math.SQRT1_2) < 1e-4 && Math.abs(d.speedPct - 0.75) < 0.006);
});
