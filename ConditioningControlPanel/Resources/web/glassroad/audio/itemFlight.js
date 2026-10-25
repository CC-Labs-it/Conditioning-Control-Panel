/* Swept closest approaches use world positions, so forks and stacked roads cannot sound nearby
 * merely because their lap progress agrees. One bounded observer, shared by audio and passing FX. */
const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
const finite3 = p => p && [0, 1, 2].every(k => Number.isFinite(p[k]));
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const LIMIT = 64, RANGE = 12, MIN_SPEED = 8;

export function createItemPassTracker(emit) {
  const history = new Map();
  let previousTime = null, lastPass = -Infinity;
  function reset() { history.clear(); previousTime = null; lastPass = -Infinity; }
  return {
    reset,
    get size() { return history.size; },
    update(projectiles, listener, timeS, { active = true, paused = false } = {}) {
      if (!active || !finite3(listener?.p) || !finite3(listener?.right) || !Number.isFinite(timeS)) { reset(); return; }
      if (paused) return;
      const dt = previousTime === null ? 0 : timeS - previousTime;
      if (dt === 0 && previousTime !== null) return;
      const continuous = dt > 0 && dt <= .25;
      if (!continuous) history.clear();
      if (dt < 0) lastPass = -Infinity;
      previousTime = timeS;
      const seen = new Set();
      let candidate = null;
      for (const pr of projectiles || []) {
        if (seen.size >= LIMIT) break;
        if (pr?.id == null || !finite3(pr.p)) continue;
        seen.add(pr.id);
        const rel = pr.p.map((n, k) => n - listener.p[k]);
        let h = history.get(pr.id);
        if (!h || h.kind !== pr.kind || pr.t < h.age) h = { fired: false, kind: pr.kind };
        if (continuous && h.rel && !h.fired) {
          const move = rel.map((n, k) => n - h.rel[k]), length2 = dot(move, move);
          const speed = Math.sqrt(length2) / dt;
          // A teleport or rescue must never manufacture an ear-level projectile pass.
          if (speed >= MIN_SPEED && speed <= 200 && length2 > 0) {
            const u = -dot(h.rel, move) / length2;
            if (u > 0 && u <= 1) {
              const close = h.rel.map((n, k) => n + move[k] * u), distanceM = Math.sqrt(dot(close, close));
              if (distanceM <= RANGE) {
                h.fired = true;
                const pan = clamp(dot(close, listener.right) / Math.max(2, distanceM), -.95, .95);
                const volume = .62 * (1 - distanceM / RANGE) ** 2 * clamp(speed / 35, .35, 1);
                const row = { projectileId: pr.id, kind: pr.kind, distanceM, speedMps: speed, pan, volume,
                  p: pr.p.map((n, k) => h.p[k] + (n - h.p[k]) * u),
                  v: pr.p.map((n, k) => (n - h.p[k]) / dt),
                  panFrom: clamp(dot(h.rel, listener.right) / Math.max(2, Math.sqrt(dot(h.rel, h.rel))), -.95, .95),
                  panTo: clamp(dot(rel, listener.right) / Math.max(2, Math.sqrt(dot(rel, rel))), -.95, .95) };
                if (!candidate || volume > candidate.volume) candidate = row;
              }
            }
          }
        }
        h.rel = rel; h.p = [...pr.p]; h.age = pr.t; history.set(pr.id, h);
      }
      for (const id of history.keys()) if (!seen.has(id)) history.delete(id);
      // At most three short pass figures overlap, even with a dense volley.
      if (candidate?.volume > .012 && timeS - lastPass >= .16) { lastPass = timeS; emit(candidate); }
    },
  };
}

const one = (id, vol = 1, rate = 1, at = 0) => ({ id, vol, rate, at });
/** Distinct releases from the shipped bank, all on the existing SFX volume bus. */
export function toyLaunchPlan(id) {
  switch (id) {
    case 'pendulum': return [one('fire-throw', 1, .78), one('land-heavy', .42, .68, .015), one('roll-tick', .75, .72, .045)];
    case 'disc': return [one('fire-throw', 1, 1.27), one('land-light', .36, .85, .012), one('pad', .4, 1.6, .035)];
    case 'tether': return [one('fire-throw', .95, .82), one('land-heavy', .38, .8, .015), one('drift-tick-1', .65, .68, .045)];
    case 'bigSpiral': return [one('fire-lob', 1, .68), one('land-heavy', .48, .6, .02), one('boost', .3, .65, .055)];
    case 'melt': return [one('fire-drop', .9, .72), one('bubble-pop', .35, .65, .045)];
    case 'snap': return [one('bubble-pop', .7, 1.55), one('pad', .45, 1.35, .035)];
    case 'surge': return [one('boost', 1, .92), one('drift-release', .3, 1.1, .08)];
    default: return null;
  }
}

const VOICES = {
  pendulum: [12, 7, 900, 2300], disc: [31, 19, 3500, 1100], tether: [2, -5, 1700, 650],
  bigSpiral: [-12, -24, 1500, 280], melt: [-5, -17, 700, 260], snap: [36, 24, 5200, 1900], surge: [7, 19, 450, 3400],
};
/** Bank-unavailable releases retain each toy's timbre rather than one generic throw. */
export function playToyLaunch(syn, id) {
  const v = VOICES[id]; if (!v) return false;
  syn.noise({ dur: .2, vol: .095, freq: v[2], sweep: v[3], q: .9 });
  syn.tone(syn.semis(v[0]), { type: id === 'tether' ? 'triangle' : 'sine', dur: .25, vol: .06, glide: syn.semis(v[1]), attack: .006 });
  if (['pendulum', 'disc', 'tether', 'bigSpiral'].includes(id)) {
    syn.tone(syn.semis(id === 'disc' ? -12 : -24), { type: 'sine', dur: .13, vol: .055, glide: syn.semis(-31), attack: .006, at: .012 });
  }
  return true;
}

/** A short Doppler-like air sweep and the toy's metallic/body note. No speech or new context. */
export function playItemPass(syn, p) {
  const v = VOICES[p.kind] || VOICES.pendulum, pan = syn.pan;
  syn.pan = p.panFrom;
  syn.noise({ dur: .19, vol: .13, freq: v[2] * 1.2, sweep: v[3], q: .65 });
  syn.tone(syn.semis(v[0]), { type: 'sine', dur: .22, vol: .045, glide: syn.semis(v[0] - 7), attack: .018 });
  syn.pan = p.panTo;
  syn.noise({ dur: .21, vol: .065, freq: v[3], sweep: Math.max(180, v[3] * .55), q: .55, at: .065 });
  syn.pan = pan;
}
