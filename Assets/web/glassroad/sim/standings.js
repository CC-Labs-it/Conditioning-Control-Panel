/* sim/standings.js - places, gaps and the race result (CONTRACT-B1.md section 3).
 *
 * progressM = lap * lengthM + d, d = distance past the start line (negative for karts still behind it on
 * the grid, or dropped back past it by a fall onto an earlier deck). Finished racers rank by finishS ahead of every unfinished one; unfinished ones by progressM.
 * The order is re-sorted from last tick's order with adjacent swaps only when the lead is real
 * (ai.placeHystM), so two karts side by side never flicker places and ties stay stable.
 * The Result is final once the player finished AND (all finished OR ai.resultsWaitS passed); racers still
 * driving get an estimated time from their mean speed. No three.js.
 */
import { S } from './state.js';
import { wrap } from '../core/math.js';

const GAPS = { 'ai.placeHystM': 0.5 };   // feel gap: lead needed to swap two places
const fg = (feel, k) => { try { return feel.get(k); } catch (e) { return GAPS[k]; } };

export function createStandings(track, feel, events) {
  const L = track.lengthM, lines = track.checklines || [], startS = lines.length ? lines[0].s : 0;
  let order = [], places = [], prog = [], clockS = 0, waitS = 0, sent = false;
  const st = { order, update, placeOf, progressOf, gapToLeader, result: null, reset };

  function reset() {
    order = st.order = []; places = []; prog = []; clockS = 0; waitS = 0; sent = false; st.result = null;
  }

  function progressM(r) {
    const s = r.st;
    let d = wrap(s[S.s] - startS, L);
    // behind the start line: on the grid, or (polish 6) a fall back onto an earlier deck past the lap line;
    // judged from the last line passed, so a kart at line k is never read a lap ahead
    const k = s[S.checkpoint] | 0, line = lines.length ? lines[k % lines.length] : null;
    if (d > (line ? wrap(line.s - startS, L) : 0) + L / 2) d -= L;
    return (s[S.lap] | 0) * L + d;
  }

  // a ranks ahead of b (with the hysteresis margin when both are still racing)
  function ahead(list, a, b, margin) {
    const ra = list[a], rb = list[b];
    if (ra.finished !== rb.finished) return ra.finished;
    if (ra.finished) return ra.finishS < rb.finishS || (ra.finishS === rb.finishS && a < b);
    return prog[a] > prog[b] + margin;
  }

  function update(list, race, dt) {
    const n = list.length;
    if (race.phase === 'racing') clockS = race.timeS;
    else if (race.phase === 'finished') clockS += dt;
    for (let i = 0; i < n; i++) { prog[i] = progressM(list[i]); list[i].progressM = prog[i]; }

    let changed = false;
    if (order.length !== n) {
      order = st.order = list.map((r, i) => i).sort((a, b) => (ahead(list, a, b, 0) ? -1 : ahead(list, b, a, 0) ? 1 : a - b));
      changed = true;
    } else {
      const m = fg(feel, 'ai.placeHystM');
      for (let pass = 0; pass < n; pass++) {          // bubble passes: adjacent swaps only, stable ties
        let swapped = false;
        for (let k = 0; k + 1 < n; k++) {
          if (ahead(list, order[k + 1], order[k], m)) { const t = order[k]; order[k] = order[k + 1]; order[k + 1] = t; swapped = changed = true; }
        }
        if (!swapped) break;
      }
    }

    const live = race.phase === 'racing' || race.phase === 'finished';
    const before = places.slice();
    for (let k = 0; k < n; k++) {
      const i = order[k], r = list[i];
      places[i] = k + 1; r.place = k + 1; r.fxv.place = k + 1;
    }
    if (changed && live && before.length === n) {
      for (let i = 0; i < n; i++) if (before[i] !== places[i]) events.emit('place-change', { racer: i, from: before[i], to: places[i] });
      // overtakes that involve the player
      for (let j = 1; j < n; j++) {
        const was = before[0] < before[j], now = places[0] < places[j];
        if (now && !was) events.emit('overtake', { racer: 0, passed: j });
        else if (!now && was) events.emit('overtake', { racer: j, passed: 0 });
      }
      events.emit('race-standings', { order: order.slice() });
    }

    // the result
    const me = list[0];
    if (!sent && me && me.finished) {
      waitS += dt;
      if (list.every((r) => r.finished) || waitS >= feel.get('ai.resultsWaitS')) {
        st.result = buildResult(list);
        sent = true;
        events.emit('race-result', st.result);
      }
    }
  }

  function buildResult(list) {
    const laps = track.laps, total = laps * L;
    const rows = list.map((r) => {
      let timeS = r.finishS;
      if (!r.finished) {
        const mean = clockS > 0 ? r.progressM / clockS : 0;
        timeS = clockS + Math.max(0, total - r.progressM) / Math.max(mean, 5);
      }
      return { place: 0, i: r.i, name: r.meta.name, isBot: r.meta.isBot, me: r.i === 0, timeS, bestLapS: r.bestLapS, finished: r.finished };
    });
    rows.sort((a, b) => (a.finished !== b.finished ? (a.finished ? -1 : 1) : a.timeS - b.timeS || a.i - b.i));
    rows.forEach((row, k) => { row.place = k + 1; });
    const mine = rows.find((row) => row.me);
    return { track: track.name || track.id, trackId: track.id, laps, rows, me: { place: mine.place, timeS: mine.timeS, bestLapS: mine.bestLapS } };
  }

  function placeOf(i) { return places[i] || i + 1; }
  function progressOf(i) { return prog[i] || 0; }
  function gapToLeader(i) {
    if (!order.length) return 0;
    return Math.max(0, (prog[order[0]] || 0) - (prog[i] || 0));
  }

  return st;
}
