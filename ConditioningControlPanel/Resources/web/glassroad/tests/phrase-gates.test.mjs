import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { phraseGateLayout, phraseExclusions } from '../track/phraseGates.js';
import { createPhraseGateContact } from '../sim/phraseGates.js';
import { createState, S } from '../sim/state.js';
import { createBus } from '../core/events.js';
import { phraseLook, gatePulse, gateMote, GATE_AMBIENT, GATE_CAPACITY } from '../render/phraseGateMotion.js';
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

test('text overshoots once, holds still, then separates echoes and leaves without a flash', () => {
  assert.ok(phraseLook(.2).scale > 1 && phraseLook(.2).scale <= 1.07);
  for (const age of [.3,.7,1.29]) {
    const p = phraseLook(age); assert.equal(p.scale,1); assert.equal(p.alpha,1);
    assert.equal(p.y,0); assert.equal(p.echo,0);
  }
  assert.ok(phraseLook(1.6).echo > .4 && phraseLook(1.6).split > 0 && phraseLook(1.6).y < 0);
  for (let age=0;age<2.5;age+=.01) {
    const p=phraseLook(age,true); assert.equal(p.scale,1); assert.equal(p.y,0); assert.equal(p.tilt,0);
    assert.equal(p.echo,0); assert.equal(p.sparks,0); assert.ok(p.alpha>=0 && p.alpha<=1);
  }
});

test('gate pulse has a 300ms rise and a bounded tail; pooled particles grow and die', () => {
  assert.equal(gatePulse(0).alpha,0); assert.ok(gatePulse(.15).alpha<gatePulse(.3).alpha);
  assert.ok(gatePulse(.3).alpha>0); assert.equal(gatePulse(1.3).alpha,0);
  assert.equal(gatePulse(.6,true).travel,0);
  for (let i=0;i<GATE_CAPACITY;i++) for(const age of [-1,0,.15,.7,1.4,3]) {
    const p=gateMote(i,100,36,age); assert.ok(Object.values(p).every(Number.isFinite));
    assert.ok(p.size>=0 && p.size<.2);
    if(i>=GATE_AMBIENT && (age<=0 || age>=1.4)) assert.equal(p.size,0);
    if(i>=GATE_AMBIENT) assert.equal(gateMote(i,100,36,.7,true).size,0);
  }
});

test('gate particles and travelling pulse freeze on pause and clear without allocating new meshes', () => {
  const scene=new Scene(), effect=createPhraseGates(scene,tracks[1],{doc:null}), gate=effect.gates[0];
  const at=z=>{const st=createState(); st[S.s]=gate.s+z;for(let i=0;i<3;i++)st[S.posX+i]=gate.frame.p[i]+gate.frame.t[i]*z;return st;};
  const root=scene.getObjectByName(gate.id), pool=root.children.find(v=>v.isInstancedMesh), children=root.children.length;
  effect.update(at(-1),.1);effect.update(at(1),.1);effect.update(at(2),.35);
  assert.equal(effect.stats.pulses,1); assert.ok(effect.stats.particles>=96);
  const before=JSON.stringify(effect.stats), matrices=pool.instanceMatrix.array.slice();
  effect.update(at(2),1,{paused:true});assert.equal(JSON.stringify(effect.stats),before);
  assert.deepEqual(pool.instanceMatrix.array,matrices);assert.equal(root.children.length,children);
  effect.clear();assert.equal(pool.count,0);assert.equal(effect.stats.pulses,0);effect.dispose();
});

test('Gentle reduces the cloud and a refused brightness budget softens the pulse', () => {
  for(const gentle of [false,true]) {
    const scene=new Scene(), effect=createPhraseGates(scene,tracks[1],{doc:null,feel:{get:()=>gentle},photosafe:{request:()=>false}});
    const gate=effect.gates[0], at=z=>{const st=createState();st[S.s]=gate.s+z;for(let i=0;i<3;i++)st[S.posX+i]=gate.frame.p[i]+gate.frame.t[i]*z;return st;};
    effect.update(at(-1),.1);effect.update(at(1),.1);effect.update(at(2),.6);
    const root=scene.getObjectByName(gate.id),pool=root.children.find(v=>v.isInstancedMesh),pulse=root.children.at(-1);
    assert.equal(pool.count,gentle?32:GATE_CAPACITY);assert.equal(Math.abs(pulse.position.z),0);assert.ok(pulse.material.opacity<=.2);
    effect.dispose();
  }
});

test('audio crossing event shares the exact visual onset and never repeats during hold or pause', () => {
  const bus=createBus(),scene=new Scene(),effect=createPhraseGates(scene,tracks[1],{doc:null,bus});
  const gate=effect.gates[0],events=[];
  bus.on('phrase-gate',e=>{events.push(e);assert.equal(effect.stats.age,0);assert.equal(effect.stats.active,true);});
  const at=z=>{const st=createState();st[S.s]=gate.s+z;for(let i=0;i<3;i++)st[S.posX+i]=gate.frame.p[i]+gate.frame.t[i]*z;return st;};
  effect.update(at(-2),.1);effect.update(at(2),.1,{paused:true});assert.equal(events.length,0);
  effect.update(at(2),.1);assert.equal(events.length,1);
  assert.deepEqual(events[0],{id:gate.id,passed:true,course:'mainframe',index:0,gentle:false});
  effect.update(at(3),.6);effect.update(at(4),.6);assert.equal(events.length,1);
  effect.clear();effect.update(at(-2),.1);effect.update(at(2),.1);assert.equal(events.length,2);
  effect.dispose();
});

test('a chart replaces fixed phrases with pooled song gates and emits the recorded cue only on crossing', () => {
  const track = tracks[1], scene = new Scene(), bus = createBus();
  const effect = createPhraseGates(scene, track, { doc: null, bus });
  const start = effect.gates[0].s - 40, events = [];
  const song = { words: [], triggers: [{ kind: 'word', w: 'follow the signal', startS: 2 }] };
  bus.on('phrase-gate', e => events.push(e));
  const pool = scene.getObjectByName('phrase-gates'), roots = pool.children.slice();
  for (let i = 0; i <= 6 * 60; i++) {
    const t = i / 60, s = start + t * 30, f = track.frameAt(s), st = createState();
    st[S.s] = s; st[S.seg] = f.seg; st[S.grounded] = 1;
    for (let k = 0; k < 3; k++) { st[S.posX + k] = f.p[k]; st[S.velX + k] = f.t[k] * 30; }
    effect.update(st, 1 / 60, { song, songS: t });
  }
  assert.equal(events.length, 1);
  assert.equal(events[0].song, true); assert.equal(events[0].text, 'follow the signal');
  assert.ok(Math.abs(events[0].songS - 2) < .05);
  assert.equal(effect.stats.hits, 1); assert.equal(effect.stats.gates, 1);
  assert.deepEqual(pool.children, roots, 'no gate meshes allocated during the song');
  assert.ok(pool.children.every(root => !root.visible), 'finished cue leaves no generic gate behind');
  effect.dispose(); assert.equal(scene.children.length, 0);
});
