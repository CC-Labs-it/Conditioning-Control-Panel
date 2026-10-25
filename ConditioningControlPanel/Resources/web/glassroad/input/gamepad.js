/* input/gamepad.js - gamepad InputSource (CONTRACT.md section 4), lifted from dtrh/race/input.js.
 * Left stick steer (dead zone rescaled, never inverted), RT throttle, LT brake, A hop and drift,
 * Start pause (and only pause). rumble() is skipped by the caller under Gentle.
 * B1: X item (an edge), stick down past 0.5 or the d-pad down = back (held, drops the item behind).
 * Polish 1: RB is a second item button; LB is unused. R3 changes view, L3 holds rear view. Pads without the
 * standard mapping read through a fallback layout (input/padmap.js). A pad that was being played
 * with and then leaves sets `padLost` once (the action map turns it into a pause edge). The shared
 * `devices` hears which pad is in use and every connect and leave, for the HUD.
 * takeNav() hands the cards one edge per press: d-pad or stick up / down / left / right, A ok, B back.
 */
import { readPad, profileFor, padFamily, loadChoices } from './padmap.js';
import { devices as sharedDevices } from './device.js';

const DEADZONE = 0.16;

export function createGamepad({ nav = typeof navigator !== 'undefined' ? navigator : null, devices = sharedDevices, choices = null } = {}) {
  let was = {}, navWas = null, navEdge = null, lostEdge = false;
  let active = -1;                       // index of the pad being played with, -1 until one is used
  const known = new Map();               // index -> id, for the connect and leave notes
  const seen = new Map();                // id -> trigger axes seen moving
  let kept = choices || loadChoices();
  const r = {};
  const look = { x: 0, y: 0 };
  const offDev = devices.on((type) => { if (type === 'layout') kept = loadChoices(); });

  const idOf = (g) => g.id || 'pad';
  const infoOf = (g) => ({ id: idOf(g), family: padFamily(g), profile: profileFor(g, kept), standard: g.mapping === 'standard' });

  /** Notes connects and leaves; returns the pad to read: the one in use, else a standard one, else any. */
  function scan() {
    const list = nav && nav.getGamepads ? nav.getGamepads() : null;
    let pick = null, std = null, any = null;
    const live = new Set();
    if (list) for (let i = 0; i < list.length; i++) {
      const g = list[i];
      if (!g || !g.connected) continue;
      const k = typeof g.index === 'number' ? g.index : i;
      live.add(k);
      if (known.get(k) !== idOf(g)) { known.set(k, idOf(g)); devices.connected(infoOf(g)); }
      if (k === active) pick = { g, k };
      if (!std && g.mapping === 'standard') std = { g, k };
      any = any || { g, k };
    }
    for (const [k, id] of [...known]) {
      if (live.has(k)) continue;
      known.delete(k);
      const wasActive = k === active;
      if (wasActive) { active = -1; lostEdge = true; was = {}; navWas = null; }
      devices.lost({ id, active: wasActive });
    }
    return pick || std || any;
  }

  function poll(into) {
    into.cameraPressed = false; into.rearView = false;
    const hit = scan();
    into.padLost = lostEdge; lostEdge = false;
    if (!hit) { was = {}; navWas = null; look.x = look.y = 0; return; }
    const { g, k } = hit, id = idOf(g);
    if (!seen.has(id)) seen.set(id, {});
    readPad(g, profileFor(g, kept), seen.get(id), r);
    const ax = r.x, ay = r.y;
    const magnitude = Math.hypot(r.lookX, r.lookY);
    const gain = magnitude > DEADZONE ? Math.min(1, (magnitude - DEADZONE) / (1 - DEADZONE)) / magnitude : 0;
    look.x = gain ? r.lookX * gain : 0; look.y = gain ? r.lookY * gain : 0;
    into.steer = Math.abs(ax) > DEADZONE ? Math.sign(ax) * Math.min(1, (Math.abs(ax) - DEADZONE) / (1 - DEADZONE)) : 0;
    into.steerKind = 'stick';                       // the action map applies the curve (input/steerCurve.js)
    into.throttle = r.rt;
    into.brake = r.lt;
    into.hop = r.south;
    into.hopPressed = r.south && !was.south;
    into.pause = r.start && !was.start;
    into.item = (r.west && !was.west) || (r.rb && !was.rb);
    into.back = ay > 0.5 || r.down;
    into.cameraPressed = r.rs && !was.rs;
    into.rearView = r.ls;
    // one nav edge per press for the cards (never fed to the sim)
    const dir = r.up || ay < -0.6 ? 'up' : r.down || ay > 0.6 ? 'down'
      : r.left || ax < -0.6 ? 'left' : r.right || ax > 0.6 ? 'right'
        : r.south ? 'ok' : r.east ? 'back' : null;
    if (dir && dir !== navWas) navEdge = dir;
    navWas = dir;
    const used = r.south || r.east || r.west || r.north || r.lb || r.rb || r.ls || r.rs || r.start || r.select ||
      r.up || r.down || r.left || r.right || r.lt > 0.1 || r.rt > 0.1 || Math.abs(ax) > 0.5 || Math.abs(ay) > 0.5 || magnitude > DEADZONE;
    if (used) { active = k; devices.use('pad', infoOf(g)); }
    was = { south: r.south, start: r.start, west: r.west, rb: r.rb, rs: r.rs };
  }
  /** The last card nav edge since the previous call, or null. */
  function takeNav() { const d = navEdge; navEdge = null; return d; }
  function rumble(strong, weak, ms) {
    const hit = scan(), act = hit && hit.g.vibrationActuator;
    if (!act || typeof act.playEffect !== 'function') return;
    const c = (v) => Math.max(0, Math.min(1, v || 0));
    try {
      const p = act.playEffect('dual-rumble', { startDelay: 0, duration: ms, strongMagnitude: c(strong), weakMagnitude: c(weak) });
      if (p && p.catch) p.catch(() => {});
    } catch (e) { /* no rumble on this pad */ }
  }
  return { poll, rumble, takeNav, look, dispose() { was = {}; look.x = look.y = 0; offDev(); } };
}
