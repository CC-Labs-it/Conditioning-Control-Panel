/* fx/payoff.js - payoff words and flashes for the LOCAL player (owner, 2026-10-25, toys kit).
 *
 * Being slowed, spun or swung pays off on screen: a cascade of subliminal words, plus a short flash card
 * when the kart spins or is slowed hard. Course hazards (the malus) pay the same cascade and flash. A boost
 * pad sometimes shows one word. Nothing here touches the sim; it drives the existing renderers
 * (fx/subliminalBurst.js reads `words`, fx/flashMedia.js reads `flash`) with screen-shaped states, so their
 * Gentle, pause, source-off and photosafe rules stay. Photosafe refuses a flash: no flash, words still play.
 * Three-free.
 */

/** What a hit on the local player pays: 'words' (a cascade), 'both' (cascade + flash), 'word' (one word). */
export const PAYOFF_HITS = Object.freeze({ pendulum: 'words', melt: 'words', snap: 'word', disc: 'both', tether: 'both', shatter: 'both', bigSpiral: 'both' });
/** Course hazards on the local player. Events with a `racer` field count only for racer 0. */
export const MALUS_EVENTS = Object.freeze(['keep-gate-hit', 'keep-chain-hit', 'boudoir-press-hit', 'boudoir-sweeper-hit',
  'boudoir-mirror-hit', 'goonworks-hit', 'scan-hit', 'perfume-contact', 'moving-trap-hit']);

const WORD_TAIL_S = 0.5;     // the burst's cards fade on their own clock; keep the canvas up a beat after durS
const FLASH_IN_S = 0.12, FLASH_OUT_S = 0.25;

export function createPayoff(bus, feel, { gentle = null, photosafe = null, rand = Math.random } = {}) {
  const g = (k) => feel.get('payoff.' + k);
  const gOn = () => !!(gentle ? gentle.on : feel.get('gentle.on'));
  const rise = (s) => (gentle ? gentle.rise(s) : s), fade = (s) => (gentle ? gentle.fade(s) : s);
  const words = { kind: 'none', k: 0, t: 0, durS: 0, strength: 1, prevKind: 'none', prevK: 0, params: { seed: 0, gentle: false } };
  const flash = { kind: 'none', k: 0, t: 0, durS: 0, prevKind: 'none', prevK: 0, params: { seed: 0, cover: 0, corner: false, scale: 0, gentle: false } };
  const stats = { cascades: 0, padWords: 0, flashes: 0, refused: 0 };
  let seed = 0, clock = 0, lastCascade = -1e9, lastFlash = -1e9, lastPad = -1e9, flashIn = FLASH_IN_S, flashOut = FLASH_OUT_S;

  function sayWords(durS) {
    Object.assign(words, { kind: 'brainDrain', t: 0, durS, k: 1 });
    words.params = { seed: ++seed, gentle: gOn() };
  }
  function showFlash() {
    if (clock - lastFlash < g('flashCoolS')) return;
    if (photosafe && !photosafe.request('flash')) { stats.refused++; return; }
    lastFlash = clock; stats.flashes++;
    flashIn = rise(FLASH_IN_S); flashOut = fade(FLASH_OUT_S);
    const corner = gOn();
    Object.assign(flash, { kind: 'flash', t: 0, durS: g('flashS'), k: 0 });
    flash.params = { seed: ++seed, cover: g('flashCover'), corner, scale: corner ? feel.get('items.gentleFlashScale') : 0, gentle: corner };
  }
  function cascade(durS, withFlash) {
    if (!g('on') || clock - lastCascade < g('malusCoolS')) return;
    lastCascade = clock; stats.cascades++;
    sayWords(Math.min(g('cascadeMaxS'), Math.max(g('cascadeMinS'), durS || 0)));
    if (withFlash) showFlash();
  }

  function onHit(e) {
    if (!e || e.victim !== 0 || e.verdict !== 'hit') return;
    const pay = PAYOFF_HITS[e.kind];
    if (!pay || !g('on')) return;
    if (pay === 'word') { if (words.kind === 'none') sayWords(g('padWordS')); return; }
    cascade((e.durationS || 0) + 0.6, pay === 'both');
  }
  function onMalus(e, name) {
    if (name === 'scan-hit' && e?.effect === 'boost') return;
    if (!e || e.racer === undefined || e.racer === 0) cascade(g('cascadeMinS'), true);
  }
  function onPad() {
    if (!g('on') || words.kind !== 'none' || clock - lastPad < g('padCoolS') || rand() >= g('padWordChance')) return;
    lastPad = clock; stats.padWords++;
    sayWords(g('padWordS'));
  }

  function update(dt) {
    if (!(dt > 0)) return;
    clock += dt;
    if (words.kind !== 'none' && (words.t += dt) >= words.durS + WORD_TAIL_S) Object.assign(words, { kind: 'none', k: 0, t: 0, durS: 0 });
    if (flash.kind !== 'none') {
      const t = (flash.t += dt), up = flashIn > 0 ? Math.min(1, t / flashIn) : 1;
      const down = t <= flash.durS ? 1 : flashOut > 0 ? Math.max(0, 1 - (t - flash.durS) / flashOut) : 0;
      flash.k = Math.min(up, down);
      if (t >= flash.durS + flashOut) Object.assign(flash, { kind: 'none', k: 0, t: 0, durS: 0 });
    }
  }
  function reset() {
    Object.assign(words, { kind: 'none', k: 0, t: 0, durS: 0 });
    Object.assign(flash, { kind: 'none', k: 0, t: 0, durS: 0 });
    clock = 0; lastCascade = lastFlash = lastPad = -1e9;
  }

  const offs = [bus.on('item-hit', onHit), bus.on('pad', onPad), ...MALUS_EVENTS.map((name) => bus.on(name, onMalus))];
  return { words, flash, stats, update, reset, dispose() { for (const off of offs) off(); } };
}

/** The screen a renderer should read: the item screen while it shows that kind, else the payoff's own. */
export function screenFor(kind, itemScreen, own) {
  return itemScreen && (itemScreen.kind === kind || itemScreen.prevKind === kind) ? itemScreen : own;
}
