/* ui/icons.js - one call draws an item's icon. Emoji for now. When the art lands, ICON_SETS names a folder per set
 * (plain, glossy) and this function returns an <img> with the emoji as its fallback; nothing else changes. */

export const ICON_SETS = Object.freeze({ emoji: null /*, plain: 'assets/img/icons/plain', glossy: 'assets/img/icons/glossy' */ });

let current = 'emoji';
export const iconSet = () => current;
export function setIconSet(name) { if (name in ICON_SETS) current = name; }

export function iconFor(doc, item) {
  const dir = ICON_SETS[current];
  if (dir) {
    const img = doc.createElement('img');
    img.src = `${dir}/${item.id}.webp`;
    img.alt = '';
    img.className = 'ico';
    img.onerror = () => { const s = doc.createElement('span'); s.className = 'ico'; s.textContent = item.emoji; img.replaceWith(s); };
    return img;
  }
  const s = doc.createElement('span');
  s.className = 'ico';
  s.setAttribute('aria-hidden', 'true');
  s.textContent = item.emoji;
  return s;
}
