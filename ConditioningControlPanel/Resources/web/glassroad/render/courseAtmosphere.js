/* Course scenery shares the world's lifetime and freezes with the race. */
import { createMainframeAtmosphere } from './mainframeAtmosphere.js';
import { createKeepAtmosphere } from './keepAtmosphere.js';
import { createBoudoirAtmosphere } from './boudoirAtmosphere.js';
import { createGoonworksAtmosphere } from './goonworksAtmosphere.js';

const factories = { mainframe: createMainframeAtmosphere, keep: createKeepAtmosphere,
  boudoir: createBoudoirAtmosphere, goonworks: createGoonworksAtmosphere };

export function createCourseAtmosphere(track, options = {}) {
  return factories[track.id]?.(track, options) || null;
}
