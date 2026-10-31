// B2-d: the online lobby's pure half (hud/lobbyMath.js), the preset chat (hud/presets.js) and the door wiring.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import {
  tableLine, hostGate, isPrime, signedIn, tableRows, rowStartsIn, startsLabel, pollDue, quickDelay, cleanHost, stepHost, hostBody, HOST_DEFAULTS,
  applySeats, applySeat, seatName, seatBadges, raceTags, seatRows, readyCount, canStart, hostSeat, lobbyNote, holdNote, hostMoved, lookChoices, cycleLook, lookLabel,
  loadLook, saveLook, LOOK_KEY, countdown, serverNow, resultLines, popsLabel, errorCopy, abortCopy, closeCopy, trackName,
} from '../hud/lobbyMath.js';
import { PRESETS, POSE_BASE, presetOf, poseOf, presetByPose, poseHoldS, emoteAllowed, isPreset, CLIP_S } from '../hud/presets.js';
import { LOOK_OUTFITS, LOOK_VEHICLES, DEFAULT_LOOK, ABORTS, CLOSE, TYPES } from '../net/protocol.js';
import { BOT_NAMES } from '../hud/format.js';
import { COURSES } from '../track/catalog.js';

const G = fileURLToPath(new URL('..', import.meta.url));
const json = (f) => JSON.parse(readFileSync(G + f, 'utf8'));

test('hosting: open tables need Prime and show locked, friends rooms need a sign in', () => {
  assert.deepEqual(hostGate(null), { open: 'signin', friends: 'signin' });
  assert.deepEqual(hostGate({ displayName: 'Ana', tier: 0 }), { open: 'locked', friends: 'ok' });
  assert.deepEqual(hostGate({ displayName: 'Ana', tier: 1 }), { open: 'locked', friends: 'ok' });
  assert.deepEqual(hostGate({ displayName: 'Ana', tier: 2 }), { open: 'ok', friends: 'ok' });
  assert.equal(isPrime({ displayName: 'x', tier: 3 }), true);
  assert.equal(signedIn({ displayName: '', tier: 2 }), false);
});

test('open tables: bad rows dropped, friend first, open seats before full, soonest start, then fullest', () => {
  const rows = [
    { roomId: 'aaaaaaaaaaaa', track: 'threshold', host: { name: 'Full' }, seats: 4, filled: 4, laps: 3, startsIn: 5 },
    { roomId: 'bbbbbbbbbbbb', track: 'threshold', host: { name: 'Late' }, seats: 8, filled: 2, laps: 3, startsIn: 40 },
    { roomId: 'cccccccccccc', track: 'threshold', host: { name: 'Soon' }, seats: 8, filled: 1, laps: 2, startsIn: 10 },
    { roomId: 'dddddddddddd', track: 'threshold', host: { name: 'Pal' }, seats: 8, filled: 1, laps: 3, startsIn: null, friend: true },
    { roomId: 'BAD', host: { name: 'x' } }, null,
    { roomId: 'eeeeeeeeeeee', host: { name: 'Clamp' }, seats: 99, filled: -3, laps: 0, startsIn: 30 },
  ];
  const out = tableRows({ rows, serverMs: 1000 }, 1000);
  assert.deepEqual(out.map((r) => r.host), ['Pal', 'Soon', 'Clamp', 'Late', 'Full']);
  const c = out.find((r) => r.host === 'Clamp');
  assert.equal(c.seats, 8); assert.equal(c.filled, 0); assert.equal(c.laps, 1); assert.equal(c.seatsLabel, '0/8');
  assert.equal(out.find((r) => r.host === 'Full').full, true);
  assert.equal(out[0].startsLabel, 'Host starts');
  assert.equal(out[0].track, 'Threshold');
  assert.equal(tableRows(null, 0).length, 0);
  assert.equal(tableRows({ rows: Array.from({ length: 30 }, (_, i) => ({ roomId: String(i).padStart(12, 'a') })) }, 0).length, 20);
});

