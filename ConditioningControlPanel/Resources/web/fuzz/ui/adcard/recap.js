/* The player's run poster plays beside the report while its tabs and actions are usable.
 * autoplay false: the card waits for start(muteMs) (the end card's intro drops it in first; beats inside muteMs are
 * folded into its landing). loop: like the exported GIF, the finished card holds 2.5 s and builds again, silently. */
import { ADCARD_COPY } from '../../data/content/adcard.js';
import { cardModel } from './model.js';
import { createAdCard, loadCardCss } from './card.js';

const GIF_HOLD_MS = 2500;

export function mountRunPoster({ doc, host, run, life, name, onBeat, autoplay = true, loop = false }) {
  const box = doc.createElement('div');
  box.className = 'adc-box';
  box.style.aspectRatio = '4 / 5';
  host.append(box);
  const model = cardModel({ run, life, name, adNumber: run.store?.dr_serial?.serial, serialLabel: ADCARD_COPY.unit });
  const win = doc.defaultView, cards = host.closest('#cards');
  const motion = win.matchMedia('(prefers-reduced-motion: reduce)');
  const reduced = () => doc.body.classList.contains('gentle') || motion.matches;
  const held = () => doc.hidden || !!host.closest('[inert]');
  let ad = null, closed = false, calmChime = false, started = !!autoplay, quietUntil = 0, loopTimer = 0, lastCalm = null;
  const beat = (n, i) => { if (performance.now() >= quietUntil) onBeat?.(n, i); };
  function scheduleLoop() {
    win.clearTimeout(loopTimer);
    if (!loop || closed || !ad || reduced()) return;
    const tl = ad.timeline;
    loopTimer = win.setTimeout(function again() {
      if (closed || !ad) return;
      if (held()) { loopTimer = win.setTimeout(again, 1000); return; }
      if (reduced()) return;
      ad.play({ quiet: true });
      scheduleLoop();
    }, (tl?.end || 6000) + (tl?.tail || 0) + GIF_HOLD_MS);
  }
  function sync() {
    if (!host.isConnected) { unmount(); return; }
    if (!ad) return;
    const calm = reduced(), paused = held();
    ad.hold(paused);
    if (started && calm !== lastCalm) { ad.setReduced(calm); if (!calm) scheduleLoop(); }
    lastCalm = calm;
    if (!calm) calmChime = false;
    if (calmChime && !paused) { calmChime = false; onBeat?.('still', 0); }
  }
  const observer = new win.MutationObserver(sync);
  observer.observe(doc.body, { attributes: true, attributeFilter: ['class'] });
  if (cards) observer.observe(cards, { attributes: true, attributeFilter: ['inert'], childList: true });
  doc.addEventListener('visibilitychange', sync);
  motion.addEventListener('change', sync);
  loadCardCss(doc).then(() => {
    if (closed || !box.isConnected) { unmount(); return; }
    calmChime = reduced();
    ad = createAdCard({ doc, box, model, label: ADCARD_COPY.posterLabel, reduced: calmChime, onBeat: onBeat ? beat : null });
    ad.card.classList.add('adc-recap');
    ad.hold(held());
    if (started) { ad.play(); scheduleLoop(); }
    sync();
  });
  function unmount() {
    if (closed) return;
    closed = true;
    win.clearTimeout(loopTimer);
    observer.disconnect();
    motion.removeEventListener('change', sync);
    doc.removeEventListener('visibilitychange', sync);
    ad?.destroy();
    box.remove();
  }
  /** Starts the build now (once); beats in the first muteMs are dropped. */
  unmount.start = (muteMs = 0) => {
    if (started || closed) return;
    started = true;
    quietUntil = performance.now() + Math.max(0, muteMs);
    if (ad) { ad.play(); scheduleLoop(); }
  };
  return unmount;
}
