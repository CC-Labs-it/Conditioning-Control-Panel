/* sim/race.js - ready, countdown on the beat, go, launch boost, finish (CONTRACT.md section 4).
 *
 * start() emits the first countdown pulse; one pulse per beat (60 / clock.bpm s) follows until go.
 * A throttle press that STARTS inside the last boost.launchWindowS before go, still held at go, pays
 * a boost.launchTier boost (launch-boost). A press held from earlier never counts.
 * Polish 5: goInS = seconds left to go (the whole count before start, negative race time after go), so the song
 * can be scheduled to land go on a downbeat (audio/music.js goDownbeat); syncCount(elapsedS) puts the count's
 * own clock on an outside one (online: server time since raceStartAt), so the bells and go keep the room's time
 * through a slow frame. Neither ever moves go itself.
 */
import { BOOST } from './state.js';
import { applyBoost } from './boost.js';

export function createRace(track, feel, events) {
  let t = 0, pulse = 0, held = false, pressT = -1;
  const goT = () => feel.get('juice.countdownPulses') * 60 / feel.get('clock.bpm');
  const race = {
    phase: 'ready', timeS: 0, lapStartS: 0, start, update, finish, reset, syncCount,
    get goInS() { return race.phase === 'ready' ? goT() : race.phase === 'countdown' ? goT() - t : -race.timeS; },
  };

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
      const n = feel.get('juice.countdownPulses'), beat = 60 / feel.get('clock.bpm'), goAt = n * beat;
      const down = a.throttle >= 0.5;
      if (down && !held) pressT = t;
      if (!down) pressT = -1;
      held = down;
      while (pulse < n && t >= pulse * beat - 1e-9) pulseOnce();
      if (t >= goAt - 1e-9) {
        race.phase = 'racing'; race.timeS = Math.max(0, t - goAt); race.lapStartS = 0;
        events.emit('go', {});
        if (pressT >= 0 && goAt - pressT <= feel.get('boost.launchWindowS') + 1e-9) {
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
  /** Countdown only: the count's clock to elapsedS (never below 0); pulses and go still fire from update. */
  function syncCount(elapsedS) {
    if (race.phase === 'countdown' && Number.isFinite(elapsedS)) t = Math.max(0, elapsedS);
  }
  function finish() { if (race.phase === 'racing') race.phase = 'finished'; }
  function reset() { race.phase = 'ready'; race.timeS = 0; race.lapStartS = 0; t = 0; pulse = 0; pressT = -1; }
  return race;
}
