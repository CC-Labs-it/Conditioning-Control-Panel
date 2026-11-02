/* Lanes: trouble emi combo exit meta after. See ENGINE.md. The bulky parts live in ./extras/. */

import { COMBO_UNLOCKS } from './extras/combos.js';
import { EMI_UNLOCKS } from './extras/emi-unlocks.js';
import { QUIZ_UNLOCKS } from './extras/quiz.js';

export default { ...QUIZ_UNLOCKS, ...EMI_UNLOCKS, ...COMBO_UNLOCKS };
