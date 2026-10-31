/* hud/kartBars.js - the kart's stat tag in the garage: a type word and four rows of 1..5 pips (owner 2026-10-31).
 * Draws what it is handed: sim/kartStats.js statBars owns the numbers, render/garage.js passes them in. The tag holds
 * no button and takes no focus, so the menu's pad and key steps never meet it. Pips change by one class; the colour
 * fade lives in garage.css and stops under g-calm (Gentle, reduced motion). */
export const PIPS = 5;
export const STAT_ROWS = [['speed', 'Speed'], ['accel', 'Accel'], ['handling', 'Handling'], ['stability', 'Stability']];

/** A stat as a pip count: always a whole number from 1 to 5, whatever comes in. */
export const pipCount = (value) => Math.max(1, Math.min(PIPS, Math.round(Number(value)) || 1));

export function barsMarkup() {
  const rows = STAT_ROWS.map(([key, label]) =>
    `<div class="g-bar" data-stat="${key}"><span>${label}</span><span class="g-pips">${'<i></i>'.repeat(PIPS)}</span></div>`).join('');
  return `<p class="g-bars-head"><span class="g-bars-name"></span><b class="g-bars-type"></b></p><div class="g-bars-rows">${rows}</div>`;
}

/** The same tag in words, for a screen reader: "Fast. Speed 5 of 5, Accel 1 of 5, ...". */
export function barsLine(bars) {
  const rows = STAT_ROWS.map(([key, label]) => `${label} ${pipCount(bars[key])} of ${PIPS}`).join(', ');
  return bars.word ? `${bars.word}. ${rows}` : rows;
}

/** bars = { speed, accel, handling, stability, word }. name: only for a kart other than the one chosen (a look, not a pick). */
export function paintKartBars(el, bars, name = '') {
  el.hidden = false;
  el.classList.toggle('g-bars-peek', !!name);
  el.setAttribute('aria-label', (name ? name + '. ' : '') + barsLine(bars));
  el.querySelector('.g-bars-name').textContent = name;
  el.querySelector('.g-bars-type').textContent = bars.word || '';
  for (const [key] of STAT_ROWS) {
    const on = pipCount(bars[key]);
    el.querySelectorAll(`[data-stat=${key}] i`).forEach((pip, i) => pip.classList.toggle('on', i < on));
  }
}
