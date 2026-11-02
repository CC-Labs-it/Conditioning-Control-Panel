/* Pure secondary mechanism motion. Metadata rates are radians/s for spin, cycles/s otherwise. */
const TAU = Math.PI * 2;
export const finite = (v, fallback = 0) => Number.isFinite(v) ? v : fallback;
export const clamp = (v, lo = 0, hi = 1) => Math.min(hi, Math.max(lo, finite(v)));

/** Exact critically damped step for a constant target. Mutates state without allocating. */
export function springStep(state, target, frequency, dt) {
  dt = clamp(dt, 0, .1);
  target = finite(target);
  const w = Math.max(.01, finite(frequency, 12));
  const x = finite(state.value) - finite(target), v = finite(state.velocity);
  const c = v + w * x, decay = Math.exp(-w * dt);
  state.value = target + (x + c * dt) * decay;
  state.velocity = (v - w * c * dt) * decay;
  return state.value;
}

export function motionState() { return { value: 0, velocity: 0, angle: 0, elapsed: 0 }; }

/** Every output remains bounded except elapsed time. motionOff gives the exact undeformed pose. */
export function stepMechanism(state, definition, dt, signals, seedPhase = 0) {
  dt = clamp(dt, 0, .1);
  state.elapsed += dt;
  if (signals.motionOff) {
    state.value = state.velocity = state.angle = 0;
    return 0;
  }
  if (signals.near === false) return state.value;
  const time = finite(signals.time, state.elapsed);
  const speed = clamp(signals.speed), boost = clamp(signals.boost), land = clamp(signals.land);
  const hit = clamp(signals.hit), steer = clamp(signals.steer, -1, 1), air = clamp(signals.air);
  const soft = signals.gentle ? .45 : 1;
  const menu = signals.menu ? (signals.selected ? .4 : .16) : .14;
  const rate = clamp(Math.abs(finite(definition.rate, 1)), 0, 30);
  const phase = finite(definition.phase) + seedPhase;
  const amplitude = clamp(Math.abs(finite(definition.amplitude)), 0, definition.mode === 'pulse' ? .4 : 1.2);
  const wave = Math.sin(time * rate * TAU + phase);
  if (definition.mode === 'spin') {
    const direction = finite(definition.rate, 1) < 0 ? -1 : 1;
    state.angle = (state.angle + direction * dt * rate * soft * (menu + speed * .9 + boost * .7)) % TAU;
    state.value = state.angle;
    return state.value;
  }
  let target;
  if (definition.mode === 'press') {
    const travel = clamp(Math.abs(finite(definition.travel, amplitude)), 0, .12);
    target = travel * soft * clamp((wave + 1) * .5 * (menu + speed * .4) + boost * .5 + land * .4);
  } else {
    const intensity = signals.menu ? menu : .25 + speed * .5 + boost * .25;
    target = amplitude * soft * clamp(wave * intensity + steer * .22 + air * .12 - land * .55 - hit * .35, -1, 1);
  }
  springStep(state, target, signals.gentle ? 8 : 14, dt);
  const limit = definition.mode === 'press' ? clamp(Math.abs(finite(definition.travel, amplitude)), 0, .12) : amplitude;
  state.value = clamp(state.value, definition.mode === 'press' ? 0 : -limit, limit);
  return state.value;
}

/** Partition complete triangles only. A corrupt/mixed ID rejects the whole cut. */
export function partitionLifeTriangles(index, ids, validIds) {
  if (!index || index.length % 3) return null;
  const groups = new Map(), keep = [];
  for (let t = 0; t < index.length; t += 3) {
    const a = index[t], b = index[t + 1], c = index[t + 2];
    if (a >= ids.length || b >= ids.length || c >= ids.length) return null;
    const id = ids[a];
    if (!Number.isInteger(id) || id < 0 || ids[b] !== id || ids[c] !== id || (id && !validIds.has(id))) return null;
    if (!id) { keep.push(a, b, c); continue; }
    if (!groups.has(id)) groups.set(id, []);
    groups.get(id).push(a, b, c);
  }
  return { keep, groups };
}
