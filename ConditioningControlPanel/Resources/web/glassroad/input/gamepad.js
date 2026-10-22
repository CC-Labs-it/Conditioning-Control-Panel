/* input/gamepad.js - gamepad InputSource, standard mapping (CONTRACT.md section 4), lifted from
 * dtrh/race/input.js. Left stick steer (dead zone rescaled, never inverted), RT throttle, LT brake,
 * A hop and drift, Start pause. rumble() is skipped by the caller under Gentle.
 * B1: X item (an edge), stick down past 0.5 or the d-pad down = back (held, drops the item behind).
 * takeNav() hands the cards one edge per press: d-pad or stick up / down / left / right, A ok, B back.
 */
const DEADZONE = 0.16;
const BTN = { a: 0, b: 1, x: 2, lt: 6, rt: 7, start: 9, up: 12, down: 13, left: 14, right: 15 };
const clamp01 = (v) => Math.max(0, Math.min(1, v || 0));

export function createGamepad({ nav = typeof navigator !== 'undefined' ? navigator : null } = {}) {
  let wasA = false, wasStart = false, wasX = false, navWas = null, navEdge = null;

  function pad() {
    const list = nav && nav.getGamepads ? nav.getGamepads() : null;
    if (!list) return null;
    let any = null;
    for (let i = 0; i < list.length; i++) {
      const g = list[i];
      if (!g || !g.connected) continue;
      if (g.mapping === 'standard') return g;
      any = any || g;
    }
    return any;
  }
  const btn = (g, i) => {
    const b = g.buttons && g.buttons[i];
    return b ? (typeof b.value === 'number' ? b.value : b.pressed ? 1 : 0) : 0;
  };

  function poll(into) {
    const g = pad();
    if (!g) { wasA = false; wasStart = false; wasX = false; navWas = null; return; }
    const ax = g.axes && g.axes.length ? g.axes[0] || 0 : 0;
    into.steer = Math.abs(ax) > DEADZONE ? Math.sign(ax) * Math.min(1, (Math.abs(ax) - DEADZONE) / (1 - DEADZONE)) : 0;
    into.throttle = clamp01(btn(g, BTN.rt));
    into.brake = clamp01(btn(g, BTN.lt));
    const a = btn(g, BTN.a) > 0.5, start = btn(g, BTN.start) > 0.5;
    into.hop = a;
    into.hopPressed = a && !wasA;
    into.pause = start && !wasStart;
    const x = btn(g, BTN.x) > 0.5, ay = g.axes && g.axes.length > 1 ? g.axes[1] || 0 : 0;
    into.item = x && !wasX;
    into.back = ay > 0.5 || btn(g, BTN.down) > 0.5;
    // one nav edge per press for the cards (never fed to the sim)
    const dir = btn(g, BTN.up) > 0.5 || ay < -0.6 ? 'up' : btn(g, BTN.down) > 0.5 || ay > 0.6 ? 'down'
      : btn(g, BTN.left) > 0.5 || ax < -0.6 ? 'left' : btn(g, BTN.right) > 0.5 || ax > 0.6 ? 'right'
        : a ? 'ok' : btn(g, BTN.b) > 0.5 ? 'back' : null;
    if (dir && dir !== navWas) navEdge = dir;
    navWas = dir;
    wasA = a; wasStart = start; wasX = x;
  }
  /** The last card nav edge since the previous call, or null. */
  function takeNav() { const d = navEdge; navEdge = null; return d; }
  function rumble(strong, weak, ms) {
    const g = pad(), act = g && g.vibrationActuator;
    if (!act || typeof act.playEffect !== 'function') return;
    try {
      const p = act.playEffect('dual-rumble', { startDelay: 0, duration: ms, strongMagnitude: clamp01(strong), weakMagnitude: clamp01(weak) });
      if (p && p.catch) p.catch(() => {});
    } catch (e) { /* no rumble on this pad */ }
  }
  return { poll, rumble, takeNav, dispose() { wasA = false; wasStart = false; wasX = false; } };
}
