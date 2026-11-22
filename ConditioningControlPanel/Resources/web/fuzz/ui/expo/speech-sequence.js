// One physical speaker at a time, with room for one upcoming announcement.
export function createSpeechSequence({start, suspend, resume, release, now = () => performance.now(), nearby = () => true}) {
  let current = null, waiting = null, paused = false, disposed = false;

  function cancel(predicate) {
    if (waiting && predicate(waiting)) {
      const drop = waiting;
      waiting = null;
      release(drop, false);
    }
  }
  function stale(entry) {
    return !nearby(entry) || now() >= entry.expires;
  }
  function advance() {
    cancel(stale);
    if (paused || disposed || current || !waiting) return;
    current = waiting;
    waiting = null;
    start(current);
  }
  function wins(entry) {
    if (waiting.kind === 'beat') return false;
    if (entry.kind === 'beat') return true;
    if (entry.kind === 'react') return waiting.kind === 'ambient' || waiting.exhibit === entry.exhibit;
    return false;
  }
  return {
    get current() { return current; },
    get queue() { return waiting ? [waiting] : []; },
    get available() { return !disposed && !paused && !current && !waiting; },
    cancel,
    submit(entry) {
      entry.kind ??= 'ambient';
      entry.expires = now() + (entry.ttl ?? (entry.kind === 'react' ? 1200 : Infinity));
      cancel(stale);
      const duringBeat = current?.kind === 'beat' && current.zone === entry.zone;
      if (disposed || stale(entry) || (entry.kind === 'ambient' && duringBeat) || (waiting && !wins(entry))) {
        release(entry, false);
        return;
      }
      cancel(() => true);
      waiting = entry;
      advance();
    },
    finish(entry, ok = true) {
      if (current !== entry) return;
      current = null;
      release(entry, ok);
      advance();
    },
    pause(on) {
      if (paused === on || disposed) return;
      paused = on;
      if (current) (on ? suspend : resume)(current);
      if (!on) advance();
    },
    dispose() {
      disposed = true;
      cancel(() => true);
      if (current) release(current, false);
      current = null;
    }
  };
}

// The exhibit commits its next page once the entire instruction has finished.
// Leaving invalidates the continuation even if the audible line is still playing.
export function createExhibitBeat(cue) {
  let active = null;
  return {
    get listening() { return active !== null; },
    leave() { active = null; },
    async run(lines, options, commit) {
      if (active) return false;
      const turn = active = {};
      try {
        for (const id of lines) {
          const heard = await cue(id, {...options, kind:'beat', replay:true});
          if (!heard || active !== turn) return false;
        }
        commit();
        return true;
      } finally {
        if (active === turn) active = null;
      }
    }
  };
}
