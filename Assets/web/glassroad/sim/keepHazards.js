import { createKeepGates } from './keepGates.js';
import { createKeepChains } from './keepChains.js';
export function createKeepHazards(track, layout) {
  const gates = createKeepGates(track, { gates: layout.gates });
  const chains = createKeepChains(track, { chains: layout.chains });
  return {
    apply(r, previous, timeS, events) { const hit = gates.apply(r, previous, timeS, events); return chains.apply(r, previous, timeS, events) || hit; },
    steer(r, actions) { return gates.steer(r, actions) || chains.steer(r, actions); },
    reset() { gates.reset(); chains.reset(); },
    inspect(timeS) { const g = gates.inspect(timeS), c = chains.inspect(timeS); return { ...g, chains: c.chains, chainHits: c.hits }; },
  };
}
