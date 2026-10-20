/* ============================================================================
 * core/storesafe.js - IS THIS PAGE RUNNING INSIDE A STORE BUILD?
 *
 * The mobile app ships two builds of this tree. The sideload build gets the
 * whole school. The Google Play and App Store builds get a STORE TREE, which
 * the vendoring script makes from this one at sync time, and the difference
 * between the two comes in two halves that need each other:
 *
 *   CONTENT, AT SYNC TIME. Every `X.store.js` in this tree replaces its `X.js`
 *   sibling in the store tree, and `store-build.json` (beside boot.js) names
 *   the files the store tree leaves out and the lexicon rows it rewrites. The
 *   word lists and clips a store build may not carry are therefore not in its
 *   download at all, rather than shipped and hidden. A host that only ever
 *   loads the full tree (the desktop, the web build, the sideload phone) never
 *   reads a `.store.js` file: nothing imports one.
 *
 *   BEHAVIOUR, AT RUNTIME. A few things are not a file swap but a control the
 *   page should not draw, such as the sort door's free-text search. Those ask
 *   `isStoreSafe()`, which reads the flag the HOST declares in
 *   `init.platform.storeSafe`. boot.js copies it onto `window.__ccpStoreSafe`
 *   the moment init lands, before any game module is imported (games load
 *   lazily, on the way into a class). A host that never sets the field leaves
 *   it undefined, which reads as false, which is the full school.
 *
 * WHY A GLOBAL AND NOT A PARAMETER. The callers are game modules several hops
 * below the shell that holds `init`. Threading a boolean down that chain would
 * change the ctx contract for every game to answer a question about the
 * building rather than about the class. The global is written once and never
 * again.
 * ==========================================================================*/

/**
 * True only when the host declared a store build.
 *
 * Read at CALL time rather than captured at import time, so a module imported
 * unusually early (a test harness, a direct-launch path) still sees the answer
 * once the handshake has set it.
 *
 * @returns {boolean}
 */
export function isStoreSafe() {
  try {
    return typeof window !== 'undefined' && window.__ccpStoreSafe === true;
  } catch (e) {
    return false;
  }
}

/**
 * Copy the host's declaration onto the window. boot.js calls this once, from
 * the init handler; anything that is not exactly `true` reads as false.
 * @param {Object} init the host's init message
 */
export function adoptStoreFlag(init) {
  try {
    if (typeof window === 'undefined') return;
    window.__ccpStoreSafe = !!(init && init.platform && init.platform.storeSafe === true);
  } catch (e) { /* a page with no window is a page with no games */ }
}

export default { isStoreSafe, adoptStoreFlag };