test('open tables: starts-in counts down on the list clock', () => {
  const r = { startsIn: 30 };
  assert.equal(rowStartsIn(r, 1000, 1000), 30);
  assert.equal(rowStartsIn(r, 1000, 11000), 20);
  assert.equal(rowStartsIn(r, 1000, 99000), 0);
  assert.equal(rowStartsIn({ startsIn: null }, 0, 0), null);
  assert.equal(startsLabel(0), 'Starting');
  assert.equal(startsLabel(12), 'Starts in 12 s');
  assert.equal(startsLabel(90), 'Starts in 2 min');
  assert.equal(trackName('threshold'), 'Threshold');
  assert.equal(trackName('keep'), 'The Keep');
  assert.equal(trackName('goonworks'), 'GOONWORKS');
  assert.equal(tableLine({ host: 'Ana', friend: true, laps: 2 }, null), 'Host Ana (friend), 2 laps, Host starts');
  assert.equal(trackName('glass-bay'), 'Glass-bay');
});

test('polling every 15 s like Goon, quick match retry clamped', () => {
  assert.equal(pollDue(-1e9, 0), true);
  assert.equal(pollDue(0, 14999), false);
  assert.equal(pollDue(0, 15000), true);
  assert.equal(quickDelay(undefined), 3000);
  assert.equal(quickDelay(10), 1000);
  assert.equal(quickDelay(60000), 10000);
});

test('host form: steps wrap, lane keeper stays off on open tables, body matches the lobby route', () => {
  let f = cleanHost(HOST_DEFAULTS);
  assert.deepEqual(f, { mode: 'open', track: null, laps: 3, seats: 8, botFill: true, laneKeeper: false });
  assert.equal(stepHost(f, 'laps', -1).laps, 2);
  assert.equal(stepHost({ ...f, laps: 9 }, 'laps', 1).laps, 1);
  assert.equal(stepHost({ ...f, seats: 2 }, 'seats', -1).seats, 8);
  assert.equal(stepHost(f, 'laneKeeper', 1).laneKeeper, false, 'open tables never take it');
  f = stepHost(f, 'mode', 1);
  assert.equal(f.mode, 'friends');
  f = stepHost(f, 'laneKeeper', 1);
  assert.equal(f.laneKeeper, true);
  assert.equal(stepHost(f, 'mode', 1).laneKeeper, false, 'switching back to open drops it');
  assert.equal(stepHost(f, 'botFill', 1).botFill, false);
  assert.equal(hostBody({ track: 'keep' }, null, 'threshold').track, 'keep', 'the host form names its course');
  assert.equal(hostBody({ track: 'nowhere' }, null, 'mainframe').track, 'mainframe', 'never a course this build does not have');
  const body = hostBody({ mode: 'open', laps: 12, seats: 1, laneKeeper: true }, { outfit: 'emi-outfit-cheer', vehicle: 'nope' });
  assert.deepEqual(body, { track: 'threshold', mode: 'open', seats: 2, laps: 9, botFill: true, laneKeeper: false,
    look: { outfit: 'emi-outfit-cheer', vehicle: 'emi-teacup-kart' } });
  assert.deepEqual(Object.keys(body).sort(), ['botFill', 'laneKeeper', 'laps', 'look', 'mode', 'seats', 'track']);
});

const SEATS = [
  { seat: 0, kind: 'human', name: 'Ana', platform: 'desktop', look: { outfit: null, vehicle: 'emi-teacup-kart' }, host: true, ready: true, idle: false, left: false },
  { seat: 1, kind: 'human', name: 'Bo', platform: 'phone', look: { outfit: 'emi-outfit-swim', vehicle: 'emi-vehicle-petal' }, host: false, ready: false, idle: false, left: false },
  { seat: 3, kind: 'bot', name: 'EMI bot', platform: null, look: { outfit: 'emi-outfit-pool', vehicle: 'emi-vehicle-trinket' }, host: false, ready: false, idle: false, left: false },
];

