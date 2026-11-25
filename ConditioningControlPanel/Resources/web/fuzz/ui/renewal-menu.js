/* ui/renewal-menu.js - the Renewal menu (brief handoff-1121/TC-ASCENSION-BRIEF.md, lane M; owner 2026-11-21).
 * A "Renewal" button on the pause card opens a paper sheet over it: the Depth number as the hero, Stardust, every bonus
 * the renewals have lit (sim/ascension.js ascensionView, grouped by lane), a "From next run" group for stars bought
 * mid-run, and "Open the Sky". The Sky opens in browse mode (ui/prestige-sky.js openSky browse: true): stars bought
 * there are paid now and felt from the next run (sim/prestige.js buyLater), and "Back to the run" returns to the pause card.
 * The game stays paused the whole time. Over the browse Sky, Escape and Wake Up bring the pause card back above it
 * (the Sky freezes under it, nothing closes); Resume ends the moment, so the sheet and the Sky close with it, as the
 * Sticker Book does. A panic closes both and leaves the pause card. This file never decides a price or a rule. */

import { ascensionView } from '../sim/ascension.js';
import { openSky, prestigeNow, SKY_CURRENCY } from './prestige-sky.js';

/** Every word the menu shows, in one place (the owner may rename them). */
export const RENEWAL_LABELS = Object.freeze({
  door: 'Renewal',
  title: 'Renewal',
  depth: 'Depth',
  of: (max) => `of ${max}`,
  sub: 'What your renewals have lit. It holds in every run.',
  empty: 'Nothing lit yet. Finish a run, renew, and the sky starts to open.',
  nextGroup: 'From next run',
  nextSub: 'Paid this run. Felt from the next one.',
  open: 'Open the Sky',
  back: 'Back',
  close: 'Close the Renewal menu',
});

