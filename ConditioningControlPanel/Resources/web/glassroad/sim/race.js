/* sim/race.js - ready, countdown on the beat, go, launch boost, finish (CONTRACT.md section 4).
 *
 * start() emits the first countdown pulse; one pulse per beat (60 / clock.bpm s) follows until go.
 * A throttle press that STARTS inside the last boost.launchWindowS before go, still held at go, pays
 * a boost.launchTier boost (launch-boost). A press held from earlier never counts.
 */
import { BOOST } from './state.js';
import { applyBoost } from './boost.js';

export function createRace(track, feel, events) {
  let t = 0, pulse = 0, held = false, pressT = -1;
  const race = { phase: 'ready', timeS: 0, lapStartS: 0, start, update, finish, reset };

  function pulseOnce() {
    events.emit('countdown', { n: feel.get('juice.countdownPulses') - pulse });
    pulse++;
  }
  function start() {
    if (race.phase !== 'ready') return;
    race.phase = 'countdown'; t = 0; pulse = 0; pressT = -1;
    pulseOnce();
  }
  function update(dt, a, st) {
    if (race.phase === 'countdown') {
      t += dt;
      const n = feel.get('juice.countdownPulses'), beat = 60 / feel.get('clock.bpm'), goT = n * beat;
      const down = a.throttle >= 0.5;
      if (down && !held) pressT = t;
      if (!down) pressT = -1;
      held = down;
      while (pulse < n && t >= pulse * beat - 1e-9) pulseOnce();
      if (t >= goT - 1e-9) {
        race.phase = 'racing'; race.timeS = Math.max(0, t - goT); race.lapStartS = 0;
        events.emit('go', {});
        if (pressT >= 0 && goT - pressT <= feel.get('boost.launchWindowS') + 1e-9) {
          const tier = feel.get('boost.launchTier');
          applyBoost(st, BOOST.LAUNCH, tier, feel, events);
          events.emit('launch-boost', { tier });
        }
      }
    } else {
      held = a.throttle >= 0.5;
      if (race.phase === 'racing') race.timeS += dt;
    }
  }
  function finish() { if (race.phase === 'racing') race.phase = 'finished'; }
  function reset() { race.phase = 'ready'; race.timeS = 0; race.lapStartS = 0; t = 0; pulse = 0; pressT = -1; }
  return race;
}
