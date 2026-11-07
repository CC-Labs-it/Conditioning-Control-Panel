/* mock/host.js - a mounted-style host on the mock server: what dev.html and the tests run on.
 * `calls` records every wallet and close (and sign-in ask) so a test can read what the page told its host. */

import { asHost } from '../host.js';

export function createMockHost(server, init = {}) {
  const calls = [];
  const host = asHost({
    init: { sp: server.state().sp, locale: 'en', lexicon: {}, motion: 'full', gentle: false, reducedMotion: false,
      tab: null, from: 'dev', platform: 'web', ...init },
    request: (op, body, idem) => server.handle(op, body, idem),
    wallet: (sp) => calls.push({ type: 'wallet', sp }),
    close: (reason) => calls.push({ type: 'close', reason }),
    signIn: () => { calls.push({ type: 'signin' }); if (typeof init.onSignIn === 'function') init.onSignIn(); },
    log: () => {},
  });
  return Object.assign(host, { calls, server });
}
