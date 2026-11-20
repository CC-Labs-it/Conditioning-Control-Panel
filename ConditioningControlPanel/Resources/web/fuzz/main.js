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
import { CHAIN as EMI_UPGRADES } from './sim/takeover.js';  // featured after the pick only: go_tx is a fate seed and sits level with the others
import { MOODS, rollMood } from './sim/moods.js';
import { createLife, forget, load, parse, serialize, store, JOURNEY_PREVIEW, PLAYER_PREVIEW, SAVE_KEY } from './sim/save.js';
import { createUi } from './ui/app.js';
import { isKineticWave } from './ui/kinetic-waves.js';
import { createEngine } from './ui/engine.js';
import { bindPalette } from './ui/palette.js';
import { REGISTRY } from './ui/unlocks/index.js';
import { openIntro } from './ui/intro.js';
import { openPictureCard } from './ui/picture-card.js';
import { readPreviewPreferences, writePreviewPreferences, PREVIEW_PREFS_KEY } from './ui/preferences.js';
import { createTips, TIPS_KEY, PREVIEW_TIPS_KEY } from './ui/tips.js';
import { loadPrestige, prestigeNow, openRenewal, openSky, SKY_CURRENCY } from './ui/prestige-sky.js';
import { applyLooks } from './sim/prestige-looks.js';
import { canKeepPlaying as keepPlayingOk, keepPlaying as keepPlayingRun } from './sim/prestige-endings.js';   // Endless (onemore)
import { sealedFor, showSeal } from './ui/seal.js';

const qs = new URLSearchParams(location.search);
/* Dev doors (jumps, granted items, cheat wallets, the age-gate skip they carry) open only on a local preview.
   On any public host the params are ignored, so a shared link can never skip the contract. */
if (!['127.0.0.1', 'localhost'].includes(location.hostname)) {
  for (const key of ['fresh', 'buy', 'pace', 'mood', 'theme', 'focus', 'waveStudy', 'shop', 'wallet', 'equip', 'style', 'icons', 'sky']) qs.delete(key);
  if (['combined', 'toys', 'waves'].includes(qs.get('preview'))) qs.delete('preview');
}
const combinedPreview = qs.get('preview') === 'combined';
if (combinedPreview || PLAYER_PREVIEW) document.documentElement.dataset.combinedPreview = '';
if (combinedPreview) {
  const defaults = {waveStudy:'harp', theme:'bimbo', icons:'plain', shop:'trails', equip:'comet', wallet:'unlimited', style:'electric'};
  for (const [key,value] of Object.entries(defaults)) if (!qs.has(key)) qs.set(key,value);
}
const toyPreview = combinedPreview || qs.get('preview') === 'toys';
const buttonPreview = toyPreview && qs.get('focus') === 'buttons';
const feedPreview = toyPreview && qs.get('focus') === 'feed';
const waveOnly = qs.get('preview') === 'waves';
const requestedStudy = qs.get('waveStudy');
// Keep links from the earlier study round playable with their replacements.
const priorStudies = new Map([['cinder','chroma'],['infinity','chroma'],['comets','chroma'],['echo','pyramid'],['octave','pyramid'],['plumes','pyramid']]);
const study = priorStudies.get(requestedStudy) ?? requestedStudy;
const waveStudy = (toyPreview || waveOnly) && isKineticWave(study) ? study : waveOnly ? 'harp' : null;
const equipWave = combinedPreview || buttonPreview || qs.get('focus') === 'wave' || Boolean(waveStudy);
const equipHeat = combinedPreview || ['score','race'].includes(qs.get('focus'));
const trailPreview = toyPreview && qs.get('shop') === 'trails';
const refillWallet = trailPreview && qs.get('wallet') === 'unlimited';
const previewFunds = 10000000;
const paceArg = Number(qs.get('pace')) || 0;
const buyArg = buttonPreview ? ['colour','bubble','stray','e_bounce','tr_skin'] : toyPreview
  ? ['colour', 'font', 'sound', 'rounded', 'softw', 'a_rain', feedPreview ? 'd_scroll' : 'v_frame', ...(equipWave ? ['bubble'] : []), 'wheel', 'quiz', 'toy_box', ...(equipHeat ? ['score_heat'] : []), 'mini_breakout', 'mini_race', 'mini_slots', 'dispenser_faster', ...(trailPreview ? [qs.get('equip') === 'comet' ? 'word_comet' : 'trail', 'taste2'] : [])]
  : waveOnly ? ['bubble', 'a_hum'] : (qs.get('buy') || '').split(',').map((s) => s.trim()).filter(Boolean);
