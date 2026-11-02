import {createLifetime} from './lifetime.js';

/** Serialize GPU work on the retained renderer. The latest pending selection wins. */
export function createCourseController({open, cover = () => {}, fail = () => {}}) {
  let revision = 0, current = null, chain = Promise.resolve(), dead = false, busy = false;
  return {
    select(id, options = {}) {
      if (dead || (!busy && current?.id === id && !options.force)) return Promise.resolve(false);
      const mine = ++revision;
      busy = true;
      cover();
      chain = chain.catch(() => {}).then(async () => {
        if (dead || mine !== revision) return false;
        current?.lifetime.dispose();
        const lifetime = createLifetime();
        current = {id, lifetime};
        const isCurrent = () => !dead && lifetime.alive && mine === revision;
        try {
          await open(id, options, lifetime, isCurrent);
          return isCurrent();
        } catch (error) {
          lifetime.dispose();
          if (current?.lifetime === lifetime) current = null;
          if (mine === revision && !dead) fail(error);
          return false;
        } finally { if (mine === revision) busy = false; }
      });
      return chain;
    },
    get active() { return current?.id || null; },
    get busy() { return busy; },
    dispose() { if (dead) return; dead = true; revision++; busy = false; current?.lifetime.dispose(); current = null; },
  };
}
