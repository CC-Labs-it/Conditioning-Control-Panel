/* ============================================================================
 * ui/widgets/index.js - one registry: item.widget key -> widget module.
 * A widget is { mount(app, buyResult), tick?(now, app), unmount?() } and sets
 * `mounted` once it has a DOM. Registry pattern as in Breakout's cues.js /
 * reactions.js: adding a layer is adding a file here, never a switch in app.js.
 *
 * Ported widgets live in their own files (see README "Module map"). Items
 * whose widget is not registered still sell and still pay; they just draw
 * nothing yet, which is how the scaffold stays playable while widgets land.
 * ==========================================================================*/

import { pink } from './pink.js';
import { title } from './title.js';
import { fpsview } from './fpsview.js';
import { spiralWidgets } from './spiral.js';
import { drifters } from './drifters.js';

export function registerWidgets(app) {
  app.register('pink', pink);
  app.register('title', title);
  app.register('fpsview', fpsview);
  app.register('spiral', spiralWidgets.spiral);
  app.register('spiralspeed', spiralWidgets.speed);
  app.register('spiralover', spiralWidgets.overlay);
  app.register('drifters', drifters);
}
