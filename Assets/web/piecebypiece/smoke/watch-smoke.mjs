/* ============================================================================
 * smoke/watch-smoke.mjs - the stands (net/watch.js), in node, no browser:
 *
 *   node smoke/watch-smoke.mjs
 *
 * The spectate v2 fake (smoke/fake-tape.mjs) plays a real game on its own
 * timeline and serves it as the brief's tape: a head, upto/n and chunk/n once
 * they are older than the delay. net/tape.js has its own smoke; this one checks
 * the DRIVER the board sees:
 *
 *   NOBODY IN THE STANDS MOVES. canPick, tryMove, legalTargets and the verbs
 *   all refuse; no seat, both sides graded.
 *   THE SNAPSHOT AND THE TAPE land on the board through the rules, moves at
 *   their moment, the clocks derived on the delayed timeline.
 *   THE BUS: watch-info, players, watchers, fx-heat / fx-fire, watch-reactions,
 *   watch-react out, watch-seat turns the camera.
 *   A WORDED REFUSAL CLOSES THE STANDS (watch-closed), a 404 not_deployed too.
 *   THE WIRE: the worded 403s and the 404 not_deployed, and watchable:false
 *   only when the player said no.
 *   THE LOBBY'S PLAYING ROWS carry match_id / watchable / watchers through.
 *   THE SWITCH: startWatchMatch puts the stands in the chair with no seat and
 *   both sides graded, and switchBack disposes them.
 * ==========================================================================*/

import { Chess } from '../vendor/chess.js';
import { createBus } from '../game/events.js';
import * as api from '../net/api.js';
import { createWatchSession, watchLine, playerOf, WATCH_DELAY_MS } from '../net/watch.js';
import { LEAD_MS, lagOf } from '../net/tape.js';
import { createDriverSwitch, startWatchMatch } from '../net/online.js';
import { createOnlineMatch } from '../net/match.js';
import { createServerLobby } from '../net/lobbyServer.js';
import { fakeTapeServer, T0, DELAY, CHUNK } from './fake-tape.mjs';

let passed = 0;
const failures = [];
const ok = (what, cond) => { if (cond) passed++; else failures.push(what); };
const eq = (what, got, want) => ok(`${what} (got ${JSON.stringify(got)}, wanted ${JSON.stringify(want)})`, JSON.stringify(got) === JSON.stringify(want));
const settle = async (n = 30) => { for (let i = 0; i < n; i++) await Promise.resolve(); };
const C = CHUNK;
const LAG = lagOf(DELAY, CHUNK, LEAD_MS);
const at = (srv, ms) => { srv.st.now = T0 + ms + LAG; };

function fakeBoard() {
  const men = new Map();
  const log = [];
  return {
    men, log,
    setSide(side, instant) { log.push(`side:${side}:${!!instant}`); },
    buzzCheck(sq) { log.push(`buzz:${sq}`); },
    pieces: {
      setPosition(map) { men.clear(); for (const sq of Object.keys(map)) men.set(sq, map[sq].side + map[sq].type); log.push('setPosition'); },
      move(from, to) { const m = men.get(from); men.delete(from); if (m) men.set(to, m); log.push(`move:${from}${to}`); },
      remove(sq) { men.delete(sq); log.push(`remove:${sq}`); },
    },
  };
}

const env = (status, data, error = null) => ({ ok: status >= 200 && status < 300, status, data, error });
const mono = (srv) => () => srv.st.now - T0 + 9000;

/* ------------------------------------------------------------------ pure bits */
{
  eq('the page\'s default delay is the server\'s (12 s)', WATCH_DELAY_MS, DELAY);
  eq('a player off the wire', playerOf({ display_name: 'velvet', rating: 1301.4 }), { name: 'velvet', rating: 1301 });
  eq('a player with no rating', playerOf({ display_name: 'moth', rating: null }), { name: 'moth', rating: null });
  eq('the stands say who won, with no "you"', watchLine({ result: 'resign', winner: 'b' }), 'white resigned, black wins');
  eq('a flag', watchLine({ result: 'flag', winner: 'w' }), "black's flag fell, white wins");
  eq('a draw by repetition reads plainly', watchLine({ result: 'draw', reason: 'threefold' }), 'a draw by threefold');
}