test('the load hold says who the start is waiting on', () => {
  const s = applySeats(SEATS);
  const name = s[1].name;
  assert.equal(holdNote(s, [], 0), '');
  assert.equal(holdNote(s, [1], 0), 'Waiting for ' + name + ' to load');
  assert.equal(holdNote(s, [1], 1), 'Still loading, the race waits for you');
  assert.equal(holdNote(s, [0, 1], 0), 'Waiting for 2 players to load');
  assert.equal(holdNote(s, [5], 0), '', 'an empty seat is nobody to wait for');
});

test('host start (owner 2026-10-30): joiners wait for the host, a hand-off says who hosts now', () => {
  const s = applySeats(SEATS);
  assert.equal(hostSeat(s), 0);
  assert.equal(lobbyNote(s, 0, 'open'), 'Open table, 1 of 2 ready');
  assert.equal(lobbyNote(s, 1, 'open'), 'Waiting for the host, 1 of 2 ready');
  assert.equal(lobbyNote(s, 1, 'friends'), 'Waiting for the host, 1 of 2 ready');
  const quick = applySeats(SEATS.map((x) => ({ ...x, host: false })));
  assert.equal(hostSeat(quick), -1);
  assert.equal(lobbyNote(quick, 1, 'open'), 'Open table, 1 of 2 ready', 'a quick room has nobody to wait for');
  // the host leaves the lobby: the room blanks seat 0 and hands the role to seat 1 in one seats list
  const after = applySeats([{ ...SEATS[1], host: true }, SEATS[2]]);
  assert.equal(hostMoved(s, after, 1), 'You are the host now');
  assert.equal(hostMoved(s, after, 3), 'Bo is the host now');
  assert.equal(hostMoved(s, s, 1), '');
  assert.equal(hostMoved(quick, after, 1), '', 'no host before: nothing moved');
  assert.equal(canStart(after, 1, 'lobby'), true);
  const src = readFileSync(fileURLToPath(new URL('../hud/lobby.js', import.meta.url)), 'utf8');
  assert.ok(!src.includes('Starts by itself'), 'an open table no longer starts by itself');
  assert.ok(src.includes("'Anyone can join. You start it'"));
});

test('seats: the room list fills eight seats, one seat message updates one seat', () => {
  assert.ok(TYPES.seatList(SEATS), 'fixture is a valid wire seat list');
  let s = applySeats(SEATS);
  assert.equal(s.length, 8);
  assert.deepEqual(s.map((x) => x.kind), ['human', 'human', 'empty', 'bot', 'empty', 'empty', 'empty', 'empty']);
  assert.equal(readyCount(s), 1);
  assert.equal(canStart(s, 0, 'lobby'), true);
  assert.equal(canStart(s, 1, 'lobby'), false, 'only the host');
  assert.equal(canStart(s, 0, 'countdown'), false);
  const before = s;
  s = applySeat(s, { t: 'seat', seat: 1, ready: true });
  assert.equal(before[1].ready, false, 'old list untouched');
  assert.equal(readyCount(s), 2);
  s = applySeat(s, { t: 'seat', seat: 1, paused: true });
  assert.deepEqual(seatBadges(s[1], 'racing'), ['paused']);
  s = applySeats(SEATS, s);
  assert.equal(s[1].paused, true, 'a seats refresh keeps the paused flag');
  s = applySeat(s, { t: 'seat', seat: 1, look: { outfit: 'zzz', vehicle: 'emi-vehicle-petal' } });
  assert.deepEqual(s[1].look, { outfit: null, vehicle: 'emi-vehicle-petal' }, 'unknown ids fall back');
  s = applySeat(s, { t: 'seat', seat: 1, left: true, reason: 'gone' });
  assert.deepEqual(seatBadges(s[1]), ['left']);
  assert.equal(s[1].paused, false);
  assert.equal(applySeat(s, { seat: 9 }), s);
});

