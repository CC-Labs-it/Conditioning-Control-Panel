/* input/gamepad.js - gamepad InputSource (CONTRACT.md section 4), lifted from dtrh/race/input.js.
 * Left stick steer (dead zone rescaled, never inverted), RT throttle, LT brake, A hop and drift,
 * Start pause (and only pause). rumble() is skipped by the caller under Gentle.
 * Items: X / RB use forward, Y drops behind (one edge per press). Stick down / d-pad down also modifies X / RB.
 * Polish 1: RB is a second item button; LB is unused. R3 changes view, L3 holds rear view. Pads without the
 * standard mapping read through a fallback layout (input/padmap.js). A pad that was being played
 * with and then leaves sets `padLost` once (the action map turns it into a pause edge). The shared
 * `devices` hears which pad is in use and every connect and leave, for the HUD.
 * takeNav(menuOpen) hands the cards their steps (input/stickNav.js: d-pad or left stick past 0.5, then a
 * held repeat), A ok, B back, one edge per press. takeNav(false) on a frame with no card makes a held
 * direction wait for a release, and a stick held through a card steers nothing until it comes back to rest.
 */
import { readPad, profileFor, padFamily, loadChoices } from './padmap.js';
import { devices as sharedDevices } from './device.js';
import { createStickNav } from './stickNav.js';

const DEADZONE = 0.16;
const usedInput = r => r.south || r.east || r.west || r.north || r.lb || r.rb || r.ls || r.rs || r.start || r.select ||
  r.up || r.down || r.left || r.right || r.lt > 0.1 || r.rt > 0.1 ||
  Math.abs(r.x) > DEADZONE || Math.abs(r.y) > DEADZONE || Math.hypot(r.lookX, r.lookY) > DEADZONE;

const wallMs = () => (typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now());

export function createGamepad({ nav = typeof navigator !== 'undefined' ? navigator : null, devices = sharedDevices, choices = null, now = wallMs } = {}) {
  let was = {}, navEdge = null, lostEdge = false;
  const stick = createStickNav();
  let menuOpen = false, steerHeld = false;   // steerHeld: a stick moved on a card steers nothing until it rests
  let active = -1;                       // index of the pad being played with, -1 until one is used
  const known = new Map();               // index -> id, for the connect and leave notes
  const seen = new Map();                // id -> trigger axes seen moving
  let kept = choices || loadChoices();
  const r = {}, probe = {};
  let sampled = null;
  const look = { x: 0, y: 0 };
  const offDev = devices.on((type) => { if (type === 'layout') kept = loadChoices(); });

  const idOf = (g) => g.id || 'pad';
  const infoOf = (g) => ({ id: idOf(g), family: padFamily(g), profile: profileFor(g, kept), standard: g.mapping === 'standard' });

  function read(g, k, out) {
    const key = k + ':' + idOf(g);
    if (!seen.has(key)) seen.set(key, {});
    return readPad(g, profileFor(g, kept), seen.get(key), out);
  }

  /** Prefer current input, retaining the active pad for idle frames and rumble. */
  function scan(readInputs = false) {
    const list = nav && nav.getGamepads ? nav.getGamepads() : null;
    let pick = null, std = null, any = null, used = null;
    const live = new Set();
    if (list) for (let i = 0; i < list.length; i++) {
      const g = list[i];
      if (!g || !g.connected) continue;
      const k = typeof g.index === 'number' ? g.index : i;
      live.add(k);
      if (known.get(k) !== idOf(g)) { known.set(k, idOf(g)); devices.connected(infoOf(g)); }
      if (k === active) pick = { g, k };
      if (readInputs && usedInput(read(g, k, probe)) && (!used || k === active)) used = { g, k };
      if (!std && g.mapping === 'standard') std = { g, k };
      any = any || { g, k };
    }
    for (const [k, id] of [...known]) {
      if (live.has(k)) continue;
      known.delete(k);
      const wasActive = k === active;
      if (wasActive) { active = -1; lostEdge = true; was = {}; stick.reset(); }
      devices.lost({ id, active: wasActive });
    }
    return used || pick || std || any;
  }

  function poll(into) {
    into.cameraPressed = false; into.rearView = false; into.item = false; into.back = false;
    const hit = scan(true);
    into.padLost = lostEdge; lostEdge = false;
    if (!hit) { sampled = null; was = {}; stick.reset(); navEdge = null; look.x = look.y = 0; return; }
    const { g, k } = hit, id = idOf(g);
    const key = k + ':' + id;
    if (sampled !== key) { sampled = key; was = {}; stick.reset(); navEdge = null; }
    read(g, k, r);
    const ax = r.x, ay = r.y;
    const magnitude = Math.hypot(r.lookX, r.lookY);
    const gain = magnitude > DEADZONE ? Math.min(1, (magnitude - DEADZONE) / (1 - DEADZONE)) / magnitude : 0;
    look.x = gain ? r.lookX * gain : 0; look.y = gain ? r.lookY * gain : 0;
    if (menuOpen && Math.abs(ax) > DEADZONE) steerHeld = true;   // this stick is walking a card
    else if (Math.abs(ax) <= DEADZONE) steerHeld = false;
    into.steer = !steerHeld && Math.abs(ax) > DEADZONE ? Math.sign(ax) * Math.min(1, (Math.abs(ax) - DEADZONE) / (1 - DEADZONE)) : 0;
    into.steerKind = 'stick';                       // the action map applies the curve (input/steerCurve.js)
    into.throttle = r.rt;
    into.brake = r.lt;
    into.hop = r.south;
    into.hopPressed = r.south && !was.south;
    into.pause = r.start && !was.start;
    const dropPressed = r.north && !was.north;
    into.item = dropPressed || (r.west && !was.west) || (r.rb && !was.rb);
    into.back = dropPressed || ay > 0.5 || r.down;
    into.cameraPressed = r.rs && !was.rs;
    into.rearView = r.ls;
    // card steps (never fed to the sim): A / B on their press, directions through the stick rule
    const dir = stick.step(r, ax, ay, now());
    if (r.south && !was.south) navEdge = 'ok';
    else if (r.east && !was.east) navEdge = 'back';
    else if (dir) navEdge = dir;
    const used = usedInput(r);
    if (used) { active = k; devices.use('pad', infoOf(g)); }
    was = { south: r.south, east: r.east, north: r.north, start: r.start, west: r.west, rb: r.rb, rs: r.rs };
  }
  /** The last card step since the previous call, or null. menuOpen false (no card): nothing, and a held direction waits for a release. */
  function takeNav(open = true) {
    const d = navEdge; navEdge = null;
    menuOpen = !!open;
    if (!menuOpen) { stick.block(); return null; }
    return d;
  }
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
