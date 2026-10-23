import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { scanPose } from '../sim/mainframeScan.js';

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true }
    : next(specifier, context);
} });
const { buildMainframe } = await import('../render/mainframe.js');
const { createPalette } = await import('../render/materials/palette.js');
hooks.deregister();
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8')));

test('Mainframe constructs server batches and scan volumes before the first update', () => {
  const world = buildMainframe(track, null);
  assert.ok(world.counts.shell > 10);
  assert.ok(world.counts.green > world.counts.shell);
  assert.equal(world.scans.length, 2);
  for (const scan of world.scans) {
    assert.equal(scan.panel.scale.x, scan.zone.width);
    assert.equal(scan.panel.scale.z, scan.zone.len, 'visible depth covers the entire collision region');
    assert.equal(scan.floor.scale.z, scan.zone.len);
  }
  world.dispose();
});

test('Mainframe scans follow the shared song clock, freeze on pause and warn after a seek', () => {
  const world = buildMainframe(track, null), scan = world.scans[0];
  world.update(0.1, { scanTimeS: 4.75, scanBpm: 120 }, false);
  assert.equal(scan.panel.position.x, scanPose(scan.zone, 4.75, 120).x);
  const before = scan.panel.position.x;
  world.update(0.1, { scanTimeS: 5.5, scanBpm: 120, paused: true });
  assert.equal(scan.panel.position.x, before);
  world.update(0.1, { scanTimeS: 5.5, scanBpm: 120, scanReady: false }, true);
  assert.equal(scan.panelMat.color.getHexString(), '53c5d4');
  assert.ok(scan.panelMat.opacity < 0.28);
  world.dispose();
});

test('Mainframe palette is opt-in and accepts all three existing section ids', () => {
  const themed = createPalette('mainframe'), standard = createPalette();
  for (const key of ['dawn', 'deep', 'afterglow']) themed.set(key, key, 1);
  assert.equal(themed.theme, 'mainframe');
  assert.equal(standard.theme, '');
  assert.ok(themed.rampRoad(themed.hue));
});