test('seats: names, badges and in-race tags', () => {
  const s = applySeats(SEATS);
  assert.equal(seatName(s[0]), 'Ana');
  assert.equal(seatName(s[2]), 'Open seat');
  assert.equal(seatName(s[3]), BOT_NAMES[3]);
  assert.deepEqual(seatBadges(s[0]), ['host', 'ready']);
  assert.deepEqual(seatBadges(s[0], 'racing'), ['host'], 'no ready badge once racing');
  assert.deepEqual(seatBadges(s[3]), ['bot']);
  assert.deepEqual(seatBadges(s[2]), []);
  const t = applySeat(applySeat(applySeat(s, { seat: 1, paused: true }), { seat: 0, idle: true }), { seat: 3, left: true });
  assert.deepEqual(raceTags(t), { 0: 'away', 1: 'paused', 3: 'left' });
  assert.deepEqual(seatBadges(applySeat(t, { seat: 1, idle: true })[1]), ['paused'], 'paused wins over away');
  const rows = seatRows(s, 1, 'lobby', 4);
  assert.equal(rows.length, 4);
  assert.equal(rows[1].me, true); assert.equal(rows[1].n, 2);
});

test('looks: Astra files give the labels, only wire ids are offered, unknown ids fall back', () => {
  const w = json('art/models/emi-bot-wardrobe.json'), v = json('art/models/emi-vehicle-variants.json');
  const c = lookChoices(w, v);
  assert.equal(c.outfit[0].id, null);
  assert.deepEqual(c.outfit.slice(1).map((x) => x.id), [...LOOK_OUTFITS]);
  assert.deepEqual(c.vehicle.map((x) => x.id), [...LOOK_VEHICLES]);
  for (const o of w.outfits) assert.equal(c.outfit.find((x) => x.id === o.id)?.label, o.label, o.id + ' label from the file');
  for (const o of v.vehicles) assert.equal(c.vehicle.find((x) => x.id === o.id)?.label, o.label, o.id + ' label from the file');
  assert.equal(c.vehicle[0].label, 'Teacup');
  const bare = lookChoices(null, null);
  assert.equal(bare.outfit.find((x) => x.id === 'emi-outfit-snow-day').label, 'Snow Day');
  assert.equal(bare.vehicle.find((x) => x.id === 'emi-vehicle-trinket').label, 'Trinket');
  let l = cycleLook({ outfit: 'not-a-thing', vehicle: 'nope' }, 'outfit', 1, c);
  assert.deepEqual(l, { outfit: LOOK_OUTFITS[0], vehicle: DEFAULT_LOOK.vehicle });
  l = cycleLook(DEFAULT_LOOK, 'outfit', -1, c);
  assert.equal(l.outfit, LOOK_OUTFITS[LOOK_OUTFITS.length - 1], 'wraps backwards');
  l = cycleLook(DEFAULT_LOOK, 'vehicle', 1, c);
  assert.equal(l.vehicle, 'emi-vehicle-petal');
  assert.equal(lookLabel(l, 'vehicle', c), 'Petal porcelain');
  assert.equal(lookLabel(DEFAULT_LOOK, 'outfit', c), 'None');
});

test('looks: kept in this browser, bad or blocked storage reads as the default', () => {
  const mem = new Map(), store = { getItem: (k) => mem.get(k) ?? null, setItem: (k, val) => mem.set(k, val) };
  assert.deepEqual(loadLook(store), DEFAULT_LOOK);
  assert.equal(saveLook(store, { outfit: 'emi-outfit-varsity', vehicle: 'emi-vehicle-trinket', extra: 1 }), true);
  assert.deepEqual(JSON.parse(mem.get(LOOK_KEY)), { outfit: 'emi-outfit-varsity', vehicle: 'emi-vehicle-trinket' });
  assert.deepEqual(loadLook(store), { outfit: 'emi-outfit-varsity', vehicle: 'emi-vehicle-trinket' });
  mem.set(LOOK_KEY, '{bad');
  assert.deepEqual(loadLook(store), DEFAULT_LOOK);
  const boom = { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); } };
  assert.deepEqual(loadLook(boom), DEFAULT_LOOK);
  assert.equal(saveLook(boom, DEFAULT_LOOK), false);
  assert.deepEqual(loadLook(null), DEFAULT_LOOK);
});

