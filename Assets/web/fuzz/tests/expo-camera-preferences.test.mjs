import test from 'node:test';
import assert from 'node:assert/strict';
import { createCameraPreferences } from '../ui/expo/camera-preferences.js';

function visit(entries = {}) {
  const data = new Map(Object.entries(entries));
  const win = { localStorage: { getItem: key => data.get(key) ?? null, setItem: (key,value) => data.set(key,value) } };
  return { win, data, ...createCameraPreferences(win) };
}

test('a fresh Expo inherits Backroom dragging and keeps arrow-key walking', () => {
  assert.deepEqual(visit().options, { dragLook:false, invertLook:false, arrowMode:'strafe' });
  const { win, data, options, set } = visit({ 'br.dragLook.v1':'true' });
  assert.equal(options.dragLook, true);
  assert.equal(set('dragLook',false),true);
  set('arrowMode','turn');
  set('invertLook',true);
  assert.deepEqual(createCameraPreferences(win).options, { dragLook:false, invertLook:true, arrowMode:'turn' });
  assert.equal(data.get('br.dragLook.v1'),'true');
});

test('damaged or obsolete saved camera choices keep usable defaults', () => {
  const inherited = { dragLook:true, invertLook:false, arrowMode:'strafe' };
  for (const stored of ['{','[]','null','{"dragLook":"false","arrowMode":"fly"}']) {
    assert.deepEqual(visit({ 'br.dragLook.v1':'true', 'expo.camera.v1':stored }).options, inherited);
  }
  const { options, set, data } = visit();
  assert.equal(set('invertLook','yes'),false);
  assert.equal(set('arrowMode','fly'),false);
  assert.equal(set('other',true),false);
  assert.equal(data.size,0);
  assert.equal(options.invertLook,false);
});

test('camera controls work for the current visit when storage is blocked', () => {
  const prefs = createCameraPreferences({ get localStorage() { throw new Error('denied'); } });
  prefs.set('arrowMode','turn');
  assert.equal(prefs.options.arrowMode,'turn');
});
