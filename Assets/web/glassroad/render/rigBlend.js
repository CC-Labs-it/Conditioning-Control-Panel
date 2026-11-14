const ease = x => { x = Math.max(0, Math.min(1, x)); return x * x * x * (x * (x * 6 - 15) + 10); };
const calm = name => /^(idle_bob|max_speed|lean_left|lean_right)$/.test(name || '');

/** Preserve the complete live blend when another action interrupts its recovery. */
export function createRigBlend(actions) {
  const rows = Object.entries(actions).map(([name, action]) => ({ name, action, from: 0, to: 0 }));
  let elapsed = 0, duration = 0, current = null;
  for (const row of rows) row.action.setEffectiveWeight(0);
  function change(ch) {
    const next = ch.clip ? actions[ch.clip] : null;
    duration = Math.max(ch.fade || 0, calm(ch.clip) || !next ? .24 : .001); elapsed = 0;
    for (const row of rows) {
      row.from = row.action.isScheduled() ? row.action.getEffectiveWeight() : 0;
      row.to = row.action === next ? 1 : 0;
      row.action.stopFading();
      if (ch.land && row.action !== next && row.from > 0) row.action.timeScale = 0;
    }
    if (next) {
      const keepPhase = /^(idle_bob|max_speed)$/.test(ch.clip) && next.isScheduled();
      if (!keepPhase) { next.reset(); next.time = ch.from || 0; }
      next.enabled = true; next.paused = false;
      next.timeScale = /^(idle_bob|max_speed)$/.test(ch.clip) ? .82 : 1;
      next.play();
      next.setEffectiveWeight(rows.find(row => row.action === next).from);
    }
    current = next;
  }
  function update(dt) {
    elapsed += Math.max(0, dt); const k = duration ? ease(elapsed / duration) : 1;
    for (const row of rows) {
      row.action.setEffectiveWeight(row.from + (row.to - row.from) * k);
      if (k === 1 && !row.to && row.action !== current && !/^(idle_bob|max_speed)$/.test(row.name)) row.action.stop();
    }
  }
  function reset() {
    for (const row of rows) { row.action.stop(); row.action.setEffectiveWeight(0); row.from = row.to = 0; }
    current = null; elapsed = duration = 0;
  }
  return { change, update, reset };
}
