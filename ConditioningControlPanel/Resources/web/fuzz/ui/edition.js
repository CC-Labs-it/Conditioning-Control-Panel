/* ui/edition.js - which edition of the game this page is (owner 2026-11-22; data in data/editions.js, wire in
 * CONTRACT.md section 6). The ONE reader of init caps.edition, caps.saveSlot and caps.expo: every other module asks
 * here (caps.platform is also read by ui/seal.js, which runs before the game does).
 *
 *   Who decides: the host, in init. Missing, unknown or no host at all = small. The page is untrusted, so a host says
 *   full only for an account it has checked (any subscriber).
 *   A page on 127.0.0.1 or localhost: with no host it is the WHOLE game (every dev drive and the Expo preview keep
 *   working), and ?edition=small or ?edition=full wins over everything there.
 *   No page at all (node tests, tools): the whole game.
 *   Before main.js calls setEdition, a real page reads as it would unhosted, so a forgotten call is small, never full.
 *
 * Pure rules take the reading as their last argument (`ed`), so tests need no page and the sim can be handed one. */

import { BY_ID } from '../data/items.js';
import { setOf } from '../data/fate.js';
import {
  SMALL_CUT, SMALL_CUT_IDS, SMALL_REQ, SMALL_BALANCE, SMALL_ENDING_SET, PHONE_CAPS, NUDGE_WHERE, NUDGE_LINKS, SAVE_CARRY_MAX_BYTES, SAVE_CARRY_MAX_CHARS, SAVE_FROM,
  TRANSMISSION_AT_LLAMAS, EXPO_DEFAULT_BASE,
} from '../data/editions.js';
import { allowedLink, openAllowedLink } from './links.js';
import { isPhone } from './phone.js';

const LOCAL = new Set(['127.0.0.1', 'localhost']);
const CUT = new Set(SMALL_CUT_IDS);
const LOCAL_EXPO = Object.freeze({ state: 'ready', base: EXPO_DEFAULT_BASE });

/** An Expo base a host may name: https, no login part, ends in a slash. The host is trusted, the shape still is checked. */
export function expoBaseOk(base) {
  if (typeof base !== 'string' || !base.endsWith('/')) return false;
  let u;
  try { u = new URL(base); } catch { return false; }
  return u.protocol === 'https:' && !u.username && !u.password;
}

function readExpo(caps, edition, platform, localWhole) {
  if (edition !== 'full') return null;
  const e = caps?.expo;
  if (platform === 'desktop' && e && typeof e === 'object') {
    const mb = Number.isInteger(e.mb) && e.mb > 0 ? e.mb : null;
    if (e.state === 'ready' && expoBaseOk(e.base)) return Object.freeze({ state: 'ready', base: e.base, mb });
    return Object.freeze({ state: 'absent', base: null, mb });
  }
  return localWhole ? LOCAL_EXPO : null;
}

/** The reading for one init frame. `where`: { hostname, search, hosted, noPage }. Pure. */
export function readEdition(init, { hostname = '', search = '', hosted = false, noPage = false } = {}) {
  const caps = (hosted && init?.caps) || {};
  const local = LOCAL.has(hostname);
  let asked = hosted ? caps.edition : (local || noPage ? 'full' : 'small');
  if (local) {
    let q = null;
    try { q = new URLSearchParams(search).get('edition'); } catch { /* an odd query changes nothing */ }
    if (q === 'small' || q === 'full') asked = q;
  }
  const edition = asked === 'full' ? 'full' : 'small';
  const platform = caps.platform === 'desktop' ? 'desktop' : 'web';
  /* Signed in = the host sent an identity at all. A fresh account has no display name yet and is still signed in. */
  const identity = init?.identity;
  /* The files sit in the tree only where nobody staged them away: a local page or a tool. */
  const localWhole = (local || noPage) && !(hosted && platform === 'desktop');
  return Object.freeze({
    edition,
    platform,
    hosted: !!hosted,
    signedIn: !!hosted && !!identity && typeof identity === 'object',
    saveSlot: !!hosted && caps.saveSlot === true,
    expo: readExpo(caps, edition, platform, localWhole),
  });
}

let cur = null;
const unhostedReading = () => {
  const loc = globalThis.location;
  return readEdition(null, { hostname: loc?.hostname || '', search: loc?.search || '', hosted: false, noPage: !loc });
};

