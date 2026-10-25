import test from 'node:test';
import assert from 'node:assert/strict';
import { createPressContacts } from '../render/pressContacts.js';
import { setMachineryBeat } from '../track/machineryBeat.js';
import { playPressThud, pressThudGain } from '../audio/pressThud.js';
import { sfxPlan } from '../audio/sfxmap.js';

const zone = { id: 'press', kind: 'piston', p: [0, 0, 0], b: [1, 0, 0], x: 0, offsetS: 0 };
test('one quiet thud at the real floor contact, no repeat during its hold', () => {
  const track = {}, heard = [], contacts = createPressContacts(track, (...args) => heard.push(args), [zone]);
  setMachineryBeat(track, { enabled: true, songS: 0, raceS: 0, bpm: 120 });
  contacts.update(.95, [0, 0, 0]);
  contacts.update(1, [0, 0, 0]);
  contacts.update(1.05, [0, 0, 0]);
  assert.equal(heard.length, 1);
  assert.equal(heard[0][0], 'press-thud');
  assert.equal(heard[0][1].gain, 1);
});
test('seeks, paused frames, menus and distant impacts stay quiet', () => {
  const track = {}, heard = [], contacts = createPressContacts(track, (...args) => heard.push(args), [zone]);
  setMachineryBeat(track, { enabled: true, songS: 0, raceS: 0, bpm: 120 });
  contacts.update(1.9, [0, 0, 0]);
  contacts.update(2, [0, 0, 0], { paused: true });
  contacts.update(6, [0, 0, 0]);
  contacts.update(9.9, [49, 0, 0]);
  contacts.update(10, [49, 0, 0]);
  contacts.update(13.9, [0, 0, 0]);
  contacts.update(14, [0, 0, 0], { active: false });
  contacts.update(14.1, [0, 0, 0]);
  assert.equal(heard.length, 0);
});
test('simultaneous nearby presses yield only the closest thud', () => {
  const heard = [], contacts = createPressContacts({}, (...args) => heard.push(args), [zone, { ...zone, id: 'farther', p: [30, 0, 0] }]);
  contacts.update(5.1, [12, 0, 0]); contacts.update(5.2, [12, 0, 0]);
  assert.equal(heard.length, 1); assert.equal(heard[0][1].id, 'press');
  assert.equal(heard[0][1].gain, .5625);
});
test('sample and synth thuds have bounded gain, with a quieter Gentle response', () => {
  assert.equal(pressThudGain({ gain: 2 }), 1);
  assert.equal(pressThudGain({ gain: 1, gentle: true }), .55);
  assert.deepEqual(sfxPlan('press-thud', {}), []);
  assert.ok(sfxPlan('press-thud', { gain: 1 })[0].vol <= .06);
  const sounds = [], syn = { tone: (...a) => sounds.push(a), noise: a => sounds.push([a]) };
  playPressThud(syn, { gain: 1 });
  assert.equal(sounds.length, 2); assert.ok(sounds[0][1].vol <= .035);
});
