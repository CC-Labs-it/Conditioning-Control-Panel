/* ============================================================================
 * smoke/tape-smoke.mjs - the stands' tape (net/tape.js) and its wire
 * (net/api.js), in node, no browser:
 *
 *   node smoke/tape-smoke.mjs
 *
 * A fake chunk server (smoke/fake-tape.mjs, shaped like the real one) plays a
 * game on its own timeline. Every time here is written in chunks (C) and the
 * lead, off the exported constants, so a retune of the server moves nothing.
 * Checked: the chunk math; a join mid-game is upto + the playhead's chunk with
 * its past folded in; the next chunk is fetched before the playhead needs it,
 * late by the session's jitter; moves, heat and fires land at their moment and
 * only for the turn in progress; a stale fire is dropped; a refused move or a
 * slept tab re-snaps, a hole in seq does not; 425 / 410 / 429 / 503 are retried
 * and a worded 403 closes; the backoff only ends when a snapshot applies; the
 * opening waits for chunk 0; a v1 head closes as not deployed; the end riding
 * the mating move; the offset errs late; the reaction poll and the batched
 * sender, also after the end; and the api routes, the public road and the
 * request sanitising.
 * ==========================================================================*/

import * as api from '../net/api.js';
import { createTape, chunkOf, readyThrough, lagOf, endOf, expand, LEAD_MS, JITTER_MS, STALE_FIRE_MS, REACT_GAP_MS, CHUNK_MS, WATCH_DELAY_MS } from '../net/tape.js';
import { fakeTapeServer, T0, DELAY, CHUNK, INITIAL } from './fake-tape.mjs';

let passed = 0;
const failures = [];
const ok = (what, cond) => { if (cond) passed++; else failures.push(what); };
const eq = (what, got, want) => ok(`${what} (got ${JSON.stringify(got)}, wanted ${JSON.stringify(want)})`, JSON.stringify(got) === JSON.stringify(want));
const settle = async (n = 20) => { for (let i = 0; i < n; i++) await Promise.resolve(); };
const C = CHUNK;
const LAG = lagOf(DELAY, CHUNK, LEAD_MS);

/** A tape on the fake server, with a client clock that is nowhere near the server's. */
function rig(srv, extra = {}) {
  const heard = { state: [], move: [], heat: [], fire: [], soon: [], end: null, closed: null, reactions: [], head: 0, net: [] };
  const mono = () => srv.st.now - T0 + 5000;
  const tape = createTape({
    matchId: 'm1', api: srv.api, now: mono, timers: false, random: extra.random || (() => 0),
    on: {
      state: (s) => { heard.state.push(s); return extra.refuseState && extra.refuseState() ? false : true; },
      move: (it) => { heard.move.push(it.uci); return extra.refuseMove ? !extra.refuseMove.includes(it.uci) : true; },
      heat: (p) => heard.heat.push(p), fire: (p) => heard.fire.push(p), soon: (p) => heard.soon.push(p),
      end: (e) => { heard.end = e; }, closed: (r) => { heard.closed = r; }, reactions: (c) => heard.reactions.push(c),
      head: () => { heard.head++; }, net: (p) => heard.net.push(p),
    },
  });
  return { tape, heard };
}
/** Move the server's now so the playhead sits at t0 + ms. */
const at = (srv, ms) => { srv.st.now = T0 + ms + LAG; };
/** The playhead (ms after t0) at which chunk n turns ready. */
const readyPh = (n) => (n + 1) * C + DELAY - LAG;
const tickN = async (tape, n = 2) => { for (let i = 0; i < n; i++) { tape.tick(); await settle(); } };