/* --------------------------------------------------------------- the session */
{
  const srv = fakeTapeServer();
  srv.play('e2e4', T0 + 1 * C); srv.play('e7e5', T0 + 2 * C); srv.play('g1f3', T0 + 4.5 * C);
  srv.play('b8c6', T0 + 7 * C);
  srv.addFx({ at: T0 + 7.5 * C, side: 'w', ply: 4, heat: [[0, 33], [C / 2, 66]], fired: [[0.6 * C, 'flash'], [0.65 * C, 'gifRain']] });
  srv.finish(T0 + 9.5 * C, '0-1', 'resign');
  at(srv, 6.25 * C);
  const bus = createBus();
  const board = fakeBoard();
  const heard = { turn: 0, watchers: [], info: [], players: null, gameover: null, closed: null, heat: [], fire: [], reactions: [] };
  bus.on('turn', () => heard.turn++);
  bus.on('watchers', (p) => heard.watchers.push(p.count));
  bus.on('watch-info', (p) => heard.info.push(p));
  bus.on('players', (p) => { heard.players = p; });
  bus.on('gameover', (p) => { heard.gameover = p; });
  bus.on('watch-closed', (p) => { heard.closed = p; });
  bus.on('fx-heat', (p) => heard.heat.push(p));
  bus.on('fx-fire', (p) => heard.fire.push(p));
  bus.on('watch-reactions', (p) => heard.reactions.push(p.counts));
  const first = await srv.api.watchHead('m_watch');
  const w = createWatchSession({ bus, board, matchId: 'm_watch', initial: first.data, api: srv.api, now: mono(srv), autoStart: false, random: () => 0 });
  await w.start();
  await settle();
  eq('the door\'s head is used: no second GET watch at the join', srv.api.calls.filter((c) => c === 'head').length, 1);
  eq('the stands see only what is behind the playhead', w.plies(), 3);
  eq('the board shows the delayed position', board.men.get('f3'), 'wn');
  eq('black to move, as of the playhead', w.turn(), 'b');
  eq('nobody here owns a colour', w.seats, []);
  eq('both sides are graded', w.graded, ['w', 'b']);
  eq('no man can be picked up', ['e7', 'b8', 'e4', 'f3'].map((sq) => w.canPick(sq)), [false, false, false, false]);
  eq('a move is refused', w.tryMove('b8', 'c6'), null);
  eq('no targets', w.legalTargets('b8'), []);
  eq('and the move did not land', w.plies(), 3);
  eq('the watcher count arrived', heard.watchers, [2]);
  eq('the players arrive named and rated', w.players(), { w: { name: 'velvet', rating: 1301 }, b: { name: 'moth', rating: null } });
  eq('players on the bus, for the HUD', [heard.players.white.name, heard.players.watching], ['velvet', true]);
  const info = heard.info[heard.info.length - 1];
  eq('watch-info: delay, how far behind, watchers, both players, the seat', [info.delayMs, info.behindMs, info.watchers, info.players.b.name, info.seat], [DELAY, LAG, 2, 'moth', 'w']);
  ok('the camera was set once, from white', board.log.filter((l) => l.startsWith('side:')).length === 1);

  // THE CLOCKS. Black has been thinking 1.75 chunks at the playhead (g1f3 at 4.5 C, playhead 6.25 C).
  const wantB = srv.moves[2].clk[1] - 1.75 * C;
  ok(`black's clock reads the delayed balance (got ${w.clock.remaining('b')}, wanted ${wantB})`, Math.abs(w.clock.remaining('b') - wantB) <= 1);
  srv.st.now += 1000;
  ok('and runs on from there', Math.abs(w.clock.remaining('b') - (wantB - 1000)) <= 1);
  eq('white\'s clock stands still while black thinks', w.clock.remaining('w'), srv.moves[2].clk[0]);

  // THE TAPE. b8c6 lands at its moment, then white's Distraction, then the crowd.
  at(srv, 6.3 * C);
  srv.st.reactions.set(Math.floor(srv.st.now / 3000) - 1, { heart: 2 });
  for (let ph = 6.3 * C; ph <= 7.05 * C; ph += 0.05 * C) { at(srv, ph); w._tick(); await settle(); }
  eq('b8c6 is on the board at its moment', board.men.get('c6'), 'bn');
  ok(`white's clock now runs, black's stopped (w ${w.clock.remaining('w')})`, w.clock.snapshot().active === 'w');
  for (let ph = 7.1 * C; ph <= 8.3 * C; ph += 0.05 * C) { at(srv, ph); w._tick(); await settle(); }
  eq('the turn player\'s heat on the bus, 0..1', heard.heat.map((h) => [h.side, Math.round(h.heat * 99)]), [['w', 33], ['w', 66]]);
  eq('and the fired kinds, in order', heard.fire.map((f) => f.kind), ['flash', 'gifRain']);
  eq('the crowd\'s reactions reach the bus', heard.reactions[0], { heart: 2 });

  // THE STANDS TALK BACK.
  bus.emit('watch-react', { id: 'clap' });
  await settle();
  eq('a preset reaction goes out', srv.api.posts.map((p) => p.counts), [{ clap: 1 }]);
  bus.emit('watch-react', { id: 'go win it' });
  await settle();
  eq('free text never does', srv.api.posts.length, 1);
  bus.emit('watch-seat', { seat: 'b' });
  eq('the seat swap turns the camera', board.log[board.log.length - 1], 'side:b:false');
  eq('and says so on watch-info', heard.info[heard.info.length - 1].seat, 'b');

  // THE END (on the tape from the start: a served chunk never changes).
  for (let ph = 8.35 * C; ph <= 9.6 * C; ph += 0.05 * C) { at(srv, ph); w._tick(); await settle(); }
  ok('the game is over in the stands', w.isOver());
  eq('gameover carries the winner', heard.gameover, { result: 'resign', winner: 'b' });
  eq('the record has no seat', w.record().me, null);
  w.dispose();
}
{
  // watch-info waits for the tape's own head: behindMs is the head's from the very first emit
  const srv = fakeTapeServer({ delay: 9000 });
  srv.st.now = T0 + 20 * C;
  const bus = createBus();
  const infos = [];
  bus.on('watch-info', (p) => infos.push(p));
  const w = createWatchSession({ bus, board: fakeBoard(), matchId: 'm_info', api: srv.api, now: mono(srv), autoStart: false, random: () => 0 });
  const started = w.start();
  eq('nothing on watch-info before the head is in', infos.length, 0);
  await started; await settle();
  eq('the first watch-info already carries the head\'s numbers', [infos[0] && infos[0].delayMs, infos[0] && infos[0].behindMs], [9000, lagOf(9000, C, LEAD_MS)]);
  w.dispose();
}

