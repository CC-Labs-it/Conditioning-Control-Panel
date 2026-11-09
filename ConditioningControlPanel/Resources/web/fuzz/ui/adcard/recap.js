/* The player's run poster plays beside the report while its tabs and actions are usable. */
import { ADCARD_COPY } from '../../data/content/adcard.js';
import { cardModel } from './model.js';
import { createAdCard, loadCardCss } from './card.js';

export function mountRunPoster({ doc, host, run, life, name, onBeat }) {
  const box = doc.createElement('div');
  box.className = 'adc-box';
  box.style.aspectRatio = '4 / 5';
  host.append(box);
  const model = cardModel({ run, life, name, adNumber: run.store?.dr_serial?.serial, serialLabel: ADCARD_COPY.unit });
  const win = doc.defaultView, cards = host.closest('#cards');
  const motion = win.matchMedia('(prefers-reduced-motion: reduce)');
  const reduced = () => doc.body.classList.contains('gentle') || motion.matches;
  const held = () => doc.hidden || !!host.closest('[inert]');
  let ad = null, closed = false, calmChime = false;
  function sync() {
    if (!host.isConnected) { unmount(); return; }
    if (!ad) return;
    const calm = reduced(), paused = held();
    ad.hold(paused);
    ad.setReduced(calm);
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
    ad = createAdCard({ doc, box, model, label: ADCARD_COPY.posterLabel, reduced: calmChime, onBeat });
    ad.card.classList.add('adc-recap');
    ad.hold(held());
    ad.play();
    sync();
  });
  function unmount() {
    if (closed) return;
    closed = true;
    observer.disconnect();
    motion.removeEventListener('change', sync);
    doc.removeEventListener('visibilitychange', sync);
    ad?.destroy();
    box.remove();
  }
  return unmount;
}
