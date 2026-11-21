// One museum narrator uses the PA, pedestal and planter speakers.
export function createSpeechSequence({start, suspend, resume, release}) {
  const queue = [];
  let current = null, paused = false, disposed = false;

  function advance() {
    if (paused || disposed || current || !queue.length) return;
    const urgent = queue.findIndex(entry => entry.priority === 'planter');
    current = queue.splice(Math.max(0,urgent),1)[0];
    start(current);
  }

  return {
    get current() { return current; },
    get queue() { return queue; },
    get available() { return !disposed && !paused && !current && !queue.length; },
    submit(entry) {
      if (disposed) { release(entry, false); return; }
      queue.push(entry);
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
      if (on) {
        if (current) suspend(current);
      } else {
        if (current) resume(current);
        advance();
      }
    },
    dispose() {
      disposed = true;
      for (const entry of [current, ...queue]) if (entry) release(entry, false);
      current = null;
      queue.length = 0;
    }
  };
}