/* ------------------------------------------------------------- pure bits */
{
  eq('the page mirrors the server: 2 s chunks, 12 s delay', [CHUNK_MS, WATCH_DELAY_MS], [2000, 12000]);
  eq('chunk of a time', [chunkOf(T0, T0), chunkOf(T0 + C - 1, T0), chunkOf(T0 + C, T0), chunkOf(T0 - 1, T0)], [0, 0, 1, -1]);
  eq('ready through: the end must be older than the delay', [readyThrough(T0 + DELAY + C - 1, T0), readyThrough(T0 + DELAY + C, T0), readyThrough(T0 + DELAY + 2 * C, T0)], [-1, 0, 1]);
  eq('the stands play delay + a chunk + the lead behind', LAG, DELAY + C + LEAD_MS);
  ok('the lead covers the jitter with room for a fetch', LEAD_MS - JITTER_MS >= 1000);
  eq('1-0 by resign', endOf({ result: '1-0', reason: 'resign', at: 5 }).winner, 'w');
  eq('0-1 on time is a flag', [endOf({ result: '0-1', reason: 'timeout' }).result, endOf({ result: '0-1', reason: 'timeout' }).winner], ['flag', 'b']);
  eq('a draw has no winner', [endOf({ result: '1/2-1/2', reason: 'threefold' }).result, endOf({ result: '1/2-1/2', reason: 'threefold' }).winner], ['draw', null]);
  const items = expand([
    { k: 'move', seq: 1, at: 100, uci: 'e2e4', clk: [1, 2] },
    { k: 'fx', at: 50, side: 'w', ply: 0, heat: [[0, 99], [100, 0]], fired: [[20, 'flash'], [60, 'video', 9000], ['x', 'flash']] },
    { k: 'end', at: 200, result: '1-0', reason: 'resign' },
    { k: 'nonsense', at: 1 }, null,
  ]);
  eq('expand: one time-ordered list', items.map((i) => i.k + '@' + i.at), ['heat@50', 'fire@70', 'move@100', 'fire@110', 'heat@150', 'end@200']);
  eq('heat is 0..1 off 0..99', items.filter((i) => i.k === 'heat').map((i) => i.heat), [1, 0]);
  eq('a fire keeps its hold', items.find((i) => i.kind === 'video').holdMs, 9000);
  eq('the end after a mate at the same instant comes after the move', expand([{ k: 'end', at: 5, result: '1-0', reason: 'checkmate' }, { k: 'move', seq: 3, at: 5, uci: 'd8h4' }]).map((i) => i.k), ['move', 'end']);
}

/* ---------------------------------------------------- the join, mid-game */
{
  const srv = fakeTapeServer();
  srv.play('e2e4', T0 + 1 * C); srv.play('e7e5', T0 + 2 * C); srv.play('g1f3', T0 + 4.5 * C);
  srv.offerDraw();   // a hole in seq
  srv.play('b8c6', T0 + 6 * C); srv.play('f1c4', T0 + 6.75 * C);
  srv.addFx({ at: T0 + 8.2 * C, side: 'b', ply: 5, heat: [[0, 20], [C / 2, 40]], fired: [[0.75 * C, 'flash'], [0.8 * C, 'video', 8000]] });
  srv.addFx({ at: T0 + 8.4 * C, side: 'w', ply: 4, heat: [[0, 90]], fired: [[0, 'flash']] });   // a record of a turn that is over
  at(srv, 6.05 * C);
  const { tape, heard } = rig(srv);
  await tape.start();
  await settle();
  eq('the join asks upto the chunk before the playhead, then that chunk', srv.api.calls.slice(0, 3), ['head', 'upto:5', 'chunk:6']);
  eq('the snapshot holds every move up to the playhead', heard.state[0].moves, ['e2e4', 'e7e5', 'g1f3', 'b8c6']);
  eq('the move still ahead is queued, not shown', [heard.move.length, tape.queued()], [0, 1]);
  eq('the clock offset is measured off the head', Math.round(tape.serverNow()), srv.st.now);
  const wantW = srv.moves[3].clk[0] - 0.05 * C;
  ok(`white's clock reads the delayed balance (got ${tape.clock.remaining('w')}, wanted ${wantW})`, Math.abs(tape.clock.remaining('w') - wantW) <= 1);
  eq('black\'s clock stands still', tape.clock.remaining('b'), srv.moves[3].clk[1]);
  eq('white is on the move', tape.clock.snapshot().active, 'w');

  at(srv, 6.8 * C); await tickN(tape);
  eq('the queued move lands at its moment, the hole in seq is not a resync', [heard.move, srv.api.calls.filter((c) => c.startsWith('upto')).length], [['f1c4'], 1]);
  eq('and black is on the move', tape.clock.snapshot().active, 'b');
  const next = tape.nextChunk();
  at(srv, readyPh(next) - 50); await tickN(tape);
  ok(`chunk ${next} is not fetched before it is ready`, !srv.api.calls.includes('chunk:' + next));
  at(srv, readyPh(next) + 50); await tickN(tape);
  ok(`chunk ${next} is fetched once ready, LEAD_MS before the playhead needs it`, srv.api.calls.includes('chunk:' + next) && readyPh(next) + 50 <= next * C - LEAD_MS + 51);
  // fetch through the chunk holding the fx records without the playhead reaching them
  while (tape.nextChunk() <= chunkOf(T0 + 8.4 * C, T0, C)) { at(srv, Math.min(8.15 * C, readyPh(tape.nextChunk()) + 50)); await tickN(tape); }
  eq('the effects still ahead are announced (prefetch), not a finished turn', heard.soon.map((s) => s.kind), ['flash', 'video']);

  at(srv, 8.25 * C); await tickN(tape);
  eq('the turn player\'s heat lands at its moment', heard.heat, [{ side: 'b', heat: 20 / 99 }]);
  at(srv, 8.5 * C); await tickN(tape);
  eq('a record for a turn that is over is ignored', [heard.heat.length, heard.fire.length], [1, 0]);
  at(srv, 9.1 * C); await tickN(tape);
  eq('then the next sample and both fires, in order', [heard.heat.length, heard.fire.map((f) => f.kind + ':' + f.holdMs)], [2, ['flash:0', 'video:8000']]);
  tape.dispose();
}

