/* One flat object language across shop, guide, purchase flights and switches. */
import { ICON_CATALOG } from './icon-catalog.js';
import { iconMarkup } from './icon-art.js';

export const ICON_SETS = Object.freeze({ plain: 'plain', glossy: 'glossy', emoji: 'plain' });
let current = 'plain';
const documents = new Set();
export const iconSet = () => current;
export function setIconSet(name) {
  if (!Object.hasOwn(ICON_SETS, name)) return;
  current = ICON_SETS[name];
  for (const doc of documents) for (const svg of doc.querySelectorAll('svg[data-fz-icon]')) {
    svg.innerHTML = iconMarkup(ICON_CATALOG[svg.dataset.fzIcon], current === 'glossy');
    svg.dataset.finish = current;
  }
}

function styles(doc) {
  if (documents.has(doc)) return;
  documents.add(doc);
  const s = doc.createElement('style');
  s.textContent = `
    svg.ico { display:inline-block;flex:none;width:48px;height:48px;overflow:visible;
      --art-a:color-mix(in srgb,var(--hue-a,var(--niche,#ee9bb7)) 55%,#fff0cf);
      --art-b:color-mix(in srgb,var(--hue-b,var(--niche-2,#a7c8dd)) 40%,#a8d8df); }
    .row svg.ico { width:46px;height:46px; }
    #goal .row svg.ico { width:58px;height:58px; }
    .purchase-ghost svg.ico { width:32px;height:32px; }
    .dk-i svg.ico { width:24px;height:24px;vertical-align:middle; }
    @media (max-width:759px) {
      .row svg.ico { width:40px;height:40px; }
      #goal .row svg.ico { width:46px;height:46px; }
    }
  `;
  doc.head.appendChild(s);
}

export function iconFor(doc, item) {
  const art = ICON_CATALOG[item.id];
  if (!art) throw new Error(`Missing clip art for ${item.id}`);
  styles(doc);
  const svg = doc.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '0 0 64 64');
  svg.setAttribute('width', '48');
  svg.setAttribute('height', '48');
  svg.setAttribute('class', 'ico');
  svg.setAttribute('aria-hidden', 'true');
  svg.setAttribute('focusable', 'false');
  svg.dataset.fzIcon = item.id;
  svg.dataset.finish = current;
  svg.innerHTML = iconMarkup(art, current === 'glossy');
  return svg;
}
