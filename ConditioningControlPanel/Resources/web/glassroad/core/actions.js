/* core/actions.js - the unified action map (CONTRACT.md section 4, CONTRACT-B1.md section 2).
 *
 * Keyboard, gamepad and touch are ADDITIVE sources, never a remap of one another: steer takes
 * whichever source has the larger magnitude, throttle and brake the max, hop is an OR, and the
 * edges (hopPressed, pause) are ORed too. Nothing here mirrors or inverts an axis, and no API for
 * that may be added (owner law: left stays left).
 * B1: createActionMap(sources, { items: true }) also ORs `item` (an edge, latched like the hop edge)
 * and `back` (held). Without the option the Phase A shape is unchanged.
 * Polish 1: a source may set `padLost` (the gamepad in use left); it reads as a pause edge in `pause`
 * and is never copied out, so a pause card's resume (the pad's own Start) cannot see it.
 * Polish 2: a source that tags `steerKind` ('key' | 'stick' | 'touch') gets its steer shaped by the
 * response curve (input/steerCurve.js: a key ramp that builds, a power curve on stick and touch) before the
 * merge; `steerKind` is never copied out. createActionMap(sources, { feel }) reads the curve numbers
 * from feel.json `input.*` (slice defaults without a feel); sample({ nowS }) lets a test drive time.
 */
import { shapeSteer, createKeyRamp } from '../input/steerCurve.js';

const wallS = () => (typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now()) / 1000;

/** @returns {{steer: number, throttle: number, brake: number, hop: boolean, hopPressed: boolean, pause: boolean}} */
export function createActions() {
  return { steer: 0, throttle: 0, brake: 0, hop: false, hopPressed: false, pause: false };
}

function zero(a, items) {
  a.steer = 0; a.throttle = 0; a.brake = 0; a.hop = false; a.hopPressed = false; a.pause = false;
  if (items) { a.item = false; a.back = false; }
  return a;
}
const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);

/**
 * @param {{poll(into: object): void, dispose(): void}[]} sources null entries are skipped
 * @param {{items?: boolean, feel?: object}} [opts] items: also merge `item` (edge) and `back` (held);
 *   feel: the curve numbers (`input.*`)
 * @returns {{sample(opts?: {nowS?: number}): object, dispose(): void}} sample() runs once per frame and returns the same object
 */
export function createActionMap(sources = [], { items = false, feel = null } = {}) {
  const list = sources.filter(Boolean);
  const ramps = list.map(() => createKeyRamp());
  const out = zero(createActions(), items), tmp = zero(createActions(), items);
  let lastS = -1;
  function sample(opts) {
    const nowS = opts && typeof opts.nowS === 'number' ? opts.nowS : wallS();
    const dtS = lastS < 0 ? 0 : Math.min(0.1, Math.max(0, nowS - lastS));   // a long gap (pause, tab away) never jumps the ramp
    lastS = nowS;
    zero(out, items);
    for (let i = 0; i < list.length; i++) {
      const src = list[i];
      zero(tmp, items);
      tmp.padLost = false; tmp.steerKind = null;
      try { src.poll(tmp); } catch (e) { continue; }
      if (tmp.padLost) out.pause = true;               // the pad in use left mid-race: pause, never resume
      if (tmp.steerKind) tmp.steer = shapeSteer(tmp.steer, tmp.steerKind, dtS, feel, ramps[i]);
      if (Math.abs(tmp.steer) > Math.abs(out.steer)) out.steer = tmp.steer;
      out.throttle = Math.max(out.throttle, tmp.throttle);
      out.brake = Math.max(out.brake, tmp.brake);
      out.hop = out.hop || !!tmp.hop;
      out.hopPressed = out.hopPressed || !!tmp.hopPressed;
      out.pause = out.pause || !!tmp.pause;
      if (items) { out.item = out.item || !!tmp.item; out.back = out.back || !!tmp.back; }
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
 * Keeps an edge (default the hop edge) across frames that run no sim step. Every source clears its
 * edge on poll, and above 60 Hz many frames run zero steps (144 Hz: 58%), so an edge seen on such a
 * frame would vanish. hold(a) after sample(), spent(a) after a step ran, clear() on pause or when a
 * card took the press.
 */
export function createEdgeLatch(key = 'hopPressed') {
  let pending = false;
  return {
    hold(a) { pending = pending || !!a[key]; a[key] = pending; return a; },
    spent(a) { pending = false; a[key] = false; },
    clear() { pending = false; },
    get pending() { return pending; },
  };
}

/** The hop edge latch (Phase A name). */
export function createHopLatch() { return createEdgeLatch('hopPressed'); }
