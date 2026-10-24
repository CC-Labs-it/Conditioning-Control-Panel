import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { phraseGateLayout, phraseExclusions } from '../track/phraseGates.js';
import { createPhraseGateContact } from '../sim/phraseGates.js';
import { createState, S } from '../sim/state.js';
const hooks = registerHooks({ resolve(s, c, next) {
  const files = { 'three/tsl': 'three.tsl.min.js', 'three/webgpu': 'three.webgpu.min.js' };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { phraseEnvelope, createPhraseGates } = await import('../render/phraseGates.js');
const { Scene } = await import('three/webgpu');
hooks.deregister();
const tracks = ['threshold', 'mainframe', 'keep', 'boudoir', 'goonworks'].map(id => loadTrack(JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url)))));

test('all five bundled tracks have spaced gates clear of jumps, forks and timed hazards', () => {
  for (const track of tracks) {
    const gates = phraseGateLayout(track), exclusions = phraseExclusions(track);
    assert.ok(gates.length >= 1 && gates.length <= 6, track.id);
    for (let i = 0; i < gates.length; i++) {
      const gate = gates[i];
      assert.ok(gate.text.length <= 20);
      assert.ok(!exclusions.some(([a, b]) => gate.s + 100 >= a && gate.s - 25 <= b), gate.id);
      if (i) assert.ok(gate.s - gates[i - 1].s >= 400);
      for (let d = -20; d <= 100; d += 10) {
        const f = track.frameAt(gate.s + d);
        assert.ok(f.w >= 14 && f.n[1] >= .88);
        assert.ok(!f.flags.includes('jump') && !f.flags.includes('magnet'));
      }
    }
  }
});
const gate = { id: 'test', text: 'LET GO', frame: { p: [0,0,0], b: [1,0,0], n: [0,1,0], t: [0,0,1], w: 24 } };
function state(z, x = 0, h = 0, lap = 0) {
  const st = createState(); st[S.posX] = x; st[S.posY] = h; st[S.posZ] = z; st[S.lap] = lap; return st;
}
test('one phrase for a forward crossing, including a hop, once per gate per lap', () => {
  const c = createPhraseGateContact([gate]);
  c.update(state(-3, 0, 2), .1);
  assert.equal(c.update(state(3, 0, 2), .1), gate);
  c.update(state(-3), 9);
  assert.equal(c.update(state(3), .1), null);
  c.update(state(-3, 0, 0, 1), 9);
  assert.equal(c.update(state(3, 0, 0, 1), .1), gate);
  c.reset(); c.update(state(-3), .1);
  assert.equal(c.update(state(3), .1), gate);
});
test('upper roads, edge bypasses, reverse passes and rescue jumps cannot trigger', () => {
  for (const [a, b] of [[state(-3,0,12),state(3,0,12)], [state(-3,15),state(3,15)],
    [state(3),state(-3)], [state(-30),state(30)], [state(-3), Object.assign(state(3), { [S.respawnPhase]: 1 })]]) {
    const c = createPhraseGateContact([gate]); c.update(a,.1); assert.equal(c.update(b,.1), null);
  }
});
test('pause freezes contact and the shared cooldown prevents multiple phrases', () => {
  const second = { ...gate, id: 'second', frame: { ...gate.frame, p: [0,0,10] } }, c = createPhraseGateContact([gate, second]);
  c.update(state(-3), .1); assert.equal(c.update(state(3), .1, {paused:true}), null);
  assert.equal(c.update(state(3), .1), gate);
  assert.equal(c.update(state(12), .1), null);
});
test('one full second at full opacity, with smooth IN and OUT in normal and Gentle', () => {
  for (const gentle of [false, true]) {
    const rise = gentle ? .5 : .3, fade = gentle ? .9 : .6;
    assert.equal(phraseEnvelope(0,gentle),0);
    assert.ok(phraseEnvelope(rise/2,gentle) > 0 && phraseEnvelope(rise/2,gentle) < 1);
    for (const d of [0,.25,.5,.75,.999]) assert.equal(phraseEnvelope(rise+d,gentle),1);
    assert.ok(phraseEnvelope(rise+1+fade/2,gentle) > 0 && phraseEnvelope(rise+1+fade/2,gentle) < 1);
    assert.equal(phraseEnvelope(rise+1+fade+.01,gentle),0);
  }
});
test('prebuilt gate resources, overlay clock and cleanup follow race lifecycle', () => {
  const track = tracks[1], scene = new Scene(), effect = createPhraseGates(scene, track, {doc:null});
  const g = effect.gates[0], at = z => {
    const st = createState(); st[S.s]=g.s+z; for(let i=0;i<3;i++) st[S.posX+i]=g.frame.p[i]+g.frame.t[i]*z; return st;
  };
  effect.update(at(-2),.1); effect.update(at(2),.1);
  assert.equal(effect.stats.active,true);
  effect.update(at(2),1,{paused:true}); assert.equal(effect.stats.age,0);
  effect.update(at(2),3); assert.equal(effect.stats.active,false);
  effect.clear(); assert.equal(effect.stats.hits,0);
  effect.dispose(); assert.equal(scene.children.length,0);
});