/* ------------------------------------------------------------ closing */
{
  const srv = fakeTapeServer();
  srv.play('d2d4', T0 + 1000);
  at(srv, 2000);
  const bus = createBus();
  let closed = null;
  bus.on('watch-closed', (p) => { closed = p; });
  const w = createWatchSession({ bus, board: fakeBoard(), matchId: 'm_watch', api: srv.api, now: mono(srv), autoStart: false });
  await w.start(); await settle();
  eq('with no head in hand the session asks for one', srv.api.calls[0], 'head');
  srv.st.chunkRefuse = { status: 403, error: 'not_watchable' };
  at(srv, 5000 - LEAD_MS + 10); w._tick(); await settle();
  eq('a worded refusal closes the stands', closed, { matchId: 'm_watch', reason: 'not_watchable' });
  eq('and the stands say so', w.status(), 'closed');
  const n = srv.api.calls.length;
  at(srv, 60000); w._tick(); await settle();
  eq('no call after the close', srv.api.calls.length, n);
  w.dispose();
}
{
  // the route is off server-side: the stands close at once, no chunk at all
  const srv = fakeTapeServer();
  srv.st.refuse = { status: 404, error: 'not_deployed' };
  const bus = createBus();
  let closed = null;
  bus.on('watch-closed', (p) => { closed = p; });
  const w = createWatchSession({ bus, board: fakeBoard(), matchId: 'm_off', api: srv.api, now: mono(srv), autoStart: false });
  await w.start(); await settle();
  eq('not deployed closes the stands', closed && closed.reason, 'not_deployed');
  eq('and never fetches a chunk', srv.api.calls.filter((c) => c.startsWith('chunk') || c.startsWith('upto')).length, 0);
  w.dispose();
}