/* ------------------------------------------------- the session's jitter */
{
  const srv = fakeTapeServer();
  srv.play('e2e4', T0 + C / 2);
  at(srv, 0.6 * C);
  const { tape } = rig(srv, { random: () => 0.999 });
  await tape.start(); await settle();
  ok('a jitter of up to JITTER_MS', tape.jitterMs() > JITTER_MS - 5 && tape.jitterMs() <= JITTER_MS);
  const n = tape.nextChunk();
  at(srv, readyPh(n) + 50); await tickN(tape);
  ok('a ready chunk waits for the session\'s jitter', !srv.api.calls.includes('chunk:' + n));
  at(srv, readyPh(n) + tape.jitterMs() + 50); await tickN(tape);
  ok('and is fetched once it has passed', srv.api.calls.includes('chunk:' + n));
  tape.dispose();
}

/* ------------------------------------------- the offset errs late (RTT) */
{
  const srv = fakeTapeServer();
  at(srv, C);
  let clockNow = 1000;
  const head = srv.api.watchHead;
  srv.api.watchHead = async (id) => { clockNow += 400; const r = await head(id); clockNow += 400; return r; };
  const tape = createTape({ matchId: 'm1', api: srv.api, now: () => clockNow, timers: false, random: () => 0 });
  await tape._fetchHead();
  // the server said sn halfway through an 800 ms round trip; we read it as sn - 400 when it lands
  eq('server now is read half a round trip behind what it said', Math.round(tape.serverNow()), srv.st.now - 400);
  tape.dispose();
}

/* ------------------------------------------------- a stale fire is dropped */
{
  const srv = fakeTapeServer();
  srv.play('e2e4', T0 + C / 2);
  srv.addFx({ at: T0 + 3 * C, side: 'b', ply: 1, heat: [[0, 50]], fired: [[100, 'flash']] });
  at(srv, 0.6 * C);
  const { tape, heard } = rig(srv);
  await tape.start(); await settle();
  for (let i = 0; i < 3; i++) { at(srv, readyPh(tape.nextChunk()) + 10); await tickN(tape); }
  at(srv, 3 * C + 100 + STALE_FIRE_MS + 500); await tickN(tape);
  eq('a fire that is already too late never fires', heard.fire.length, 0);
  eq('but its heat still lands', heard.heat.length, 1);
  tape.dispose();
}

