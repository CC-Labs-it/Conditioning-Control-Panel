/* ============================================================================
 * smoke/fxlog-smoke.mjs - a seat's Distraction record for the stands (net/fxlog.js):
 *
 *   node smoke/fxlog-smoke.mjs
 *
 * A fake clock and fake timers drive the log; a fake api records the posts.
 * Checked: nothing is posted while nobody watches; a window starts with the
 * seat's own turn and posts every ~5 s and once on the move; the opponent's
 * turn and the stands never post; the 3 s rate gap is waited out; unknown
 * kinds are dropped; the body has the wire's shape and stays under 4 KB.
 * ==========================================================================*/

import { createBus } from '../game/events.js';
import { createFxLog, fxBody, firedEntry, FX_KINDS } from '../net/fxlog.js';

let passed = 0;
const failures = [];
const ok = (what, cond) => { if (cond) passed++; else failures.push(what); };
const eq = (what, got, want) => ok(`${what} (got ${JSON.stringify(got)}, wanted ${JSON.stringify(want)})`, JSON.stringify(got) === JSON.stringify(want));

function rig({ side = 'w', watch = false, online = true } = {}) {
  let t = 1000000;
  const queue = [];   // { at, fn, every? }
  const timers = {
    setTimeout(fn, ms) { const h = { at: t + ms, fn }; queue.push(h); return h; },
    clearTimeout(h) { const i = queue.indexOf(h); if (i >= 0) queue.splice(i, 1); },
    setInterval(fn, ms) { const h = { at: t + ms, fn, every: ms }; queue.push(h); return h; },
    clearInterval(h) { const i = queue.indexOf(h); if (i >= 0) queue.splice(i, 1); },
  };
  const advance = (ms) => {
    const end = t + ms;
    for (;;) {
      queue.sort((a, b) => a.at - b.at);
      const h = queue[0];
      if (!h || h.at > end) break;
      t = h.at;
      if (h.every) h.at += h.every; else queue.shift();
      h.fn();
    }
    t = end;
  };
  const posts = [];
  const api = { postFx: (id, body) => { posts.push({ id, body, at: t }); return Promise.resolve({ ok: true }); } };
  let plies = 0;
  const game = {
    isOnline: online, isWatch: watch, seats: watch ? [] : [side], current: { matchId: 'm_1' },
    watchers: () => 0, plies: () => plies,
  };
  const bus = createBus();
  const log = createFxLog({ bus, game, api, now: () => t, timers });
  return { bus, log, posts, advance, setPlies: (n) => { plies = n; }, rename: (id) => { game.current.matchId = id; }, now: () => t };
}

// ---- pure helpers
eq('kinds are the ramp layer names', FX_KINDS, ['flash', 'gifRain', 'video', 'glitch', 'melt', 'spiral', 'blur', 'overlay']);
eq('a fired entry keeps the hold', firedEntry({ kind: 'video', holdMs: 15000.4 }, 1200.6), [1201, 'video', 15000]);
eq('a fired entry without a hold', firedEntry({ kind: 'blur' }, 10), [10, 'blur']);
eq('an unknown kind is dropped', firedEntry({ kind: 'url', holdMs: 1 }, 0), null);
{
  const heat = Array.from({ length: 400 }, (_, i) => [i * 1000, 99]);
  const fired = Array.from({ length: 200 }, (_, i) => [i * 100, 'gifRain', 12000]);
  const b = fxBody({ ply: 7, startMs: 0, heat, fired }, 9000);
  ok('a fat window is trimmed under 4 KB', JSON.stringify(b).length <= 4000);
  eq('the body shape', Object.keys(b), ['ply', 'from_ms_ago', 'heat', 'fired']);
  eq('from_ms_ago is measured at the send', b.from_ms_ago, 9000);
}

// ---- nobody watching: nothing
{
  const r = rig();
  r.bus.emit('local', { mode: 'online', sides: ['w'] });
  r.bus.emit('turn', { side: 'w', ply: 0 });
  r.bus.emit('ramp-heat', { heat: 0.5 });
  r.bus.emit('ramp-fired', { kind: 'flash' });
  r.advance(12000);
  r.bus.emit('turn', { side: 'b', ply: 1 });
  r.advance(4000);
  eq('no watchers, no posts', r.posts.length, 0);
}