/* ------------------------------------------------------------------ the wire */
{
  const sent = [];
  let answer = { status: 200, body: '{}' };
  api.setTransport(async (method, path, body) => { sent.push({ method, path, body }); return answer; });
  await api.watch('m 1');
  eq('GET watch', [sent[0].method, sent[0].path], ['GET', '/v2/pbp/watch/m%201']);
  ok('the v1 long poll is gone', typeof api.watchEvents === 'undefined' && !('watchEvents' in api.ROUTES));
  answer = { status: 404, body: JSON.stringify({ error: 'not_deployed' }) };
  eq('a worded 404 not_deployed', (await api.watch('m1')).error, 'not_deployed');
  answer = { status: 404, body: JSON.stringify({ error: 'not_found' }) };
  eq('a 404 not_found is still not found', (await api.watch('m1')).error, 'not_found');
  answer = { status: 404, body: '' };
  eq('a bare 404 is not deployed', (await api.watch('m1')).error, 'not_deployed');
  answer = { status: 403, body: JSON.stringify({ error: 'not_watchable' }) };
  eq('403 not_watchable', (await api.watch('m1')).error, 'not_watchable');
  answer = { status: 403, body: JSON.stringify({ error: 'your_match' }) };
  eq('403 your_match', (await api.watch('m1')).error, 'your_match');
  answer = { status: 403, body: JSON.stringify({ error: 'forbidden' }) };
  eq('any other 403 is still unauthorized', (await api.watch('m1')).error, 'unauthorized');

  // "Let people watch my games": only a no travels
  answer = { status: 200, body: '{}' };
  sent.length = 0;
  await api.lobbyEnter(api.timeControl(300000, 0)); await api.quick(api.timeControl(300000, 0)); await api.join('p_x');
  eq('on (the default): no watchable field', sent.map((s) => 'watchable' in s.body), [false, false, false]);
  let allow = false;
  api.setWatchPolicy(() => allow);
  sent.length = 0;
  await api.lobbyEnter(api.timeControl(300000, 0)); await api.quick(api.timeControl(300000, 0)); await api.join('p_x');
  eq('off: watchable:false on enter, quick and join', sent.map((s) => s.body.watchable), [false, false, false]);
  sent.length = 0;
  await api.challenge('p_x');
  await api.acceptChallenge('c_1');
  eq('off: watchable:false on a challenge and its accept too (the server ignores it on a friend invite)', sent.map((s) => s.body.watchable), [false, false]);
  api.setWatchPolicy(() => { throw new Error('boom'); });
  sent.length = 0;
  await api.quick(api.timeControl(300000, 0));
  ok('a policy that throws reads as yes', !('watchable' in sent[0].body));
  api.setWatchPolicy(null);
  api.setTransport(null);
}

/* ------------------------------------------------------- the lobby's playing rows */
{
  const lobbyApi = {
    lobbyList: async () => env(200, { ok: true, players: [], playing: [
      { white: 'lace', black: 'hush', time_control: { initial_ms: 600000, increment_ms: 0 }, moves: 4, match_id: 'm_a', watchable: true, watchers: 3 },
      { white: 'velvet', black: 'moth', moves: 9, watchable: false },
      { white: 'rose', black: 'thorn', moves: 1, watchable: true },
    ] }),
    challenges: async () => env(200, { ok: true, incoming: [], outgoing: [] }),
  };
  const lobby = createServerLobby({ api: lobbyApi, force: true, pollMs: 5 });
  const t = await lobby.tables();
  eq('a watchable row carries its match id and count', [t.playing[0].matchId, t.playing[0].watchable, t.playing[0].watchers], ['m_a', true, 3]);
  eq('an unwatchable row carries none', [t.playing[1].matchId, t.playing[1].watchable], [null, false]);
  eq('watchable with no id is not watchable', t.playing[2].watchable, false);
  lobby.dispose?.();
}

