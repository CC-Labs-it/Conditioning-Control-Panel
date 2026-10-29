import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { createState, S } from '../sim/state.js';
import { createBus } from '../core/events.js';

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { Scene } = await import('three/webgpu');
const { createPhraseGates } = await import('../render/phraseGates.js');
hooks.deregister();
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url))));

function fixture(text) {
  const drawn = [], canvases = [];
  const element = () => ({ style: {}, children: [], setAttribute() {}, remove() {}, appendChild(child) { this.children.push(child); } });
  const doc = { body: element(), createElement(tag) {
    if (tag !== 'canvas') return element();
    const context = { clearRect() {}, strokeText() {}, fillText(text) { drawn.push(text); }, measureText: text => ({ width: text.length * 55 }) };
    const canvas = { width: 0, height: 0, getContext: () => context }; canvases.push(canvas); return canvas;
  } };
  const scene = new Scene(), bus = createBus(), events = [];
  const view = createPhraseGates(scene, track, { doc, bus }); bus.on('phrase-gate', event => events.push(event));
  const start = view.gates[0].s - 40, song = { words: [], triggers: [{ kind: 'word', w: text, startS: 2 }] };
  function tick(t, options = {}) {
    const s = start + t * 30, frame = track.frameAt(s), st = createState();
    st[S.s] = s; st[S.seg] = frame.seg; st[S.grounded] = 1;
    for (let k = 0; k < 3; k++) { st[S.posX + k] = frame.p[k]; st[S.velX + k] = frame.t[k] * 30; }
    view.update(st, 1 / 60, { song, songS: t, ...options });
  }
  return { scene, view, events, drawn, canvases, tick };
}
const near = (a, b, slack = .0001) => assert.ok(Math.abs(a - b) <= slack, `${a} != ${b}`);

test('song signs retain the complete recorded phrase and share RESET/EMPTY identities before crossing', () => {
  for (const [text, kind] of [['Reset the signal', 7], ['Empty the buffer', 8], ['stay with me', 0]]) {
    const { view, tick, drawn } = fixture(text);
    for (let i = 0; i <= 100; i++) tick(i / 60);
    const [row] = view._dev.live();
    assert.equal(row.text, text); assert.equal(row.identity[0], kind); assert.equal(row.identity[3], text.length);
    near(row.identity[1], 100 / 60 - 2); assert.equal(row.identity[2], -1);
    assert.ok(drawn.includes(text)); assert.equal(view.stats.hits, 0);
    view.dispose();
  }
});

test('real gate crossing drives source collection once without moving signs or recreating their resources', () => {
  const { view, scene, tick, events, drawn, canvases } = fixture('Reset the signal');
  const root = scene.getObjectByName('phrase-gates'), signs = [];
  root.traverse(object => { if (object.name === 'phrase-gate-word') signs.push(object); });
  const resources = signs.map(sign => [sign, sign.material, sign.material.map, sign.geometry]);
  for (let i = 0; i <= 122; i++) tick(i / 60);
  assert.equal(events.length, 1); assert.equal(events[0].text, 'Reset the signal'); assert.equal(events[0].song, true);
  const crossed = view._dev.live()[0], fixedS = crossed.s, draws = drawn.length;
  assert.equal(crossed.state, 'hit'); near(crossed.hitS, 2, .04);
  for (let i = 123; i <= 140; i++) tick(i / 60);
  const midway = view._dev.live()[0]; near(midway.identity[2], (140 / 60 - crossed.hitS) / .6); assert.equal(midway.s, fixedS);
  for (let i = 141; i <= 160; i++) tick(i / 60);
  assert.equal(view._dev.live()[0].identity[2], 1, 'source finishes while the physical gate retains its own exit');
  assert.equal(events.length, 1); assert.equal(drawn.length, draws, 'only a changed caption redraws its texture');
  assert.equal(canvases.length, signs.length);
  assert.deepEqual(signs.map(sign => [sign, sign.material, sign.material.map, sign.geometry]), resources);
  view.dispose(); assert.equal(scene.children.length, 0);
});

test('pause freezes the song-sign clock and Gentle/off keep real lifetime with a static semantic pose', () => {
  const { view, tick } = fixture('Empty the buffer');
  for (let i = 0; i <= 120; i++) tick(i / 60);
  const paused = view._dev.live(); tick(2.4, { paused: true }); assert.deepEqual(view._dev.live(), paused);
  tick(121 / 60, { motion: 'less' }); assert.equal(view._dev.live()[0].motion, .5);
  tick(122 / 60, { motion: 'off' }); assert.equal(view._dev.live()[0].motion, 0);
  for (let i = 123; i <= 145; i++) tick(i / 60, { gentle: true });
  const gentle = view._dev.live()[0]; assert.equal(gentle.motion, 0); near(gentle.identity[2], (145 / 60 - 2) / .85, .03);
  for (let i = 146; i <= 174; i++) tick(i / 60, { gentle: true });
  assert.equal(view._dev.live()[0].identity[2], 1);
  view.clear(); assert.deepEqual(view._dev.live(), []); view.dispose();
});
