import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { ITEMS, BY_ID } from '../data/items.js';
import {
  EDITIONS, SMALL_CUT, SMALL_CUT_IDS, SMALL_REQ, PHONE_CAPS, NUDGE_WHERE, NUDGE_WANT, NUDGE_LINKS,
  SAVE_CARRY_MAX_BYTES, SAVE_CARRY_MAX_CHARS, SAVE_FROM, SAVE_RESULT_REASONS, TRANSMISSION_AT_LLAMAS, EXPO_FAIL_REASONS, EXPO_DEFAULT_BASE,
} from '../data/editions.js';
import * as Save from '../sim/save.js';
import * as Ed from '../ui/edition.js';
import { allowedLink, DESKTOP_URL } from '../ui/links.js';
import { PHONE_QUERY } from '../ui/phone.js';
import { sealedFor, sealMarkup, SEAL_OPEN_AT_MS } from '../ui/seal.js';

const PUBLIC = 'tc.cclabs.app';
const hosted = (caps, extra = {}, hostname = PUBLIC, search = '') => Ed.readEdition({ protocol: 1, caps, ...extra }, { hostname, search, hosted: true });
const unhosted = (hostname = PUBLIC, search = '', caps = {}) => Ed.readEdition({ protocol: 1, hosted: false, caps }, { hostname, search, hosted: false });
const small = hosted({ edition: 'small', platform: 'web' });
const full = hosted({ edition: 'full', platform: 'desktop' }, {}, 'ccp.game');
const cut = new Set(SMALL_CUT_IDS);

test('the cut list is 56 real rows, each named once, and 260 stay', () => {
  assert.deepEqual(EDITIONS, ['small', 'full']);
  assert.equal(ITEMS.length, 316);
  assert.equal(SMALL_CUT_IDS.length, 56);
  assert.equal(cut.size, 56, 'no row is cut twice');
  for (const id of SMALL_CUT_IDS) assert.ok(BY_ID[id], `${id} is in the catalog`);
  assert.equal(ITEMS.filter((i) => !cut.has(i.id)).length, 260);
});

test('every crossover is cut, and nothing else hides in that list', () => {
  assert.deepEqual([...SMALL_CUT.combos].sort(), ITEMS.filter((i) => i.lane === 'combo').map((i) => i.id).sort());
});

test('the rows that stay are the ones the plan names', () => {
  for (const id of ['mini_slots', 'wheel', 'quiz', 'melt', 'nag', 'crowd', 'pbubble', 'onlyyes', 'em_head', 'em_pet', 'em_chat', 'em_hat', 'em_nap']) {
    assert.ok(BY_ID[id], `${id} exists`);
    assert.equal(cut.has(id), false, `${id} stays`);
  }
  for (const id of ['buzz', 'toy', 'dualwield', 'sparkle', 'ft_panel', 'll_end']) assert.equal(cut.has(id), true, `${id} goes`);
});

test('cut only bites in the small edition', () => {
  for (const id of SMALL_CUT_IDS) {
    assert.equal(Ed.isCut(id, small), true, id);
    assert.equal(Ed.isCut(id, full), false, id);
  }
  assert.equal(Ed.isCut('start', small), false);
  assert.deepEqual(Ed.keptIds(BY_ID.melt.fold, small), ['nag', 'crowd', 'pbubble']);
  assert.equal(Ed.keptIds(BY_ID.melt.fold, full), BY_ID.melt.fold);
});

test('in small no kept row needs a cut one, and every kept row can still be reached', () => {
  const reach = new Set();
  const rows = ITEMS.filter((i) => !cut.has(i.id));
  for (const r of rows) {
    const { req, any } = Ed.reqFor(r, small);
    for (const q of req) assert.ok(BY_ID[q] && !cut.has(q), `${r.id} needs ${q}`);
    if (r.any) assert.ok(any.length > 0 && any.every((q) => !cut.has(q)), `${r.id} keeps a way in`);
    else assert.equal(any, undefined);
  }
  for (let grew = true; grew;) {
    grew = false;
    for (const r of rows) {
      if (reach.has(r.id)) continue;
      const { req, any } = Ed.reqFor(r, small);
      if (req.every((q) => reach.has(q)) && (!any || any.some((q) => reach.has(q)))) { reach.add(r.id); grew = true; }
    }
  }
  assert.deepEqual(rows.filter((r) => !reach.has(r.id)).map((r) => r.id), []);
});

