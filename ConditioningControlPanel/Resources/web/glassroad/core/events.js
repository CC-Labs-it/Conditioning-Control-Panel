/* core/events.js - a tiny synchronous event bus, plus a queue the sim emits into.
 *
 * The sim never calls fx or audio directly: during the fixed steps it emits into a
 * queue, and boot.js flushes the queue into the bus once per frame (after the steps,
 * before fx). Handlers run in subscription order; a throwing handler is logged and
 * the rest still run.
 */

/** @returns {{on(name: string, fn: (payload: object, name: string) => void): () => void,
 *            off(name: string, fn: Function): void, emit(name: string, payload?: object): void}} */
export function createBus() {
  const map = new Map();
  function on(name, fn) {
    let list = map.get(name);
    if (!list) map.set(name, (list = []));
    list.push(fn);
    return () => off(name, fn);
  }
  function off(name, fn) {
    const list = map.get(name);
    if (!list) return;
    const i = list.indexOf(fn);
    if (i >= 0) list.splice(i, 1);
  }
  function emit(name, payload = {}) {
    const list = map.get(name) || [];
    for (const fn of list.slice()) {
      try { fn(payload, name); } catch (e) { console.warn('[events]', name, e); }
    }
    const any = map.get('*');
    if (any && name !== '*') for (const fn of any.slice()) {
      try { fn(payload, name); } catch (e) { console.warn('[events] *', e); }
    }
  }
  return { on, off, emit };
}

/** A sink with the same `emit` as the bus that holds events until `flush(bus)`.
 * @returns {{emit(name: string, payload?: object): void, flush(bus: {emit: Function}): number,
 *            clear(): void, readonly length: number}} */
export function createQueue() {
  let items = [];
  return {
    emit(name, payload = {}) { items.push(name, payload); },
    flush(bus) {
      const list = items;
      items = [];
      for (let i = 0; i < list.length; i += 2) bus.emit(list[i], list[i + 1]);
      return list.length / 2;
    },
    clear() { items = []; },
    get length() { return items.length / 2; },
  };
}
