import test from 'node:test';
import assert from 'node:assert/strict';
import { windowPreferences, readPreviewPreferences, writePreviewPreferences, PREVIEW_PREFS_KEY } from '../ui/preferences.js';
import { serialize, parse, createLife } from '../sim/save.js';

test('window layout survives a new run and uses old run positions before migration', () => {
  const extras = {}, prefs = windowPreferences(extras), old = { x: 20, y: 40, min: false };
  assert.deepEqual(prefs.get('quiz:_dock1', old), old);
  prefs.set('quiz:_dock1', { ...old, w: 350, h: 280, z: 9 });
  const saved = parse(serialize(null, createLife(), extras));
  assert.deepEqual(windowPreferences(saved.extras).get('quiz:_dock1'), { ...old, w: 350, h: 280, z: 9 });
});
test('preview stores only layout and mix and never changes the normal save', () => {
  const data = new Map([['fuzz.save.v2', 'normal save']]);
  const storage = { getItem: k => data.get(k), setItem: (k, v) => data.set(k, v) };
  writePreviewPreferences(storage, { windowLayouts: { 'quiz:_dock1': { x: 20, w: 300 } }, audioMix: { rain: { volume: .2, muted: false } }, muted: true, masterVolume: .65, fuzz: 100000 });
  assert.equal(data.get('fuzz.save.v2'), 'normal save');
  assert.deepEqual(readPreviewPreferences(storage), { windowLayouts: { 'quiz:_dock1': { x: 20, w: 300 } }, audioMix: { rain: { volume: .2, muted: false } }, muted: true, masterVolume: .65 });
  assert.equal(JSON.parse(data.get(PREVIEW_PREFS_KEY)).fuzz, undefined);
});
test('malformed or unavailable preference storage falls back safely', () => {
  assert.deepEqual(readPreviewPreferences({ getItem() { throw new Error('denied'); } }), {});
  assert.deepEqual(readPreviewPreferences({ getItem: () => '{bad' }), {});
  assert.doesNotThrow(() => writePreviewPreferences({ setItem() { throw new Error('full'); } }, {}));
});
