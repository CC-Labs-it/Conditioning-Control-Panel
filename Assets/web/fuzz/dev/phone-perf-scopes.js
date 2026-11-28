(() => {
  const acc = window.__scopeAcc = {};
  const wrap = (id, fn, kind) => function (...a) { const t = performance.now(); try { return fn.apply(this, a); } finally { const k = id + ':' + kind; acc[k] = (acc[k] || 0) + performance.now() - t; } };
  for (const [id, s] of window.fuzz.engine.scopes) {
    const fr = [...s.frames]; s.frames.clear(); fr.forEach((f, i) => s.frames.add(wrap(id, f, 'frame' + i)));
    for (const t of s.timers) t.fn = wrap(id, t.fn, 'timer');
    if (s.ret?.tick) s.ret.tick = wrap(id, s.ret.tick, 'tick');
  }
})();
