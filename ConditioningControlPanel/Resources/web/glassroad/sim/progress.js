/* sim/progress.js - checkpoints, laps, sections and the finish (CONTRACT.md section 4).
 *
 * Runs per tick after respawn. Checkpoints count forward only and in order: the next line counts
 * when this tick's forward travel along s passes it. Crossing checkpoint 0 after all the others
 * completes a lap; lap reaching track.laps finishes (emits finish and calls race.finish()).
 * Section changes are reported in any race phase; checkpoints only while racing.
 */
import { S } from './state.js';
import { wrap } from '../core/math.js';
import { registeredSkip } from './routes.js';

/** Metres of forward s in one tick beyond which the move is a skip, not driving (top x 1.3 is under 1 m a tick). */
export const STEP_MAX_M = 30;

export function createProgress(track, events) {
  const L = track.lengthM, lines = track.checklines || [];
  let prevS = 0;

  function reset(st) {
    prevS = st[S.s];
    st[S.checkpoint] = 0; st[S.lap] = 0;
    st[S.sectionIdx] = track.sectionAt(st[S.s]);
  }

  function update(st, race) {
    const s = st[S.s];
    const d = wrap(s - prevS + L / 2, L) - L / 2;          // signed travel this tick, wrap aware
    // in the air the probe can snap to another stretch (a drop shortcut's lower road, a far search while
    // falling): hold the last s until touchdown or the rescue, then judge the whole move at once
    if (Math.abs(d) > STEP_MAX_M && !(st[S.grounded] > 0) && st[S.respawnPhase] === 0) return;
    const sec = track.sectionAt(s);
    if (sec !== st[S.sectionIdx]) {
      events.emit('section', { from: st[S.sectionIdx], to: sec });
      st[S.sectionIdx] = sec;
    }
    // a forward jump longer than any tick of driving counts only as a registered drop shortcut; any
    // other (a probe snapping to another stretch while falling, a teleport) credits nothing
    const skip = d > STEP_MAX_M ? registeredSkip(track, prevS, s) : null;
    if (skip) events.emit('route-skip', { id: skip.id, fromS: prevS, toS: s });
    if (race && race.phase === 'racing' && d > 0 && lines.length && (d <= STEP_MAX_M || skip)) {
      for (let k = 0; k < lines.length; k++) {              // a skip can pass more than one line, in order
        const next = ((st[S.checkpoint] | 0) + 1) % lines.length;
        if (wrap(lines[next].s - prevS, L) > d) break;
        st[S.checkpoint] = next;
        events.emit('checkpoint', { idx: next });
        if (next === 0) {
          const lap = (st[S.lap] = st[S.lap] + 1);
          events.emit('lap', { lap, lapTimeS: race.timeS - race.lapStartS, timeS: race.timeS, final: lap === track.laps - 1 });
          race.lapStartS = race.timeS;
          if (lap >= track.laps) {
            events.emit('finish', { timeS: race.timeS });
            race.finish();
            break;
          }
        }
      }
    }
    prevS = s;
  }

  return { update, reset };
}
