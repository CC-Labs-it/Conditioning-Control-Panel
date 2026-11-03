/* Source preferences live separately from playback. Restoring a save does not reveal locked sounds. */
export function createMixer() {
  const labels = new Map(), prefs = new Map(), listeners = new Set();
  const valid = (id) => typeof id === 'string' && id.length > 0 && id.length <= 128;
  const read = (id) => prefs.get(id) || { volume: 1, muted: false };
  const sources = () => [...labels].map(([id, label]) => ({ id, label, ...read(id) }));
  const emit = () => { for (const fn of [...listeners]) fn(sources()); };
  function change(id, patch) {
    if (!valid(id)) return;
    const before = read(id), next = { ...before, ...patch };
    if (before.volume === next.volume && before.muted === next.muted) return;
    prefs.set(id, next);
    emit();
  }
  return {
    sources,
    register(id, label) {
      if (!valid(id)) return;
      const next = typeof label === 'string' && label.trim() ? label.trim().slice(0, 120) : labels.get(id) || id;
      if (labels.get(id) === next) return;
      labels.set(id, next);
      emit();
    },
    setVolume(id, volume) { if (Number.isFinite(volume)) change(id, { volume: Math.max(0, Math.min(1, volume)) }); },
    setMuted(id, muted) { change(id, { muted: !!muted }); },
    subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); },
    snapshot() { return Object.fromEntries([...prefs].map(([id, pref]) => [id, { ...pref }])); },
    restore(saved) {
      prefs.clear();
      if (saved && typeof saved === 'object' && !Array.isArray(saved)) {
        for (const [id, pref] of Object.entries(saved)) {
          if (!valid(id) || !pref || typeof pref !== 'object') continue;
          prefs.set(id, { volume: Number.isFinite(pref.volume) ? Math.max(0, Math.min(1, pref.volume)) : 1, muted: pref.muted === true });
        }
      }
      emit();
    },
  };
}
