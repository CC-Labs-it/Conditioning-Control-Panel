// One museum narrator uses the PA, pedestal and planter speakers.
export function createSpeechSequence({start, suspend, resume, release}) {
  const queue = [];
  let current = null, interrupted = null, paused = false, disposed = false;

  function advance() {
    if (paused || disposed) return;
    const urgent = queue.findIndex(entry => entry.priority === 'planter');
    if (current) {
      if (urgent < 0 || current.priority === 'planter') return;
      interrupted = current;
      current = null;
      suspend(interrupted);
    }
    if (urgent >= 0) {
      current = queue.splice(urgent, 1)[0];
      start(current);
    } else if (interrupted) {
      current = interrupted;
      interrupted = null;
      resume(current);
    } else if (queue.length) {
      current = queue.shift();
      start(current);
    }
  }

  return {
    get current() { return current; },
    get interrupted() { return interrupted; },
    get queue() { return queue; },
    get available() { return !disposed && !paused && !current && !queue.length; },
    submit(entry) {
      if (disposed) { release(entry, false); return; }
      queue.push(entry);
      advance();
    },
    finish(entry, ok = true) {
      // A browser can deliver an ended event immediately after another speaker interrupted it.
      if (interrupted === entry) {
        interrupted = null;
        release(entry, ok);
        return;
      }
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
        if (current && (current.priority === 'planter' || !queue.some(entry => entry.priority === 'planter'))) resume(current);
        advance();
      }
    },
    dispose() {
      disposed = true;
      for (const entry of [current, interrupted, ...queue]) if (entry) release(entry, false);
      current = interrupted = null;
      queue.length = 0;
    }
  };
}