/* ------------------------- a closing fx post (the turn that just ended) */
{
  const srv = fakeTapeServer();
  srv.play('e2e4', T0 + C / 2);
  const turnStart = T0 + C / 2;
  srv.play('e7e5', T0 + 4 * C);   // black's move closes black's turn
  // black's last window, posted 2.5 s after the move with an 11 s origin: the server clamps it into the turn
  const rec = srv.addFxPost({ now: T0 + 4 * C + 2500, ago: 11000, side: 'b', ply: 1, from: turnStart, to: T0 + 4 * C,
    heat: [[4 * C - 500, 60]], fired: [[4 * C - 200, 'flash']] });
  ok('the server keeps the closing post\'s last flash', rec && rec.fired.length === 1 && rec.at >= turnStart && rec.at + rec.fired[0][0] < T0 + 4 * C);
  at(srv, 0.6 * C);
  const { tape, heard } = rig(srv);
  await tape.start(); await settle();
  for (let i = 0; i < 6; i++) { at(srv, Math.min(4 * C - 10, readyPh(tape.nextChunk()) + 10)); await tickN(tape); }
  at(srv, 4 * C - 10); await tickN(tape);
  eq('it plays before the move, as black\'s own turn', [heard.fire.map((f) => f.side + ':' + f.kind), heard.move], [['b:flash'], []]);
  at(srv, 4 * C + 10); await tickN(tape);
  eq('then the move', heard.move, ['e7e5']);
  tape.dispose();
}

/* ------------------------------------------------------------- the end */
{
  const srv = fakeTapeServer();
  srv.play('e2e4', T0 + C / 2); srv.finish(T0 + 1.5 * C, '0-1', 'resign');
  at(srv, C);
  const { tape, heard } = rig(srv);
  await tape.start(); await settle();
  eq('not over yet on the delayed timeline', heard.end, null);
  at(srv, 1.6 * C); await tickN(tape, 3);
  eq('the end lands with the server\'s reason', [heard.end && heard.end.result, heard.end && heard.end.winner], ['resign', 'b']);
  ok('and the tape stops', tape.over() && !tape.clock.isRunning());
  const n = srv.api.calls.length;
  at(srv, 60 * C); await tickN(tape);
  eq('no call after the end', srv.api.calls.length, n);
  ok('a cheer after the end is still taken', tape.react('clap'));
  await settle();
  eq('and sent', srv.api.posts.map((p) => p.counts), [{ clap: 1 }]);
  tape.react('wow');
  await settle();
  srv.st.now += REACT_GAP_MS + 10; await tickN(tape);
  eq('one waiting for its gap goes out after the end too', srv.api.posts.map((p) => p.counts), [{ clap: 1 }, { wow: 1 }]);
}
{
  // a mate: the end rides the mating move at the same instant
  const srv = fakeTapeServer();
  srv.play('f2f3', T0 + C / 4); srv.play('e7e5', T0 + C / 2); srv.play('g2g4', T0 + 0.75 * C); srv.play('d8h4', T0 + 1.2 * C);
  srv.finish(undefined, '0-1', 'checkmate');
  at(srv, C);
  const { tape, heard } = rig(srv);
  await tape.start(); await settle();
  at(srv, 1.3 * C); await tickN(tape, 3);
  eq('the mating move, then the end', [heard.move, heard.end && heard.end.result, heard.end && heard.end.winner], [['d8h4'], 'checkmate', 'b']);
}
{
  // joined after it ended: the snapshot carries the end
  const srv = fakeTapeServer();
  srv.play('e2e4', T0 + C / 2); srv.finish(T0 + C, '1/2-1/2', 'agreement');
  at(srv, 10 * C);
  const { tape, heard } = rig(srv);
  await tape.start(); await settle();
  eq('a finished game arrives finished', [heard.end && heard.end.result, heard.state[0].moves], ['draw', ['e2e4']]);
}

