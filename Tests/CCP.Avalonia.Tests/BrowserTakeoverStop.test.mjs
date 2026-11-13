import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const source = fs.readFileSync(process.env.CCP_STOP_SOURCE || new URL('../../CCP.Avalonia/Views/Controls/WebHostTakeover.cs', import.meta.url), 'utf8');
const body = source.split('internal const string StopScript =')[1].split(';\n    }')[0];
const script = [...body.matchAll(/"([^"\n]*)"/g)].map(m => m[1]).join('');
const paused = [];
let exits = 0;
const window = {};
const document = {
  fullscreenElement: {},
  querySelectorAll(selector) {
    assert.equal(selector, 'video,audio');
    return [{ pause: () => paused.push('video') }, { pause: () => paused.push('audio') }];
  },
  exitFullscreen() { exits++; return Promise.resolve(); }
};
vm.runInNewContext(script, { window, document });
assert.equal(window._ccpTakeoverStopped, true);
assert.equal(window._ccpForcedFs, false);
assert.deepEqual(paused, ['video', 'audio']);
assert.equal(exits, 1);
