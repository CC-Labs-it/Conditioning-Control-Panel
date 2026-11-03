/* ui/unlocks/index.js - one registry, one file per group. Unlock id -> { mount(ctx) { return {tick?, off?, grow?} } }.
 * An id with no entry still sells and pays and draws nothing. Group files must not touch the DOM at import time. */

import basics from './basics.js';
import routes from './routes.js';
import themesA from './themes-a.js';
import themesB from './themes-b.js';
import extras from './extras.js';
import spiral from './wave2/spiral.js';
import edges from './wave2/edges.js';
import meta from './wave2/meta.js';
import arcade from './arcade.js';
import { mergeGroups } from './merge.js';
import { mountAudioMixer } from './audio-mixer.js';

const audioMixer = { audio_mixer: { mount: mountAudioMixer } };

export const GROUPS = Object.freeze({ basics, routes, 'themes-a': themesA, 'themes-b': themesB, extras, spiral, edges, meta, arcade, audioMixer });
export const REGISTRY = Object.freeze(mergeGroups([basics, routes, themesA, themesB, extras, spiral, edges, meta, arcade, audioMixer]));