test('a cut parent hands down its own parents: the three re-links and the Overlay list', () => {
  assert.deepEqual(Ed.reqFor('nag', small), { req: ['melt'], any: undefined });
  assert.deepEqual(Ed.reqFor('onlyyes', small), { req: ['pbubble'], any: undefined });
  assert.deepEqual(Ed.reqFor('em_nap', small), { req: ['em_hat'], any: undefined });
  assert.equal(Ed.reqFor('overlay', small).any.includes('ft_panel'), false);
  assert.equal(BY_ID.overlay.any.includes('ft_panel'), true, 'the catalog itself is untouched');
  for (const id of Object.keys(SMALL_REQ)) assert.ok(BY_ID[id] && !cut.has(id), `${id} is a kept row`);
});

test('the full edition reads the catalog as it is', () => {
  for (const it of ITEMS) {
    const r = Ed.reqFor(it, full);
    assert.equal(r.req, it.req);
    assert.equal(r.any, it.any);
  }
  assert.deepEqual(Ed.reqFor('no_such_row', small), { req: [], any: undefined });
});

test('who gets which edition: the host decides, and anything unclear is small', () => {
  assert.equal(unhosted().edition, 'small', 'a page on its own');
  assert.equal(unhosted(PUBLIC, '', { edition: 'full' }).edition, 'small', 'a page on its own cannot say full');
  assert.equal(hosted({}).edition, 'small', 'a host that says nothing');
  assert.equal(hosted({ edition: 'FULL' }).edition, 'small');
  assert.equal(hosted({ edition: true }).edition, 'small');
  assert.equal(hosted({ edition: 'small' }).edition, 'small');
  assert.equal(hosted({ edition: 'full' }).edition, 'full');
  assert.equal(Ed.readEdition(null).edition, 'small');
  assert.equal(unhosted(PUBLIC, '?edition=full').edition, 'small', 'the query is a local preview thing only');
  assert.equal(hosted({ edition: 'small' }, {}, PUBLIC, '?edition=full').edition, 'small');
});

test('a local preview is the whole game unless it asks for small; a tool with no page too', () => {
  assert.equal(unhosted('127.0.0.1').edition, 'full');
  assert.equal(unhosted('localhost').edition, 'full');
  assert.equal(unhosted('127.0.0.1', '?edition=small').edition, 'small');
  assert.equal(unhosted('127.0.0.1', '?edition=nonsense').edition, 'full');
  assert.equal(hosted({}, {}, 'localhost').edition, 'small', 'a local host that says nothing is still a host');
  assert.equal(hosted({}, {}, 'localhost', '?edition=full').edition, 'full');
  assert.equal(Ed.readEdition(null, { noPage: true }).edition, 'full');
  assert.equal(Ed.editionState().edition, 'full', 'this test run has no page');
});

test('platform, sign-in and the save slot come only from a host', () => {
  assert.equal(hosted({}).platform, 'web');
  assert.equal(hosted({ platform: 'desktop' }).platform, 'desktop');
  assert.equal(hosted({ platform: 'phone' }).platform, 'web');
  assert.equal(hosted({}, { identity: { displayName: ' Sam ' } }).signedIn, true);
  assert.equal(hosted({}, { identity: { displayName: '' } }).signedIn, true, 'a fresh account has no name yet and is still signed in');
  assert.equal(hosted({}, { identity: {} }).signedIn, true);
  assert.equal(hosted({}, { identity: null }).signedIn, false);
  assert.equal(hosted({}, { identity: 'Sam' }).signedIn, false);
  assert.equal(hosted({}).signedIn, false);
  assert.equal(hosted({ saveSlot: true }).saveSlot, true);
  assert.equal(hosted({ saveSlot: 'yes' }).saveSlot, false);
  const alone = Ed.readEdition({ caps: { saveSlot: true, platform: 'desktop' }, identity: { displayName: 'Sam' } }, { hostname: PUBLIC, hosted: false });
  assert.deepEqual([alone.saveSlot, alone.signedIn, alone.platform, alone.hosted], [false, false, 'web', false]);
});

