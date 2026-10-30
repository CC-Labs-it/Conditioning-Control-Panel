import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { createWaterField } from '../render/waterField.js';
import { waterBedDepth } from '../render/waterBed.js';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { waterJoinWeight, transitionSupport, waterTransitionEnds, reservoirTransitionEnds, transitionOutlets, buildWaterTransitions } = await import('../render/waterTransitions.js');
hooks.deregister();

const straight = () => ({ lengthM: 100, nodeS: [0, 20, 60, 100], sections: [],
  frameAt(s) { return { p: [0, 5, -s], b: [1, 0, 0], n: [0, 1, 0], t: [0, 0, -1], w: 12, surface: s >= 20 && s < 60 ? 'stream' : 'glass', flags: [] }; },
  surfaceAt(s, x) { const f = this.frameAt(s); f.p[0] = x; return f; } });

test('shoreline tapers its displacement over six metres and preserves continuous wet lap seams', () => {
  const run = { fromS: 20, toS: 60 };
  assert.equal(waterJoinWeight(run, 20), 0); assert.equal(waterJoinWeight(run, 60), 0);
  assert.equal(waterJoinWeight(run, 26), 1); assert.equal(waterJoinWeight(run, 54), 1);
  assert.ok(waterJoinWeight(run, 20.1) < .001);
  const runs = [{ fromS: 0, toS: 20 }, { fromS: 60, toS: 100 }];
  assert.equal(waterJoinWeight(runs[0], 0, 100, runs), 1);
  assert.equal(waterJoinWeight(runs[1], 100, 100, runs), 1);
  for (const size of [2, 7, 12, 40]) {
    const r = { fromS: 20, toS: 20 + size };
    for (let s = r.fromS; s <= r.toS; s += .25)
      assert.equal(waterJoinWeight(r, s, 100), waterBedDepth(s, { runs: [r] }, 100, 1), 'short runs use the same shoreline slope as their bed');
  }
});

test('both dry ends carry shallow supported skirts and flush geometric grates', t => {
  const tr = straight(), runs = [{ fromS: 20, toS: 60 }], drains = buildWaterTransitions(tr, runs);
  t.after(() => drains.dispose());
  assert.deepEqual(drains.ends.map(e => [e.s, e.dir, e.dryM]), [[20, -1, 13], [60, 1, 13]]);
  assert.ok(drains.stats.grateVertices > 100 && drains.stats.grateVertices < 5000);
  for (const mesh of drains.object.children) {
    const p = mesh.geometry.attributes.position;
    for (let i = 0; i < p.count; i++) assert.ok(p.getY(i) >= 5 && p.getY(i) < 5.04, 'dressing stays flush with the actual road');
  }
});

test('skirts stop before gaps, stone fields and another road layer', () => {
  const tr = straight(), original = tr.frameAt;
  tr.frameAt = function (s) { const f = original.call(this, s); if (s >= 64) f.flags = ['jump']; return f; };
  assert.equal(waterTransitionEnds(tr, [{ fromS: 20, toS: 60 }])[1].dryM, 3.5);
  assert.equal(transitionSupport(tr, 65, 0), null);
  tr.fields = [{ fromS: 61, toS: 63 }]; assert.equal(transitionSupport(tr, 62), null);
  tr.query = () => ({ onRoad: true, height: 8, p: [0, -3, -30] }); assert.equal(transitionSupport(tr, 30), null);
});

test('curved bank contacts retain their normals and outlet falls refuse lower road collisions', () => {
  const tr = straight();
  tr.surfaceAt = function (s, x) { const f = this.frameAt(s); f.p = [x, 5 + .1 * x * x, -s]; f.n = [-.2 * x, 1, 0]; return f; };
  const contact = transitionSupport(tr, 70, 2); assert.equal(contact.p[1], 5.4); assert.equal(contact.n[0], -.4);
  const ends = waterTransitionEnds(tr, [{ fromS: 20, toS: 60 }]);
  assert.equal(transitionOutlets(tr, ends).length, 1);
  tr.query = () => ({ onRoad: true, height: 0 }); assert.equal(transitionOutlets(tr, ends).length, 0);
});

test('actual courses cover all shallow wet runs including grip-tagged Mainframe coolant', () => {
  for (const id of ['threshold', 'keep', 'mainframe', 'boudoir', 'goonworks']) {
    const tr = loadTrack(JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8')));
    const field = createWaterField(tr), ends = waterTransitionEnds(tr, field.runs);
    if (field.runs.length) assert.ok(ends.length > 0, id + ' has supported water boundaries');
    assert.ok(ends.length <= field.runs.length * 2);
    for (const e of ends) assert.ok(e.dryM > 0 && e.dryM <= 13);
    if (id === 'mainframe') assert.ok(ends.some(e => tr.sections.some(s => s.id === 'coolant-canal' && e.s >= s.fromS - 1 && e.s <= s.toS + 1)));
  }
});

test('Mainframe reservoir keeps authored entry membership and uses the true exit water-height crossing', () => {
  const tr = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8')));
  const ends = reservoirTransitionEnds(tr);
  assert.equal(ends.length, 2);
  assert.deepEqual(ends.map(e => e.dir), [-1, 1]);
  for (const end of ends) {
    if (end.authoredEntry) assert.equal(end.s, tr.sections.find(s => s.id === 'submerged').fromS);
    else {
      assert.ok(Math.abs(tr.frameAt(end.s).p[1] - tr.reservoir.waterY) < .0001);
      for (let d = 0; d < end.dryM; d += .5) assert.ok(tr.frameAt(end.s + end.dir * d).p[1] >= tr.reservoir.waterY - .001);
    }
  }
});
