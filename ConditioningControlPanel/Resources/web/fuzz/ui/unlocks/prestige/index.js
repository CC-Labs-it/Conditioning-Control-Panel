/* ui/unlocks/prestige/index.js - Renewal Sky lane entries: lane id -> { mount(ctx), always? }. The engine mounts an
 * entry at run start when the lane has a star (or always, with `always: true`). Brief: handoff-1109/TC-PRESTIGE-WIRE-BRIEF.md.
 * Lane files must not touch the DOM at import time. */
import boosts from './boosts.js';
import trig from './trig.js';
import ghost from './ghost.js';
import fine from './fine.js';

export const PRESTIGE_REGISTRY = Object.freeze({ ...boosts, ...trig, ...ghost, ...fine });