test('nudges: two fixed ids, picked by where the player is', () => {
  assert.deepEqual(NUDGE_WHERE, ['ending', 'llama8', 'transmission', 'locked-tile', 'pause', 'save-sleep']);
  assert.deepEqual(NUDGE_WANT, ['account', 'download', 'upgrade']);
  const desk = hosted({ edition: 'small', platform: 'desktop' }, {}, 'ccp.game');
  const guest = hosted({ edition: 'small', platform: 'web' });
  const member = hosted({ edition: 'small', platform: 'web' }, { identity: { displayName: 'Sam' } });
  assert.equal(Ed.nudgeWant(desk), 'upgrade', 'the app never says desktop only: its door is the plan card');
  assert.equal(Ed.nudgeWant(guest), 'account');
  assert.equal(Ed.nudgeWant(member), 'download');
  assert.equal(Ed.nudgeWant(unhosted()), 'account');
  assert.deepEqual(Ed.nudgeFrame('llama8', member), { where: 'llama8', want: 'download' });
  assert.equal(Ed.nudgeFrame('anywhere', member), null);
  assert.equal(Ed.nudgeFrame('https://example.com', member), null);
  for (const want of NUDGE_WANT) assert.equal(Ed.nudgeLink(want), allowedLink(NUDGE_LINKS[want]), want);
  for (const want of NUDGE_WANT) assert.ok(Ed.nudgeLink(want), `${want} passes the link rule`);
  assert.equal(NUDGE_LINKS.download, DESKTOP_URL);
  assert.equal(Ed.nudgeLink('nonsense'), null);
});

test('a nudge only leaves from a real press: hosted the frame, unhosted the fixed link', () => {
  const sent = [], opened = [];
  const send = (type, fields) => sent.push({ type, ...fields });
  const win = { open: (...a) => opened.push(a) };
  const member = hosted({ edition: 'small', platform: 'web' }, { identity: { displayName: 'Sam' } });
  assert.equal(Ed.sendNudge('ending', { inPress: () => false, send, win, ed: member }), false);
  assert.equal(Ed.sendNudge('nowhere', { inPress: () => true, send, win, ed: member }), false);
  assert.deepEqual([sent, opened], [[], []]);
  assert.equal(Ed.sendNudge('ending', { inPress: () => true, send, win, ed: member }), true);
  assert.deepEqual(sent, [{ type: 'nudge', where: 'ending', want: 'download' }]);
  assert.deepEqual(opened, [], 'a hosted page opens nothing itself');
  assert.equal(Ed.sendNudge('pause', { inPress: () => true, send, win, ed: unhosted() }), true);
  assert.deepEqual(opened, [[NUDGE_LINKS.account, '_blank', 'noopener']]);
  assert.equal(sent.length, 1);
});