/** Boot: main.js hands over the init frame once, before anything draws. */
export function setEdition(init, where) { cur = readEdition(init, where); return cur; }
/** The current reading. */
export function editionState() { return cur || (cur = unhostedReading()); }

export const edition = () => editionState().edition;
export const isSmall = (ed = editionState()) => ed.edition === 'small';
export const isFull = (ed = editionState()) => ed.edition === 'full';

/* THE LLAMA HOLE (owner 2026-11-24): the full edition on the WEB has no Expo (only the desktop host sends caps.expo; a
 * local page keeps its own files). There the Llama set (data/editions.js SMALL_CUT.expo: The Panel and Llama Expo 2037)
 * is cut exactly as in small, so a run can never reach a door that cannot open; the llama-8 card says where it lives
 * (ui/transmission.js llama8Due). The desktop is never holed. */
const EXPO_CUT = new Set(SMALL_CUT.expo);
/** True for a full web reading without the Expo's files: the Llama set is cut there as in small. */
export const expoHole = (ed = editionState()) => ed?.edition === 'full' && ed.platform !== 'desktop' && ed.expo?.state !== 'ready';
const cutHere = (id, ed) => (ed.edition === 'small' ? CUT.has(id) : EXPO_CUT.has(id) && expoHole(ed));

/** True when this row is never offered here. A save that owns one keeps it, asleep (CONTRACT.md 6.4). */
export const isCut = (id, ed = editionState()) => cutHere(id, ed);
/** A list of row ids without the ones cut here (fold riders, any-lists, shop shelves). */
export const keptIds = (ids, ed = editionState()) => (ed.edition === 'small' || expoHole(ed) ? ids.filter((id) => !cutHere(id, ed)) : ids);

/* ONE ENDING (owner 2026-11-24, the funnel): small holds only the fate set SMALL_ENDING_SET (Go Outside). Every other
 * set's cards and ending are LOCKED here: never offered, bought or granted, and asleep when a save already owns one (a
 * full save opened in small: its set sleeps and the run falls to Go Outside). Unlike a cut row, a locked set is shown to
 * the player, locked and by name (sim/economy.js teasedSets), so it is kept apart from SMALL_CUT and isCut. */
const fateLockedIn = (id) => { const set = setOf(id); return !!set && set !== SMALL_ENDING_SET; };
/** True when this row is a fate card or ending of a set the small edition keeps locked. */
export const fateLocked = (id, ed = editionState()) => ed.edition === 'small' && fateLockedIn(id);
/** True when this row never plays here, and sleeps when owned: cut (isCut) or of a locked fate set (fateLocked). */
export const sleepsHere = (id, ed = editionState()) => (ed.edition === 'small' ? CUT.has(id) || fateLockedIn(id) : cutHere(id, ed));
/** True when this fate set (a data/fate.js id) may be held as a run's ending here. */
export const endingSetOpen = (setId, ed = editionState()) => ed.edition !== 'small' || setId === SMALL_ENDING_SET;

function handDown(id, seen) {
  if (!CUT.has(id)) return [id];
  if (seen.has(id)) return [];
  seen.add(id);
  return (BY_ID[id]?.req || []).flatMap((q) => handDown(q, seen));
}

/** What a row needs in this edition: { req, any }. Full = the catalog. Small: a cut parent hands down its own parents,
 *  a cut id leaves an any-list, and data/editions.js SMALL_REQ pins a row by hand when the balance pass wants it. */
export function reqFor(item, ed = editionState()) {
  const it = typeof item === 'string' ? BY_ID[item] : item;
  if (!it) return { req: [], any: undefined };
  if (ed.edition !== 'small') return { req: it.req, any: it.any && expoHole(ed) ? it.any.filter((q) => !EXPO_CUT.has(q)) : it.any };
  const pin = SMALL_REQ[it.id];
  if (pin) return { req: pin.req, any: pin.any };
  const req = [...new Set(it.req.flatMap((q) => handDown(q, new Set())))];
  return { req, any: it.any ? it.any.filter((q) => !CUT.has(q) && !fateLockedIn(q)) : undefined };
}

/** Renewal (Sky, Depth, Stardust, the Renewal menu and badge) is a full-edition thing, for everyone. */
export const renewalAllowed = (ed = editionState()) => ed.edition === 'full';

