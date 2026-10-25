import {test} from 'node:test';
import assert from 'node:assert/strict';

test('Loom bridge says ready and exchanges save/list frames on the Avalonia string carrier', async () => {
  const sent = [], lists = [];
  globalThis.window = {invokeCSharpAction: json => sent.push(JSON.parse(json))};
  try {
    const bridge = await import('../../dtrh/bridge.js?loom-test');
    bridge.announceReady();
    bridge.send({type: 'loom-save', name: 'example', gifBase64: 'gif'});
    window.__ccpRnPush(JSON.stringify({type: 'loom-list', spirals: [{slug: 'example'}]}));
    bridge.on('loom-list', frame => lists.push(frame.spirals)); // buffered before studio registration
    assert.deepEqual(sent.map(x => x.type), ['ready', 'loom-save']);
    assert.deepEqual(lists, [[{slug: 'example'}]]);
  } finally { delete globalThis.window; }
});
