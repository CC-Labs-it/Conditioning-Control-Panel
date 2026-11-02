// Race a friend through a link (NET.md section 13): the pure parts in hud/lobbyMath.js and the boot wiring.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { linkToken, dropLinkParam, linkLeft, linkNote, linkButton, linkCopy, waitingCopy, claimStep, errorCopy } from '../hud/lobbyMath.js';

const TOKEN = 'AbCdEfGhIjKlMnOpQrStUv';
const H = 3600000;

test('linkToken: only a well shaped r comes out', () => {
  assert.equal(linkToken('?r=' + TOKEN), TOKEN);
  assert.equal(linkToken('?bot=1&r=' + TOKEN + '&x=2'), TOKEN);
  for (const s of ['', '?r=', '?r=short', '?r=' + 'a'.repeat(65), '?r=abc%2F' + TOKEN, '?x=' + TOKEN, null, undefined]) assert.equal(linkToken(s), null, String(s));
  assert.equal(linkToken('?r=' + 'A_-'.repeat(6)), 'A_-'.repeat(6));
});

test('dropLinkParam: the address keeps every other flag and the hash', () => {
  assert.equal(dropLinkParam('https://app.cclabs.app/glassroad/index.html?r=' + TOKEN), '/glassroad/index.html');
  assert.equal(dropLinkParam('http://127.0.0.1:8800/index.html?lobby=x&r=' + TOKEN + '&uid=u#k'), '/index.html?lobby=x&uid=u#k');
  assert.equal(dropLinkParam('not a url'), null);
});

test('linkLeft and linkNote: seats left and time left, ms or seconds', () => {
  const now = 1e12;
  assert.equal(linkLeft({ maxUses: 8, uses: 0, exp: now + 24 * H }, now).label, '8 seats, 24 h');
  assert.equal(linkLeft({ maxUses: 8, uses: 7, exp: now + 3.4 * H }, now).label, '1 seat, 3 h');
  assert.equal(linkLeft({ maxUses: 8, uses: 9, exp: now + 20 * 60000 }, now).label, '0 seats, 20 min');
  assert.equal(linkLeft({ maxUses: 8, uses: 2, exp: (now + 2 * H) / 1000 }, now).label, '6 seats, 2 h', 'exp in seconds');
  assert.equal(linkLeft({ exp: now - 1 }, now).label, '8 seats, no time');
  assert.equal(linkLeft(null, now).seats, 8);
  assert.equal(linkNote({ maxUses: 8, uses: 0, exp: now + 24 * H }, now, true), 'Link copied. 8 seats, 24 h.');
  assert.equal(linkNote({ maxUses: 8, uses: 0, exp: now + 24 * H }, now, false), 'Copy the link below. 8 seats, 24 h.');
});

test('linkButton: friends room host, in the lobby phase, with access().host', () => {
  const cfg = { mode: 'friends' }, me = { host: true }, acc = { host: true };
  assert.equal(linkButton(cfg, me, acc, 'lobby'), true);
  assert.equal(linkButton({ mode: 'open' }, me, acc, 'lobby'), false);
  assert.equal(linkButton(cfg, { host: false }, acc, 'lobby'), false);
  assert.equal(linkButton(cfg, me, { host: false }, 'lobby'), false);
  assert.equal(linkButton(cfg, me, null, 'lobby'), false);
  assert.equal(linkButton(cfg, me, acc, 'racing'), false);
});

test('claimStep: seat, wait, retry on network or rate, and the refusal copy', () => {
  const join = { roomId: 'abcdefabcdef', ticket: 't', wsUrl: 'ws://x', seat: 3, expiresAt: 1, host: 'Ana' };
  assert.deepEqual(claimStep(join), { kind: 'seat', join, host: 'Ana' });
  assert.deepEqual(claimStep({ waiting: true, retryMs: 3000, host: 'Ana' }), { kind: 'wait', host: 'Ana', delayMs: 3000, msg: 'Waiting for Ana to open a room.' });
  assert.equal(claimStep({ waiting: true, retryMs: 50 }).delayMs, 1000, 'never faster than 1 s');
  assert.equal(claimStep({ waiting: true, retryMs: 1e9 }).delayMs, 10000, 'never slower than 10 s');
  assert.equal(claimStep({ waiting: true }).msg, 'Waiting for your friend to open a room.');
  for (const code of ['network', 'timeout', 'rate']) assert.equal(claimStep({ error: code }).kind, 'wait', code);
  assert.deepEqual(claimStep({ error: 'tier' }), { kind: 'refused', code: 'tier', msg: 'Racing a friend needs Prime.' });
  assert.equal(claimStep({ error: 'gone' }).msg, 'This link has expired.');
  assert.equal(claimStep({ error: 'full' }).msg, 'That race is full.');
  assert.equal(claimStep({ error: 'weird' }).msg, errorCopy('bad'));
  assert.equal(claimStep(null).kind, 'refused');
  assert.equal(linkCopy('preview'), 'Online racing is in a closed preview.');
  assert.equal(errorCopy('preview'), 'Online racing is in a closed preview.');
  assert.equal(waitingCopy('x'.repeat(50)).length, 'Waiting for  to open a room.'.length + 32);
});

test('copy: no dashes or exclamation marks in the link lines', () => {
  const lines = ['tier', 'gone', 'full', 'preview', 'started'].map(linkCopy).concat([waitingCopy('Ana'), linkNote({ exp: 2 * H }, 0, true), linkNote({ exp: 2 * H }, 0, false)]);
  const bad = new RegExp('[!' + String.fromCharCode(0x2013, 0x2014) + ']');
  for (const l of lines) assert.ok(!bad.test(l), l);
});

test('boot: ?r= opens the lobby card on a claim, the address drops r, the link calls reach the lobby', () => {
  const boot = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  assert.ok(/linkToken\(location\.search\)/.test(boot), 'boot reads the token through linkToken');
  assert.ok(/hud\.setCard\('lobby', \{ claim: linkTok \}\)/.test(boot), 'the lobby card claims it');
  assert.ok(/history\.replaceState\(/.test(boot) && /dropLinkParam\(location\.href\)/.test(boot), 'r leaves the address bar');
  for (const k of ['makeLink', 'revokeLink', 'claimLink', 'access']) assert.ok(boot.includes(k + ': '), k + ' wired');
  const hud = readFileSync(new URL('../hud/hud.js', import.meta.url), 'utf8');
  assert.ok(/get lobby\(\) \{ return cards\.lobby; \}/.test(hud), 'hud hands boot the lobby handle');
  for (const f of ['../net/lobbyApi.js', '../hud/lobbyMath.js']) {
    const src = readFileSync(new URL(f, import.meta.url), 'utf8');
    assert.ok(!/from ['"]three|document\.|window\./.test(src), f + ' stays three-free and DOM-free');
  }
});
