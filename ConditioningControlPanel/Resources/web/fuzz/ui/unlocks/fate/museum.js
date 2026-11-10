/* ui/unlocks/fate/museum.js - the Gooning Museum fate set (handoff-1110/TC-FATE-CARDS-BRIEF.md): ft_ticket, ft_placard,
 * ft_rope, ft_gift and the ending go_mus. The cards hint at a museum without saying where it leads: a ticket stub, brass
 * labels, a velvet rope round the button, a gift shop. The ending runs the narrated museum ride (ui/documentary) as its
 * show, fed with this run, then hands over to the ordinary recap. Code: ./museum/. */

import { mountTicket } from './museum/ticket.js';
import { mountPlacards } from './museum/placards.js';
import { mountRope } from './museum/rope.js';
import { mountGift } from './museum/gift.js';
import { museumEnding, MUSEUM_SHOW_MAX_MS } from './museum/ending.js';

export default {
  ft_ticket: { mount: mountTicket },
  ft_placard: { mount: mountPlacards },
  ft_rope: { mount: mountRope },
  ft_gift: { mount: mountGift },

  go_mus: {
    /* Nothing to draw during the run; the row is the door. */
    mount() { return {}; },
    /* The fade lands on the colour of the ride's spiral cover. */
    fadeColour: '#14131c',
    /* The ride runs minutes, then parks on its plain wall for as long as the player stays: never the 90 s cap. */
    showMaxMs: MUSEUM_SHOW_MAX_MS,
    ending: museumEnding,
  },
};
