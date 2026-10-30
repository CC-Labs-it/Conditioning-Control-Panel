// The Prize Counter's Wear on a kart look (7.2 fix wave F1/F2): the desktop host closes the counter back to the
// garage and re-sends `owned { grants, wear: <counter row id> }`; the garage puts that item on like its own picker.
// Unknown or unowned ids change nothing; the web host and phone never send `wear`.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { DEFAULT, LOCKED, KARTS, FREE_KART, wearPick, ownedLooks, ownedKarts, lockedKarts, cleanGarage, raceLook } from '../hud/garageState.js';

const read = (rel) => readFileSync(new URL(rel, import.meta.url), 'utf8');
const ALL = ['cos.kart.outfit.pit-crew', 'cos.kart.outfit.ringmaster', 'cos.kart.outfit.ballerina', 'cos.kart.paint.pattern-bundle',
  'cos.kart.umbrella.lace', 'cos.prime.paint.host-gold', 'cos.prime.trail.podium', 'cos.kart.body.petal'];
const ctx = (grants) => ({ owns: ownedLooks(grants), locked: lockedKarts(new Set(), ownedKarts(grants)) });

test('wear: each counter family maps to its garage section, shaped like the picker', () => {
  const c = ctx(ALL);
  assert.deepEqual(wearPick('kart.body.petal', DEFAULT, c), { kind: 'kart', id: 'petal', label: 'Petal', values: { kart: 'petal' } });
  assert.deepEqual(wearPick('kart.outfit.ringmaster', DEFAULT, c), { kind: 'outfit', id: 'ringmaster', label: 'Ringmaster', values: { outfit: 'ringmaster' } });
  assert.deepEqual(wearPick('kart.umbrella.lace', DEFAULT, c), { kind: 'umbrella', id: 'lace', label: 'Lace parasol', values: { umbrella: 'lace' } });
  assert.deepEqual(wearPick('prime.trail.podium', DEFAULT, c), { kind: 'trail', id: 'podium', label: 'Podium ribbons', values: { trail: 'podium' } });
  const gold = wearPick('prime.paint.host-gold', DEFAULT, c);
  assert.equal(gold.kind, 'paint');
  assert.deepEqual(gold.values, { paint: 'host-gold', original: false, primary: '#d79b14', secondary: '#fff09a', skin: 'Host gold' });
});

test('wear: the pattern bundle puts on hearts, or keeps spiral when spiral is already worn', () => {
  const c = ctx(ALL);
  assert.deepEqual(wearPick('kart.paint.pattern-bundle', DEFAULT, c).values, { paint: 'hearts', original: false });
  const spiral = { ...DEFAULT, original: false, paint: 'spiral' };
  assert.equal(wearPick('kart.paint.pattern-bundle', spiral, c).id, 'spiral');
  assert.equal(wearPick('kart.paint.pattern-bundle', { ...spiral, original: true }, c).id, 'hearts', 'an Original skin wears no paint');
  assert.equal(wearPick('kart.paint.pattern-bundle', { ...DEFAULT, original: false, paint: 'host-gold' }, c).id, 'hearts');
  // a retired v1 row still names its own paint
  assert.equal(wearPick('kart.paint.spiral', DEFAULT, ctx(['cos.kart.paint.spiral'])).id, 'spiral');
});

test('wear: unknown, unowned, dark or junk ids are ignored', () => {
  const none = ctx([]);
  for (const id of ['kart.outfit.ringmaster', 'kart.paint.pattern-bundle', 'kart.umbrella.lace', 'prime.trail.podium', 'prime.paint.host-gold', 'kart.body.petal'])
    assert.equal(wearPick(id, DEFAULT, none), null, id + ' is not owned');
  const all = ctx(ALL);
  for (const id of ['kart.body.nope', 'kart.outfit.nope', 'kart.outfit.varsity', 'rain.dress.x', 'desk.hat.crown', 'prime.frame.room-host',
    'cos.kart.outfit.ringmaster', 'kart.body.reverie', 'KART.BODY.PETAL', 'kart.body.petal"]', '', null, undefined, 42, {}, 'x'.repeat(80)])
    assert.equal(wearPick(id, DEFAULT, all), null, String(id));
  // the free kart and an earned one need no grant
  assert.equal(wearPick('kart.body.' + FREE_KART, DEFAULT).id, FREE_KART);
  assert.equal(wearPick('kart.body.petal', DEFAULT, { owns: ownedLooks([]), locked: LOCKED }), null);
});

test('wear: what the garage stores after a Wear survives its own clean and rides the race look', () => {
  const c = ctx(ALL);
  let s = { ...DEFAULT };
  for (const id of ['kart.body.petal', 'kart.outfit.ballerina', 'prime.paint.host-gold', 'kart.umbrella.lace', 'prime.trail.podium'])
    s = { ...s, ...wearPick(id, s, c).values };
  const kept = cleanGarage(JSON.parse(JSON.stringify(s)), c.locked, c.owns);
  assert.deepEqual({ kart: kept.kart, outfit: kept.outfit, paint: kept.paint, umbrella: kept.umbrella, trail: kept.trail },
    { kart: 'petal', outfit: 'ballerina', paint: 'host-gold', umbrella: 'lace', trail: 'podium' });
  const look = raceLook(kept, c.owns);
  assert.equal(look.vehicle, 'emi-vehicle-petal');
  assert.equal(look.outfit, 'emi-outfit-ballerina');
  assert.equal(look.paint, 'host-gold');
  assert.equal(look.umbrella, 'lace');
  assert.equal(look.trail, 'podium');
  assert.ok(KARTS.some(([id]) => id === kept.kart));
});

test('wear: boot hands owned.wear to the garage after adopting the grants; the garage saves through paint()', () => {
  const boot = read('../boot.js');
  const handler = boot.slice(boot.indexOf("shared.bridge.on('owned'"), boot.indexOf("shared.bridge.on('owned'") + 260);
  assert.ok(handler.indexOf('adoptOwned(m?.grants)') >= 0 && handler.indexOf('adoptOwned(m?.grants)') < handler.indexOf('garage?.wear?.(m.wear)'),
    'grants land first, so a look bought in the same visit is wearable');
  assert.match(handler, /typeof m\?\.wear === 'string'/, 'a frame without wear (web, phone, plain back) does nothing more');
  const garage = read('../render/garage.js');
  assert.match(garage, /function wear\(item\)\{const pick=wearPick\(item,state,\{locked,owns\}\);if\(!pick\)return false;/);
  assert.match(garage, /change\(pick\.values,false,true\)/);
  assert.match(garage, /function paint\(\)\{.*save\(\);\}/, 'paint() saves the garage like the picker');
  assert.match(garage, /select,customize,change,wear,/);
  const ui = read('../hud/garage.js');
  assert.match(ui, /worn\(kind,id,line\)/);
  const css = read('../hud/garage.css');
  for (const k of ['g-note-in', 'g-note-out', 'g-worn-ring']) assert.ok(css.includes('@keyframes ' + k), k + ' IN/OUT juice');
  assert.match(css, /g-calm \.g-worn\{animation:none\}/, 'calm keeps it to a fade');
});
