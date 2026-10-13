import { test } from 'node:test';
import assert from 'node:assert/strict';

// The Avalonia head (WebKitGTK) has no chrome.webview: it injects window.invokeCSharpAction (string) and answers
// through window.__ccpRnPush(json). Without this carrier the board is unhosted on Linux: it never says ready,
// never gets pbp:settings / pbp:identity, its net lane goes direct with no token and pbp:exit never reaches the host.
test('the string carrier hosts the board: ready, ping/pong, settings, the net lane, media and exit', async () => {
  const sent = [];
  globalThis.window = { invokeCSharpAction: s => sent.push(JSON.parse(s)) };
  const b = await import('../bridge.js');
  assert.equal(b.isHosted, true);
  b.signalReady();
  assert.equal(sent[0].type, 'ready');
  const got = [];
  b.onHostMessage(m => got.push(m.type));
  window.__ccpRnPush(JSON.stringify({ type: 'pbp:settings', videoHoldSec: 15, reducedMotion: false, whispers: [] }));
  assert.deepEqual(got, ['pbp:settings']);
  window.__ccpRnPush(JSON.stringify({ type: 'ping' }));
  assert.ok(sent.some(m => m.type === 'pong'));          // a missed pong closes the window
  window.__ccpRnPush(JSON.stringify({ type: 'pbp:identity', unifiedId: 'u1', online: true, net: { serverBase: 'x', authToken: 't', viaHost: true } }));
  assert.equal(b.netInfo().viaHost, true);
  const reply = b.netRequest('GET', '/v2/pbp/lobby', null);
  const call = sent.find(m => m.type === 'pbp:net');
  assert.equal(call.path, '/v2/pbp/lobby');
  window.__ccpRnPush(JSON.stringify({ type: 'pbp:net-result', id: call.id, status: 200, body: '{}' }));
  assert.deepEqual(await reply, { status: 200, body: '{}' });
  const { createHostMedia } = await import('../ramp/media.js');
  const media = createHostMedia([], { requestGapMs: 0 });
  media.request?.();
  assert.ok(sent.some(m => m.type === 'pbp:media-request'));
  media.dispose?.();
  b.exitToHost();
  assert.equal(sent.at(-1).type, 'pbp:exit');
});
