// Local presentation choices. Host and OS motion restrictions always win.
import { onHostMessage, postToHost } from '../bridge.js';
import { gentleSpeed } from '../board/intro-plan.js';
const KEY = 'pbp-presentation-v1';
let saved = {};
try { saved = JSON.parse(globalThis.localStorage?.getItem(KEY) || '{}') || {}; } catch { /* private storage */ }
if (!saved || typeof saved !== 'object' || Array.isArray(saved)) saved = {};
const settings = () => globalThis.window?.PBP?.settings || {};
let hostReduced = !!settings().reducedMotion;
// Gentle visuals (desktop): > 0 only when Gentle is the one reason the host reduces motion; the intros
// then play in full, slower (board/intro-plan.js gentleSpeed). 0 = none.
let hostGentle = Number(settings().gentleIntroSpeed) || 0;
let hostVolume = Number.isFinite(settings().sfxVolume) ? settings().sfxVolume : .6;
// "Let people watch my games": the desktop keeps it in AppSettings (pbp:settings letPeopleWatch);
// a web or phone page keeps its own. null = the host has not said.
let hostWatch = null;
// The stands' own effects (owner, 2026-10-14): on by default, their own Amount. The desktop keeps them in
// AppSettings (pbp:settings watchEffects / watchAmount); a web or phone page keeps its own. null = not said.
const hostWatchFx = { on: null, amount: null };
const listeners = new Set();
const osMotion = globalThis.window?.matchMedia?.('(prefers-reduced-motion: reduce)');
const clamp = value => Math.max(0, Math.min(1, value));
const AMOUNTS = ['less', 'normal', 'more'];
const SPEEDS = ['slow', 'normal', 'fast'];
const strength = value => Math.round(Math.max(0.1, Math.min(2, value)) * 20) / 20;
export function presentation() {
  const volume = Number.isFinite(saved.volume) ? clamp(saved.volume) : hostVolume;
  return {
    // Distraction is the default (2026-09-28). Only a saved explicit 'classic' keeps
    // Classic: setPresentation writes `experience` only when a button asks for it,
    // so nobody was ever saved as classic by default.
    experience: saved.experience === 'classic' ? 'classic' : 'distraction',
    volume: Math.min(volume, hostVolume),
    reducedMotion: !!saved.reducedMotion || hostReduced || !!osMotion?.matches,
    motionLocked: hostReduced || !!osMotion?.matches,
    // kept apart so the intros can tell Gentle from a choice the player or the OS made
    ownReduced: !!saved.reducedMotion,
    osReduced: !!osMotion?.matches,
    introSpeed: gentleSpeed({ host: hostGentle, ownReduced: !!saved.reducedMotion, osReduced: !!osMotion?.matches }),
    // Camera (owner, 2026-09-28): follow the move, replay captures, and the turn card's look.
    followCam: saved.followCam !== false,
    replays: saved.replays !== false,
    // The clockwork rise when a new game's board comes up (board/intro.js), on by default.
    boardIntro: saved.boardIntro !== false,
    // The card the page opens on (ui/open-intro.js), on by default; read once, when the page opens.
    openIntro: saved.openIntro !== false,
    turnCard: ['slam', 'ribbon', 'tag'].includes(saved.turnCard) ? saved.turnCard : 'slam',
    // Camera drag direction (ccp-bugs #1329), off by default.
    invertX: saved.invertX === true,
    invertY: saved.invertY === true,
    // Distraction dials (owner, 2026-10-02): how many pictures, how strong, how fast it climbs.
    amount: AMOUNTS.includes(saved.amount) ? saved.amount : 'normal',
    strength: Number.isFinite(saved.strength) ? strength(saved.strength) : 1,
    rampSpeed: SPEEDS.includes(saved.rampSpeed) ? saved.rampSpeed : 'normal',
    soundLocked: hostVolume === 0,
    // Spectators (owner, 2026-10-05): on by default; off sends watchable:false when a public game is made.
    letPeopleWatch: hostWatch !== null ? hostWatch : saved.letPeopleWatch !== false,
    // Watching a game: the turn player's effects replayed with the watcher's own pictures, on by default.
    watchEffects: hostWatchFx.on !== null ? hostWatchFx.on : saved.watchEffects !== false,
    watchAmount: hostWatchFx.amount || (AMOUNTS.includes(saved.watchAmount) ? saved.watchAmount : 'normal'),
  };
}
function apply() {
  const p = presentation();
  Object.assign(settings(), { experience: p.experience, sfxVolume: p.volume, reducedMotion: p.reducedMotion });
  globalThis.window?.PBP?.board?.sfx?.setVolume?.(p.volume);
  for (const fn of listeners) fn(p);
  return p;
}
export function setPresentation(patch) {
  if (['classic', 'distraction'].includes(patch.experience)) saved.experience = patch.experience;
  if (typeof patch.reducedMotion === 'boolean') saved.reducedMotion = patch.reducedMotion;
  if (Number.isFinite(patch.volume)) saved.volume = clamp(patch.volume);
  if (typeof patch.followCam === 'boolean') saved.followCam = patch.followCam;
  if (typeof patch.replays === 'boolean') saved.replays = patch.replays;
  if (typeof patch.boardIntro === 'boolean') saved.boardIntro = patch.boardIntro;
  if (typeof patch.openIntro === 'boolean') saved.openIntro = patch.openIntro;
  if (['slam', 'ribbon', 'tag'].includes(patch.turnCard)) saved.turnCard = patch.turnCard;
  if (typeof patch.invertX === 'boolean') saved.invertX = patch.invertX;
  if (typeof patch.invertY === 'boolean') saved.invertY = patch.invertY;
  if (AMOUNTS.includes(patch.amount)) saved.amount = patch.amount;
  if (Number.isFinite(patch.strength)) saved.strength = strength(patch.strength);
  if (SPEEDS.includes(patch.rampSpeed)) saved.rampSpeed = patch.rampSpeed;
  if (typeof patch.letPeopleWatch === 'boolean') {
    saved.letPeopleWatch = patch.letPeopleWatch;
    if (hostWatch !== null) hostWatch = patch.letPeopleWatch;
    // the host keeps it too (desktop: AppSettings.PbpLetPeopleWatch); no host, no-op
    postToHost({ type: 'pbp:setting', key: 'letPeopleWatch', value: patch.letPeopleWatch });
  }
  // the stands' effects: saved here and kept by the host the same way (desktop: PbpWatchEffects, PbpWatchAmount)
  if (typeof patch.watchEffects === 'boolean') {
    saved.watchEffects = patch.watchEffects;
    if (hostWatchFx.on !== null) hostWatchFx.on = patch.watchEffects;
    postToHost({ type: 'pbp:setting', key: 'watchEffects', value: patch.watchEffects });
  }
  if (AMOUNTS.includes(patch.watchAmount)) {
    saved.watchAmount = patch.watchAmount;
    if (hostWatchFx.amount !== null) hostWatchFx.amount = patch.watchAmount;
    postToHost({ type: 'pbp:setting', key: 'watchAmount', value: patch.watchAmount });
  }
  try { globalThis.localStorage?.setItem(KEY, JSON.stringify(saved)); } catch { /* session only */ }
  return apply();
}
// The camera's orbit drag, flipped per axis when the player asked for it.
export function invertDrag(dx, dy, p = presentation()) {
  return [p.invertX ? -dx : dx, p.invertY ? -dy : dy];
}
export function onPresentation(fn) { listeners.add(fn); fn(presentation()); return () => listeners.delete(fn); }
onHostMessage(m => {
  if (m.type !== 'pbp:settings') return;
  if (typeof m.reducedMotion === 'boolean') hostReduced = m.reducedMotion;
  if (Number.isFinite(m.sfxVolume)) hostVolume = clamp(m.sfxVolume);
  if (Number.isFinite(m.gentleIntroSpeed)) hostGentle = m.gentleIntroSpeed;
  if (typeof m.letPeopleWatch === 'boolean') hostWatch = m.letPeopleWatch;
  if (typeof m.watchEffects === 'boolean') hostWatchFx.on = m.watchEffects;
  if (AMOUNTS.includes(m.watchAmount)) hostWatchFx.amount = m.watchAmount;
  apply();
});
osMotion?.addEventListener?.('change', apply);
apply();
