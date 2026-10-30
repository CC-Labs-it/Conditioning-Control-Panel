/* tests/sim-stones.test.mjs - wave 2 STONES: the lip kick, the stone / void events, the rescue anchor and the
 * slipstream hold on a FAKE field (tests/sim-fake.mjs track + a `fields` row and a query that answers `stone` /
 * `hole.lip` from a lane layout, the contract in BRIEF-STONES.md), plus the rescue anchor and a fall on Threshold.
 * SHIM: until the TRACK lane lands the real `fields` row on Threshold, the Threshold tests here shim `track.fields`
 * and wrap `track.query` so it answers the void inside the field (noted in the brief). */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { S, BOOST } from '../sim/state.js';
import { clearSpeed, fieldRescueNode, lipLeave, fieldAtS } from '../sim/stones.js';
import { anchorNode } from '../sim/respawn.js';
import { createContact } from '../sim/contact.js';
import { createFeelOverlay, identityMods } from '../sim/racers.js';
import { gap } from '../sim/gaps.js';
import { loadTrack } from '../track/load.js';
import { DT, makeFeel, fakeTrack, place, recorder, acts, run } from './sim-fake.mjs';

/** Lay stones along lanes as the generator does: lane li starts staggerM * li past the field start (so the deck hands
 * over to a 3 m void on lane 0 and 6 m on lane +7), stoneM long, gapM apart, the last one ending at least 2 m before
 * the deck resumes. */
function layStones({ fromS, toS, lanes, stoneM, gapM, staggerM, w }) {
  const stones = [];
  lanes.forEach((x, li) => {
    let s = fromS + staggerM * li, k = 0;
    while (s + stoneM <= toS - 2) {
      stones.push({ id: `l${li}-${k++}`, s, x, w, len: stoneM });
      s += stoneM + gapM;
    }
  });
  return stones;
}

/** A fake field on a fake track: `fields` row + a query answering `stone` / `hole.lip` per the TRACK contract. */
function fieldTrack({ fromS = 120, lenM = 47, lanes = [-7, 0, 7], stoneM = 5, gapM = 4, staggerM = 3, w = 5, spacing = 10 } = {}) {
  const toS = fromS + lenM;
  const inField = (seg) => seg * spacing >= fromS && seg * spacing < toS;
  const tr = fakeTrack({ w: 21, spacing, flags: (seg) => (inField(seg) ? ['open'] : ['rail']) });
  const stones = layStones({ fromS, toS, lanes, stoneM, gapM, staggerM, w });
  const node = (s) => { let i = 0; while (tr.nodeS[i] < s - 1e-6) i++; return i; };
  const field = { id: 'f', from: node(fromS), to: node(toS), fromS, toS, lanes, stones };
  const VOID = { id: 'f:void', field, lip: true };   // one shared object per field (the contract)
  tr.fields = [field];
  const base = tr.query;
  tr.query = (px, py, pz, hint = -1, out = {}) => {
    const q = base(px, py, pz, hint, out);
    q.stone = null; q.hole = null; q.holeDist = Infinity;
    if (q.s < fromS || q.s >= toS) return q;
    let best = Infinity;
    for (const st of stones) {
      const dx = Math.max(0, Math.abs(q.lateral - st.x) - st.w / 2), ds = Math.max(0, st.s - q.s, q.s - (st.s + st.len));
      if (dx === 0 && ds === 0 && q.s < st.s + st.len) { q.stone = st; return q; }
      best = Math.min(best, Math.hypot(dx, ds));
    }
    q.hole = VOID; q.onRoad = false; q.holeDist = -best;
    return q;
  };
  return tr;
}

const lane0 = (tr) => tr.fields[0].stones.filter((s) => s.x === 0);

