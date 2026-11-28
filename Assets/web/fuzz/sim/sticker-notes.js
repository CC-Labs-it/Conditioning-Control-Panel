/* sim/sticker-notes.js - the corner card queue for stickers earned while playing (owner 2026-11-25). Pure: no DOM,
 * no clock.
 *
 * The owner's report: buying Achievements "sends us all the stuck or previous ones, so we get about 15 sec of pings".
 * Rules since then:
 *  - only a sticker earned NOW gets a card (kind 'one'); what was already true when the row arrived, or when a save
 *    loads, goes in the book quietly and may get ONE silent summary card (kind 'waiting');
 *  - one card on screen at a time, and the queue never holds more than NOTE_QUEUE_MAX cards: a sticker that would
 *    make it longer joins one "+N more" card (kind 'more'), which never names what is inside (hidden stickers stay
 *    hidden until earned, and the more card earned them all at once anyway). */

import { STICKER_NOTE_COPY } from '../data/content/stickers.js';

/** How long a card holds before it slides out (engine time: a pause holds it too). */
export const NOTE_HOLD_MS = 4000;
/** Cards waiting behind the one on screen, at most. */
export const NOTE_QUEUE_MAX = 3;

/** One card per sticker up to the cap, then one "+N more" that swallows the rest. */
export function createNoteQueue(max = NOTE_QUEUE_MAX) {
  const q = [];
  return {
    /** A sticker earned now. */
    add(sticker) {
      if (!sticker) return;
      const last = q[q.length - 1];
      if (last?.kind === 'more') { last.stickers.push(sticker); return; }
      if (q.length < max) { q.push({ kind: 'one', sticker }); return; }
      /* Full: the last named card and this one become the more card. */
      if (last?.kind === 'one') q[q.length - 1] = { kind: 'more', stickers: [last.sticker, sticker] };
      else q.push({ kind: 'more', stickers: [sticker] });
    },
    /** The quiet summary: n stickers went in the book without a card each. Always first in line. */
    waiting(n) {
      if (!(n > 0)) return;
      const old = q.find((c) => c.kind === 'waiting');
      if (old) old.n += n;
      else q.unshift({ kind: 'waiting', n });
    },
    next: () => q.shift() || null,
    get size() { return q.length; },
  };
}

const fill = (s, n) => s.replace('{n}', String(n));

/** The summary card's words for n stickers. */
export function waitingCopy(n) {
  const one = n === 1;
  return { name: fill(one ? STICKER_NOTE_COPY.waitingOne : STICKER_NOTE_COPY.waitingMany, n), line: one ? STICKER_NOTE_COPY.waitingLineOne : STICKER_NOTE_COPY.waitingLineMany };
}

/** The "+N more" card's words. */
export function moreCopy(n) {
  return { name: fill(STICKER_NOTE_COPY.moreName, n), line: STICKER_NOTE_COPY.moreLine };
}
