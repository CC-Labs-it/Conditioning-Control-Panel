/* core/actions.js - the unified action map (CONTRACT.md section 4).
 *
 * Keyboard, gamepad and touch are ADDITIVE sources, never a remap of one another: steer takes
 * whichever source has the larger magnitude, throttle and brake the max, hop is an OR, and the
 * edges (hopPressed, pause) are ORed too. Nothing here mirrors or inverts an axis, and no API for
 * that may be added (owner law: left stays left).
 */

/** @returns {{steer: number, throttle: number, brake: number, hop: boolean, hopPressed: boolean, pause: boolean}} */
export function createActions() {
  return { steer: 0, throttle: 0, brake: 0, hop: false, hopPressed: false, pause: false };
}

function zero(a) {
  a.steer = 0; a.throttle = 0; a.brake = 0; a.hop = false; a.hopPressed = false; a.pause = false;
  return a;
}
const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);

/**
 * @param {{poll(into: object): void, dispose(): void}[]} sources null entries are skipped
 * @returns {{sample(): object, dispose(): void}} sample() runs once per frame and returns the same object
 */
export function createActionMap(sources = []) {
  const list = sources.filter(Boolean);
  const out = createActions(), tmp = createActions();
  function sample() {
    zero(out);
    for (const src of list) {
      zero(tmp);
      try { src.poll(tmp); } catch (e) { continue; }
      if (Math.abs(tmp.steer) > Math.abs(out.steer)) out.steer = tmp.steer;
      out.throttle = Math.max(out.throttle, tmp.throttle);
      out.brake = Math.max(out.brake, tmp.brake);
      out.hop = out.hop || !!tmp.hop;
      out.hopPressed = out.hopPressed || !!tmp.hopPressed;
      out.pause = out.pause || !!tmp.pause;
    }
    out.steer = clamp(out.steer, -1, 1);
    out.throttle = clamp(out.throttle, 0, 1);
    out.brake = clamp(out.brake, 0, 1);
    return out;
  }
  function dispose() { for (const src of list) { try { src.dispose(); } catch (e) { /* already gone */ } } }
  return { sample, dispose };
}

/**
 * Keeps the hop edge across frames that run no sim step. Every source clears its edge on poll, and
 * above 60 Hz many frames run zero steps (144 Hz: 58%), so an edge seen on such a frame would vanish.
 * hold(a) after sample(), spent(a) after a step ran, clear() on pause or when a card took the press.
 */
export function createHopLatch() {
  let pending = false;
  return {
    hold(a) { pending = pending || !!a.hopPressed; a.hopPressed = pending; return a; },
    spent(a) { pending = false; a.hopPressed = false; },
    clear() { pending = false; },
    get pending() { return pending; },
  };
}