test('countdown runs on server time: grid, the race count, go', () => {
  const start = { countAt: 10000, startAt: 13000 };
  assert.deepEqual(countdown(7001, start), { phase: 'grid', n: 3, label: 'Grid in 3' });
  assert.equal(countdown(9999, start).label, 'Grid in 1');
  assert.deepEqual(countdown(10000, start), { phase: 'count', n: 3, label: '3' });
  assert.equal(countdown(12500, start).label, '1');
  assert.equal(countdown(13000, start).phase, 'go');
  assert.equal(countdown(0, {}).phase, 'none');
  assert.equal(serverNow(500, { now: () => 9000 }), 9000, 'the room clock wins');
  assert.equal(serverNow(1500, null, { serverMs: 50000 }, 1000), 50500, 'else welcome serverMs plus local time since');
  assert.equal(serverNow(1500, { now: () => NaN }, null, 0), 1500);
});

test('results: by place, You, bots by our names, no time for a DNF, pops', () => {
  const rows = [
    { seat: 2, place: 2, name: 'EMI bot', bot: true, finished: true, timeMs: 185500, bestLapMs: 60000, pops: 1 },
    { seat: 0, place: 1, name: 'Ana', bot: false, finished: true, timeMs: 181234, bestLapMs: 59001, pops: 212 },
    { seat: 5, place: 3, name: 'Bo', bot: false, finished: false, timeMs: null, bestLapMs: null, pops: 30 },
  ];
  assert.ok(TYPES.finishRows(rows), 'fixture is a valid wire finish list');
  const l = resultLines(rows, 5);
  assert.deepEqual(l.map((x) => x.name), ['Ana', BOT_NAMES[2], 'You']);
  assert.equal(l[0].time, '3:01.23'); assert.equal(l[0].best, '0:59.00');
  assert.equal(l[2].time, 'no time'); assert.equal(l[2].me, true); assert.equal(l[2].place + l[2].suf, '3rd');
  assert.equal(popsLabel(1), '1 pop'); assert.equal(popsLabel(212), '212 pops');
  assert.deepEqual(resultLines(null, 0), []);
});

