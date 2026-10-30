/* sim/race.js - ready, countdown on the beat, go, launch boost, finish (CONTRACT.md section 4).
 *
 * start() emits the first countdown pulse; one pulse per beat (60 / clock.bpm s) follows until go.
 * A throttle press that STARTS inside the last boost.launchWindowS before go, still held at go, pays
 * a boost.launchTier boost (launch-boost). A press held from earlier never counts.
 * Polish 5: goInS = seconds left to go (the whole count before start, negative race time after go), so the song
 * can be scheduled to land go on a downbeat (audio/music.js goDownbeat); syncCount(elapsedS) puts the count's
 * own clock on an outside one (online: server time since raceStartAt), so the bells and go keep the room's time
 * through a slow frame. Neither ever moves go itself.
 * Feel wave (rocket start ladder): while the throttle is held in the count, rev {level, over} reports how far the
 * hold has built (level 0..1 over launch.revFullBeats beats, emitted when it moves by launch.revStep; over = held
 * past the sweet spot). At go, launch {grade, tier}: perfect = held about one beat (pressed on the last bell,
 * +- launch.perfectS), tier launch.perfectTier; good = a shorter hold (the old launchWindowS press included), tier
 * launch.goodTier; early = held past the sweet spot (overcharge), no boost and a launch.earlyStallS stall; none.
 * launch-boost {tier} still fires for every paid grade. The hold is timed on the count's own clock t, which
 * syncCount puts on the room clock online; never wall time.
 */
import { BOOST, S } from './state.js';
import { applyBoost } from './boost.js';

export function createRace(track, feel, events) {
  let t = 0, pulse = 0, held = false, pressT = -1, revSent = 0, overSent = false;
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
    race.phase = 'countdown'; t = 0; pulse = 0; pressT = -1; revSent = 0; overSent = false;
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
      rev(goAt, beat);
      while (pulse < n && t >= pulse * beat - 1e-9) pulseOnce();
      if (t >= goAt - 1e-9) {
        race.phase = 'racing'; race.timeS = Math.max(0, t - goAt); race.lapStartS = 0;
        events.emit('go', {});
        // The beat can fall between fixed steps; keep the actual GO frame eligible.
        const grade = pressT >= 0 && pressT <= goAt + dt + 1e-9 ? launchGrade(goAt - pressT, beat, feel) : 'none';
        const tier = grade === 'perfect' ? feel.get('launch.perfectTier') : grade === 'good' ? feel.get('launch.goodTier') : 0;
        if (tier > 0) {
          applyBoost(st, BOOST.LAUNCH, tier, feel, events);
          events.emit('launch-boost', { tier });
        } else if (grade === 'early' && st) st[S.stalled] = Math.max(st[S.stalled], feel.get('launch.earlyStallS'));
        events.emit('launch', { grade, tier });
      }
    } else {
      held = a.throttle >= 0.5;
      if (race.phase === 'racing') race.timeS += dt;
    }
  }
  function rev(goAt, beat) {
    const holdS = pressT >= 0 ? Math.max(0, Math.min(t, goAt) - pressT) : 0;
    const fullS = feel.get('launch.revFullBeats') * beat;
    const level = Math.min(1, holdS / fullS), over = holdS > fullS + feel.get('launch.perfectS') + 1e-9;
    if (Math.abs(level - revSent) >= feel.get('launch.revStep') - 1e-9 || (level === 0) !== (revSent === 0) || over !== overSent) {
      revSent = level; overSent = over;
      events.emit('rev', { level, over });
    }
  }
  /** Countdown only: the count's clock to elapsedS (never below 0); pulses and go still fire from update. */
  function syncCount(elapsedS) {
    if (race.phase === 'countdown' && Number.isFinite(elapsedS)) t = Math.max(0, elapsedS);
  }
  function finish() { if (race.phase === 'racing') race.phase = 'finished'; }
  function reset() { race.phase = 'ready'; race.timeS = 0; race.lapStartS = 0; t = 0; pulse = 0; pressT = -1; revSent = 0; overSent = false; }
  return race;
}

/** The launch grade for a throttle held holdS seconds at go (beat = one count beat in seconds). */
export function launchGrade(holdS, beat, feel) {
  const sweet = feel.get('launch.revFullBeats') * beat, win = feel.get('launch.perfectS');
  if (holdS > sweet + win + 1e-9) return 'early';
  if (holdS >= sweet - win - 1e-9) return 'perfect';
  return 'good';
}
