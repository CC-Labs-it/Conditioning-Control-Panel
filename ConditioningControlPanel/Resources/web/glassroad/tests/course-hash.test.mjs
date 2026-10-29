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

test('a last-bit engine wobble in the runtime course keeps the seat; a real move still refuses it', async () => {
  // Safari's Math.hypot / sin are not V8's: the same course can come out a few ulps apart (2026-10-28 "No connection")
  const track = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
  const feel = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
  const base = { aerials: [{ gapS0: 1400.123456789, gapS1: 1510.5, y: -3.25 }] };
  const hash = await courseHash(track, feel, base);
  const wobble = { aerials: [{ gapS0: 1400.123456789 + 4e-12, gapS1: 1510.5 - 2e-12, y: -3.25 + 1e-13 }] };
  assert.equal(await courseHash(track, feel, wobble), hash);
  const moved = { aerials: [{ gapS0: 1400.124, gapS1: 1510.5, y: -3.25 }] };
  assert.notEqual(await courseHash(track, feel, moved), hash);
});

test('?join=<roomId> from a CCP lobby row: only the lobby room id shape, dropped after use', async () => {
  const { joinParam, dropLinkParam } = await import('../hud/lobbyMath.js');
  assert.equal(joinParam('?join=abc123def456'), 'abc123def456');
  assert.equal(joinParam('?track=keep&join=abc123def456'), 'abc123def456');
  for (const bad of ['', '?join=', '?join=ABC123DEF456', '?join=abc123', '?join=abc123def456x', '?join=abc123def45%2F']) assert.equal(joinParam(bad), null, bad);
  assert.equal(dropLinkParam('https://x.test/glassroad/index.html?track=keep&join=abc123def456', 'join'), '/glassroad/index.html?track=keep');
  assert.equal(dropLinkParam('https://x.test/g/?r=tok&join=abc123def456'), '/g/?join=abc123def456');
});

test('a refused seat says why instead of "No connection"', async () => {
  const { connectFailCopy } = await import('../hud/lobbyMath.js');
  assert.equal(connectFailCopy({ reason: 'version', closeCode: 4003 }), 'This build is out of date');
  assert.equal(connectFailCopy({ reason: null, closeCode: 4004 }), 'The room is full');
  assert.equal(connectFailCopy({ reason: null, closeCode: 1006 }), 'No connection');
  assert.equal(connectFailCopy(null), 'No connection');
});
