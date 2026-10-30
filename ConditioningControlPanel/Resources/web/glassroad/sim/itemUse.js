import { ITEMS } from './itemTable.js';

/** Rear use lays offensive items on the road; shields, speed and the Tunnel Vision ride still affect their owner. */
export function itemUseMode(id, behind = false) {
  const use = ITEMS[id]?.use;
  return use === 'trap' || (behind && use && use !== 'self' && use !== 'ride') ? 'drop' : use;
}
