import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { keepArtPlan, KEEP_REPLACES } from '../render/keepTheme.js';

const manifest = JSON.parse(await readFile(new URL('../art/manifest.json', import.meta.url)));
const rows = new Map(manifest.map((r) => [r.id, r]));
const slots = [...Object.keys(KEEP_REPLACES), 'sky-dawn-galaxy', 'sky-afterglow-galaxy',
  'sky-dawn-stars', 'sky-afterglow-stars', 'chess-king-lod0', 'slot-cabinet-lod0', 'threshold-slot-row-lod0', 'emi-teacup-kart'];

test('Keep selects only phone replacements, aliases all sky states, and leaves Threshold props unloaded', () => {
  const plan = keepArtPlan(slots, 'phone');
  for (const id of plan.ids) assert.ok(rows.has(id), id);
  for (const [slot, replacement] of Object.entries(KEEP_REPLACES)) {
    assert.equal(plan.aliases[slot], replacement + '-phone');
    assert.ok(!plan.ids.includes(slot));
    assert.ok(!plan.ids.includes(replacement));
  }
  assert.equal(plan.aliases['sky-dawn-galaxy'], 'keep-sky-galaxy-phone');
  assert.equal(plan.aliases['sky-afterglow-stars'], 'sky-deep-stars');
  assert.ok(!plan.ids.some((id) => /chess|slot-cabinet|threshold-slot/.test(id)));
  assert.ok(plan.ids.includes('emi-teacup-kart'));
  assert.ok(plan.ids.includes('keep-colossus-chainpile-lod1'));
});

test('Keep replacement residency stays within the accepted phone budget and uses R masks', () => {
  const plan = keepArtPlan(slots, 'phone');
  const ids = Object.values(KEEP_REPLACES).map((id) => id + '-phone');
  assert.equal(ids.reduce((sum, id) => sum + rows.get(id).texBytes, 0), 3276800);
  for (const id of ids) {
    assert.equal(rows.get(id).colorSpace, 'linear');
    assert.ok(rows.get(id).mipLevels > 1);
    assert.ok(plan.ids.includes(id));
  }
});

test('Keep high and mid share desktop assets without loading a second texture tier', () => {
  for (const tier of ['high', 'mid']) {
    const plan = keepArtPlan(slots, tier);
    assert.ok(!plan.ids.some((id) => id.endsWith('-phone')));
    assert.ok(plan.ids.includes('keep-colossus-padlock-lod0'));
    assert.ok(plan.ids.includes('hazard-keep-footprint'));
  }
});
