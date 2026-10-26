import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { courseHash } from '../net/courseHash.js';

test('bridge centre and retreating edges follow rendered slabs through both transitions on every course', async () => {
  const hooks = registerHooks({ resolve(specifier, context, next) {
    if (specifier === 'three/webgpu') return { url: new URL('../vendor/three/build/three.webgpu.min.js', import.meta.url).href, shortCircuit: true };
    if (specifier === 'three/tsl') return { url: new URL('../vendor/three/build/three.tsl.min.js', import.meta.url).href, shortCircuit: true };
    return next(specifier, context);
  } });
  try {
    const { Scene, Raycaster, Vector3 } = await import('three/webgpu');
    const { createMovingHazardView } = await import('../render/movingHazards.js');
    for (const id of ['threshold', 'keep', 'mainframe', 'boudoir', 'goonworks']) {
      const json = JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8'));
      const track = loadTrack(json, { moving: true }), bridge = track.moving.bridge;
      const fork = track.forks.find(f => f.id === bridge.forkId);
      const view = createMovingHazardView(new Scene(), track, { primaryHex: () => '#8f969e', accentHex: () => '#e6b464' });
      const roads = Array.from({ length: bridge.slabs }, (_, i) => view.group.getObjectByName('bridge-piston-' + i).getObjectByName('forming-road'));
      const ray = new Raycaster(); ray.far = 3;
      const check = (f, x, time, expected = null) => {
        const p = f.p.map((v, j) => v + f.b[j] * x + f.n[j] * .25);
        ray.set(new Vector3(...p.map((v, j) => v + f.n[j] * 2)), new Vector3(...f.n).negate());
        const hit = ray.intersectObjects(roads, false)[0], visible = !!hit;
        const label = `${id} time=${time.toFixed(3)} lateral=${x.toFixed(3)}`;
        if (expected !== null) assert.equal(visible, expected, label + ' visible deck');
        const q = track.query(...p, f.seg);
        assert.equal(q.onRoad, visible, label + ' contact matches the rendered box');
        assert.equal(q.railed, false, label + ' no invisible bridge rail');
        assert.equal(track.query.below(...p)?.forkId === fork.id, visible, label + ' landing probe');
        if (visible) assert.ok(Math.hypot(...q.p.map((v, j) => v - hit.point.getComponent(j))) < .0001, label + ' contact plane');
        else assert.equal(q.hole, 'piston-bridge');
      };
      try {
        for (const i of new Set([0, 1, Math.floor(bridge.slabs / 2), bridge.slabs - 1])) {
          const f = fork.frameAt(bridge.fromM + (i + .5) * bridge.slabM), side = i % 2 ? -1 : 1;
          const stagger = i / (bridge.slabs - 1) * bridge.transitionS * .5;
          const closing = bridge.openS + stagger, opening = bridge.periodS - bridge.transitionS + stagger;
          for (const time of [0, closing + .08, closing + .2, closing + .4, closing + .7, 10,
            opening + (bridge.periodS - opening) * .25, opening + (bridge.periodS - opening) * .75, bridge.periodS - .01]) {
            track.moving.timeS = time; view.update(time);
            const expected = time === 0 || time === closing + .08 || time === bridge.periodS - .01 ? true : time === 10 ? false : null;
            check(f, 0, time, expected);
            for (const x of [-1, 1]) check(f, x * (f.w / 2 - .15), time);
            const edge = roads[i].parent.position.x - side * (f.w + .06) / 2;
            if (Math.abs(edge) < f.w / 2 - .2) {
              check(f, edge + side * .06, time, true);
              check(f, edge - side * .06, time, false);
            }
            const bypass = track.frameAt(fork.mainS(f.localS));
            assert.ok(track.query(...bypass.p, bypass.seg).onRoad, id + ' permanent bypass');
          }
        }
        const f = fork.frameAt(bridge.fromM + bridge.slabM / 2);
        track.moving.timeS = 8.1; view.update(8.1);
        const paused = track.query(...f.p, f.seg);
        assert.deepEqual(track.query(...f.p, f.seg), paused, id + ' unchanged race clock freezes support');
        track.moving.timeS = 0; view.update(0);
        check(f, 0, 0, true);
        assert.notEqual(await courseHash(json, {}, { moving: track.moving }),
          await courseHash(json, {}, { moving: { ...track.moving, revision: 1 } }), id + ' old authority is rejected');
      } finally { view.dispose(); }
    }
  } finally { hooks.deregister(); }
});