/* ------------------------------------------- the opening, before the tape */
{
  const srv = fakeTapeServer();
  srv.play('d2d4', T0 + 1.5 * C);
  at(srv, -2 * C);
  const { tape, heard } = rig(srv);
  await tape.start(); await settle();
  eq('no upto and no chunk before the game started', srv.api.calls.filter((c) => c.startsWith('upto') || c.startsWith('chunk')), []);
  eq('the opening position, full clocks', [heard.state[0].moves, tape.clock.remaining('w'), tape.clock.remaining('b')], [[], INITIAL, INITIAL]);
  for (let i = 0; i < 3; i++) { at(srv, Math.min(1.6 * C, readyPh(tape.nextChunk()) + 10)); await tickN(tape); }
  at(srv, 1.6 * C); await tickN(tape);
  eq('chunk 0 is fetched when ready and its move lands', heard.move, ['d2d4']);
  tape.dispose();
}

/* -------------------------------------------------- resync and resnap */
{
  const srv = fakeTapeServer();
  srv.play('e2e4', T0 + C / 2);
  srv.play('e7e5', T0 + 2.5 * C);
  at(srv, C);
  const { tape, heard } = rig(srv, { refuseMove: ['e7e5'] });
  await tape.start(); await settle();
  for (let i = 0; i < 3; i++) { at(srv, Math.min(2.4 * C, readyPh(tape.nextChunk()) + 10)); await tickN(tape); }
  const uptos = srv.api.calls.filter((c) => c.startsWith('upto')).length;
  at(srv, 2.6 * C); await tickN(tape, 3);
  eq('a move the board refuses re-snaps off upto', srv.api.calls.filter((c) => c.startsWith('upto')).length, uptos + 1);
  eq('and the snapshot carries it', heard.state[heard.state.length - 1].moves, ['e2e4', 'e7e5']);
  srv.play('g1f3', T0 + 10 * C);
  at(srv, 40 * C); await tickN(tape);
  eq('a slept tab re-snaps instead of streaming', heard.state[heard.state.length - 1].moves, ['e2e4', 'e7e5', 'g1f3']);
  tape.dispose();
}
{
  // a long game's trimmed opening: upto sends the old moves with seq/at/clk null
  const srv = fakeTapeServer({ trim: 1 });
  srv.play('e2e4', T0 + C / 4); srv.play('e7e5', T0 + C / 2); srv.play('g1f3', T0 + 3 * C);
  at(srv, 3.5 * C);
  const { tape, heard } = rig(srv);
  await tape.start(); await settle();
  eq('null seq/at/clk in a snapshot still replay', heard.state[0].moves, ['e2e4', 'e7e5', 'g1f3']);
  tape.dispose();
}

