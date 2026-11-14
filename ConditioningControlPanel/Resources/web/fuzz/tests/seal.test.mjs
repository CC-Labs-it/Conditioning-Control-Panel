import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { sealedFor, sealCountdown, SEAL_OPEN_AT_MS, SEAL_HOST_URL, SEAL_PRIME_URL } from '../ui/seal.js';

const before = SEAL_OPEN_AT_MS - 1;
const standalone = { protocol: 1, hosted: false, caps: {} };
const hosted = (plan) => ({ protocol: 1, caps: { platform: 'web', plan } });

test('the anniversary is midnight UTC on 22 December 2026', () => {
  assert.equal(new Date(SEAL_OPEN_AT_MS).toISOString(), '2026-12-22T00:00:00.000Z');
});

test('before the anniversary only a host that says Prime gets the game', () => {
  assert.equal(sealedFor(standalone, before, 'tc.cclabs.app'), true, 'a page on its own has no plan');
  assert.equal(sealedFor(hosted('free'), before, 'tc.cclabs.app'), true);
  assert.equal(sealedFor(hosted('basic'), before, 'tc.cclabs.app'), true);
  assert.equal(sealedFor(hosted(undefined), before, 'tc.cclabs.app'), true);
  assert.equal(sealedFor(null, before, 'tc.cclabs.app'), true);
  assert.equal(sealedFor(hosted('prime'), before, 'tc.cclabs.app'), false);
});

test('from the anniversary on nobody is sealed', () => {
  for (const init of [standalone, hosted('free'), hosted('basic'), hosted('prime'), null]) {
    assert.equal(sealedFor(init, SEAL_OPEN_AT_MS, 'tc.cclabs.app'), false);
    assert.equal(sealedFor(init, SEAL_OPEN_AT_MS + 86400000, 'tc.cclabs.app'), false);
  }
  assert.equal(sealedFor(standalone, Number.NaN, 'tc.cclabs.app'), false, 'a broken clock never locks the game');
});

test('a local preview is sealed only when it asks', () => {
  assert.equal(sealedFor(standalone, before, '127.0.0.1'), false);
  assert.equal(sealedFor(standalone, before, 'localhost'), false);
  assert.equal(sealedFor(standalone, before, '127.0.0.1', true), true);
  assert.equal(sealedFor(standalone, before, 'tc.cclabs.app', false), true, 'the switch cannot unseal a public host');
});

test('the countdown splits what is left and stops at zero', () => {
  const ms = ((2 * 24 + 3) * 3600 + 4 * 60 + 5) * 1000;
  assert.deepEqual(sealCountdown(SEAL_OPEN_AT_MS - ms), { days: 2, hours: 3, minutes: 4, seconds: 5, done: false });
  assert.deepEqual(sealCountdown(SEAL_OPEN_AT_MS + 5000), { days: 0, hours: 0, minutes: 0, seconds: 0, done: true });
});

test('the notice only links to the website, and main.js asks before it starts', () => {
  assert.equal(SEAL_HOST_URL, 'https://app.cclabs.app/tc');
  assert.ok(SEAL_PRIME_URL.startsWith('https://app.cclabs.app/'));
  const main = readFileSync(new URL('../main.js', import.meta.url), 'utf8');
  assert.match(main, /sealedFor\(init, Date\.now\(\), location\.hostname/);
  assert.ok(main.indexOf('sealedFor(init') < main.indexOf('function start(init)'), 'the check sits in front of start');
});
