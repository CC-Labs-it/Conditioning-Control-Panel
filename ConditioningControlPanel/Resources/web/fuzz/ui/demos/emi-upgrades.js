import { DEMOS } from './kit.js';
import { emiUpgradeArt, EMI_ICON_BEATS } from '../emi-upgrade-art.js';

// Also replaces the old Sunrise demo attached to the Goon ending id.
export function registerEmiUpgradeDemos() {
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