/* ------------------------------------------------- refusals and retries */
{
  // 410 gone on upto: not a close, retried as the playhead moves on
  const srv = fakeTapeServer({ trim: 1 });
  srv.play('e2e4', T0 + C / 4); srv.play('e7e5', T0 + 5 * C);
  at(srv, 3.5 * C);
  const { tape, heard } = rig(srv);
  await tape.start(); await settle();
  eq('410 gone does not close the stands', [heard.closed, tape.caught(), srv.api.calls.includes('upto:2')], [null, false, true]);
  at(srv, 6.5 * C); await tickN(tape);
  eq('and a later try snaps', [tape.caught(), heard.state.length && heard.state[0].moves], [true, ['e2e4', 'e7e5']]);
  tape.dispose();
}
{
  // 503 on upto (the list is behind the match) and a snapshot that will not apply: the backoff only ends on a real snap
  const srv = fakeTapeServer();
  srv.play('e2e4', T0 + C / 2);
  at(srv, 2.5 * C);
  let refuse = 2;
  const { tape, heard } = rig(srv, { refuseState: () => refuse-- > 0 });
  srv.st.force.upto = { status: 503 };
  await tape.start(); await settle();
  eq('503 is retried, not a close', [heard.closed, heard.net.map((n) => n.error)], [null, ['server_error']]);
  const gaps = [];
  for (let i = 0; i < 3; i++) {
    const before = heard.state.length;
    let ms = 0;
    while (ms <= 20000 && heard.state.length === before) { srv.st.now += 100; ms += 100; tape.tick(); await settle(); }
    gaps.push(ms);
  }
  ok(`a snapshot that will not apply keeps backing off (gaps ${gaps})`, gaps[1] > gaps[0] && gaps[2] > gaps[1]);
  ok('and the third one applies', heard.state.length === 3 && tape.caught());
  tape.dispose();
}
{
  const srv = fakeTapeServer();
  srv.play('e2e4', T0 + C / 2);
  at(srv, C);
  const n = chunkOf(T0 + C, T0, C) + 1;   // the chunk after the playhead's: ready the moment the join lands
  srv.st.notReadyOnce.add(n);
  const { tape, heard } = rig(srv);
  await tape.start(); await settle();
  await tickN(tape, 1);
  eq('a 425 is not an error', [heard.closed, heard.net.length, tape.nextChunk()], [null, 0, n]);
  tape.tick(); await settle();
  eq('and is not asked again at once', srv.api.calls.filter((c) => c === 'chunk:' + n).length, 1);
  srv.st.now += 500; tape.tick(); await settle();
  eq('but after a short wait', [srv.api.calls.filter((c) => c === 'chunk:' + n).length, tape.nextChunk()], [2, n + 1]);
  srv.st.force.chunk = { status: 429 };
  at(srv, readyPh(n + 1) + 10); await tickN(tape, 1);
  eq('a 429 on a chunk is a retry', [heard.closed, heard.net[heard.net.length - 1].error], [null, 'rate_limited']);
  srv.st.chunkRefuse = { status: 403, error: 'not_watchable' };
  srv.st.now += 20000; await tickN(tape, 3);
  eq('a worded 403 on a chunk closes the stands', heard.closed, 'not_watchable');
  tape.dispose();
}
{
  const srv = fakeTapeServer();
  at(srv, C);
  srv.st.refuse = { status: 403, error: 'your_match' };
  const { tape, heard } = rig(srv);
  eq('a seat cannot watch its own match', [await tape.start(), heard.closed], [false, 'your_match']);
}
{
  const srv = fakeTapeServer();
  srv.st.noT0 = true;
  at(srv, C);
  const { tape, heard } = rig(srv);
  await tape.start();
  eq('a v1 server (no t0_ms) is not deployed for the tape', heard.closed, 'not_deployed');
}
{
  const srv = fakeTapeServer();
  srv.play('e2e4', T0 + C / 2);
  at(srv, C);
  const { tape } = rig(srv);
  await tape.start(); await settle();
  srv.st.watchers = 7;
  srv.st.now += 30000; tape.tick(); await settle();
  eq('the 30 s heartbeat refreshes the head', [srv.api.calls.filter((c) => c === 'head').length, tape.watchers()], [2, 7]);
  tape.dispose();
}

/* --------------------------------------------------------- reactions */
{
  const srv = fakeTapeServer();
  at(srv, C);
  const bucket = Math.floor(srv.st.now / 3000) - 1;
  srv.st.reactions.set(bucket, { fire: 3, wow: 1, nonsense: 9 });
  const { tape, heard } = rig(srv);
  await tape.start(); await settle();
  tape.tick(); await settle();
  eq('the previous bucket is fetched and its known ids heard', heard.reactions, [{ fire: 3, wow: 1 }]);
  tape.tick(); await settle();
  eq('a bucket is fetched once', srv.api.calls.filter((c) => c === 'reactions:' + bucket).length, 1);
  srv.st.now += 3000; tape.tick(); await settle();
  eq('an empty bucket says nothing', heard.reactions.length, 1);

  ok('a known id is taken', tape.react('fire'));
  await settle();
  tape.react('fire'); tape.react('wow'); tape.react('fire');
  ok('free text is refused', !tape.react('hello'));
  await settle();
  eq('the first click goes at once, the rest wait for the gap', srv.api.posts.map((p) => p.counts), [{ fire: 1 }]);
  srv.st.now += REACT_GAP_MS - 400; tape.tick(); await settle();
  eq('just short of the gap is not enough', srv.api.posts.length, 1);
  srv.st.now += 500; tape.tick(); await settle();
  eq('then go as one batch', srv.api.posts.map((p) => p.counts), [{ fire: 1 }, { fire: 2, wow: 1 }]);
  // a 429 keeps the batch and sends it with the next one
  srv.st.force.react = { status: 429 };
  srv.st.now += REACT_GAP_MS + 100; tape.react('heart'); await settle();
  for (let i = 0; i < 12; i++) tape.react('heart');
  srv.st.now += REACT_GAP_MS + 100; tape.tick(); await settle();
  eq('a 429 keeps the batch, merged and clamped to 10', srv.api.posts[srv.api.posts.length - 1].counts, { heart: 10 });
  tape.dispose();
}

