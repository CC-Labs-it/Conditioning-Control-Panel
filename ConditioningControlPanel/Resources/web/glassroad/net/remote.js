/* net/remote.js - the driver of a `remote` racer (sim/racers.js): each sim tick it writes the racer's KartState
 * from net/interp.js at server time and turns the record into what a bot's view carries, so render, audio and
 * fx animate a remote kart exactly like a bot (NET.md section 3, "Remote animation"): fxv effect / shield / echo
 * from the aura byte, the held item, finished, paused, and the one-shot pulses re-emitted as racer-event
 * (hop, land, boost-start, airborne lip, trick, word-pop, bubble-pop). Air time is counted here from the rising
 * edge. Pure, three-free.
 */
import { S } from '../sim/state.js';
import { BOOST_NAME } from '../sim/boost.js';
import { PULSE } from './protocol.js';

const POSE_GRADE = ['', 'good', 'great'];

export function createRemoteDriver(interp, serverNow, events) {
  const emit = (r, name, p) => events.emit('racer-event', { racer: r.i, name, p });

  /** racers.step calls this for a remote racer after copying st into prev. */
  function drive(r, dt) {
    const seat = r.meta.seat, wasAir = r.prev[S.airborne] > 0, now = serverNow();
    const k = interp.sample(seat, now, r.st);
    if (!k) return;
    if (r.st[S.airborne] > 0) r.st[S.airTime] = wasAir ? r.prev[S.airTime] + dt : 0;
    r.finished = k.finished;
    r.meta.paused = k.paused;
    if (r.item) { r.item.id = k.item; r.item.rolling = k.rolling; }
    if (r.fxv) { r.fxv.effect = k.effect; r.fxv.shield = k.shield === 'echo' ? null : k.shield; r.fxv.echo = k.echo; r.fxv.pose = k.pose; }
    const pu = interp.takePulses(seat, now);
    if (!pu) return;
    if (pu & PULSE.HOP) emit(r, 'hop', { speed: r.st[S.engineV] });
    if (pu & PULSE.LIP) emit(r, 'airborne', { cause: 'lip' });
    if (pu & PULSE.LAND_HEAVY) emit(r, 'land', { tier: 3, airTime: r.prev[S.airTime] });
    else if (pu & PULSE.LAND) emit(r, 'land', { tier: 1, airTime: r.prev[S.airTime] });
    if (pu & PULSE.BOOST) emit(r, 'boost-start', { kind: BOOST_NAME[k.boostKind] || 'pad', tier: 0, amount: 0, holdS: 0 });
    if (pu & PULSE.TRICK) emit(r, 'trick', { grade: POSE_GRADE[k.pose] || 'good', airS: r.st[S.airTime], at: 'remote' });
    if (pu & PULSE.WORD) emit(r, 'word-pop', {});
    if (pu & PULSE.BUBBLE) emit(r, 'bubble-pop', {});
  }

  return { drive };
}
