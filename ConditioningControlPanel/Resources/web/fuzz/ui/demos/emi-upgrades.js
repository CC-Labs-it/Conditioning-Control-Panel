import { DEMOS } from './kit.js';
import { emiUpgradeArt, EMI_ICON_BEATS } from '../emi-upgrade-art.js';
import { createSponsorCard } from '../sponsor-card.js';
import { EMI_AD_READS } from '../../data/content/ads.js';
import { BY_ID } from '../../data/items.js';
import { HER_TILES } from '../../sim/takeover.js';
import { emiTileSprite } from '../emi-tile-sprite.js';

// Also replaces the old Sunrise demo attached to the Goon ending id.
export function registerEmiUpgradeDemos() {
  for (const tile of HER_TILES) DEMOS[tile.id] = kit => {
    kit.page('#fff4de');
    const cycle = 2400, loop = { iterations: Infinity };
    const emi = (x, y, size = 72) => kit.add(x, y, `width:${size}px;height:${size}px;transform-origin:center bottom;`,
      `<svg viewBox="0 0 64 64" aria-hidden="true">${emiTileSprite()}</svg>`);
    const main = emi(84, 40);
    if (tile.id === 'h_amp') {
      kit.anim(main, [{ transform: 'scale(1)' }, { transform: 'scale(1.35)', offset: .55 }, { transform: 'scale(1)' }], cycle, loop);
    } else if (tile.id === 'h_oc') {
      kit.anim(main, [{ transform: 'translateX(-38px)' }, { transform: 'translate(38px,-4px)' }, { transform: 'translateX(-38px)' }], 1400, loop);
      const clock = kit.add(166, 68, 'width:30px;height:30px;', '<img src="assets/clipart/clock.webp" width="30" height="30" alt="">');
      kit.anim(clock, [{ transform: 'rotate(-12deg)' }, { transform: 'rotate(12deg)' }, { transform: 'rotate(-12deg)' }], 700, loop);
    } else if (tile.id === 'h_loud') {
      for (const [i, words] of ['also,', 'one more thing.'].entries()) {
        const bubble = kit.add(i ? 118 : 22, i ? 49 : 19, 'padding:5px 8px;background:white;border:1px solid #512b38;border-radius:8px;font-weight:700;', words);
        kit.anim(bubble, [{ opacity: 0 }, { opacity: 1, offset: .15 }, { opacity: 1, offset: .7 }, { opacity: 0 }], 1500, { ...loop, delay: i * 750 });
      }
    } else if (tile.id === 'h_rep') {
      main.style.top = '22px';
      for (const direction of [-1, 1]) {
        const copy = emi(84, 22);
        kit.anim(copy, [{ opacity: 0, transform: 'translateX(0)' }, { opacity: 0, transform: 'translateX(0)', offset: .25 },
          { opacity: 1, transform: `translateX(${direction * 66}px)`, offset: .65 }, { opacity: 0, transform: `translateX(${direction * 66}px)` }], cycle, loop);
      }
      const button = kit.btn({ x: 91, y: 113, w: 58, h: 24 });
      kit.every(cycle, () => kit.after(600, () => kit.press(button)));
    } else {
      const toy = kit.add(84, 40, 'width:72px;height:72px;', '<img src="assets/clipart/brickgame.webp" width="72" height="72" alt="">');
      kit.anim(toy, [{ opacity: 1, transform: 'rotate(0)' }, { opacity: 1, transform: 'rotate(-5deg)', offset: .25 },
        { opacity: 1, transform: 'rotate(5deg)', offset: .32 }, { opacity: 0, offset: .45 }, { opacity: 0 }], cycle, loop);
      kit.anim(main, [{ opacity: 0 }, { opacity: 0, offset: .4 }, { opacity: 1, offset: .55 }, { opacity: 1, offset: .9 }, { opacity: 0 }], cycle, loop);
    }
  };
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
