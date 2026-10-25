import { test } from 'node:test';
import assert from 'node:assert/strict';
import { courseHash } from '../net/courseHash.js';
import { readFileSync } from 'node:fs';

test('course fingerprint ignores whitespace and changes with geometry or feel', async () => {
  const raw = readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8');
  const track = JSON.parse(raw), feel = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
  const hash = await courseHash(track, feel);
  assert.match(hash, /^[a-f0-9]{64}$/);
  assert.equal(await courseHash(JSON.parse(JSON.stringify(track, null, 2)), feel), hash);
  const aerials = { aerialRevision: 1, aerials: [{ gapS0: 1400, gapS1: 1510 }] };
  const airborneHash = await courseHash(track, feel, aerials);
  assert.notEqual(airborneHash, hash, 'old rooms refuse the changed physical course');
  assert.equal(await courseHash(track, feel, structuredClone(aerials)), airborneHash);
  aerials.aerials[0].gapS1 += 1;
  assert.notEqual(await courseHash(track, feel, aerials), airborneHash);
  const changed = structuredClone(track); changed.nodes[0].w += .01;
  assert.notEqual(await courseHash(changed, feel), hash);
  feel.speed.topMps += 1;
  assert.notEqual(await courseHash(track, feel), hash);
});
