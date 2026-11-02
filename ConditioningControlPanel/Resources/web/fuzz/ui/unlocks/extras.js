/* Lanes: trouble emi combo exit meta after. See ENGINE.md. The bulky parts live in ./extras/. */

import { AFTER_UNLOCKS } from './extras/after.js';
import { COMBO_UNLOCKS } from './extras/combos.js';
import { EMI_UNLOCKS } from './extras/emi-unlocks.js';
import { EXIT_UNLOCKS } from './extras/exits.js';
import { MOOD_UNLOCKS } from './extras/moods.js';
import { QUIZ_UNLOCKS } from './extras/quiz.js';

export default { ...QUIZ_UNLOCKS, ...EMI_UNLOCKS, ...COMBO_UNLOCKS, ...EXIT_UNLOCKS, ...MOOD_UNLOCKS, ...AFTER_UNLOCKS };
