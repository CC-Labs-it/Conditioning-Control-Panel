/* input/gamepad.js - gamepad InputSource, standard mapping (CONTRACT.md section 4), lifted from
 * dtrh/race/input.js. Left stick steer (dead zone rescaled, never inverted), RT throttle, LT brake,
 * A hop and drift, Start pause. rumble() is skipped by the caller under Gentle.
 */
const DEADZONE = 0.16;
const BTN = { a: 0, lt: 6, rt: 7, start: 9 };
const clamp01 = (v) => Math.max(0, Math.min(1, v || 0));

export function createGamepad({ nav = typeof navigator !== 'undefined' ? navigator : null } = {}) {
  let wasA = false, wasStart = false;

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
    if (!g) { wasA = false; wasStart = false; return; }
    const ax = g.axes && g.axes.length ? g.axes[0] || 0 : 0;
    into.steer = Math.abs(ax) > DEADZONE ? Math.sign(ax) * Math.min(1, (Math.abs(ax) - DEADZONE) / (1 - DEADZONE)) : 0;
    into.throttle = clamp01(btn(g, BTN.rt));
    into.brake = clamp01(btn(g, BTN.lt));
    const a = btn(g, BTN.a) > 0.5, start = btn(g, BTN.start) > 0.5;
    into.hop = a;
    into.hopPressed = a && !wasA;
    into.pause = start && !wasStart;
    wasA = a; wasStart = start;
  }
  function rumble(strong, weak, ms) {
    const g = pad(), act = g && g.vibrationActuator;
    if (!act || typeof act.playEffect !== 'function') return;
    try {
      const p = act.playEffect('dual-rumble', { startDelay: 0, duration: ms, strongMagnitude: clamp01(strong), weakMagnitude: clamp01(weak) });
      if (p && p.catch) p.catch(() => {});
    } catch (e) { /* no rumble on this pad */ }
  }
  return { poll, rumble, dispose() { wasA = false; wasStart = false; } };
}
