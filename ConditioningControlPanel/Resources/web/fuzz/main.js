/* ============================================================================
 * main.js - boot. Order: bridge ready -> init (or unhosted at once) -> audio
 * kit -> spiral -> app -> widgets -> restore the save -> start the loop.
 * Escape from the host arrives as kept-escape (host-escape.js) and lands in
 * the same keydown the app already handles: pause card, never close.
 * ==========================================================================*/

import * as bridge from './bridge.js';
import { createHostEscape } from './host-escape.js';
import { showSlip } from './sparkles-slip.js';
import { createKit } from './audio/kit.js';
import { createSpiral } from './fx/spiral.js';
import { createApp } from './ui/app.js';
import { registerWidgets } from './ui/widgets/index.js';

const qs = new URLSearchParams(location.search);
const pace = Math.max(1, Math.min(50, Number(qs.get('pace')) || 1));

bridge.boot({
  onInit(init) {
    const reduced = !!init.reduced || !!init.gentle || matchMedia('(prefers-reduced-motion: reduce)').matches;
    const audio = createKit();
    const spiral = createSpiral(document.getElementById('spiral'), { reduced });
    const app = createApp({ audio, spiral, bridge, pace, init });
    app.init = init;
    registerWidgets(app);

    window.fuzz = { state: app.state, buy: (id) => app.buy(id), add: (n) => { app.state.fuzz += n; app.state.total += n; app.drawCounter(); app.drawShop(); }, app };

    const escape = createHostEscape();
    bridge.on('kept-escape', () => escape.kept());
    bridge.on('suspend', (f) => { if (f.on) { audio.silence(); app.pause(); } });
    bridge.on('close', () => { audio.silence(); bridge.send('exit-done'); });
    bridge.on('setting', (f) => { if (f.key === 'gentle') spiral.setStill(!!f.value); });
    bridge.on('sparkles-receipt', showSlip);

    app.restore();
    app.start();
    bridge.log('info', `fuzz booted pace=${pace} hosted=${bridge.isHosted}`);
  },
});
