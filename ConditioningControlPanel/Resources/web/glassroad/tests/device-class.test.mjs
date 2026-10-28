// Owner report 2026-10-28 (iPhone, Quality pinned to High): "loading stalled after water (threshold, webgpu, high)".
// Every phone protection keyed on the TIER NAME, and a pinned Quality replaces the tier, so a phone pinned above its
// auto pick ran the desktop warm (water probe, full mirrors, six decoders, the whole scene in one pass) and iOS killed
// the GPU process. The LOOK still follows the pinned tier; the MEMORY protections now key on the DEVICE
// (render/deviceClass.js), which no pin can change.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { DEVICE, deviceClass, memoryGuard, setDevice, memoryTier, pinAboveAuto, qualityHint, QUALITY_ABOVE_AUTO } from '../render/deviceClass.js';

const src = (p) => readFileSync(new URL('../' + p, import.meta.url), 'utf8');

test('deviceClass: a coarse pointer or a phone host is a phone; anything else is a desktop', () => {
  assert.equal(deviceClass({ coarse: true }), 'phone');
  assert.equal(deviceClass({ coarse: false, platform: 'phone' }), 'phone');
  assert.equal(deviceClass({ coarse: false, platform: 'web' }), 'desktop');
  assert.equal(deviceClass({ coarse: false, platform: 'desktop' }), 'desktop');
  assert.equal(deviceClass(), 'desktop');
});

test('memoryGuard: a phone budgets as the phone tier whatever is pinned; a desktop follows its tier', () => {
  for (const tier of ['high', 'mid', 'phone']) {
    assert.equal(memoryGuard('phone', tier), 'phone');
    assert.equal(memoryGuard('desktop', tier), tier);
  }
  assert.equal(setDevice('phone'), 'phone'); assert.equal(DEVICE.name, 'phone'); assert.equal(memoryTier('high'), 'phone');
  assert.equal(setDevice('web'), 'desktop'); assert.equal(memoryTier('high'), 'high');
  assert.equal(setDevice(null), 'desktop');
});

test('pinAboveAuto and the Quality hint: only a phone pinned above its auto pick is warned, in the house voice', () => {
  assert.ok(pinAboveAuto('high', 'phone')); assert.ok(pinAboveAuto('mid', 'phone')); assert.ok(pinAboveAuto('high', 'mid'));
  assert.ok(!pinAboveAuto('phone', 'phone')); assert.ok(!pinAboveAuto('auto', 'phone')); assert.ok(!pinAboveAuto('high', null)); assert.ok(!pinAboveAuto('mid', 'high'));
  assert.equal(qualityHint('high', { device: 'phone', autoTier: 'phone' }), QUALITY_ABOVE_AUTO);
  assert.equal(qualityHint('mid', { device: 'phone', autoTier: 'phone' }), QUALITY_ABOVE_AUTO);
  assert.equal(qualityHint('phone', { device: 'phone', autoTier: 'phone' }), 'Auto follows your PC');
  assert.equal(qualityHint('auto', { device: 'phone', autoTier: 'phone' }), 'Auto follows your PC');
  assert.equal(qualityHint('high', { device: 'desktop', autoTier: 'mid' }, 'stock'), 'stock');
  assert.doesNotMatch(QUALITY_ABOVE_AUTO, /[!–—]/);
  assert.ok(QUALITY_ABOVE_AUTO.length <= 48);
});

test('readers: the memory protections key on the device, the look keys stay on the tier', () => {
  const r = src('render/renderer.js');
  assert.match(r, /const device = setDevice\(deviceClass\(\{ coarse, platform \}\)\)/);
  assert.match(r, /get device\(\) \{ return device; \}/);
  assert.match(r, /get memoryTier\(\) \{ return memoryGuard\(device, Q\.name\); \}/);
  assert.match(r, /get pinned\(\) \{ return !!pinOf\(\); \}/);
  const boot = src('boot.js');
  assert.match(boot, /platform: init\.caps\.platform/, 'the host platform reaches the renderer');
  assert.match(boot, /readyCount: renderer\.device === 'phone' \? PHONE_READY_CLIPS : Infinity/, 'the clips the cover waits for');
  assert.match(boot, /const staged = renderer\.device === 'phone' && !warmPose/, 'the staged warm');
  assert.doesNotMatch(boot, /readyCount: renderer\.tier === 'phone'/);
  assert.doesNotMatch(boot, /const staged = renderer\.tier === 'phone'/);
  const water = src('render/waterVolume.js');
  assert.match(water, /memoryQuality = memoryTier\(quality\)/);
  assert.match(water, /const liveReflections = \(TIERS\[memoryQuality\] \|\| TIERS\.high\)\.sceneReflections !== false/, 'the water probe');
  assert.match(water, /createWaterReflections\(\{ scene, renderer, exclude: object, quality: memoryQuality, track \}\)/, 'the probe cube size');
  assert.match(water, /createWaterFoam\(track, field, spills, \{ quality \}\)/, 'foam counts stay on the look tier');
  assert.match(src('render/boudoirMirrorRoom.js'), /\{ quality = memoryTier\(Q\.name\) \}/, 'mirror capture targets');
  const media = src('render/media.js');
  assert.match(media, /mediaClipLimit\(memoryTier\(Q\.name\)\)/, 'decoder count');
  assert.match(media, /maxPlayingFor\(memoryTier\(Q\.name\)\)/, 'web clips playing');
  // the look stays on the tier: art LODs and particle counts never moved
  assert.match(src('render/art.js'), /materialTier === 'phone' \? 'threshold-slot-row-lod0'/);
  assert.match(src('render/goonworks.js'), /Q\.name==='phone'\?450:1900/);
});

test('garage: the options card hands the device and the auto pick to the Quality hint', () => {
  const cards = src('hud/cards.js');
  assert.match(cards, /setDevice\(v\) \{ host\.device = v && v\.device === 'phone' \? 'phone' : 'desktop'; host\.autoTier = \(v && v\.autoTier\) \|\| null; \}/);
  assert.match(cards, /data\.from === 'pause' \? QUALITY_LATER : qualityHint\(opts\.quality, host, hint\)/, 'the pause card keeps its own line');
  assert.match(src('hud/hud.js'), /setDevice\(v\) \{ cards\.setDevice\?\.\(v\); \}/);
  assert.match(src('boot.js'), /hud\.setDevice\?\.\(\{ device: renderer\.device, autoTier: renderer\.autoTier \}\)/);
});
