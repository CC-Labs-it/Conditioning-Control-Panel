/* ui/unlocks/fate/outside.js - the Go Outside fate cards (handoff-1110/TC-FATE-CARDS-BRIEF.md): ft_window, ft_air,
 * ft_grass, ft_door. Each quietly hints at going outside without saying so. The ending itself (outside) keeps its show
 * in ui/unlocks/extras/exits.js; these hand over by letting go when it starts (everything here is ctx-made, so the ending's
 * unmount takes it all). The pieces live in ./outside/. */

import { mountWindow } from './outside/window.js';
import { mountAir } from './outside/air.js';
import { mountGrass } from './outside/grass.js';
import { mountDoor } from './outside/door.js';

export default {
  /* A small window on the page whose sky follows the real time of day. Its row pays its own small per-second rate. */
  ft_window: { mount: mountWindow },
  /* A breeze now and then: leaves cross, things lean and settle, sometimes a bird. */
  ft_air: { mount: mountAir },
  /* Grass along the bottom edge; brushing a blade pays a little, once in a while per blade. */
  ft_grass: { mount: mountGrass },
  /* A strip of white light at the right edge, a little wider with every later purchase. */
  ft_door: { mount: mountDoor },
};
