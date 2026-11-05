// Runs the real page bridge against the Avalonia headless test's string channel.
import { createInterface } from 'node:readline';
import { createBridge } from '../host/bridge.js';

const send = value => process.stdout.write(JSON.stringify(value) + '\n');
let heartbeat;
const win = {
  location: { search: '' },
  invokeCSharpAction: json => send({ event: 'post', json }),
  setInterval: fn => { heartbeat = fn; return 1; }, clearInterval() {},
  setTimeout: () => 2, clearTimeout() {},
};
const bridge = createBridge({ win });
send({ event: 'bridge', hosted: bridge.hosted });
let frames = 0;
createInterface({ input: process.stdin }).on('line', line => {
  if (line === 'tick') { heartbeat?.(); return; }
  if (typeof win.__ccpRnPush === 'function') win.__ccpRnPush(line);
  if (++frames === 2) send({ event: 'second-init' });
});
bridge.init.then(init => {
  send({ event: 'init', init });
  bridge.ready();
});