const CSS = `
#renewal-menu { position: fixed; inset: 0; z-index: 96; display: grid; place-items: center; padding: 12px; padding-top: 52px; background: rgba(19, 12, 37, .62); pointer-events: auto; }
#renewal-menu[hidden] { display: none; }
.rn { --paper: #fbf6ee; --paper-2: #efe4d1; --ink: #2a2031; --ink-soft: #6d6273; --stamp: #e8395f; --gold: #f6c544;
  position: relative; width: min(460px, 100%); max-height: 100%; display: flex; flex-direction: column; min-height: 0; box-sizing: border-box;
  background: var(--paper); color: var(--ink); border: 3px solid var(--ink); border-radius: 18px 13px 19px 12px; box-shadow: 6px 7px 0 rgba(0,0,0,.45);
  font: 500 15px/1.3 SkyFredoka, Fredoka, "Trebuchet MS", system-ui, sans-serif; animation: rnIn .22s ease-out; }
@keyframes rnIn { from { opacity: 0; transform: translateY(8px) rotate(-.6deg); } }
.rn * { box-sizing: border-box; }
.rn button { font: inherit; color: inherit; cursor: pointer; touch-action: manipulation; -webkit-tap-highlight-color: transparent; }
.rn button:focus-visible { outline: 3px solid var(--stamp); outline-offset: 2px; }
.rn-x { position: absolute; top: 8px; right: 8px; width: 38px; height: 38px; border: 2px solid var(--ink); border-radius: 50%; background: var(--paper); font-size: 22px; line-height: 1; padding: 0; z-index: 1; }
.rn-head { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 4px 16px; align-items: center; padding: 16px 52px 12px 16px; border-bottom: 2px dashed var(--paper-2); }
.rn-depth { grid-row: span 3; display: grid; place-items: center; align-content: center; width: 112px; min-height: 112px; padding: 6px 4px; color: var(--stamp); border: 3px solid var(--stamp);
  border-radius: 12px 8px 13px 7px; transform: rotate(-5deg); box-shadow: 3px 4px 0 rgba(0,0,0,.25); background: var(--paper); line-height: .95; text-transform: uppercase; }
.rn-depth small { font-size: 13px; letter-spacing: .16em; font-weight: 600; }
.rn-depth b { font-size: 66px; line-height: .9; font-weight: 700; font-variant-numeric: tabular-nums; }
.rn-depth i { font-style: normal; font-size: 11px; letter-spacing: .1em; font-weight: 600; color: var(--ink-soft); text-transform: none; }
.rn-head h2 { margin: 0; font-size: 26px; font-weight: 700; }
.rn-head p { margin: 0; color: var(--ink-soft); font-size: 14px; }
.rn-dust { justify-self: start; display: inline-flex; align-items: center; gap: 6px; border: 2px solid var(--ink); border-radius: 14px 10px 15px 9px; padding: 3px 10px 3px 5px;
  font-weight: 600; font-size: 18px; font-variant-numeric: tabular-nums; background: #fff; }
.rn-dust svg { width: 24px; height: 24px; }
.rn-dust small { font-size: 12px; font-weight: 500; color: var(--ink-soft); }
.rn-body { overflow-y: auto; min-height: 0; padding: 8px 16px 4px; overscroll-behavior: contain; }
.rn-group { margin: 6px 0 12px; }
.rn-group h3 { display: flex; align-items: center; gap: 8px; margin: 0 0 6px; font-size: 16px; font-weight: 700; }
.rn-group h3::before { content: ""; width: 14px; height: 14px; border-radius: 4px 3px 5px 2px; background: var(--c, var(--gold)); border: 2px solid var(--ink); flex: none; }
.rn-group h3 small { font-weight: 500; color: var(--ink-soft); font-size: 13px; }
.rn-row { display: grid; grid-template-columns: 40px minmax(0, 1fr); gap: 10px; align-items: center; padding: 5px 6px; border-radius: 10px 7px 11px 6px; }
.rn-row + .rn-row { border-top: 1px dashed var(--paper-2); }
.rn-row img { width: 40px; height: 40px; object-fit: contain; }
.rn-row b { display: block; font-size: 15px; font-weight: 600; }
.rn-row > div > span { display: block; font-size: 13.5px; color: var(--ink-soft); }
.rn-next { border: 2px dashed var(--ink-soft); border-radius: 12px 9px 13px 8px; padding: 8px 8px 4px; background: #fff; }
.rn-next .rn-row img { opacity: .65; }
.rn-tag { display: inline-block; margin-left: 6px; padding: 0 7px; border: 1.5px solid var(--stamp); border-radius: 999px; color: var(--stamp); font-size: 11px; font-weight: 600; vertical-align: 2px; }
.rn-empty { margin: 10px 0 14px; color: var(--ink-soft); font-size: 15px; }
.rn-acts { display: flex; flex-wrap: wrap; gap: 8px; justify-content: flex-end; padding: 12px 16px 16px; border-top: 2px dashed var(--paper-2); }
.rn-go { display: inline-flex; align-items: center; gap: 8px; min-height: 44px; padding: 6px 16px 6px 10px; border: 3px solid var(--ink); border-radius: 16px 12px 17px 11px;
  background: var(--gold); font-weight: 600; font-size: 17px; box-shadow: 3px 4px 0 rgba(0,0,0,.35); }
.rn-go img { width: 30px; height: 30px; }
.rn-go.plain { background: var(--paper); padding-left: 16px; }
.rn-go:active { transform: translateY(1px); box-shadow: 1px 1px 0 rgba(0,0,0,.35); }
body.gentle .rn, .rn.calm { animation: rnFade .2s ease both; }
@keyframes rnFade { from { opacity: 0; } }
@media (prefers-reduced-motion: reduce) { .rn { animation: rnFade .2s ease both; } }
@media (max-width: 480px) {
  #renewal-menu { padding: 8px; padding-top: 48px; }
  .rn-depth { width: 92px; min-height: 92px; } .rn-depth b { font-size: 52px; }
  .rn-head { gap: 4px 12px; padding: 14px 48px 10px 12px; } .rn-head h2 { font-size: 22px; }
}`;

