import { S } from '../sim/state.js';

export function racerFork(track, state) {
  return track.forks ? track.forks.indexOf(track.forkAtSeg(state[S.seg])) + 1 : 0;
}
export function pathFork(track, id, s) {
  const fork = track.forks?.[(id || 0) - 1];
  return fork && s >= fork.fromS && s <= fork.toS ? fork : null;
}
export function pathFrame(track, id, s, out) {
  const fork = pathFork(track, id, s);
  return fork ? fork.frameAt(fork.localS(s), out) : track.frameAt(s, out);
}
export function pathScale(track, id, s) {
  const fork = pathFork(track, id, s);
  return fork ? (fork.toS - fork.fromS) / fork.lengthM : 1;
}
