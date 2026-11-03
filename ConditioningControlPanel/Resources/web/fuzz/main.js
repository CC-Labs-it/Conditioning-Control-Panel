/* ============================================================================
 * main.js - boot: bridge -> init (or unhosted at once) -> save -> contract -> engine -> unlocks -> the loop. Also the
 * debug door: ?pace=N ?buy=id,id ?mood=m_x ?fresh=1 ?pictures=x and window.fuzz (ENGINE.md).
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
import { bindPalette } from './ui/palette.js';
import { REGISTRY } from './ui/unlocks/index.js';
import { openIntro } from './ui/intro.js';
import { openPictureCard, pictureCardDue, PICTURE_ROUTES } from './ui/picture-card.js';

const qs = new URLSearchParams(location.search);
const toyPreview = qs.get('preview') === 'toys';
const paceArg = Number(qs.get('pace')) || 0;
const buyArg = toyPreview
  ? ['colour', 'font', 'sound', 'rounded', 'softw', 'a_rain', 'v_frame', 'quiz', 'toy_box', 'mini_breakout', 'mini_race', 'mini_slots', 'dispenser_faster']
  : (qs.get('buy') || '').split(',').map((s) => s.trim()).filter(Boolean);
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
  /* New run reloads the page: that is not a visit, so it does not ask again. One shot, set only by newRun below. */
  const newRunReload = !!extras.again && life.contract;
  delete extras.again;
  const pictureDoor = qs.get('pictures') || (toyPreview ? 'online' : null);
  /* A saved 'online' is the old placeholder default. Only the dev door reaches it now. */
  const savedPictures = extras.pictures === 'online' ? 'none' : extras.pictures;
  const mood = MOODS[moodArg] ? moodArg : rollMood(life.runs);
  const run = saved?.run || E.createRun({ pace: paceArg || 1, mood, seed: Math.floor(Math.random() * 2 ** 32), perk: life.perk });
  if (paceArg) run.pace = Math.min(CONFIG.MAX_PACE, paceArg);
  if (moodArg && MOODS[moodArg]) { run.mood = moodArg; E.recalc(run); }

  const audio = createKit();
  const ui = createUi({ init, audio, extras });
  let engine = null;
  let leaving = false;   // set before a reload that replaces the save, so pagehide cannot write the old run back
  /* Nothing is written until the window is accepted: a declined player leaves no save, and a returning one keeps theirs as it was. */
  let accepted = devDoor || newRunReload;
  const persist = () => {
    if (leaving || !accepted) return;
    if (engine && !pictureDoor) extras.pictures = engine.pictures.source;
    store(storage, run, life, extras);
  };

  engine = createEngine({
    run, life, ui, audio, registry: REGISTRY, save: persist,
    host: { send: bridge.send, isHosted: bridge.isHosted },
    env: { caps: init.caps || {}, mod: init.mod || 'default', hosted: bridge.isHosted, pictureSource: pictureDoor || savedPictures },
  });

  /* ---- the pictures card ---- */
  let pictureCard = null;
  const laneName = (lane) => (lane ? brandText(FORKS.f3.opts[lane].name) : null);
  function askPictures({ lane = null, lost = false, above = false, onClose = () => {} }) {
    if (pictureCard?.open) return;
    pictureCard = openPictureCard({
      pictures: engine.pictures,
      lane: laneName(lane),
      lost,
      above,
      gentle: ui.gentle,
      onChoice() { extras.picturesAsked = 1; persist(); },
      onClose,
    });
  }
  engine.subscribe('fork', ({ fork, lane }) => {
    if (fork !== 'f3' || !PICTURE_ROUTES.includes(lane) || pictureDoor) return;
    if (pictureCardDue(engine.pictures, extras.picturesAsked)) askPictures({ lane });
  });

  const newRun = () => { leaving = true; extras.again = 1; store(storage, null, life, extras); location.reload(); };
  const hooks = {
    newRun,
    changePictures(redraw) {
      if (pictureCard?.open) { ui.resume(); return; }   // the route's own card is already waiting under the pause card
      askPictures({ above: true, onClose: redraw });
    },
    escapeTaken() {
      if (!pictureCard?.open || !pictureCard.above) return false;
      pictureCard.close();
      return true;
    },
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
  let intro = null;
  bridge.on('kept-escape', () => escape.kept());
  bridge.on('suspend', (f) => {
    if (f.on) {
      if (pictureCard?.open && pictureCard.above) pictureCard.close();
      ui.pause(true);
      engine.setSuspended(true, f.reason);
    } else engine.setSuspended(false, f.reason);
    intro?.suspend(f.on);
  });
  bridge.on('close', () => { intro?.close(); audio.hold(true); persist(); bridge.send('exit-done'); });
  bridge.on('setting', (f) => { if (f.key === 'gentle') ui.setInit({ gentle: !!f.value }); });
  bridge.on('sparkles-receipt', showSlip);
  window.addEventListener('pagehide', persist);
  document.addEventListener('visibilitychange', () => { if (document.hidden) persist(); });

  /* The debug door. */
  if (toyPreview) {
    const theme = qs.get('theme');
    run.picks.f1 = 'ears';
    run.picks.f3 = 'slides';
    run.picks.f5 = FORKS.f5.opts[theme] ? theme : 'trance';
    run.store.a_phones = { on: true };
    run.store.mini_slots = { machine: { coins: 3, last: [1, 3, 0] } };
    run.store.mini_race = { race: { leftMs: 0 } };
    E.lump(run, 100000, 'dev');
  }
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
    bindPalette(engine, document);
    if (run.ended) { engine.end(run.ended.id, { restored: true }); ui.start(); return; }
    engine.mountAll(!!saved?.run);
    engine.syncQuiet();
    ui.drawPhrase();
    if (run.mood && run.clock < 500) {
      const m = BY_ID[run.mood];
      ui.cards.open({ title: 'Today', body: `${m.name}. ${m.what}`, buttons: [{ label: 'Fine' }] });
    }
    /* Own files are gone after a reload: offer the picker again rather than pretend. The frames stay empty meanwhile. */
    const lane = run.picks.f3;
    if (PICTURE_ROUTES.includes(lane) && engine.pictures.source === 'own' && engine.pictures.ownCount === 0) askPictures({ lane, lost: true });
    ui.draw(true);
    ui.start();
    persist();
    bridge.log('info', `booted pace=${run.pace} hosted=${bridge.isHosted}`);
  };

  intro = openIntro({
    life,
    skip: devDoor || newRunReload,
    hosted: bridge.isHosted,
    leave: hooks.leave,
    gentle: ui.gentle,
    base: run.fuzz,
    showCount: ui.showCount,
  });
  if (intro.wake) ui.setWakeGate(intro.wake);
  intro.done.then(({ points }) => {
    ui.setWakeGate(null);
    accepted = true;
    intro = null;
    if (points) {
      /* The hidden things carry into the run as a starting lump, and fly to the counter. */
      E.lump(run, points, 'intro');
      ui.paid(points, { x: window.innerWidth / 2, y: window.innerHeight * 0.7 });
    }
    go();
  });
}