/* ------------------------------------------------------------ the wire */
{
  const sent = [];
  const pub = [];
  let answer = { status: 200, body: '{}' };
  api.setTransport(async (method, path, body) => { sent.push({ method, path, body }); return answer; });
  api.setPublicTransport(async (method, path, body) => { pub.push({ method, path, body }); return answer; });
  answer = { status: 200, body: JSON.stringify({ ok: true, match_id: 'm 1', t0_ms: 5, status: 'live', players: { w: { display_name: 'a' }, b: { display_name: 'b' } } }) };
  const head = await api.watchHead('m 1');
  eq('GET watch is the head, on the auth road', [sent[0].method, sent[0].path.split('?')[0], pub.length], ['GET', '/v2/pbp/watch/m%201', 0]);
  eq('the head comes back as the server sent it', [head.data.t0_ms, head.data.players.w.display_name, 'match' in head.data], [5, 'a', false]);
  ok('watch is the same call', api.watch === api.watchHead);
  answer = { status: 200, body: '{}' };
  await api.watchUpto('m1', 4); await api.watchChunk('m1', 5); await api.reactions('m1', 77);
  eq('the public routes', pub.map((p) => p.method + ' ' + p.path), ['GET /v2/pbp/watch/m1/upto/4', 'GET /v2/pbp/watch/m1/chunk/5', 'GET /v2/pbp/watch/m1/reactions/77']);
  ok('go the public road with no uid in the query (one cached answer for everybody)', pub.every((p) => !p.path.includes('unified_id')) && sent.length === 1);
  answer = { status: 425, body: JSON.stringify({ error: 'not_ready' }) };
  eq('425 is not_ready', (await api.watchChunk('m1', 9)).error, 'not_ready');
  answer = { status: 410, body: JSON.stringify({ error: 'gone' }) };
  eq('410 is gone', (await api.watchUpto('m1', 0)).error, 'gone');
  answer = { status: 403, body: JSON.stringify({ error: 'not_watchable' }) };
  eq('403 not_watchable on a chunk', (await api.watchChunk('m1', 9)).error, 'not_watchable');
  answer = { status: 204, body: '' };
  sent.length = 0;
  await api.react('m1', { fire: 40, wow: 1, hello: 3, clap: 0 });
  eq('react: known ids, clamped to 10, on the auth road', [sent[0].path, sent[0].body.r], ['/v2/pbp/watch/m1/react', { fire: 10, wow: 1 }]);
  eq('nothing to send is not sent', [(await api.react('m1', { hello: 1 })).ok, sent.length], [false, 1]);
  await api.postFx('m1', { ply: 6, from_ms_ago: 4999.6, heat: [[0, 12.4], [1000, 140], ['x', 1]], fired: [[300, 'flash'], [900, 'video', 8000], [5, 'url', 'https://x'], [7, 'gifRain', 0]], url: 'https://leak', niche: 'x' });
  eq('postFx: the seat route, a clean body and nothing about media', [sent[1].path, Object.keys(sent[1].body).sort()], ['/v2/pbp/match/m1/fx', ['fired', 'from_ms_ago', 'heat', 'ply']]);
  eq('heat rounded and clamped to 0..99', sent[1].body.heat, [[0, 12], [1000, 99]]);
  eq('only known kinds, holds only where they mean something', sent[1].body.fired, [[300, 'flash'], [900, 'video', 8000], [7, 'gifRain']]);
  eq('from_ms_ago rounded', sent[1].body.from_ms_ago, 5000);
  api.setTransport(null);
  api.setPublicTransport(null);
}

console.log(`tape-smoke: ${passed} checks passed`);
for (const f of failures) console.log('FAILED ' + f);
process.exit(failures.length ? 1 : 0);