const DUST_SVG = '<svg viewBox="-20 -20 40 40" aria-hidden="true"><path d="M0-17 C3-5 5-3 17 0 C5 3 3 5 0 17 C-3 5 -5 3 -17 0 C-5-3 -3-5 0-17Z" fill="#f6c544" stroke="#2a2031" stroke-width="3" stroke-linejoin="round"/></svg>';
const CLIP = (name) => `assets/clipart/${name}.webp`;
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const fmt = (n) => { n = Math.max(0, Math.floor(+n || 0)); return n < 1000 ? String(n) : n < 1e6 ? (n / 1e3).toFixed(n < 1e4 ? 1 : 0).replace(/\.0$/, '') + 'k' : (n / 1e6).toFixed(1).replace(/\.0$/, '') + 'M'; };

/** Rows grouped by lane in the order ascensionView gives them (sky order). Pure: exported for the tests. */
export function groupByLane(rows) {
  const out = [];
  for (const r of rows || []) {
    let g = out.find((x) => x.lane === r.lane);
    if (!g) out.push(g = { lane: r.lane, name: r.laneName, hue: r.hue, rows: [] });
    g.rows.push(r);
  }
  return out;
}

/** The sheet's inner html for a view (ascensionView's shape). Pure: exported for the tests. */
export function renewalHtml(view, L = RENEWAL_LABELS) {
  const v = view || { depth: 0, maxDepth: 10, dust: 0, bonuses: [], next: [] };
  const row = (r) => `<div class="rn-row" title="${esc(r.line)}"><img alt="" src="${esc(CLIP(r.icon))}"><div><b>${esc(r.name)}${r.next ? `<span class="rn-tag">${esc(L.nextGroup)}</span>` : ''}</b><span>${esc(r.plain)}</span></div></div>`;
  const groups = groupByLane(v.bonuses).map((g) => `<section class="rn-group" style="--c:${esc(g.hue)}"><h3>${esc(g.name)} <small>${g.rows.length}</small></h3>${g.rows.map(row).join('')}</section>`).join('');
  const next = v.next?.length ? `<section class="rn-group rn-next" style="--c:#e8395f"><h3>${esc(L.nextGroup)} <small>${esc(L.nextSub)}</small></h3>${v.next.map(row).join('')}</section>` : '';
  const empty = !v.bonuses?.length && !v.next?.length ? `<p class="rn-empty">${esc(L.empty)}</p>` : '';
  return `<button class="rn-x" type="button" data-act="back" aria-label="${esc(L.close)}">&times;</button>
    <div class="rn-head">
      <div class="rn-depth" role="img" aria-label="${esc(`${L.depth} ${v.depth} ${L.of(v.maxDepth)}`)}"><small>${esc(L.depth)}</small><b>${v.depth}</b><i>${esc(L.of(v.maxDepth))}</i></div>
      <h2>${esc(L.title)}</h2>
      <p>${esc(L.sub)}</p>
      <div class="rn-dust" title="${esc(SKY_CURRENCY)}">${DUST_SVG}<span data-k="dust">${fmt(v.dust)}</span> <small>${esc(SKY_CURRENCY)}</small></div>
    </div>
    <div class="rn-body">${empty}${groups}${next}</div>
    <div class="rn-acts"><button class="rn-go plain" type="button" data-act="back">${esc(L.back)}</button><button class="rn-go" type="button" data-act="sky"><img alt="" src="${CLIP('moon')}">${esc(L.open)}</button></div>`;
}

