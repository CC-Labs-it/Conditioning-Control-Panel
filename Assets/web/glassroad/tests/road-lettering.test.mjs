import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { ROAD_LOOK } from '../sim/roadWords.js';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const name = { 'three/tsl': 'three.tsl.min.js', three: 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js' }[specifier];
  return name ? { url: new URL('../vendor/three/build/' + name, import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { glyphFit, letteringLane, letteringFootprint, roadLetteringProfile } = await import('../render/roadLettering.js');
hooks.deregister();

test('full strip and its outline fit every sampled road row, including lap wrap and narrowing', () => {
  for (const id of ['threshold', 'keep', 'mainframe', 'boudoir', 'goonworks']) {
    const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8')));
    for (let s = 0; s < track.lengthM; s += 17) for (const lane of [-9, 0, 9]) {
      const f = letteringFootprint(track, s, lane, 14, ROAD_LOOK.lenM);
      for (let row = 0; row <= 8; row++) {
        const w = track.frameAt((s + ROAD_LOOK.lenM * (row / 8 - .5) + track.lengthM) % track.lengthM).w;
        assert.ok(Math.abs(f.lane) + f.width / 2 <= w / 2 - .64, id + ' ' + s);
      }
    }
  }
});

test('lane following is speed bounded and holds exactly through pause', () => {
  let lane = 0;
  for (let i = 0; i < 60; i++) {
    const next = letteringLane(lane, 8, 1 / 60);
    assert.ok(Math.abs(next - lane) <= 1.1 / 60 + 1e-9); lane = next;
  }
  assert.ok(Math.abs(lane - 1.1) < 1e-9);
  assert.equal(letteringLane(lane, -8, 0), lane);
  assert.equal(letteringLane(lane, -8, -1), lane);
});

test('tall capitals and descenders reserve vertical atlas room for the complete outline', () => {
  for (const height of [45, 90, 130, 200]) {
    const fit = glyphFit({ actualBoundingBoxAscent: height * .8, actualBoundingBoxDescent: height * .2 }, 100, 128, .2, 1.95);
    assert.ok(fit <= 1 && fit > 0);
    assert.ok((height + 2 * 100 * .2 / 1.95) * fit <= 128 * .76 + 1e-9);
  }
});

test('five track identities provide different readable complete styles', () => {
  const ids = ['threshold', 'keep', 'mainframe', 'boudoir', 'goonworks'];
  const inks = ids.map(id => {
    const p = roadLetteringProfile(id);
    assert.equal(p.id, id); assert.equal(p.styles.length, 7);
    for (const style of p.styles) {
      assert.ok(style.stroke >= .14 && style.stroke <= .2);
      assert.ok(Math.max(...style.ink) > .8 && Math.max(...style.edge) < .4);
    }
    return JSON.stringify(p.styles.map(s => [s.face, s.ink]));
  });
  assert.equal(new Set(inks).size, 5);
});
