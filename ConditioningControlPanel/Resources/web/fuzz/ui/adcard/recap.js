/* The player's finished poster beside the Unit Complete report. It shares the Ad
 * artwork, run totals and portrait, and has no separate reveal to wait through. */
import { ADCARD_COPY } from '../../data/content/adcard.js';
import { cardModel } from './model.js';
import { createCardFace, loadCardCss } from './card.js';

export function mountRunPoster({ doc, host, run, life, name }) {
  const box = doc.createElement('div');
  box.className = 'adc-box';
  box.style.aspectRatio = '4 / 5';
  host.append(box);
  const model = cardModel({ run, life, name, adNumber: run.store?.dr_serial?.serial, serialLabel: ADCARD_COPY.unit });
  let face = null, closed = false;
  loadCardCss(doc).then(() => {
    if (closed || !box.isConnected) return;
    face = createCardFace({ doc, box, model, label: ADCARD_COPY.posterLabel });
    face.card.classList.add('adc-recap');
    for (const layer of face.card.querySelectorAll('.ac-layer')) {
      const seconds = Number(layer.dataset.sec);
      layer.style.setProperty('--adc-period', Math.abs(seconds) + 's');
      layer.style.setProperty('--adc-turn', seconds < 0 ? '-1turn' : '1turn');
    }
  });
  return () => {
    closed = true;
    face?.destroy();
    box.remove();
  };
}