test('copy: every lobby error, abort and close code has a line; dry, no exclamation, no dashes', () => {
  for (const c of ['bad', 'auth', 'tier', 'friends', 'gone', 'full', 'started', 'stakes-off', 'rate', 'net', 'off']) assert.ok(errorCopy(c).length > 3, c);
  assert.equal(errorCopy('what'), errorCopy('bad'));
  for (const r of ABORTS) assert.notEqual(abortCopy(r), '', r);
  for (const code of Object.values(CLOSE)) assert.notEqual(closeCopy(code), 'Lost the room', String(code));
  assert.equal(closeCopy(1006), 'Lost the room');
  for (const f of ['hud/lobby.js', 'hud/lobbyMath.js', 'hud/presets.js', 'hud/cards.js']) {
    const src = readFileSync(G + f, 'utf8');
    assert.ok(!/[–—]/.test(src), f + ' has a dash');
    for (const s of src.match(/'[^'\n]*'/g) || []) assert.ok(!s.includes('!') || /!==?|!\w|\(!/.test(s), f + ': ' + s);
    assert.ok(!/speechSynthesis/.test(src), f);
  }
});

test('preset chat: wire-safe ids, poses from 16, clips and faces from Astra files, one per 2 s', () => {
  const bind = json('art/models/emi-kart-event-bindings.json');
  const clips = new Map(bind.actions.map((a) => [a.clip, a.durationSec]));
  const faces = new Set(bind.faceAtlas.ids);
  assert.ok(PRESETS.length >= 6 && PRESETS.length <= 16);
  assert.equal(new Set(PRESETS.map((p) => p.code)).size, PRESETS.length);
  PRESETS.forEach((p, i) => {
    assert.ok(TYPES.code(p.code), p.code + ' fits the wire code type');
    assert.ok(p.text.length > 0 && p.text.length <= 12, p.text);
    assert.ok(!/[!–—]/.test(p.text), p.text);
    assert.ok(faces.has(p.face), p.code + ' face ' + p.face);
    if (p.clip) { assert.ok(clips.has(p.clip), p.code + ' clip ' + p.clip); assert.equal(CLIP_S[p.clip], clips.get(p.clip), p.clip + ' length'); }
    assert.equal(poseOf(p.code), POSE_BASE + i);
    assert.equal(presetByPose(POSE_BASE + i), p);
    assert.ok(POSE_BASE + i <= 255);
  });
  assert.equal(presetOf('free text'), null); assert.equal(isPreset('hi'), true);
  assert.equal(poseOf('nope'), 0); assert.equal(presetByPose(1), null, 'trick poses are not emotes'); assert.equal(presetByPose(0), null);
  assert.equal(poseHoldS('hi'), 1.5); assert.equal(poseHoldS('sec'), 1); assert.equal(poseHoldS('x'), 0);
  assert.equal(emoteAllowed(-Infinity, 0), true);
  assert.equal(emoteAllowed(1000, 2999), false);
  assert.equal(emoteAllowed(1000, 3000), true);
});

test('wiring: one Race online door opens the lobby in the card; no free text input anywhere in the lobby', () => {
  const cards = readFileSync(G + 'hud/cards.js', 'utf8'), lobby = readFileSync(G + 'hud/lobby.js', 'utf8');
  assert.ok(/btn\('online', '<b>Race online<\/b>/.test(cards), 'the door');
  assert.ok(!/Online opens later/.test(cards) && !/c-door soon/.test(cards), 'no longer a soon door');
  assert.ok(/show\('lobby', \{ view: 'home' \}\)/.test(cards), 'the door opens the lobby');
  assert.ok(/a\.startsWith\('lb-'\)\) \{ lobby\.act\(el\)/.test(cards), 'lobby buttons route to the lobby');
  assert.ok(/kind === 'lobby' && lobby\.nav\(dir\)/.test(cards), 'pad and arrows reach the lobby rows');
  assert.ok(/kind === 'lobby'\) lobby\.back\(\)/.test(cards), 'Escape goes up one view');
  assert.ok(!/<input|<textarea|contenteditable/.test(lobby), 'no text entry');
  assert.ok(!/\buid\b[^;]*innerHTML|data-uid/.test(lobby), 'uids never reach the DOM');
  assert.ok(/"lb-leave"\]'\)\?\.focus/.test(lobby), 'Escape in a room only moves focus to Leave');
});

// ---- the Your look picker: owned only, and the extras ride the room look message (fix wave 2, F4c) ------------------

import { ownedChoices, fitLook, lookKeys } from '../hud/lobbyMath.js';
import { roomLook } from '../net/protocol.js';

test('look picker: offers only the rides and outfits this account has, plus each owned extra', () => {
  const none = lookChoices(null, null, ownedChoices([], new Set()));
  assert.deepEqual(none.vehicle.map((c) => c.id), ['emi-teacup-kart', 'emi-vehicle-lovebomb'], 'the teacup and the free kart');
  assert.equal(none.outfit.some((c) => /pit-crew|ringmaster|ballerina/.test(c.id || '')), false, 'no shop outfit unowned');
  assert.equal(none.paint, undefined); assert.equal(none.umbrella, undefined); assert.equal(none.trail, undefined);
  const rich = lookChoices(null, null, ownedChoices(['cos.kart.body.petal', 'cos.kart.outfit.ballerina', 'cos.kart.paint.pattern-bundle', 'cos.kart.umbrella.lace', 'cos.prime.trail.podium'], new Set()));
  assert.ok(rich.vehicle.some((c) => c.id === 'emi-vehicle-petal'));
  assert.ok(!rich.vehicle.some((c) => c.id === 'emi-vehicle-trinket'), 'an unowned body stays out');
  assert.ok(rich.outfit.some((c) => c.id === 'emi-outfit-ballerina'));
  assert.deepEqual(rich.paint.map((c) => c.id), [null, 'hearts', 'spiral']);
  assert.deepEqual(rich.umbrella.map((c) => c.id), [null, 'lace']);
  assert.deepEqual(rich.trail.map((c) => c.id), [null, 'podium']);
  assert.deepEqual(lookKeys({ vehicle: 'emi-vehicle-petal' }, rich), ['outfit', 'vehicle', 'umbrella', 'trail'], 'paint needs a custom skin');
  const skin = 'e7a7c080344ed74b80f1cdd7c94e7dfff1d20';
  assert.deepEqual(lookKeys({ vehicle: 'emi-vehicle-petal', skin }, rich), ['outfit', 'vehicle', 'paint', 'umbrella', 'trail']);
  assert.equal(lookChoices().owned, undefined, 'no ownership: every wire id, as before');
});

test('look picker: an unowned pick comes off, the extras cycle to none and back, the message keeps them', () => {
  const c = lookChoices(null, null, ownedChoices(['cos.kart.umbrella.lace'], new Set()));
  const fit = fitLook({ outfit: 'emi-outfit-ballerina', vehicle: 'emi-vehicle-trinket', umbrella: 'lace', trail: 'podium' }, c);
  assert.deepEqual(fit, { outfit: null, vehicle: 'emi-vehicle-lovebomb', umbrella: 'lace' });
  const off = cycleLook(fit, 'umbrella', 1, c);
  assert.equal('umbrella' in off, false, 'none takes the key off');
  assert.deepEqual(cycleLook(off, 'umbrella', 1, c).umbrella, 'lace');
  assert.deepEqual(roomLook(fit), { outfit: null, vehicle: 'emi-vehicle-lovebomb', umbrella: 'lace' });
  assert.deepEqual(fitLook(fit, lookChoices()), fit, 'choices without ownership leave a look alone');
});

test('look picker: the lobby sends the whole look, and boot hands it the garage ownership', () => {
  const lobby = readFileSync(fileURLToPath(new URL('../hud/lobby.js', import.meta.url)), 'utf8');
  assert.match(lobby, /send\(\{ t: 'look', \.\.\.roomLook\(st\.look\) \}\)/);
  assert.doesNotMatch(lobby, /send\(\{ t: 'look', outfit: st\.look\.outfit, vehicle: st\.look\.vehicle \}\)/);
  const boot = readFileSync(fileURLToPath(new URL('../boot.js', import.meta.url)), 'utf8');
  assert.match(boot, /lookOwned:\(\)=>ownedChoices\(shared\.owned\.grants,unlockedSet\(loadRecord\(\)\)\)/);
});

test('the host picks the course: it wraps, and brings its own laps unless the host changed them', () => {
  let f = cleanHost({ ...HOST_DEFAULTS, track: 'threshold', laps: 3 });
  const seen = [];
  for (let i = 0; i < 5; i++) { f = stepHost(f, 'track', 1); seen.push([f.track, f.laps]); }
  assert.deepEqual(seen, [['mainframe', 3], ['keep', 2], ['boudoir', 2], ['goonworks', 2], ['threshold', 3]]);
  assert.equal(stepHost(f, 'track', -1).track, 'goonworks');
  const mine = stepHost(stepHost(f, 'laps', 1), 'track', 1);
  assert.equal(mine.laps, 4, 'a hand-picked lap count stays');
});

test('every course in the catalog carries the lap count of its track file', () => {
  for (const c of COURSES) assert.equal(JSON.parse(readFileSync(new URL('../tracks/' + c.id + '.json', import.meta.url), 'utf8')).laps, c.laps, c.id);
});
