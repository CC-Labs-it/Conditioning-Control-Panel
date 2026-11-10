import { DEMOS } from './kit.js';
import { emiUpgradeArt, EMI_ICON_BEATS } from '../emi-upgrade-art.js';
import { createSponsorCard } from '../sponsor-card.js';
import { EMI_AD_READS } from '../../data/content/ads.js';
import { BY_ID } from '../../data/items.js';

// Also replaces the old Sunrise demo attached to the Goon ending id.
export function registerEmiUpgradeDemos() {
  DEMOS.o_emi = kit => {
    kit.page('dark');
    const counter = kit.counter({ v: 20000, y: 7, css: 'font-size:14px;' });
    kit.every(200, () => counter.add(BY_ID.o_emi.fps / 5), true);
    const root = kit.add(4, 35, 'width:320px;transform:scale(.72);transform-origin:0 0;');
    const lines = EMI_AD_READS[1];
    const card = createSponsorCard(root, lines.length);
    let index = 0;
    kit.every(1250, () => {
      card.show(lines[index], index);
      index = (index + 1) % lines.length;
    });
  };
  for (const [id, frames] of Object.entries(EMI_ICON_BEATS)) {
    DEMOS[id] = kit => {
      kit.page('#fff4de');
      const size = 112;
      const art = kit.add((kit.W - size) / 2, (kit.H - size) / 2, `width:${size}px;height:${size}px`,
        `<svg viewBox="0 0 64 64" aria-hidden="true">${emiUpgradeArt(id)}</svg>`);
      const moving = art.querySelector('[data-emi-motion]');
      moving.style.transformOrigin = 'center';
      moving.style.transformBox = 'fill-box';
      kit.anim(moving, frames, 1320, { iterations: Infinity });
    };
  }
}