test('clearSpeed: the kick clears the design gap under the design speed and 10 m/s does not', () => {
  const feel = makeFeel();
  const gapM = feel.get('stones.clearGapM'), kick = feel.get('stones.kickUpMps'), g = feel.get('hop.gravityMps2'), snap = gap(feel, 'kart.snapM');
  assert.equal(gapM, 4); assert.equal(kick, 3.5); assert.equal(g, 28); assert.equal(snap, 0.35);
  const v = clearSpeed(gapM, kick, g, snap);
  assert.ok(v <= feel.get('stones.minClearMps'), `clear speed ${v} under ${feel.get('stones.minClearMps')}`);
  assert.ok(v > 10, `10 m/s does not clear (needs ${v})`);
  // with no snap band the flight is the bare arc: 2 k / g
  assert.ok(Math.abs(clearSpeed(gapM, kick, g, 0) - gapM / (2 * kick / g)) < 1e-9);
});

test('lipLeave: any supported ground (deck or stone) to the field void, never a court hole or from the void', () => {
  const stone = { id: 's' }, lip = { id: 'f:void', lip: true }, court = { id: 'h' };
  assert.equal(lipLeave({ onRoad: true, stone }, { hole: lip, stone: null }), true, 'off a stone');
  assert.equal(lipLeave({ onRoad: true, stone: null }, { hole: lip, stone: null }), true, 'off the deck at the field start');
  assert.equal(lipLeave({ onRoad: false, stone: null }, { hole: lip }), false, 'not from the void');
  assert.equal(lipLeave({ onRoad: true, stone }, { hole: court }), false, 'a court hole has no lip');
  assert.equal(lipLeave({ onRoad: true, stone }, { hole: null, stone }), false, 'still on the stone');
  assert.equal(lipLeave(null, { hole: lip }), false);
});

test('20 m/s straight down lane 0: every edge kicks once as a lip, N stone events, grounded on the far side, no drop', () => {
  const feel = makeFeel(), tr = fieldTrack(), rec = recorder(), f = tr.fields[0];
  const st = place(tr, { s: f.fromS - 20, lateral: 0, speed: 20 });
  run(st, tr, feel, acts({ throttle: 1 }), 6, rec, (s) => s[S.s] > f.toS + 12);
  const stones = lane0(tr);
  assert.ok(stones.length >= 4, 'a real field ' + stones.length);
  assert.equal(rec.all('drop').length, 0); assert.equal(rec.all('void').length, 0); assert.equal(rec.all('fall').length, 0);
  assert.deepEqual(rec.all('stone').map((e) => e.payload.id), stones.map((s) => s.id), 'one stone event per stone, in order');
  const air = rec.all('airborne');
  assert.equal(air.length, stones.length + 1, 'one kick at the deck entry and one per stone edge');
  assert.ok(air.every((e) => e.payload.cause === 'lip'), 'every take-off is a lip');
  assert.equal(rec.all('edge-enter').length, 0, 'no edge glow at a stone edge');
  assert.equal(st[S.grounded], 1); assert.ok(st[S.s] > f.toS, 'past the field');
  assert.ok(Math.abs(st[S.lateral]) < 2.5, 'kept its lane ' + st[S.lateral]);
  const ev = rec.first('stone');
  assert.ok(ev.payload.s >= stones[0].s - 0.5 && Math.abs(ev.payload.x) < 1, 'payload s, x');
  // entering from the deck: lane 0's first stone starts 3 m in, the deck edge kicks and the kart clears the entry void
  assert.equal(stones[0].s - f.fromS, 3, 'the 3 m entry void');
  assert.ok(air[0].t < ev.t && ev.payload.id === stones[0].id, 'the deck kick lands on the first stone of lane 0');
});