/** Mounts the pause card door once. Returns { open(), close(), get shown, get sky }. */
export function mountRenewalMenu({ doc, engine, audio = null, gentle = () => false }) {
  const win = doc.defaultView;
  const pauseEl = doc.getElementById('pause');
  const small = pauseEl?.querySelector('.card-buttons.small');
  if (!pauseEl || !small) return null;
  const row = doc.createElement('div');
  row.className = 'card-buttons renewal-row';
  const door = doc.createElement('button');
  door.type = 'button';
  door.id = 'renewal-door';
  door.textContent = RENEWAL_LABELS.door;
  row.appendChild(door);
  small.before(row);

  let layer = null, sheet = null, sky = null;
  const otherSky = () => { const el = doc.getElementById('ps-sky'); return !!el && !(sky?.open); };
  /* The renewal Sky after a finished run (or the skydemo door) is its own screen: no second Sky over it. */
  const showDoor = () => { door.hidden = !!engine.run?.ended || otherSky(); };
  const showPause = () => { if (engine.paused) { pauseEl.hidden = false; doc.getElementById('resume')?.focus({ preventScroll: true }); } };

  function build() {
    if (!doc.getElementById('renewal-menu-css')) {
      const st = doc.createElement('style'); st.id = 'renewal-menu-css'; st.textContent = CSS; doc.head.appendChild(st);
    }
    layer = doc.createElement('div'); layer.id = 'renewal-menu'; layer.hidden = true;
    sheet = doc.createElement('section'); sheet.className = 'rn';
    sheet.setAttribute('role', 'dialog'); sheet.setAttribute('aria-modal', 'true'); sheet.setAttribute('aria-label', RENEWAL_LABELS.title);
    layer.appendChild(sheet); doc.body.appendChild(layer);
    layer.addEventListener('click', (e) => {
      if (engine.suspended) return;
      if (e.target === layer) { hide(); return; }
      const b = e.target.closest('button'); if (!b) return;
      if (b.dataset.act === 'back') hide();
      else if (b.dataset.act === 'sky') openBrowse();
    });
  }
  function paint() { sheet.innerHTML = renewalHtml(ascensionView(engine.life)); sheet.classList.toggle('calm', !!gentle()); }
  function open() {
    if (engine.suspended || !engine.paused) return;
    if (sky?.open) { uncover(); return; }   // the Sky is still there under the pause card: back to it
    if (!layer) build();
    paint();
    layer.hidden = false;
    sheet.querySelector('[data-act="sky"]')?.focus({ preventScroll: true });
  }
  function hide() { if (layer) layer.hidden = true; if (!sky?.open) showPause(); }

  /* ---- the Sky, browse mode ---- */
  function openBrowse() {
    let P = null;
    try { P = prestigeNow(); } catch { P = null; }
    if (!P) return;
    hide();
    const life = engine.life;
    /* Before any renewal there is no record: the Sky shows the fogged map from a blank one that is never saved. */
    const view = life?.prestige ? life : { prestige: P.createPrestige() };
    pauseEl.hidden = true;
    try {
      sky = openSky({ doc, win, life: view, P, audio, gentle, browse: true,
        frozen: () => engine.suspended || !pauseEl.hidden,
        save: () => { if (view === life) engine.save(); },
        onBack: () => { sky = null; showPause(); } });   // Back to the run: the pause card, one Resume from play
    } catch (err) { console.error(err); sky = null; showPause(); }
  }
  function uncover() { pauseEl.hidden = true; doc.querySelector('#ps-sky canvas')?.focus?.({ preventScroll: true }); }
  function cover() { pauseEl.hidden = false; doc.getElementById('resume')?.focus({ preventScroll: true }); }
  function closeSky() { const s = sky; sky = null; s?.close(); }

  /* Escape over the browse Sky: the pause card comes back above it (the game's pause rule), and nothing closes. The
   * next Escape is the pause card's own (it resumes, and the Sky goes with the moment). Wake Up does the same. */
  doc.addEventListener('keydown', (e) => {
    if (e.key !== 'Escape' || !sky?.open || !pauseEl.hidden || !engine.paused) return;
    e.preventDefault(); e.stopImmediatePropagation();
    cover();
  }, true);
  doc.getElementById('wake')?.addEventListener('click', () => { if (sky?.open && pauseEl.hidden && engine.paused) cover(); }, true);
  door.addEventListener('click', open);

  engine.subscribe('pause', showDoor);
  engine.subscribe('resume', () => { if (layer) layer.hidden = true; closeSky(); });
  engine.subscribe('suspend', ({ on, reason } = {}) => {
    if (!on) return;
    if (layer) layer.hidden = true;
    if (reason === 'panic' && sky?.open) { closeSky(); showPause(); }   // panic: nothing of this stays up
  });
  engine.subscribe('ending', () => { if (layer) layer.hidden = true; closeSky(); });
  showDoor();

  return { open, close: () => { hide(); closeSky(); showPause(); }, get shown() { return !!layer && !layer.hidden; }, get sky() { return sky; } };
}
