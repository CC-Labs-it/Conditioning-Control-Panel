export const CHEER_DURATION = Object.freeze({ nod: 1.5, cheer: 2.4, worry: 2.7, win: 4.2, comfort: 3.2, wave: 3.2, pet: 2.3 });
const PRIORITY = { nod: 1, wave: 1, pet: 2, cheer: 3, worry: 4, comfort: 5, win: 6 };

export function cheerForEvent(kind, payload, side) {
  if (kind === 'land') return payload.refused || payload.capture ? null : payload.side === side ? 'nod' : null;
  if (kind === 'capture') return payload.victimSide === side ? 'worry' : payload.by === side ? 'cheer' : ['w','b'].includes(payload.by) ? 'worry' : null;
  if (kind === 'check') return payload.side === side ? 'scared' : 'cheer';
  if (kind === 'gameover') return !payload.winner ? 'wave' : payload.winner === side ? 'win' : 'comfort';
  return null;
}

export function createCheerState() {
  let time = 0, action = null, started = 0, checked = false;
  const recent = new Map();
  return {
    trigger(kind, key = kind) {
      if (checked || !CHEER_DURATION[kind] || (recent.has(key) && time - recent.get(key) < 1.2)) return false;
      recent.set(key, time);
      if (action && PRIORITY[kind] < PRIORITY[action]) return false;
      action = kind; started = time;
      return true;
    },
    update(dt) {
      time += Math.max(0, Math.min(Number.isFinite(dt) ? dt : 0, .1));
      if (action && time - started >= CHEER_DURATION[action]) action = null;
      for (const [key, at] of recent) if (time - at > 2) recent.delete(key);
      return { time, action: checked ? 'scared' : action, checked, age: time - started };
    },
    setChecked(value) { if (checked !== !!value) { checked = !!value; action = null; recent.clear(); started = time; } },
    reset(clearCheck = false) { action = null; recent.clear(); started = time; if (clearCheck) checked = false; },
  };
}