test('8 m/s: the kick comes down below the next deck, void then drop cause hole, rescue anchors before the field', () => {
  const feel = makeFeel(), tr = fieldTrack(), rec = recorder(), f = tr.fields[0];
  // 2 m before the first gap at 8 m/s, coasting (throttle would ramp the engine past the clear speed)
  const first = lane0(tr)[0], st = place(tr, { s: first.s + first.len - 2, lateral: 0, speed: 8 });
  run(st, tr, feel, acts(), 4, rec, () => !!rec.first('drop'));
  const v = rec.first('void'), d = rec.first('drop');
  assert.ok(v, 'void fired'); assert.ok(d, 'dropped');
  assert.ok(v.t < d.t, 'void before the drop');
  assert.equal(d.payload.cause, 'hole');
  assert.equal(rec.all('void').length, 1);
  assert.equal(rec.all('stone').length, 1, 'the first stone only');
  assert.ok(v.payload.s > f.fromS && v.payload.s < f.toS, 'void inside the field ' + v.payload.s);
  const a = anchorNode(st, tr);
  assert.ok(tr.nodeS[a] < f.fromS && tr.nodeS[a] >= f.fromS - 10 - 1e-6, 'anchored at the last node before the field ' + tr.nodeS[a]);
  // and the rescue lets go there: the kart is back on the deck before the field
  run(st, tr, feel, acts(), 2, rec, () => !!rec.first('rescue-release'));
  run(st, tr, feel, acts(), feel.get('respawn.releaseS') + DT, rec);
  assert.ok(st[S.s] < f.fromS, 'released before the field ' + st[S.s]);
});

test('the kick never fires on the deck; a hop on the same stone is no new stone event', () => {
  // the deck before the field (the field track's query answers stone null outside the field, as the contract says;
  // the sim's query out objects are shared, so a plain fake track would carry a stale stone from an earlier test)
  const feel = makeFeel(), rec = recorder(), tr = fieldTrack(), f = tr.fields[0], first = lane0(tr)[0];
  const st = place(tr, { s: f.fromS - 60, speed: 20 });
  run(st, tr, feel, acts({ throttle: 1 }), 2, rec);
  assert.ok(st[S.s] < f.fromS, 'still on the deck');
  assert.equal(rec.all('airborne').length, 0); assert.equal(rec.all('stone').length, 0);
  const rec2 = recorder();
  // Slow, so the higher hop (about 0.48 s in the air) comes down on the same 5 m stone.
  const k = place(tr, { s: first.s + 0.5, lateral: 0, speed: 4 });
  run(k, tr, feel, acts(), 2 * DT, rec2);
  assert.deepEqual(rec2.all('stone').map((e) => e.payload.id), [first.id]);
  run(k, tr, feel, acts({ hopPressed: true }), 0.6, rec2, () => !!rec2.first('land'));
  assert.ok(rec2.first('hop') && rec2.first('land'), 'hopped and landed');
  assert.equal(rec2.all('stone').length, 1, 'same stone, no new event');
});

test('fieldRescueNode: inside the field walks back to the deck before it, outside is untouched', () => {
  const tr = fieldTrack(), f = tr.fields[0];
  assert.equal(fieldAtS(tr, f.fromS + 1), f); assert.equal(fieldAtS(tr, f.fromS - 1), null); assert.equal(fieldAtS(tr, f.toS), null);
  for (let i = f.from; i < f.to; i++) assert.equal(fieldRescueNode(tr, i), f.from - 1);
  assert.equal(fieldRescueNode(tr, f.from - 1), f.from - 1);
  assert.equal(fieldRescueNode(tr, f.to), f.to);
  assert.equal(fieldRescueNode(fakeTrack(), 7), 7, 'no fields: untouched');
});

