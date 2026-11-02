/* ============================================================================
 * main.js - boot: bridge -> init (or unhosted at once) -> save -> age card -> engine -> unlocks -> the loop. Also the
 * debug door: ?pace=N ?buy=id,id ?mood=m_x ?fresh=1 and window.fuzz (ENGINE.md).
 * ==========================================================================*/

import * as bridge from './bridge.js';
import { createHostEscape } from './host-escape.js';
import { showSlip } from './sparkles-slip.js';
import { BRAND, brandText } from './data/brand.js';
import { BY_ID, ITEMS, FORKS } from './data/items.js';
import { CONFIG } from './data/config.js';
import { createKit } from './audio/kit.js';
import * as E from './sim/economy.js';
import { MOODS, rollMood } from './sim/moods.js';
import { createLife, forget, load, parse, serialize, store, SAVE_KEY } from './sim/save.js';
import { createUi } from './ui/app.js';
import { createEngine } from './ui/engine.js';
import { REGISTRY } from './ui/unlocks/index.js';
import { loadAds } from './ui/ads.js';
import { runIntro } from './ui/intro.js';

const qs = new URLSearchParams(location.search);
const paceArg = Number(qs.get('pace')) || 0;
const buyArg = (qs.get('buy') || '').split(',').map((s) => s.trim()).filter(Boolean);
const moodArg = qs.get('mood');
const fresh = qs.has('fresh') || buyArg.length > 0;
const devDoor = fresh || paceArg > 0 || !!moodArg;

/** Fresh mode keeps nothing: a dev jump never touches the real save. */
function memoryStorage() {
  const m = new Map();
  return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), removeItem: (k) => m.delete(k) };
}
let storage;
try { storage = fresh ? memoryStorage() : window.localStorage; } catch { storage = memoryStorage(); }

bridge.boot({ onInit: start });

function start(init) {
  document.title = BRAND.game;
  const saved = load(storage);
  const life = saved?.life || createLife();
  const extras = saved?.extras || {};
  const mood = MOODS[moodArg] ? moodArg : rollMood(life.runs);
  const run = saved?.run || E.createRun({ pace: paceArg || 1, mood, seed: Math.floor(Math.random() * 2 ** 32), perk: life.perk });
  if (paceArg) run.pace = Math.min(CONFIG.MAX_PACE, paceArg);
  if (moodArg && MOODS[moodArg]) { run.mood = moodArg; E.recalc(run); }

  const audio = createKit();
  const ui = createUi({ init, audio, extras });
  let engine = null;
  let leaving = false;   // set before a reload that replaces the save, so pagehide cannot write the old run back
  const persist = () => {
    if (leaving) return;
    if (engine) extras.pictures = engine.pictures.source;
    store(storage, run, life, extras);
  };

  engine = createEngine({
    run, life, ui, audio, registry: REGISTRY, save: persist,
    host: { send: bridge.send, isHosted: bridge.isHosted },
    env: { caps: init.caps || {}, mod: init.mod || 'default', hosted: bridge.isHosted, pictureSource: qs.get('pictures') || extras.pictures },
  });

  const newRun = () => { leaving = true; store(storage, null, life, extras); location.reload(); };
  const hooks = {
    newRun,
    forget() { leaving = true; forget(storage); location.reload(); },
    exportSave: () => serialize(run, life, extras),
    importSave(text) {
      if (!parse(text)) return false;
      try { storage.setItem(SAVE_KEY, text); } catch { return false; }
      leaving = true;
      location.reload();
      return true;
    },
    leave: bridge.isHosted ? () => bridge.send('exit', { reason: 'back' }) : null,
    routeNames: (r) => Object.keys(FORKS).filter((f) => r.picks[f]).map((f) => brandText(FORKS[f].opts[r.picks[f]].name)).join(', '),
  };
  ui.attach(engine, hooks);

  /* Host wire. */
  const escape = createHostEscape();
  bridge.on('kept-escape', () => escape.kept());
  bridge.on('suspend', (f) => {
    if (f.on) { ui.pause(); engine.setSuspended(true, f.reason); }
    else engine.setSuspended(false, f.reason);
  });
  bridge.on('close', () => { audio.hold(true); persist(); bridge.send('exit-done'); });
  bridge.on('setting', (f) => { if (f.key === 'gentle') ui.setInit({ gentle: !!f.value }); });
  bridge.on('sparkles-receipt', showSlip);
  window.addEventListener('pagehide', persist);
  document.addEventListener('visibilitychange', () => { if (document.hidden) persist(); });

  /* The debug door. */
  for (const id of buyArg) {
    if (!BY_ID[id]) console.warn(`?buy: no item ${id}`);
    else if (!E.grant(run, id).length && !E.has(run, id)) console.warn(`?buy: ${id} is on a lane this route locked`);
  }
  window.fuzz = {
    engine, ui, life, items: ITEMS, forks: FORKS,
    get state() { return run; },
    buy: (id) => engine.buy(id),
    pick: (fork, lane) => engine.choose(fork, lane),
    add(n) { E.lump(run, n, 'dev'); ui.draw(true); },
    grant: (id) => engine.grant(id),
    tap(n = 1) { for (let i = 0; i < n; i++) engine.tap({}); },
    end(id = 'drop') { E.grant(run, id); run.ended = { id, at: run.clock }; engine.end(id); },
    newRun,
  };

  const go = () => {
    if (run.ended) { engine.end(run.ended.id, { restored: true }); ui.start(); return; }
    engine.mountAll();
    engine.syncQuiet();
    if (run.mood && run.clock < 500) {
      const m = BY_ID[run.mood];
      ui.cards.open({ title: 'Today', body: `${m.name}. ${m.what}`, buttons: [{ label: 'Fine' }] });
    }
    ui.draw(true);
    ui.start();
    loadAds();
    persist();
    bridge.log('info', `booted pace=${run.pace} hosted=${bridge.isHosted}`);
  };

  runIntro({
    cards: ui.cards,
    life,
    skip: devDoor,
    leave() { if (bridge.isHosted) bridge.send('exit', { reason: 'back' }); else location.href = 'about:blank'; },
  }).then(go);
}