test('the Expo: full only, and only once its files are within reach', () => {
  assert.equal(EXPO_DEFAULT_BASE, 'assets/expo/');
  assert.deepEqual(EXPO_FAIL_REASONS, ['offline', 'disk', 'refused']);
  assert.equal(Ed.expoAllowed(small), false);
  assert.equal(Ed.expoAllowed(hosted({ edition: 'small', platform: 'desktop', expo: { state: 'ready', base: 'https://ccp.assets/tc-expo/' } }, {}, 'ccp.game')), false, 'small never, whatever the host says');
  assert.equal(Ed.expoAllowed(hosted({ edition: 'full', platform: 'web' })), false, 'the website has no Expo files');
  assert.equal(Ed.expoAllowed(full), false, 'a desktop host that names no Expo has none');
  const absent = hosted({ edition: 'full', platform: 'desktop', expo: { state: 'absent', mb: 250 } }, {}, 'ccp.game');
  assert.deepEqual(absent.expo, { state: 'absent', base: null, mb: 250 });
  assert.equal(Ed.expoAllowed(absent), false);
  const ready = hosted({ edition: 'full', platform: 'desktop', expo: { state: 'ready', base: 'https://ccp.assets/tc-expo/' } }, {}, 'ccp.game');
  assert.equal(Ed.expoAllowed(ready), true);
  assert.equal(Ed.expoBase(ready), 'https://ccp.assets/tc-expo/');
  for (const base of ['http://ccp.assets/tc-expo/', 'https://ccp.assets/tc-expo', 'https://u:p@ccp.assets/x/', 'file:///C:/x/', 'assets/expo/', 7, null]) {
    const bad = hosted({ edition: 'full', platform: 'desktop', expo: { state: 'ready', base } }, {}, 'ccp.game');
    assert.equal(Ed.expoAllowed(bad), false, String(base));
    assert.equal(Ed.expoBase(bad), EXPO_DEFAULT_BASE);
  }
  const preview = unhosted('127.0.0.1');
  assert.equal(Ed.expoAllowed(preview), true, 'a local preview has the files in the tree');
  assert.equal(Ed.expoBase(preview), EXPO_DEFAULT_BASE);
  assert.equal(Ed.expoAllowed(unhosted('127.0.0.1', '?edition=small')), false);
});

test('the transmission card: accept the download in full, a nudge in small, nothing before 4 llamas', () => {
  assert.equal(TRANSMISSION_AT_LLAMAS, 4);
  const absent = hosted({ edition: 'full', platform: 'desktop', expo: { state: 'absent', mb: 250 } }, {}, 'ccp.game');
  const ready = hosted({ edition: 'full', platform: 'desktop', expo: { state: 'ready', base: 'https://ccp.assets/tc-expo/' } }, {}, 'ccp.game');
  assert.equal(Ed.transmissionKind(3, absent), null);
  assert.equal(Ed.transmissionKind(4, absent), 'fetch');
  assert.equal(Ed.transmissionKind(12, absent), 'fetch', 'still absent at the wormhole: the same card first');
  assert.equal(Ed.transmissionKind(8, ready), null);
  assert.equal(Ed.transmissionKind(8, full), null, 'no Expo on this host, no card');
  assert.equal(Ed.transmissionKind(3, small), null);
  assert.equal(Ed.transmissionKind(4, small), 'nudge');
  assert.equal(Ed.transmissionKind(Number.NaN, small), null);
});

test('expo-ready from the host opens the Expo, and only where it was waited for', () => {
  try {
    Ed.setEdition({ caps: { edition: 'full', platform: 'desktop', expo: { state: 'absent', mb: 250 } } }, { hostname: 'ccp.game', hosted: true });
    assert.equal(Ed.expoAllowed(), false);
    assert.equal(Ed.expoArrived('http://evil.example/'), false);
    assert.equal(Ed.expoAllowed(), false);
    assert.equal(Ed.expoArrived('https://ccp.assets/tc-expo/'), true);
    assert.equal(Ed.expoAllowed(), true);
    assert.equal(Ed.expoBase(), 'https://ccp.assets/tc-expo/');
    assert.equal(Ed.editionState().expo.mb, 250);
    Ed.setEdition({ caps: { edition: 'small', platform: 'desktop' } }, { hostname: 'ccp.game', hosted: true });
    assert.equal(Ed.expoArrived('https://ccp.assets/tc-expo/'), false, 'small never opens it');
    Ed.setEdition({ caps: { edition: 'full', platform: 'web' } }, { hostname: PUBLIC, hosted: true });
    assert.equal(Ed.expoArrived('https://ccp.assets/tc-expo/'), false, 'a host that named no Expo cannot deliver one');
  } finally {
    Ed.setEdition(null, { noPage: true });
  }
  assert.equal(Ed.edition(), 'full');
});

test('Renewal is a full-edition thing, for everyone', () => {
  assert.equal(Ed.renewalAllowed(small), false);
  assert.equal(Ed.renewalAllowed(hosted({ edition: 'small', plan: 'prime' })), false, 'subscribers included');
  assert.equal(Ed.renewalAllowed(full), true);
  assert.equal(Ed.isSmall(small) && Ed.isFull(full) && !Ed.isFull(small) && !Ed.isSmall(full), true);
});