test('slipstream: the kick waits from stones.leadM before a field to its end', () => {
  const feel = makeFeel(), tr = fieldTrack({ fromS: 120 }), rec = recorder(), f = tr.fields[0];
  const racer = (i, opts) => ({ i, st: place(tr, opts), feel: createFeelOverlay(feel), mods: identityMods(), fxv: { ghost: false, slip: 0 }, events: recorder() });
  const c = createContact(tr, feel, rec), RACING = { phase: 'racing', timeS: 10 };
  const lead = racer(0, { s: 108, lateral: 0, speed: 28 }), tail = racer(1, { s: 100, lateral: 0.5, speed: 28 });
  for (const r of [lead, tail]) r.st[S.speedPct] = 28 / feel.get('speed.topMps');
  assert.ok(100 >= f.fromS - feel.get('stones.leadM'), 'the tail sits inside the lead');
  const n = Math.ceil(feel.get('contact.slipChargeS') / DT) + 4;
  for (let i = 0; i < n; i++) { rec.t += DT; c.update(DT, [lead, tail], RACING); }
  const states = () => rec.all('slipstream').filter((e) => e.payload.racer === 1).map((e) => e.payload.state);
  assert.deepEqual(states(), ['in'], 'charged but held before the field');
  assert.equal(tail.st[S.boostKind], BOOST.NONE);
  lead.st[S.s] = f.toS + 10; tail.st[S.s] = f.toS + 2;
  rec.t += DT; c.update(DT, [lead, tail], RACING);
  assert.deepEqual(states(), ['in', 'fire'], 'fires once the field is behind');
  assert.equal(tail.st[S.boostKind], BOOST.PAD);
});

// Threshold: the real bridge. SHIM until the TRACK lane lands `fields` there: the field sits on the bridge's old
// open-rail region (nodes 32..34 are the only `open` nodes today) and the query is wrapped to answer the void.
function thresholdWithField() {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
  if (track.fields && track.fields.length) return track;        // TRACK landed: the real field
  const from = 32, fromS = track.nodeS[from], toS = fromS + 50;
  let to = from; while (track.nodeS[to] < toS - 1e-6) to++;
  const lanes = [-7, 0, 7];
  const stones = layStones({ fromS, toS, lanes, stoneM: 5, gapM: 4, staggerM: 3, w: 5 });
  const field = { id: 'bridge-stones', from, to, fromS, toS, lanes, stones }, VOID = { id: 'bridge-stones:void', field, lip: true };
  track.fields = [field];
  const base = track.query;
  const query = (px, py, pz, hint = -1, out) => {
    const q = base(px, py, pz, hint, out);
    q.stone = null;
    if (q.s < fromS || q.s >= toS || !q.onRoad) return q;
    const st = stones.find((s) => q.s >= s.s && q.s < s.s + s.len && Math.abs(q.lateral - s.x) <= s.w / 2);
    if (st) q.stone = st; else { q.hole = VOID; q.holeDist = -1; q.onRoad = false; }
    return q;
  };
  query.below = base.below;
  track.query = query;
  return track;
}

test('Threshold: a rescue inside the bridge field anchors before it (shimmed field until TRACK lands)', () => {
  const track = thresholdWithField(), f = track.fields[0];
  const a = fieldRescueNode(track, f.from + 1);
  assert.ok(track.nodeS[a] < f.fromS, 'before the field');
  assert.equal(fieldRescueNode(track, a), a);
});

test('Threshold: 8 m/s down lane 0 of the bridge field drops below the deck: void, then the fall or the hole drop', () => {
  const track = thresholdWithField(), feel = makeFeel(), rec = recorder(), f = track.fields[0];
  // lane 0 opens with a lead stone (0..8 m): start 2 m before its end so the coast steps off it into the first gap
  const lead = f.stones.find(h => h.x === 0);
  const st = place(track, { s: lead.s + lead.len - 2, lateral: 0, speed: 8 });
  run(st, track, feel, acts(), 5, rec, () => !!(rec.first('drop') || rec.first('fall-land')));
  const v = rec.first('void');
  assert.ok(v, 'void fired: ' + rec.names().join(','));
  assert.ok(rec.first('stone') && rec.first('stone').t <= v.t, 'the lead stone first (every lane starts with one at the deck edge)');
  const end = rec.first('fall') || rec.first('drop');
  assert.ok(end && end.t >= v.t, 'then the fall or the drop');
  if (end.name === 'drop') assert.equal(end.payload.cause, 'hole');
  assert.equal(rec.all('void').length, 1);
});
