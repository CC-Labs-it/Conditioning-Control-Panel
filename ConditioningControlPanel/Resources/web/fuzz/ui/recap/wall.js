/* ui/recap/wall.js - the small edition's one card at the end of the run (owner 2026-11-24,
 * handoff-1124/tc-small-funnel/BRIEF.md: "the wall is the end of the run"). One panel on the recap, in the recap's own
 * comic look: the plain offer (the full game is in CCP, for subscribers), the endings small never opens as a fanned hand of
 * locked cards with their names, a pill row of what the full game adds (counts from the data, ui/small-funnel.js), ONE
 * main button (nudge where 'ending', want 'upgrade') and one quiet second button (want 'download', web only).
 *
 * The recap mounts it only when main.js hands the small edition's hook (hooks.nudge); nothing here reads the edition.
 * Its in is the recap intro's 'wall' beat (ui/recap/intro.js); its out is the recap's own. Gentle visuals and reduced
 * motion get no fan motion and no hover tilt (recap.css, the intro skips under calm). COPY: drafts in
 * data/content/small-funnel.js, except the endings line, which is the approved NUDGE_COPY.endingButton. */

import { NUDGE_COPY } from '../nudges.js';
import { SMALL_WALL_COPY as W } from '../../data/content/small-funnel.js';
import { wallModel } from '../small-funnel.js';

const LOCK = '<svg viewBox="0 0 20 20" aria-hidden="true"><g fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" '
  + 'stroke-linejoin="round"><rect x="4" y="9" width="12" height="8.5" rx="1.5"/><path d="M6.5 9V6.5a3.5 3.5 0 0 1 7 0V9"/></g></svg>';

/**
 * Builds the wall into `parent` (the recap's column). go(ev, want): the ending hook (ui/nudges.js endingNudge), called
 * from the press itself. icon(id, cls): the recap's icon box. star(): a crayon star svg string for Renewal.
 * offersDownload: false hides the second button (inside the desktop app). Answers the section.
 */
export function mountWall({ doc, parent, go, icon, star = () => '', sfx = () => {}, calm = () => false, offersDownload = true }) {
  const h = (tag, cls, text, p) => { const e = doc.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; p?.append(e); return e; };
  const { endings, parts } = wallModel();

  const wall = h('section', 'rr-panel rr-wall', null, parent);
  wall.dataset.rr = 'wall';
  h('span', 'rr-cap', W.cap, wall);
  h('p', 'rr-offer', W.offer, wall);
  h('p', 'rr-wsub', W.sub, wall);

  /* the locked endings, a fanned hand of cards with their names */
  const hand = h('div', 'rr-hand', null, wall);
  h('span', 'rr-handcap', NUDGE_COPY.endingButton, hand);
  const fan = h('div', 'rr-fan', null, hand);
  fan.setAttribute('role', 'list');
  const n = endings.length;
  endings.forEach((e, i) => {
    const c = h('div', 'rr-wend', null, fan);
    c.setAttribute('role', 'listitem');
    c.setAttribute('aria-label', e.name);
    c.dataset.end = e.id;
    const t = n > 1 ? i / (n - 1) - .5 : 0;   // -0.5 .. 0.5 across the hand
    c.style.setProperty('--r', (t * 22).toFixed(2) + 'deg');
    c.style.setProperty('--y', (Math.abs(t) * 18).toFixed(1) + 'px');
    c.style.zIndex = String(10 - Math.round(Math.abs(t) * 8));
    const art = icon(e.id, 'rr-ic rr-sil');
    art.setAttribute('aria-hidden', 'true');
    c.append(art);
    const lock = h('i', 'rr-wlock', null, c);
    lock.setAttribute('aria-hidden', 'true');
    lock.innerHTML = LOCK;
    h('span', 'rr-wname', e.name, c);
  });

  /* what else the full game adds, one pill each */
  const row = h('div', 'rr-parts', null, wall);
  row.setAttribute('role', 'list');
  for (const p of parts) {
    const pill = h('span', 'rr-part', null, row);
    pill.setAttribute('role', 'listitem');
    pill.dataset.part = p.key;
    if (p.icon) pill.append(icon(p.icon, 'rr-ic'));
    else { const s = h('span', 'rr-ic rr-pstar', null, pill); s.innerHTML = star(); }
    h('span', '', p.label, pill);
  }

  /* one main button, one quiet one */
  const btns = h('div', 'rr-wbtns', null, wall);
  const main = h('button', 'rr-wmain', W.main, btns);
  main.type = 'button';
  const squash = (b) => { if (!calm()) b.animate?.([{ transform: 'none' }, { transform: 'scale(1.08,.86)', offset: .35 }, { transform: 'scale(.97,1.04)', offset: .7 }, { transform: 'none' }], { duration: 300, easing: 'ease-out' }); };
  main.addEventListener('click', (ev) => { sfx('gold', .3); squash(main); go(ev, 'upgrade'); });
  if (offersDownload) {
    const second = h('button', 'rr-wsecond', W.second, btns);
    second.type = 'button';
    second.addEventListener('click', (ev) => { sfx('chip', .2); squash(second); go(ev, 'download'); });
  }
  return wall;
}
