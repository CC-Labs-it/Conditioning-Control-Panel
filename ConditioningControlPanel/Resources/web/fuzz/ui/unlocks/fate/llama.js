/* ui/unlocks/fate/llama.js - The Panel (owner 2026-11-21, handoff-1121/TC-LLAMA-ROUND-BRIEF.md change 2): the extra open
 * fate set a full pen opens (data/fate.js HERD_SETS: life.llamas >= 12). One card, Section 17 of the contract, word for
 * word, and its ending. The card draws nothing during the run (its row pays like any seed); the ending runs the three
 * scene courtroom show (./llama/ending.js), then hands over to the ordinary recap, where the chair sits in the herd on
 * the Living Ad card. Code: ./llama/. */

import { panelEnding, PANEL_PAPER } from './llama/ending.js';

export default {
  /* Section 17: The Panel. The row is the door; nothing on the page. */
  ft_panel: { mount() { return {}; } },

  ll_end: {
    /* Nothing to draw during the run; the row is the door. */
    mount() { return {}; },
    /* The fade lands on the courtroom's paper. */
    fadeColour: PANEL_PAPER,
    ending: panelEnding,
  },
};
