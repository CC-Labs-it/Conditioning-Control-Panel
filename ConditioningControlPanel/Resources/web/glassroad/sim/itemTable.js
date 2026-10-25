/* sim/itemTable.js - the effect bubble table and the weighted draw (CONTRACT-B1.md section 3).
 *
 * `use` says how a press fires it, `view` how hard it blocks the victim's screen (full, mild, none) or
 * null for self items, `bot` how a bot victim feels it. Draw weights live in feel.json as
 * items.<id>Weights = [front, middle, back]; the bucket comes from place and the gap to the leader.
 * Three-free, so node --test runs it.
 *
 * Toys kit (owner pick 2026-10-25): Pendulum, Disc, Tether, Melt (the spiral trap, bigger), Snap, Surge and
 * Big Spiral. They act on the kart, never cover the screen; the payoff words and flashes play in
 * fx/payoff.js. The first twelve ids keep their wire order and draw at weight 0. Icons of the new
 * ids borrow old art until the toys art lands.
 */

export const ITEM_IDS = ['flash', 'spiral', 'brainDrain', 'freeze', 'blackout', 'pink', 'melt', 'shatter', 'surge', 'glass', 'echo', 'golden',
  'pendulum', 'disc', 'tether', 'snap', 'bigSpiral'];

/** Kinds whose hit stalls and spins the kart (sim/items.js land()). */
export const SPIN_KINDS = Object.freeze(['shatter', 'disc', 'tether']);

const fx = (file) => 'art/fx/' + file;

export const ITEMS = Object.freeze({
  flash: { use: 'throw', view: 'full', bot: 'blind', icon: fx('flash.png') },
  spiral: { use: 'throw', view: 'full', bot: 'steer', icon: fx('spiral.png') },
  brainDrain: { use: 'throw', view: 'full', bot: 'blind', icon: fx('braindrain.png') },
  freeze: { use: 'throw', view: 'mild', bot: 'steer', icon: fx('bambifreeze.png') },
  blackout: { use: 'lob', view: 'full', bot: 'blind', icon: fx('blackout.png') },
  pink: { use: 'wide', view: 'mild', bot: 'blind', icon: fx('pinkfilter.png') },
  melt: { use: 'trap', view: 'none', bot: 'ice', icon: fx('melt.png') },
  shatter: { use: 'throw', view: 'none', bot: 'spin', icon: fx('prism.png') },
  surge: { use: 'self', view: null, bot: null, icon: fx('gold_droplet.png') },
  glass: { use: 'self', view: null, bot: null, icon: fx('bubble.png') },
  echo: { use: 'self', view: null, bot: null, icon: fx('glitch.png') },
  golden: { use: 'self', view: null, bot: null, icon: fx('golden.png') },
  pendulum: { use: 'throw', view: 'none', bot: 'steer', icon: fx('golden.png') },
  disc: { use: 'bounce', view: 'none', bot: 'spin', icon: fx('prism.png') },
  tether: { use: 'tether', view: 'none', bot: 'spin', icon: fx('glitch.png') },
  snap: { use: 'self', view: null, bot: 'steer', icon: fx('bubble.png') },
  bigSpiral: { use: 'lob', view: 'none', bot: 'steer', icon: fx('spiral.png') },
});

export const BUCKETS = ['front', 'middle', 'back'];

/** place 1 or within items.frontGapM of the leader: front; items.backGapM or more behind: back; else middle. */
export function bucketOf(feel, place, gapToLeaderM) {
  if (place <= 1 || gapToLeaderM <= feel.get('items.frontGapM')) return 'front';
  if (gapToLeaderM >= feel.get('items.backGapM')) return 'back';
  return 'middle';
}

/** @returns {Array<[string, number]>} ids with a weight above 0 in this bucket, table order */
export function weightsFor(feel, bucket) {
  const b = Math.max(0, BUCKETS.indexOf(bucket));
  const out = [];
  for (const id of ITEM_IDS) {
    const w = feel.get('items.' + id + 'Weights')[b] || 0;
    if (w > 0) out.push([id, w]);
  }
  return out;
}

/** One weighted draw. rand() returns 0..1. */
export function rollItem(feel, bucket, rand) {
  const list = weightsFor(feel, bucket);
  if (!list.length) return 'surge';
  let total = 0;
  for (const [, w] of list) total += w;
  let x = rand() * total;
  for (const [id, w] of list) { if ((x -= w) < 0) return id; }
  return list[list.length - 1][0];
}

/** Seconds an item lasts once used or once it lands: items.<id>S; glass -> items.glassMaxS. */
export function durationOf(feel, id) {
  return id === 'glass' ? feel.get('items.glassMaxS') : feel.get('items.' + id + 'S');
}

/** Seeded 0..1 generator (mulberry32). */
export function makeRand(seed = 1) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
