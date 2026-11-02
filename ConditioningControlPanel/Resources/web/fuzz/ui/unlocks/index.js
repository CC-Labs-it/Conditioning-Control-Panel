/* ui/unlocks/index.js - one registry, one file per group. Unlock id -> { mount(ctx) { return {tick?, off?, grow?} } }.
 * An id with no entry still sells and pays and draws nothing. Group files must not touch the DOM at import time. */

import basics from './basics.js';
import routes from './routes.js';
import themesA from './themes-a.js';
import themesB from './themes-b.js';
import extras from './extras.js';
import { mergeGroups } from './merge.js';

export const GROUPS = Object.freeze({ basics, routes, 'themes-a': themesA, 'themes-b': themesB, extras });
export const REGISTRY = Object.freeze(mergeGroups([basics, routes, themesA, themesB, extras]));