test('the phone caps apply on a phone in small, nowhere else', () => {
  assert.deepEqual(PHONE_CAPS, { bouncingButtons: 4, bouncingWords: 1, pictureFrames: 1, adBanners: 1 });
  const phone = { matchMedia: (q) => ({ matches: q === PHONE_QUERY }) };
  const desk = { matchMedia: () => ({ matches: false }) };
  assert.equal(Ed.phoneCaps(phone, small), PHONE_CAPS);
  assert.equal(Ed.phoneCaps(desk, small), null);
  assert.equal(Ed.phoneCaps(phone, full), null);
  assert.equal(Ed.phoneCaps(undefined, small), null);
});

test('save carry: one string, capped, checked both ways', () => {
  assert.equal(SAVE_CARRY_MAX_BYTES, 512000);
  assert.deepEqual(SAVE_FROM, ['web', 'desktop']);
  assert.deepEqual(SAVE_RESULT_REASONS, ['sign-in', 'too-big', 'offline', 'refused', 'none']);
  const save = JSON.stringify({ v: 2, run: null, life: {}, extras: {} });
  assert.deepEqual(Ed.saveOfferFrame(save), { save });
  for (const bad of ['', 'not json', '{"run":1}', '{"v":"2"}', 'null', 7, null, undefined, { v: 2 }]) assert.equal(Ed.saveOfferFrame(bad), null, String(bad));
  /* The byte cap bites on a save full of wide characters (two bytes each), well under the character cap. */
  const wide = (n) => JSON.stringify({ v: 2, pad: '\u00e9'.repeat(n) });
  const overhead = wide(0).length;
  assert.ok(Ed.saveFits(wide((SAVE_CARRY_MAX_BYTES - overhead) / 2)), 'exactly at the byte cap');
  assert.equal(Ed.saveFits(wide((SAVE_CARRY_MAX_BYTES - overhead) / 2 + 1)), false, 'the cap counts bytes, not characters');
  /* The slot never keeps a save the parser would refuse: the character cap IS the parser's limit. */
  assert.equal(SAVE_CARRY_MAX_CHARS, 400000);
  const real = (n) => { const base = JSON.stringify({ v: 2, run: null, life: {}, extras: { pad: '' } }); return JSON.stringify({ v: 2, run: null, life: {}, extras: { pad: 'x'.repeat(n - base.length) } }); };
  const atLimit = real(SAVE_CARRY_MAX_CHARS), over = real(SAVE_CARRY_MAX_CHARS + 1);
  assert.equal(atLimit.length, SAVE_CARRY_MAX_CHARS);
  assert.ok(Save.parse(atLimit), 'the parser reads a save at the limit');
  assert.equal(Save.parse(over), null, 'and refuses one character more');
  assert.equal(Ed.saveFits(atLimit), true);
  assert.equal(Ed.saveFits(over), false);
  const ok = { type: 'save-incoming', save, savedAt: 1795000000000, from: 'web' };
  assert.deepEqual(Ed.readSaveIncoming(ok), { save, savedAt: 1795000000000, from: 'web' });
  for (const bad of [null, {}, { ...ok, from: 'phone' }, { ...ok, savedAt: '1795000000000' }, { ...ok, savedAt: 0 }, { ...ok, save: 'x' }, { ...ok, save: undefined }]) {
    assert.equal(Ed.readSaveIncoming(bad), null);
  }
});