/** The phone caps, or null where they do not apply (full, or any screen that is not a phone). */
/** The small edition's balance table (data/editions.js SMALL_BALANCE), or null: the full game is never rebalanced. */
export const smallBalance = (ed = editionState()) => (ed?.edition === 'small' ? SMALL_BALANCE : null);
export const phoneCaps = (win, ed = editionState()) => (ed.edition === 'small' && isPhone(win) ? PHONE_CAPS : null);

/** The Expo may load: full edition and its files are within reach. */
export const expoAllowed = (ed = editionState()) => ed.edition === 'full' && ed.expo?.state === 'ready';
/** The download size the host gave for the Expo (the transmission card's size line), or null. */
export const expoMb = (ed = editionState()) => ed.expo?.mb ?? null;
/** Where every Expo file loads from. */
export const expoBase = (ed = editionState()) => (ed.expo?.state === 'ready' && ed.expo.base) || EXPO_DEFAULT_BASE;
/** The host's `expo-ready`: the download landed. Answers whether the Expo is now open. */
export function expoArrived(base) {
  const ed = editionState();
  if (ed.edition !== 'full' || ed.platform !== 'desktop' || !ed.expo || !expoBaseOk(base)) return false;
  cur = Object.freeze({ ...ed, expo: Object.freeze({ state: 'ready', base, mb: ed.expo.mb ?? null }) });
  return true;
}

/** Which transmission card this many llamas calls for: 'fetch' (accept the download), 'nudge' (it does not fit
 *  here) or null. Showing it once, NOT NOW and the pen sign are the card's own business. */
export function transmissionKind(llamas, ed = editionState()) {
  if (!(llamas >= TRANSMISSION_AT_LLAMAS)) return null;
  if (ed.edition === 'small') return 'nudge';
  /* The size must be on the card before a download is offered (owner, fourth dialog): no size from the host, no offer. */
  return ed.platform === 'desktop' && ed.expo?.state === 'absent' && expoMb(ed) > 0 ? 'fetch' : null;
}

/** What a nudge asks for here: the plan card in the app, an account for a visitor, the download for a signed-in one. */
export const nudgeWant = (ed = editionState()) => (ed.platform === 'desktop' ? 'upgrade' : ed.signedIn ? 'download' : 'account');
/** The `nudge` frame for a place, or null for a place that is not on the list. */
export const nudgeFrame = (where, ed = editionState()) => (NUDGE_WHERE.includes(where) ? { where, want: nudgeWant(ed) } : null);
/** Where a nudge leads with no host to open its own door. */
export const nudgeLink = (want) => allowedLink(NUDGE_LINKS[want]);

/** Send one nudge from a real press: hosted, the fixed frame; unhosted, the fixed link. Answers whether it went. */
export function sendNudge(where, { inPress = () => false, send = () => {}, win = globalThis.window, ed = editionState() } = {}) {
  const frame = nudgeFrame(where, ed);
  if (!frame || !inPress()) return false;
  if (ed.hosted) { send('nudge', frame); return true; }
  return openAllowedLink(nudgeLink(frame.want), { inPress, hosted: false, win });
}

const bytes = (s) => new TextEncoder().encode(s).length;
/** A save string fit to travel: a string, within both caps, JSON with a numeric v. The character cap is the save
 *  parser's own limit (sim/save.js parse): a slot must never keep a save this page could not read back. */
export function saveFits(save) {
  if (typeof save !== 'string' || save === '' || save.length > SAVE_CARRY_MAX_CHARS || bytes(save) > SAVE_CARRY_MAX_BYTES) return false;
  try { return typeof JSON.parse(save)?.v === 'number'; } catch { return false; }
}
/** The host keeps one save string for this account (caps.saveSlot): the carry buttons show only then. */
export const saveSlotOn = (ed = editionState()) => ed.saveSlot === true;
/** The `save-offer` fields for this save, or null when it may not travel. Only ever sent from a player's press. */
export const saveOfferFrame = (save) => (saveFits(save) ? { save } : null);
/** A host's `save-incoming`, checked: { save, savedAt, from } or null. Never applied without the player's yes. */
export function readSaveIncoming(frame) {
  if (!frame || !saveFits(frame.save) || !SAVE_FROM.includes(frame.from)) return null;
  if (!Number.isFinite(frame.savedAt) || frame.savedAt <= 0) return null;
  return { save: frame.save, savedAt: frame.savedAt, from: frame.from };
}
