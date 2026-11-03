/* Short cues share a small voice budget. A delayed download is never a queue of old rewards. */
export function cuePolicy(url) {
  const file = String(url).split('/').at(-1).toLowerCase();
  if (/^pop\d*\.mp3$/.test(file)) return { group: 'bubble-pop', gap: 300, scale: .7 };
  if (/^(ui_click|tally_tick|count_tick)\./.test(file)) return { group: file, gap: 100, scale: .75 };
  if (/^wood_\d+\.wav$/.test(file)) return { group: 'pluck', gap: 85, scale: 1 };
  return { group: file, gap: 180, scale: 1 };
}

export function createCueBudget() {
  const requested = new Map(), started = new Map(), playing = new Set();
  let epoch = 0;
  return {
    request(url, now) {
      const policy = cuePolicy(url);
      if (now - (requested.get(policy.group) ?? -Infinity) < policy.gap) return null;
      requested.set(policy.group, now);
      return { ...policy, url, at: now, epoch };
    },
    start(ticket, now) {
      if (!ticket || ticket.epoch !== epoch || now - ticket.at > 250 || playing.size >= 4 || playing.has(ticket.url)) return null;
      if (now - (started.get(ticket.group) ?? -Infinity) < ticket.gap) return null;
      started.set(ticket.group, now);
      playing.add(ticket.url);
      let released = false;
      return () => { if (!released) { released = true; if (ticket.epoch === epoch) playing.delete(ticket.url); } };
    },
    reset() { epoch++; requested.clear(); started.clear(); playing.clear(); },
    get live() { return playing.size; },
  };
}
