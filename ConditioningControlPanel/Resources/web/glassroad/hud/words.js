/* hud/words.js - the popped-words count under the lap timer (sim/words.js pops), only while a song with
 * words plays. A pop nudges it up a little and back (IN and OUT); Gentle keeps it still.
 */

/** @returns {{ set(pops: number, total: number): void, show(on: boolean): void, pop(gentle?: boolean): void, dispose(): void }} */
export function createWordCount(root, { doc = globalThis.document } = {}) {
  const host = (root && root.querySelector && root.querySelector('.hud-tl')) || root;
  const el = doc.createElement('span');
  el.className = 'hud-words';
  el.style.cssText = 'font-size:13px;font-weight:700;letter-spacing:0.03em;opacity:0.9;transform-origin:left center;' +
    'transition:transform 180ms ease-out;display:none;';
  if (host) host.appendChild(el);
  let last = '', timer = 0;
  return {
    set(pops, total) {
      const t = total > 0 ? `${pops} ${pops === 1 ? 'word' : 'words'}` : '';
      if (t !== last) { el.textContent = last = t; }
    },
    show(on) { el.style.display = on ? '' : 'none'; },
    pop(gentle = false) {
      if (gentle) return;
      el.style.transform = 'scale(1.18)';
      clearTimeout(timer);
      timer = setTimeout(() => { el.style.transform = ''; }, 160);
    },
    dispose() { clearTimeout(timer); el.remove(); },
  };
}