// ---- watched: every ~5 s on the turn, once on the move
{
  const r = rig();
  r.bus.emit('local', { mode: 'online', sides: ['w'] });
  r.bus.emit('watchers', { count: 2 });
  r.bus.emit('turn', { side: 'w', ply: 4 });
  for (let i = 0; i < 6; i++) { r.advance(1000); r.bus.emit('ramp-heat', { heat: i / 5 }); }
  r.bus.emit('ramp-fired', { kind: 'gifRain', holdMs: 9000 });
  r.bus.emit('ramp-fired', { kind: 'nonsense' });
  eq('one window posted after 5 s', r.posts.length, 1);
  const first = r.posts[0];
  eq('the post names the match', first.id, 'm_1');
  eq('the post is for the turn ply', first.body.ply, 4);
  eq('heat is 0..99 against the window start', first.body.heat.slice(0, 2), [[1000, 0], [2000, 20]]);
  ok('from_ms_ago spans the window', first.body.from_ms_ago >= 5000);
  r.advance(1500);   // the move lands 1.5 s after the post: inside the 3 s gap
  r.bus.emit('turn', { side: 'b', ply: 5 });
  eq('the move waits out the rate gap', r.posts.length, 1);
  r.advance(1900);
  eq('then the move window goes', r.posts.length, 2);
  const last = r.posts[1];
  ok('the move post is 3.3 s after the last', last.at - first.at >= 3300);
  eq('the move window carries the fire, not the junk kind', last.body.fired, [[1000, 'gifRain', 9000]]);
  r.bus.emit('ramp-heat', { heat: 1 });
  r.bus.emit('ramp-fired', { kind: 'flash' });
  r.advance(12000);
  eq("the opponent's turn posts nothing", r.posts.length, 2);
  r.bus.emit('watchers', { count: 0 });
  r.bus.emit('turn', { side: 'w', ply: 6 });
  r.bus.emit('ramp-heat', { heat: 1 });
  r.advance(12000);
  eq('the last watcher gone, nothing posts', r.posts.length, 2);
  r.setPlies(6);
  r.bus.emit('watchers', { count: 1 });
  r.advance(1000); r.bus.emit('ramp-heat', { heat: 0.3 });
  r.advance(5000);
  eq('a watcher arriving mid-turn opens a window', r.posts.length, 3);
  eq('...for the turn in hand', r.posts[2].body.ply, 6);
  r.bus.emit('gameover', { result: 'checkmate', winner: 'w' });
  r.advance(10000);
  ok('gameover stops the log', r.posts.length <= 4);
  r.log.dispose();
}

// ---- a move inside the gap keeps its own match; a new deal drops it
{
  const r = rig();
  r.bus.emit('local', { mode: 'online', sides: ['w'] });
  r.bus.emit('watchers', { count: 1 });
  r.bus.emit('turn', { side: 'w', ply: 0 });
  for (let i = 0; i < 5; i++) { r.advance(1000); r.bus.emit('ramp-heat', { heat: 0.5 }); }
  eq('first window', r.posts.length, 1);
  r.advance(500); r.bus.emit('ramp-fired', { kind: 'flash' });
  r.bus.emit('turn', { side: 'b', ply: 1 });
  r.rename('m_2');
  r.advance(4000);
  eq('the deferred window goes to the match it was made in', r.posts.map((p) => p.id), ['m_1', 'm_1']);
  r.bus.emit('turn', { side: 'w', ply: 2 });
  for (let i = 0; i < 6; i++) { r.advance(1000); r.bus.emit('ramp-heat', { heat: 0.5 }); }
  r.advance(200); r.bus.emit('ramp-heat', { heat: 0.6 });
  r.bus.emit('turn', { side: 'b', ply: 3 });
  const n = r.posts.length;
  ok('a move inside the gap is deferred', r.log.debug().deferred);
  r.bus.emit('local', { mode: 'hotseat', sides: ['w', 'b'] });
  ok('a new deal drops the deferred window', !r.log.debug().deferred);
  r.advance(5000);
  eq('...and it never goes', r.posts.length, n);
}

// ---- the stands and a hotseat never post
for (const [what, opts] of [['the stands', { watch: true }], ['a hotseat', { online: false }]]) {
  const r = rig(opts);
  r.bus.emit('local', { mode: opts.watch ? 'watch' : 'hotseat', sides: [] });
  r.bus.emit('watchers', { count: 5 });
  r.bus.emit('turn', { side: 'w', ply: 0 });
  r.bus.emit('ramp-heat', { heat: 0.9 });
  r.bus.emit('ramp-fired', { kind: 'flash' });
  r.advance(11000);
  r.bus.emit('turn', { side: 'b', ply: 1 });
  r.advance(4000);
  eq(`${what} never posts`, r.posts.length, 0);
}

// ---- no api.postFx yet (lane WATCH not merged): quiet
{
  const bus = createBus();
  const log = createFxLog({ bus, game: { isOnline: true, seats: ['w'], current: { matchId: 'x' }, watchers: () => 3, plies: () => 0 }, api: {}, timers: { setTimeout, clearTimeout, setInterval: () => 0, clearInterval: () => {} } });
  bus.emit('local', { mode: 'online' });
  bus.emit('turn', { side: 'w', ply: 0 });
  bus.emit('ramp-heat', { heat: 1 });
  let threw = false;
  try { log.flush('move'); } catch { threw = true; }
  ok('a missing postFx is quiet', !threw && log.debug().posts === 0);
  log.dispose();
}

if (failures.length) {
  console.error(`fxlog-smoke: ${failures.length} failed, ${passed} passed`);
  for (const f of failures) console.error('  FAIL ' + f);
  process.exit(1);
}
console.log(`fxlog-smoke: ${passed} passed`);
