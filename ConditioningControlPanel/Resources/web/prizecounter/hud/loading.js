/* hud/loading.js - the card that holds the page while the counter is set out.
 *
 * Opening the counter used to be a dark page, then the shelves snapping in whenever the scene, the models and the
 * state had all arrived. This is the in-between: a paper tag with a star and three ink dots, wordless on purpose
 * (it is up before the host has sent a lexicon, so it has nothing to read from).
 *
 * IN:  the tag is plain markup in index.html (#pc-loading), so it is there on the first paint, before any module
 *      has loaded. A host page without that markup gets the same tag built here.
 * OUT: done() lets the tag lift and the dark sheet fade, then removes both. Gentle and reduced motion keep the
 *      fade and drop the lift and the dots (page.css).
 *
 * The card is never the reason the page is stuck: done() is safe to call twice, and main.js calls it from a
 * finally, with a cap on how long it waits for the first shelf. */

export const LOADING_ID = 'pc-loading';
export const LOADING_OUT_MS = 420;
// The longest the card waits for the first shelf to hang once the state is in. Past this the shelf pops in
// behind a card that is already leaving, which is still better than a card that never goes.
export const LOADING_CAP_MS = 6000;

const el = (doc, tag, cls) => { const n = doc.createElement(tag); n.className = cls; return n; };

export function buildLoading(doc) {
  const sheet = el(doc, 'div', 'pc-loading');
  sheet.id = LOADING_ID;
  sheet.setAttribute('role', 'status');
  sheet.setAttribute('aria-busy', 'true');
  const tag = el(doc, 'div', 'pc-loading-tag');
  const dots = el(doc, 'div', 'pc-loading-dots');
  dots.append(el(doc, 'i', 'pc-loading-dot'), el(doc, 'i', 'pc-loading-dot'), el(doc, 'i', 'pc-loading-dot'));
  tag.append(el(doc, 'span', 'pc-loading-hole'), el(doc, 'span', 'pc-star pc-loading-star'), dots);
  sheet.append(tag);
  return sheet;
}

/** Take over the card index.html drew (or build one), and hand back its OUT.
 *  `wait(ms, fn)` is setTimeout unless a test passes its own. */
export function createLoading(doc, { parent = doc.body, wait = (ms, fn) => setTimeout(fn, ms) } = {}) {
  let sheet = typeof doc.getElementById === 'function' ? doc.getElementById(LOADING_ID) : null;
  if (!sheet) { sheet = buildLoading(doc); parent.append(sheet); }
  let gone = false;
  return {
    el: sheet,
    /** Gentle visuals: fade only, no dots. Set as soon as the host has said so. */
    gentle(on) { if (on) sheet.dataset.gentle = ''; },
    motion(level) { if (level) sheet.dataset.motion = level; },
    get gone() { return gone; },
    done() {
      if (gone) return;
      gone = true;
      sheet.setAttribute('aria-busy', 'false');
      sheet.classList.add('pc-loading-out');
      wait(LOADING_OUT_MS, () => sheet.remove());
    },
  };
}

/** Resolves when `p` settles or after `ms`, whichever is first. Never rejects. */
export function settledWithin(p, ms, wait = (t, fn) => setTimeout(fn, t)) {
  return new Promise((resolve) => {
    wait(ms, resolve);
    Promise.resolve(p).then(resolve, resolve);
  });
}