const moodArg = qs.get('mood');
/* ?sky=demo (local only): opens the Renewal Sky three renewals in, to look at it. Saves nothing. */
const skyDemo = qs.get('sky') === 'demo';
const fresh = qs.has('fresh') || buyArg.length > 0 || skyDemo;
const devDoor = fresh || paceArg > 0 || !!moodArg;

/** Fresh mode keeps nothing: a dev jump never touches the real save. */
function memoryStorage() {
  const m = new Map();
  return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), removeItem: (k) => m.delete(k) };
}
let storage;
try { storage = fresh ? memoryStorage() : window.localStorage; } catch { storage = memoryStorage(); }
let preferenceStorage;
try { preferenceStorage = window.localStorage; } catch { preferenceStorage = memoryStorage(); }

/* Prime only until the anniversary (ui/seal.js): no Prime host, no game, just the notice. ?seal=1 shows it on a local preview. */
bridge.boot({ onInit: (init) => (sealedFor(init, Date.now(), location.hostname, qs.get('seal') === '1') ? showSeal(document) : start(init)) });

function start(init) {
  document.title = BRAND.game;
  const saved = load(storage);
  const life = saved?.life || createLife();
  const extras = toyPreview ? readPreviewPreferences(preferenceStorage) : saved?.extras || {};
  /* New run reloads the page: that is not a visit, so it does not ask again. One shot, set only by newRun below. */
  const newRunReload = !!extras.again && life.contract;
  delete extras.again;
  // Explicit local fixtures keep the standalone development drives repeatable.
  // A saved preference or a shared Scrolller link never opts this visit in.
  const pictureDoor = devDoor && (!combinedPreview || qs.get('pictures') === 'none')
    && qs.get('pictures') !== 'scrolller' ? qs.get('pictures') || (toyPreview ? 'online' : 'none') : null;
  const mood = MOODS[moodArg] ? moodArg : rollMood(life.runs);
  const run = saved?.run || E.createRun({ pace: paceArg || 1, mood, seed: Math.floor(Math.random() * 2 ** 32), perk: life.perk });
  /* A saved run's pace already carries its Renewal Sky pay (sim/save.js); a dev ?pace keeps it on a reload. */
  if (paceArg) run.pace = Math.min(CONFIG.MAX_PACE, paceArg) * (saved?.run && run.prestige?.pay > 0 ? run.prestige.pay : 1);
  /* PRESTIGE LOOKS HOOK (sim/prestige-looks.js): looks bought in the Sky after the core are owned from the first second of
     every run. Writes ownership flags only (never equips), idempotent, so it runs on every boot, fresh or saved. */
  if (life.prestige) applyLooks(run, life.prestige);
  /* A Sky visit still open when the page went away (a reload or a closed tab between Renew and Sign and start): the
     renewal is already applied and saved, so the Sky reopens as it was instead of a run starting under it. */
  const skyVisit = !saved?.run && life.prestige && extras.sky && typeof extras.sky === 'object' ? extras.sky : null;
  if (moodArg && MOODS[moodArg]) { run.mood = moodArg; E.recalc(run); }

  const audio = createKit();
  const ui = createUi({ init, audio, extras });
  let engine = null;
  let leaving = false;   // set before a reload that replaces the save, so pagehide cannot write the old run back
  /* Nothing is written until the window is accepted: a declined player leaves no save, and a returning one keeps theirs as it was. */
  let accepted = devDoor || newRunReload;
  if (skyVisit) leaving = true;   // the run under a reopened Sky is a stand-in: it is never saved
  const persist = () => {
    if (leaving || !accepted) return;
    if (refillWallet && run.fuzz < previewFunds) E.lump(run, previewFunds-run.fuzz, 'dev');
    if (engine && !pictureDoor) extras.pictures = engine.pictures.source;
    store(storage, run, life, extras);
    if (toyPreview) {
      const {choice,bouncer,emiPose} = run.store.bubble || {};
      if (choice || bouncer || emiPose) extras.buttonWardrobe = {choice,bouncer,emiPose};
      writePreviewPreferences(preferenceStorage,extras);
    }
  };

  engine = createEngine({
    run, life, ui, audio, registry: REGISTRY, save: persist,
    host: { send: bridge.send, isHosted: bridge.isHosted },
    env: { journey: JOURNEY_PREVIEW, featuredShop: [...EMI_UPGRADES.slice(1), ...(feedPreview ? ['d_autoscroll'] : []), ...(trailPreview ? ['starburst', 'word_comet'] : [])], caps: init.caps || {}, identity: typeof init.identity?.displayName === 'string' && init.identity.displayName.trim() ? { displayName: init.identity.displayName.trim().slice(0, 40) } : null, mod: init.mod || 'default', hosted: bridge.isHosted, pictureSource: pictureDoor || 'none' },
  });
  engine.subscribe('wave-equipped', persist);
  engine.subscribe('wardrobe-equipped',persist);
  engine.subscribe('bounce-equipped',persist);
  audio.mixer?.restore(extras.audioMix);
  bridge.on('scrolller-feed', frame => engine.pictures.receiveScrolller(frame));
  bridge.on('setting', frame => {
    if (frame.key === 'media.remoteConsent') engine.pictures.setRemoteConsent(frame.value, frame.reason);
  });
  audio.mixer?.subscribe(() => { extras.audioMix = audio.mixer.snapshot(); persist(); });

  /* ---- the pictures card ---- */
  let pictureCard = null;
  function askPictures({ above = false, onClose = () => {} } = {}) {
    if (pictureCard?.open) return;
    pictureCard = openPictureCard({
      pictures: engine.pictures,
      above,
      gentle: ui.gentle,
      onChoice: persist,
      onClose,
    });
  }
  engine.subscribe('choose-pictures', () => {
    if (pictureCard?.open || engine.suspended) return;
    if (engine.prestige?.run?.rules?.includes('blank')) return;   // Fine Print: Blank, no pictures this run
    ui.pause();
    askPictures({ above: true, onClose: ui.resume });
  });

  const newRun = () => { leaving = true; extras.again = 1; store(storage, null, life, extras); location.reload(); };
  /* Endless ending star (onemore, Renewal Sky; Endings lane): Keep playing on the credits card. The same run is saved no
     longer ended (its ending lights now) and reloads, so every unlock mounts again as a restored run, at full pay. */
  const keepPlaying = () => {
    if (!keepPlayingOk(life, run)) return;
    keepPlayingRun(life, run);
    leaving = true; extras.again = 1; store(storage, run, life, extras); location.reload();
  };
  /* Renew (prestige, ui/prestige-sky.js): the renewal is applied and saved at once with no run, so neither a reload nor
     pagehide can write the finished run back or renew it twice. The Sky opens; its Next run sheet starts the run the way
     New run does. A paused or suspended game freezes the Sky (its pause card and Wake Up sit above it). */
  loadPrestige();
  let renewing = false;
  const renew = async () => {
    if (renewing || !run.ended) return;
    renewing = true;
    leaving = true;
    const visit = { caught: 0 };
    try {
      await openRenewal({ doc: document, win: window, life, run, audio, gentle: ui.gentle, visit,
        frozen: () => engine.paused || engine.suspended, save: () => saveSky(visit), start: () => { saveSky(null); location.reload(); } });
    } catch (err) { console.error(err); newRun(); }
  };
  /* The Sky's saves: life with no run, the New run reload flag, and the open visit (null once Sign and start is pressed). */
  const saveSky = (visit) => {
    extras.again = 1;
    if (visit) extras.sky = { caught: visit.caught || 0 }; else delete extras.sky;
    store(storage, null, life, extras);
  };
  const reopenSky = () => {
    try {
      openSky({ doc: document, win: window, life, P: prestigeNow(), audio, gentle: ui.gentle, visit: skyVisit,
        frozen: () => engine.paused || engine.suspended, save: () => saveSky(skyVisit), start: () => { saveSky(null); location.reload(); } });
    } catch (err) { console.error(err); saveSky(null); location.reload(); }
  };
  const hooks = {
    newRun,
    renew,
    keepPlaying,                                            // Endless (onemore): the credits card's Keep playing
    canKeepPlaying: () => keepPlayingOk(life, run),
    renewCurrency: SKY_CURRENCY,
    renewDust() {
      const P = prestigeNow();
      if (!P || !run.ended) return null;
      try { return P.dustFor(run, life.prestige || P.createPrestige()); } catch { return null; }
    },
    changePictures(redraw) {
      if (pictureCard?.open) { ui.resume(); return; }   // the route's own card is already waiting under the pause card
      askPictures({ above: true, onClose: redraw });
    },
    escapeTaken() {
      if (!pictureCard?.open || !pictureCard.above) return false;
      pictureCard.close();
      return true;
    },
    forget() { leaving = true; forget(storage); if (toyPreview) preferenceStorage.removeItem(PREVIEW_PREFS_KEY); location.reload(); },
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
  let playing = false;
  let endingMedia = false;
  const syncMedia = () => engine.pictures.setGameplayActive(playing && !document.hidden && !engine.paused && !engine.suspended && (!engine.stopped || endingMedia));
  for (const event of ['pause','resume','suspend','ending']) engine.subscribe(event,syncMedia);
  engine.subscribe('ending-media', active => { endingMedia = active; syncMedia(); });
  document.addEventListener('visibilitychange',syncMedia);
  window.addEventListener('pagehide',() => engine.pictures.setGameplayActive(false));
  window.addEventListener('pageshow',syncMedia);

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
  bridge.on('close', () => { playing=false; syncMedia(); intro?.close(); audio.hold(true); persist(); bridge.send('exit-done'); });
  bridge.on('setting', (f) => { if (f.key === 'gentle') ui.setInit({ gentle: !!f.value }); });
  bridge.on('sparkles-receipt', showSlip);
  window.addEventListener('pagehide', persist);
  document.addEventListener('visibilitychange', () => { if (document.hidden) persist(); });

  /* The debug door. */
  if (waveStudy) document.documentElement.dataset.waveStudy = waveStudy;
  if (waveOnly) {
    run.store.bubble = { choice:'round' };
    for (const id of ['stray','wallpop','confetti']) run.off[id] = true;
  }
  if (toyPreview) {
    const theme = qs.get('theme');
    // Toy prerequisites grant Glossy. Let the art preview open on the plain set.
    if (equipWave) {
      run.store.bubble = { ...run.store.bubble, waveStyles: { prism:true }, waveStyle:'prism' };
    }
    if (qs.get('icons') === 'plain') run.off.glossy = true;
    if (extras.buttonWardrobe) {
      const {choice,bouncer,emiPose} = extras.buttonWardrobe;
      run.store.bubble = {...run.store.bubble,choice,bouncer,emiPose};
    }
    run.picks.f1 = buttonPreview ? 'eyes' : 'ears';
    run.picks.f3 = feedPreview ? 'scroll' : 'slides';
    run.picks.f5 = FORKS.f5.opts[theme] ? theme : 'trance';
    if (trailPreview && !buttonPreview) run.store.taste2 = { theme: run.picks.f5 === 'bimbo' ? 'trance' : 'bimbo' };
    if (refillWallet && qs.get('style') === 'electric') {
      run.store.cursorKit = {wordEditor:true,words:true,wordLook:{style:'electric',palette:'lagoon',size:'large'}};
    }
    run.store.a_phones = { on: true };
    run.store.mini_slots = { machine: { coins: 3, last: [1, 3, 0] } };
    run.store.mini_race = { race: { leftMs: 0 } };
    E.lump(run, refillWallet ? previewFunds : 100000, 'dev');
  }
  for (const id of buyArg) {
    if (!BY_ID[id]) console.warn(`?buy: no item ${id}`);
    else if (!E.grant(run, id).length && !E.has(run, id)) console.warn(`?buy: ${id} is on a lane this route locked`);
  }
  window.fuzz = {
    engine, ui, life, items: ITEMS, forks: FORKS,
    get state() { return run; },
    buy: (id, option) => engine.buy(id, option),
    pick: (fork, lane) => engine.choose(fork, lane),
    add(n) { E.lump(run, n, 'dev'); ui.draw(true); },
    grant: (id) => engine.grant(id),
    tap(n = 1) { for (let i = 0; i < n; i++) engine.tap({}); },
    end(id = 'drop') { E.grant(run, id); run.ended = { id, at: run.clock }; engine.end(id); },
    newRun,
    prestige: { get p() { return life.prestige; }, get view() { return engine.prestige; }, renew: () => engine.renew() },
  };

  const go = () => {
    bindPalette(engine, document);
    if (run.ended) { engine.end(run.ended.id, { restored: true }); ui.start(); return; }
    playing = true;
    syncMedia();
    engine.mountAll(!!saved?.run);
    engine.syncQuiet();
    ui.drawPhrase();
    if (run.mood && run.clock < 500) {
      const m = BY_ID[run.mood];
      ui.cards.open({ title: 'Today', body: `${m.name}. ${m.what}`, buttons: [{ label: 'Fine' }] });
    }
    ui.draw(true);
    ui.start();
    /* First-run tips (ui/tips.js): once per device, ?tips=reset plays them again. Fresh dev loads keep nothing. */
    window.fuzz.tips = createTips({ doc: document, engine, ui, audio, storage: fresh ? memoryStorage() : preferenceStorage,
      key: toyPreview || PLAYER_PREVIEW ? PREVIEW_TIPS_KEY : TIPS_KEY, reset: qs.get('tips') === 'reset' });
    if (toyPreview && qs.get('focus') === 'buttons') engine.emit('open-button-shop',{tab:'Button'});
    if (toyPreview && qs.get('focus') === 'wave') engine.emit('open-button-shop',{tab:'Wave'});
    if (toyPreview && qs.get('focus') === 'race') engine.scopes.get('mini_race')?.ret.show?.();
    if (feedPreview) engine.scopes.get('d_scroll')?.ret.show?.();
    else if ((combinedPreview && !buttonPreview) || (toyPreview && qs.get('focus') === 'wheel')) engine.scopes.get('wheel')?.ret.show?.();
    if(trailPreview && ['words','trails'].includes(qs.get('menu'))) engine.emit('open-cursor-shop',{category:qs.get('menu')==='words'?'Words':'Trails'});
    persist();
    bridge.log('info', `booted pace=${run.pace} hosted=${bridge.isHosted}`);
  };

  function beginIntro() {
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
        E.lump(run, points, 'intro');
        ui.paid(points, { x: window.innerWidth / 2, y: window.innerHeight * 0.7 });
      }
      go();
    });
  }
  if (skyVisit) reopenSky();
  else if (skyDemo) {
    const P = prestigeNow();
    const p = P.createPrestige();
    p.depth = 4; p.dust = 1400;
    for (const id of ['pay_1', 'pay_2', 'pay_3', 'head_1', 'head_2', 'idle_1', 'idle_2', 'active_1', 'active_2', 'trig_1', 'trig_2', 'luck_1', 'ghost_1', 'ghost_2', 'fine_1', 'fine_2']) p.own[id] = 1;
    p.lit = { drop: 1, bi_end: 1 };
    p.last = { route: {}, toys: ['wheel', 'toy_box'], ms: 5_400_000 };
    life.prestige = p;
    leaving = true;
    openSky({ doc: document, win: window, life, P, audio, gentle: ui.gentle, visit: { caught: 0 },
      frozen: () => engine.paused || engine.suspended, save: () => {}, start: () => location.reload() });
  }
  else if (pictureDoor) beginIntro();
  else {
    ui.setWakeGate(() => pictureCard?.wake());
    askPictures({ onClose: () => { ui.setWakeGate(null); beginIntro(); } });
  }
}
