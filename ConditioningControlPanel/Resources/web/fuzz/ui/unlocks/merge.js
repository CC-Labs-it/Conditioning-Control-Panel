/* ui/unlocks/merge.js - folds the group files into one registry. A fork row may have a handler in several groups (both
 * theme files answer f5), so they all mount on the one row and one failing never stops the others. Any other row has one
 * owner; tests/contract.test.mjs fails if two groups claim it. */

import { BY_ID } from '../../data/items.js';

const quietly = (fn, ...args) => { try { return fn(...args); } catch (e) { console.error(e); return undefined; } };

function compose(entries) {
  return {
    mount(ctx) {
      const rets = entries.map((e) => quietly(e.mount, ctx) || {});
      return {
        tick(dt, now) { for (const r of rets) if (r.tick) quietly(r.tick, dt, now); },
        off() { for (const r of rets) if (r.off) quietly(r.off); },
      };
    },
  };
}

export function mergeGroups(groups) {
  const claims = new Map();
  for (const group of groups) {
    for (const [id, entry] of Object.entries(group)) {
      if (!claims.has(id)) claims.set(id, []);
      claims.get(id).push(entry);
    }
  }
  const out = {};
  for (const [id, entries] of claims) out[id] = entries.length > 1 && BY_ID[id]?.kind === 'fork' ? compose(entries) : entries[entries.length - 1];
  return out;
}
