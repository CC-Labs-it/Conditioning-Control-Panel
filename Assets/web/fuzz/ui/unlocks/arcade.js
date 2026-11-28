import { mountToyBox } from './arcade/toy-box.js';
import { mountBreakout } from './arcade/breakout.js';
import { mountRace } from './arcade/race.js';
import { mountSlots } from './arcade/slots.js';
import { mountDispenser } from './arcade/dispenser.js';
import { mountDeepEnd } from './arcade/deep-end.js';

export default {
  toy_box: { mount: mountToyBox },
  mini_breakout: { mount: mountBreakout },
  mini_race: { mount: mountRace },
  mini_deep: { mount: mountDeepEnd },
  mini_slots: { mount: mountSlots },
  dispenser: { mount: mountDispenser },
  dispenser_fast: { mount() { return {}; } },
  dispenser_faster: { mount() { return {}; } },
};
