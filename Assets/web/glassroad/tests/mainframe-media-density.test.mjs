import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { mediaLook, roadSamples, rectPoints, tubeClash, haloSlots, tunnelLayout } from '../render/mediaMath.js';
import { selectScrolllerSlots } from '../render/mediaSelection.js';
import { fitMainframeBillboard, motionEnvelope, moveMainframePanel, PANEL_FLIGHT } from '../render/mainframeMediaMotion.js';

const read = name => JSON.parse(readFileSync(new URL('../tracks/' + name, import.meta.url), 'utf8'));
const track = loadTrack(read('mainframe.json')), layout = read('mainframe.media.json');
const M = mediaLook(), samples = roadSamples(track, 4), fine = roadSamples(track, 2);
const boards = layout.billboards.map(spec => fitMainframeBillboard(spec, track, samples, M));

test('actual flying media pauses exactly and Gentle halves its travel clock', async t => {
  const hooks = registerHooks({ resolve(specifier, context, next) {
    const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
    return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true }
      : next(specifier, context);
  } });
  const { createMediaSlots } = await import('../render/media.js'); hooks.deregister();
  const gradient = { addColorStop() {} }, context = new Proxy({}, { get: (o,k) => k in o ? o[k]
    : /Gradient/.test(k) ? () => gradient : () => {} });
  const make = (width,height) => ({ width,height,getContext:()=>context });
  const normal = createMediaSlots(null,track,{layout,make}), gentle = createMediaSlots(null,track,{layout,make});
  t.after(() => { normal.dispose(); gentle.dispose(); });
  const id = boards.find(b=>b.flight).id, a = normal.slots.find(b=>b.id===id), b = gentle.slots.find(b=>b.id===id);
  normal.update(3); gentle.update(6,{gentle:true}); assert.deepEqual(a.C,b.C);
  const held = [...a.C]; normal.update(4,{paused:true}); assert.deepEqual(a.C,held);
  normal.update(0); assert.deepEqual(a.C,held);
  normal.update(2); assert.notDeepEqual(a.C,held);
});

test('Mainframe media reaches the entire lap with dense live-feed eligible screens', () => {
  assert.equal(boards.filter(Boolean).length, layout.billboards.length);
  assert.ok(boards.length >= 90, 'control, engine and containment corridors all carry picture faces');
  const all = [...boards, ...haloSlots(layout.halo, track.frameAt(0), layout.halo.count, 0, M),
    ...tunnelLayout(layout.tunnel, track, M).slots];
  const selected = selectScrolllerSlots(all);
  assert.ok(selected.length >= 60, 'sixty world faces accept the existing shared animated clip pool');
  const positions = boards.map(b => b.s).sort((a,b) => a-b);
  const gaps = positions.map((s,i) => (positions[(i+1)%positions.length]-s+track.lengthM)%track.lengthM);
  assert.ok(Math.max(...gaps) < 140, 'no long empty stretch between media landmarks, including the scan bypass');
  for (let i=0;i<8;i++) assert.ok(selected.some(b => b.s >= track.lengthM*i/8 && b.s < track.lengthM*(i+1)/8));
  assert.ok(selectScrolllerSlots(boards.filter(b => b.phone)).length >= 25, 'phone retains live feed along the lap');
});

test('Mainframe floating image envelopes clear every stacked road and retain real media eligibility', () => {
  const flying = boards.filter(b => b.flight), selected = new Set(selectScrolllerSlots(boards).map(b => b.id));
  assert.ok(flying.length >= 24);
  assert.ok(flying.filter(b => selected.has(b.id)).length >= 10);
  for (const slot of flying) {
    assert.equal(slot.stalk, undefined, 'an airborne picture has no glass stalk tether');
    assert.equal(tubeClash(rectPoints(motionEnvelope(slot, M), 7), fine, M), null, slot.id);
    const home = [...slot.home];
    for (const clock of [0, 2, 8, 21, 57, 140]) {
      moveMainframePanel(slot, clock);
      assert.ok(Math.hypot(...slot.C.map((v,i)=>v-home[i])) <= Math.hypot(PANEL_FLIGHT.swayM,PANEL_FLIGHT.bobM)+1e-6);
      assert.equal(tubeClash(rectPoints(slot, 5), fine, M), null, slot.id+' at '+clock);
    }
    const held = [...slot.C]; moveMainframePanel(slot, 140);
    assert.deepEqual(slot.C, held, 'a held clock is exactly stationary');
    moveMainframePanel(slot, 144); assert.notDeepEqual(slot.C, held);
    assert.deepEqual(slot.home, home, 'motion never accumulates drift outside the fitted envelope');
  }
});