test('the seal in the app: a subscriber plays the full game now, a free account waits for the 22nd with everyone', () => {
  const before = SEAL_OPEN_AT_MS - 1;
  const deskInit = (edition) => ({ protocol: 1, caps: { platform: 'desktop', edition } });
  const reading = (edition) => Ed.readEdition(deskInit(edition), { hostname: 'ccp.game', hosted: true });
  assert.equal(sealedFor(deskInit('full'), before, 'ccp.game', false, reading('full')), false);
  assert.equal(sealedFor(deskInit('small'), before, 'ccp.game', false, reading('small')), true);
  assert.equal(sealedFor(deskInit('full'), before, 'ccp.game'), true, 'no reading handed over = sealed');
  assert.equal(sealedFor(deskInit('small'), SEAL_OPEN_AT_MS, 'ccp.game', false, reading('small')), false, 'open from the anniversary');
  assert.equal(sealedFor({ protocol: 1, caps: { platform: 'desktop', plan: 'prime' } }, before, 'ccp.game', false, reading(undefined)), true, 'the app goes by the edition, not by the plan field');
  assert.equal(sealedFor({ protocol: 1, caps: { platform: 'web', plan: 'basic' } }, before, PUBLIC, false, small), false, 'the website is never sealed (owner 2026-11-24)');
  assert.equal(sealedFor({ protocol: 1, caps: { platform: 'web', plan: 'prime' } }, before, PUBLIC, false, small), false);
  assert.equal(sealedFor({ protocol: 1, hosted: false, caps: {} }, before, PUBLIC), false);
});

test('the sealed notice in the app says subscribers and has one button for the plan card; the website keeps its text', () => {
  const web = sealMarkup(false), desk = sealMarkup(true);
  assert.match(web, /may be read by Prime members only\./);
  assert.match(web, /I have Prime/);
  assert.match(web, /Get Prime/);
  assert.equal(sealMarkup(), web);
  assert.match(desk, /may be read by subscribers only\./);
  assert.equal(/Prime/.test(desk), false, 'any subscriber has the full game in the app');
  assert.match(desk, /data-seal="plans">See plans</);
  assert.equal(/https?:/.test(desk), false, 'no link out of the app: the host opens the plan card');
  for (const html of [web, desk]) assert.match(html, /They open to everyone on <b>22 December<\/b>\./);
});

test('main.js seals with the edition reading and sends the plan nudge from the notice button', () => {
  const main = readFileSync(new URL('../main.js', import.meta.url), 'utf8');
  assert.ok(main.includes("sealedFor(init, Date.now(), location.hostname, qs.get('seal') === '1', ed)"));
  assert.ok(main.includes("sendNudge('locked-tile', { inPress: () => !!ev?.isTrusted, send: bridge.send, ed })"));
  assert.ok(main.includes("desktop: ed.platform === 'desktop'"));
});

test('main.js reads the edition once at boot, in front of the seal and the game', () => {
  const main = readFileSync(new URL('../main.js', import.meta.url), 'utf8');
  const at = main.indexOf('setEdition(init, { hostname: location.hostname, search: location.search, hosted: bridge.isHosted })');
  assert.ok(at > 0, 'the boot hook is there');
  assert.ok(at < main.indexOf('sealedFor(init'), 'before the seal');
  assert.ok(at < main.indexOf('function start(init)'), 'before the game');
});

test('main.js hands the host\'s expo-ready to the switch, wired before boot so no frame is missed', () => {
  const main = readFileSync(new URL('../main.js', import.meta.url), 'utf8');
  const at = main.indexOf("bridge.on('expo-ready', (frame) => { expoArrived(frame?.base); });");
  assert.ok(at > 0, 'the frame reaches expoArrived');
  assert.ok(at < main.indexOf('bridge.boot('), 'registered before the bridge boots');
  assert.match(main, /import \{[^}]*\bexpoArrived\b[^}]*\} from '\.\/ui\/edition\.js';/);
});

test('ui/edition.js is the one reader: no other module looks at caps.edition, caps.expo or caps.saveSlot', () => {
  const root = new URL('../', import.meta.url);
  const skip = /^(tests|dev|vendor|node_modules)[\\/]/;
  const reads = /caps\??\.(edition|expo|saveSlot)\b/;
  const hits = [];
  for (const rel of readdirSync(root, { recursive: true })) {
    const p = String(rel);
    if (!p.endsWith('.js') || skip.test(p) || p.replace(/\\/g, '/') === 'ui/edition.js') continue;
    if (reads.test(readFileSync(new URL(p.replace(/\\/g, '/'), root), 'utf8'))) hits.push(p);
  }
  assert.deepEqual(hits, []);
});
