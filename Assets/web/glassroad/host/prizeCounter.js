/* host/prizeCounter.js - the garage's "Prize Counter" row (7.2, lane B3-G1; CONTRACT.md section 7).
 *
 * Only a host that says `caps.prizeCounter: true` in its init gets the row (the CCP desktop; the web and phone
 * shims once their counter route exists). Pressing it sends page -> host
 *   open-prize-counter { tab: 'kart', from: 'glassroad' }
 * and the host opens the counter over the paused garage (desktop: the counter window; web: /prize-counter?tab=&from=;
 * phone: the counter screen). When the counter closes the host re-sends `owned { grants }`, which the garage adopts
 * like any other snapshot (meta/owned.js), so a look bought there lights up at once. Three-free, DOM-free.
 */
export const COUNTER_TAB = 'kart';
export const COUNTER_FROM = 'glassroad';

/** May this page offer the host's Prize Counter? */
export const hostCounter = (bridge, init) => !!(bridge && bridge.hosted && init && init.caps && init.caps.prizeCounter === true);

/** The page -> host message for the row. */
export const counterMessage = () => ({ type: 'open-prize-counter', tab: COUNTER_TAB, from: COUNTER_FROM });