/* ------------------------------------------- a player sees the crowd (seat routes) */
{
  const bus = createBus();
  const heard = [];
  bus.on('watch-reactions', (p) => heard.push(p.counts));
  let t = 0;
  const m = createOnlineMatch({ bus, board: fakeBoard(), matchId: 'm_seat', color: 'w', api: {}, now: () => t, autoStart: false });
  m._applyEnvelope({ events: [], watchers: 3, reactions: { fire: 2, hello: 5 } });
  m._applyEnvelope({ events: [], watchers: 3, reactions: { fire: 2 } });
  eq('the seat poll reactions reach the bus once per bucket, known ids only', heard, [{ fire: 2 }]);
  t += 3500;
  m._applyEnvelope({ events: [], watchers: 3, reactions: { fire: 2 } });
  m._applyEnvelope({ events: [], watchers: 3, reactions: {} });
  eq('the same counts a bucket later are a new bucket; an empty one says nothing', heard.length, 2);
  m.dispose();
}
{
  // reactions_buckets: the last three completed buckets, deduped by b
  const bus = createBus();
  const heard = [];
  bus.on('watch-reactions', (p) => heard.push(p.counts));
  const m = createOnlineMatch({ bus, board: fakeBoard(), matchId: 'm_seat2', color: 'b', api: {}, now: () => 0, autoStart: false });
  m._applyEnvelope({ events: [], watchers: 2, reactions: { wow: 1 }, reactions_buckets: [{ b: 10, r: { fire: 1 } }, { b: 11, r: { wow: 1 } }] });
  m._applyEnvelope({ events: [], watchers: 2, reactions: { wow: 1 }, reactions_buckets: [{ b: 11, r: { wow: 1 } }, { b: 10, r: { fire: 1 } }] });
  eq('each bucket is told once, oldest first, whatever order it came in', heard, [{ fire: 1 }, { wow: 1 }]);
  m._applyEnvelope({ events: [], watchers: 2, reactions: { wow: 1 }, reactions_buckets: [{ b: 11, r: { wow: 1 } }, { b: 12, r: { wow: 1 } }, { b: 14, r: { clap: 2, hello: 4 } }] });
  eq('the same counts in a new bucket are new; a skipped poll loses nothing', heard, [{ fire: 1 }, { wow: 1 }, { wow: 1 }, { clap: 2 }]);
  m.dispose();
}

/* --------------------------------------------------------------------- the switch */
{
  const srv = fakeTapeServer();
  srv.play('e2e4', T0 + 1000);
  at(srv, 2000);
  const bus = createBus();
  const board = fakeBoard();
  const hot = { rules: { chess: new Chess() }, start() {}, canPick: () => true, plies: () => 0, turn: () => 'w', isOver: () => false, result: () => null, record: () => ({}) };
  const game = createDriverSwitch(hot);
  let local = null;
  bus.on('local', (p) => { local = p; });
  const first = await srv.api.watchHead('m_watch');
  const d = startWatchMatch({ bus, board, game, match: { id: 'm_watch', state: first.data }, api: srv.api, now: mono(srv) });
  await settle();
  eq('the local event names no seat and grades both', [local.mode, local.sides, local.graded], ['watch', [], ['w', 'b']]);
  ok('the switch says the stands are in the chair', game.isWatch === true && game.isOnline === true);
  eq('through the switch: no seat, both graded', [game.seats, game.graded], [[], ['w', 'b']]);
  eq('through the switch: nothing to pick', game.canPick('e2'), false);
  eq('through the switch: the watcher count', game.watchers(), 2);
  eq('through the switch: the move behind the playhead', game.plies(), 1);
  game.switchBack();
  ok('switchBack puts the hotseat back', game.current === hot && game.isWatch === false);
  const n = srv.api.calls.length;
  d._tick(); await settle();
  eq('and the stands were disposed (the tape no longer calls)', srv.api.calls.length, n);
  eq('a hotseat still grades both through the switch', game.graded, ['w', 'b']);
}

console.log(`watch-smoke: ${passed} checks passed`);
for (const f of failures) console.log('FAILED ' + f);
process.exit(failures.length ? 1 : 0);
