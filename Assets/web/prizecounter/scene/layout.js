/* layout.js - where the server's shelves land on the counter. Pure: no DOM, no three.
 * CONTRACT.md: shelf i hangs in bay_i, row j sits in slot r{floor(j/3)}c{j%3}, its tag on tag_r?c?. */

export const BAYS = 4, SLOTS = 9;

export function slotName(j) {
  if (!Number.isInteger(j) || j < 0 || j >= SLOTS) return null;
  return `r${Math.floor(j / 3)}c${j % 3}`;
}

// Accepts the row index (0..8) or 'r1c2' / 'slot_r1c2'; returns the row index or -1.
export function slotIndex(slot) {
  if (Number.isInteger(slot)) return slot >= 0 && slot < SLOTS ? slot : -1;
  const m = /^(?:slot_)?r([0-2])c([0-2])$/.exec(String(slot));
  return m ? Number(m[1]) * 3 + Number(m[2]) : -1;
}

// What the counter shows for a row: the price tag while it sells, the stamp once owned, nothing while 'soon'.
export function tagFor(row) {
  if (!row) return null;
  if (row.owned) return 'owned';
  return row.sale === 'on' ? 'price' : null;
}

// Root node name of an item GLB: item_<id with . as _>.
export const itemRoot = (id) => 'item_' + String(id).replace(/\./g, '_');

/* One placement per row: { bay, slot, slotNode, tagNode, id, tag, row }. Shelves past bay_3 and rows past the
 * ninth slot are dropped (the server never sends them; the page must not crash if it does). */
export function layoutTab(shelves) {
  const out = [];
  (Array.isArray(shelves) ? shelves : []).slice(0, BAYS).forEach((shelf, bay) => {
    (shelf?.rows || []).slice(0, SLOTS).forEach((row, slot) => {
      if (!row || !row.id) return;
      const n = slotName(slot);
      out.push({ bay, slot, slotNode: 'slot_' + n, tagNode: 'tag_' + n, id: row.id, tag: tagFor(row), row });
    });
  });
  return out;
}

// Every shelf the server sent hangs, even an empty one; bays past the last shelf stay bare.
export const usedBays = (shelves) => (Array.isArray(shelves) ? shelves : []).slice(0, BAYS).map((_, i) => i);
