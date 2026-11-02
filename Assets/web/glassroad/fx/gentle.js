/* fx/gentle.js - Gentle visuals (the host photosensitivity switch, or ?gentle=1 in a browser).
 *
 * On: band and spiral contrast and scroll scaled by gentle.contrastMul / scrollMul, no fov
 * kick, shake, vibration or bloom pulses, every fade at least fadeMs, every rise at least
 * riseMs. Off: today's behaviour. The state lives in feel (gentle.*) so the sim can read
 * gentle.on for soft rails; set() is the only writer.
 */

/** @returns {Gentle} */
export function createGentle(feel, { search = globalThis.location?.search || '' } = {}) {
  const g = {
    get on() { return !!feel.get('gentle.on'); },
    get fadeMs() { return feel.get('gentle.fadeMs'); },
    get riseMs() { return feel.get('gentle.riseMs'); },
    /** From host init.gentle or a live host `gentle` message. */
    set({ on, fadeMs, riseMs, speed } = {}) {
      if (typeof on === 'boolean') feel.set('gentle.on', on);
      if (fadeMs > 0) feel.set('gentle.fadeMs', fadeMs);
      if (riseMs > 0) feel.set('gentle.riseMs', riseMs);
      if (speed > 0 && speed <= 1) feel.set('gentle.scrollMul', speed);
    },
    rise(s) { return g.on ? Math.max(s, g.riseMs / 1000) : s; },
    fade(s) { return g.on ? Math.max(s, g.fadeMs / 1000) : s; },
    kick(v) { return g.on ? 0 : v; },
    contrast(v) { return g.on ? v * feel.get('gentle.contrastMul') : v; },
    scroll(v) { return g.on ? v * feel.get('gentle.scrollMul') : v; },
  };
  if (/[?&]gentle=1(?:&|$)/.test(search)) g.set({ on: true });
  return g;
}
