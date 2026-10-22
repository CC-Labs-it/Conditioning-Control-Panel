/* hud/words.js - the script counter under the lap timer (sim/words.js): bubbles popped out of the script's
 * total ("37 / 249 words"), only while a song with words plays, and a streak chip ("x12") while a clean run
 * of pops lasts. A pop kicks the count up and back (IN and OUT); a row or a finished line kicks it harder and
 * warms it gold; a streak breaking fades its chip out. Gentle keeps everything still (the colours still turn).
 */

export const WORD_HUD = Object.freeze({ streakFrom: 3, kickScale: 1.16, bigKickScale: 1.3, kickMs: 150, warmMs: 420 });

/** The counter's text: popped / total, "word" or "words" by the total. */
export const countText = (pops, total) => (total > 0 ? `${pops} / ${total} ${total === 1 ? 'word' : 'words'}` : '');

/** @returns {{ set(pops: number, total: number, streak?: number): void, show(on: boolean): void, pop(gentle?: boolean, e?: object): void, dispose(): void }} */
export function createWordCount(root, { doc = globalThis.document, H = WORD_HUD } = {}) {
  const host = (root && root.querySelector && root.querySelector('.hud-tl')) || root;
  const wrap = doc.createElement('span');
  wrap.className = 'hud-words';
  wrap.style.cssText = 'display:none;align-items:baseline;gap:8px;';
  const el = doc.createElement('span');
  el.style.cssText = 'font-size:13px;font-weight:700;letter-spacing:0.03em;opacity:0.92;display:inline-block;transform-origin:left center;' +
    'transition:transform 160ms ease-out, color 300ms ease-out, text-shadow 300ms ease-out;';
  const chip = doc.createElement('span');
  chip.style.cssText = 'font-size:12px;font-weight:800;letter-spacing:0.04em;color:#ffd36b;display:inline-block;opacity:0;' +
    'transform:scale(0.7);transform-origin:left center;transition:opacity 220ms ease-out, transform 200ms ease-out;';
  wrap.appendChild(el); wrap.appendChild(chip);
  if (host) host.appendChild(wrap);
  let last = '', lastChip = '', kick = 0, warm = 0, chipOn = false;
  return {
    set(pops, total, streak = 0) {
      const t = countText(pops, total);
      if (t !== last) el.textContent = last = t;
      const on = streak >= H.streakFrom;
      const c = on ? `x${streak}` : lastChip;
      if (c !== lastChip) chip.textContent = lastChip = c;
      if (on !== chipOn) { chipOn = on; chip.style.opacity = on ? '1' : '0'; chip.style.transform = on ? 'scale(1)' : 'scale(0.7)'; }
    },
    show(on) { wrap.style.display = on ? 'inline-flex' : 'none'; },
    pop(gentle = false, e = null) {
      const big = !!(e && (e.row || e.lineDone));
      el.style.color = big ? '#ffd36b' : '#ffc2df';
      el.style.textShadow = gentle ? '' : `0 0 ${big ? 10 : 6}px rgba(255,${big ? 211 : 140},${big ? 107 : 200},0.85)`;
      clearTimeout(warm);
      warm = setTimeout(() => { el.style.color = ''; el.style.textShadow = ''; }, H.warmMs);
      if (gentle) return;
      el.style.transform = `scale(${big ? H.bigKickScale : H.kickScale})`;
      if (chipOn) chip.style.transform = 'scale(1.2)';
      clearTimeout(kick);
      kick = setTimeout(() => { el.style.transform = ''; if (chipOn) chip.style.transform = 'scale(1)'; }, H.kickMs);
    },
    dispose() { clearTimeout(kick); clearTimeout(warm); wrap.remove(); },
  };
}
