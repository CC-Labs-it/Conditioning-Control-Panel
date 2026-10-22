/* sim/progress.js - checkpoints, laps, sections and the finish (CONTRACT.md section 4).
 *
 * Runs per tick after respawn. Checkpoints count forward only and in order: the next line counts
 * when this tick's forward travel along s passes it. Crossing checkpoint 0 after all the others
 * completes a lap; lap reaching track.laps finishes (emits finish and calls race.finish()).
 * Section changes are reported in any race phase; checkpoints only while racing.
 */
import { S } from './state.js';
import { wrap } from '../core/math.js';

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
    const sec = track.sectionAt(s);
    if (sec !== st[S.sectionIdx]) {
      events.emit('section', { from: st[S.sectionIdx], to: sec });
      st[S.sectionIdx] = sec;
    }
    if (race && race.phase === 'racing' && d > 0 && lines.length) {
      const next = ((st[S.checkpoint] | 0) + 1) % lines.length;
      if (wrap(lines[next].s - prevS, L) <= d) {
        st[S.checkpoint] = next;
        events.emit('checkpoint', { idx: next });
        if (next === 0) {
          const lap = (st[S.lap] = st[S.lap] + 1);
          events.emit('lap', { lap, lapTimeS: race.timeS - race.lapStartS, timeS: race.timeS });
          race.lapStartS = race.timeS;
          if (lap >= track.laps) {
            events.emit('finish', { timeS: race.timeS });
            race.finish();
          }
        }
      }
    }
    prevS = s;
  }

  return { update, reset };
}
