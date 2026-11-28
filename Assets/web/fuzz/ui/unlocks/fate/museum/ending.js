/* ui/unlocks/fate/museum/ending.js - go_mus: the Gooning Museum ride (ui/documentary, owner approved) as the run's ending
 * show. The ride is three.js and long, so it loads lazily: the themed spiral cover mounts at once on the ending layer
 * (as the ride's README asks), the module arrives under it, and the cover is handed to the ride, which keeps it up until
 * the first rendered view. The ride is fed the real run: the player's picture source (ctx.pictures, DOM only), name
 * (ctx.identity), plan (ctx.caps, subscribers get no AD), Gentle and reduced motion, and the clauses the player accepted
 * (./terms.js). The approved ride shows no placards on its run screens, so no run numbers are printed there. Its New run
 * resolves the show; the engine then shows the ordinary recap with the poster. Wake Up, pause, Escape and a host suspend
 * stay the ride's own, as before. */

import { createMuseumArrival } from '../../../documentary/arrival.js';
import { acceptedTerms } from './terms.js';

/* The ride's own ceiling is DOCUMENTARY_SHOW_MAX_MS (4 h of the post-ride wall). The engine's show cap runs on wall time,
 * also through a pause, so it gets two more hours: the ride ends itself through New run long before. */
export const MUSEUM_SHOW_MAX_MS = 6 * 3600e3;

let warming = null;
/** Starts loading the ride (module graph only, no DOM). The last museum card calls it so the ending opens quickly. */
export function warmMuseum() {
  warming ||= import('../../../documentary/index.js');
  warming.catch(() => { warming = null; });
  return warming;
}

/* Local preview only (the same hosts as main.js's dev doors): ?museumAt=<the ride's own preview door, e.g. stage, wakeup,
 * floorboard or a distance> starts the ride there, and window.__museumRide is the ride's tunnel for a test drive. */
function devDoor() {
  const win = globalThis.window;
  if (!win?.location || !['127.0.0.1', 'localhost'].includes(win.location.hostname)) return null;
  const raw = new URLSearchParams(win.location.search).get('museumAt');
  const at = raw == null ? undefined : (/^[0-9]+$/.test(raw) ? Number(raw) : raw);
  return { win, at };
}

/* In the game the top right corner belongs to the lock, the speaker and Wake Up (#game-controls). The ride's own Center
 * view and CC buttons move down one row, still side by side; phones already stack them below (scene.css). */
const GAME_CORNER = `@media (min-width:601px){
#l-ending .td-root .td-center-view{top:62px;right:12px}
#l-ending .td-root .td-subtitle-toggle{top:62px;right:118px}}`;

export async function museumEnding(ctx) {
  ctx.css(GAME_CORNER);
  const arrival = createMuseumArrival(ctx, ctx.layer('ending'));
  let gone = false, mediaOn = true;
  const stopMedia = () => { if (mediaOn) { mediaOn = false; ctx.emit('ending-media', false); } };
  // The run has stopped; this keeps the shared picture warm-up running for the ride's screens (as Bimbodoll does).
  ctx.emit('ending-media', true);
  ctx.cleanup(() => { gone = true; arrival.dispose(); stopMedia(); });
  let cancel;
  const cancelled = new Promise((resolve) => { cancel = resolve; });
  ctx.on('suspend', (e) => { if (e?.on && e.reason === 'panic') { gone = true; arrival.dispose(); cancel(null); } });
  try {
    const mod = await Promise.race([warmMuseum(), cancelled]);
    if (!mod || gone) return;
    const dev = devDoor();
    await mod.tabDocumentary(ctx, {
      arrival,
      narration: true,   // the recorded narrator, as with no connect; connect below is only a local test handle
      acceptedClauseIds: acceptedTerms(ctx.state),
      ...(dev ? { at: dev.at, connect: (tunnel) => { dev.win.__museumRide = tunnel; } } : {}),
    });
  } catch (error) {
    console.error(error);   // a ride that cannot open falls through to the recap
  } finally {
    arrival.dispose();
    stopMedia();
  }
}
