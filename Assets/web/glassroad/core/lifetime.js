/** Resources belong to one course. Closing it first silences callbacks, then releases resources. */
export function createLifetime() {
  let alive = true;
  const cleanups = [];
  return {
    get alive() { return alive; },
    defer(fn) {
      if (typeof fn !== 'function') return fn;
      if (alive) cleanups.push(fn); else fn();
      return fn;
    },
    listen(target, type, fn, options) {
      if (!alive) return;
      target?.addEventListener(type, fn, options);
      cleanups.push(() => target?.removeEventListener(type, fn, options));
    },
    dispose() {
      if (!alive) return;
      alive = false;
      for (const cleanup of cleanups.reverse()) {
        try { cleanup(); } catch (error) { console.warn('[glassroad/course] cleanup', error); }
      }
      cleanups.length = 0;
    },
  };
}

/** The shared audio hears current events; all old course subscriptions disappear together. */
export function scopedBus(parent, lifetime) {
  const subscriptions = new Map();
  return {
    on(name, fn) {
      if (!lifetime.alive) return () => {};
      const list = subscriptions.get(name) || []; subscriptions.set(name, list);
      const remove = parent.on(name, (...args) => { if (lifetime.alive) fn(...args); });
      let active = true;
      const row = { fn, off() {
        if (!active) return; active = false; remove();
        list.splice(list.indexOf(row), 1); if (!list.length) subscriptions.delete(name);
      } };
      list.push(row); lifetime.defer(row.off); return row.off;
    },
    off(name, fn) { subscriptions.get(name)?.find(row => row.fn === fn)?.off(); },
    emit(name, payload) { if (lifetime.alive) parent.emit(name, payload); },
  };
}

/** Keep the one host transport while retiring a course's handlers. */
export function scopedBridge(parent, lifetime) {
  return {
    ...parent,
    on(type, fn) { const off = parent.on(type, (...args) => { if (lifetime.alive) fn(...args); }); lifetime.defer(off); return off; },
    dispose() {},
  };
}
