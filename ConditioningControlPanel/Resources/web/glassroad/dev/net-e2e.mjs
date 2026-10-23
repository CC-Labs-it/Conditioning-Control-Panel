/* dev/net-e2e.mjs - the B2 local exit, end to end on this PC (no production, no Cloudflare):
 *   a local lobby + room (CCP-Server proxy/scripts/race-local.mjs, spawned from --server, or --lobby when one runs),
 *   dev/serve.mjs for the page, ONE muted headless Edge page (?bot=1: the AI drives it) and three node players
 *   (dev/netbots.mjs): A opens a table (5 seats, 3 laps, bots fill), the page lists and joins it through the lobby UI,
 *   B joins by id, C quick-matches; the room auto-starts, counts down on server time, one empty seat races as a bot
 *   kept by a client, A pauses at 20 s and the page pauses at 30 s, items fly, the room's finish lands in the ledger.
 *
 *   node dev/net-e2e.mjs --server C:/wt-race-server --node-path <proxy node_modules> [--log file] [--shots dir]
 *        [--page-port 8800] [--cdp-port 8808] [--lobby-port 8811] [--room-port 8810] [--laps 3]
 *   node dev/net-e2e.mjs --lobby http://127.0.0.1:8811            (a race-local.mjs already running)
 * Prints JSON lines (also to --log); the last line is the verdict. Exit 0 when every check passes.
 * Kills only the processes it started.
 */
import { spawn } from 'node:child_process';
import { mkdirSync, mkdtempSync, writeFileSync, appendFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { runNetBot } from './netbots.mjs';

const ROOT = fileURLToPath(new URL('..', import.meta.url));
const argv = process.argv.slice(2);
const arg = (k, d) => { const i = argv.indexOf('--' + k); return i >= 0 && argv[i + 1] && !argv[i + 1].startsWith('--') ? argv[i + 1] : d; };
const PAGE = Number(arg('page-port', 8800)), CDP = Number(arg('cdp-port', 8808));
const LOBBY_PORT = Number(arg('lobby-port', 8811)), ROOM_PORT = Number(arg('room-port', 8810));
const LAPS = Number(arg('laps', 3));
const OWNER = [8790, 8792, 8794, 8796];
if ([PAGE, CDP, LOBBY_PORT, ROOM_PORT].some((p) => OWNER.includes(p))) { console.error('net-e2e: owner ports are off limits'); process.exit(2); }
const LOG = arg('log', null), SHOTS = arg('shots', null);
const BROWSER = arg('browser', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const t0 = Date.now();
if (LOG) writeFileSync(LOG, '');
const say = (ev, o = {}) => { const line = JSON.stringify({ t: +((Date.now() - t0) / 1000).toFixed(1), ev, ...o }); console.log(line); if (LOG) appendFileSync(LOG, line + '\n'); };
const checks = [];
const check = (name, ok, info) => { checks.push({ name, ok: !!ok }); say('check', { name, ok: !!ok, info }); };

const kids = [];
function child(cmd, args, opts = {}) {
  const p = spawn(cmd, args, { stdio: ['ignore', 'pipe', 'pipe'], ...opts });
  kids.push(p);
  return p;
}
function cleanup() { for (const p of kids) { try { p.kill(); } catch { /* gone */ } } }
process.on('exit', cleanup);

async function waitFor(fn, ms, step = 200) {
  const end = Date.now() + ms;
  for (;;) { try { const v = await fn(); if (v) return v; } catch { /* not yet */ } if (Date.now() > end) return null; await sleep(step); }
}

// ---- 1. lobby + room, page server ----------------------------------------------------------------------------
let lobby = arg('lobby', null);
const roomLog = [];   // the local room's own lines (keeper moves with their reason, refused tick claims)
const ownRoom = !lobby;
if (lobby && !/^http:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/.test(lobby)) { console.error('net-e2e: local lobbies only'); process.exit(2); }
if (!lobby) {
  const server = arg('server', null);
  if (!server) { console.error('net-e2e: --server <CCP-Server checkout> or --lobby <url>'); process.exit(2); }
  const env = { ...process.env, ...(arg('node-path', null) ? { NODE_PATH: arg('node-path') } : {}) };
  const rl = child(process.execPath, [join(server, 'proxy/scripts/race-local.mjs'), '--port', String(LOBBY_PORT), '--room-port', String(ROOM_PORT)], { env, cwd: join(server, 'proxy') });
  rl.stdout.on('data', (d) => {
    for (const l of String(d).split('\n')) {
      if (!l.trim()) continue;
      const j = l.indexOf('{"room"');
      if (j >= 0) { try { const o = JSON.parse(l.slice(j)); roomLog.push(o); say('room-log', o); continue; } catch { /* a plain line */ } }
      say('lobby-out', { line: l.trim().slice(0, 300) });
    }
  });
  rl.stderr.on('data', (d) => say('lobby-err', { line: String(d).trim().slice(0, 300) }));
  lobby = `http://127.0.0.1:${LOBBY_PORT}`;
}
const pageSrv = child(process.execPath, [join(ROOT, 'dev/serve.mjs'), String(PAGE)], { cwd: ROOT });
pageSrv.stderr.on('data', (d) => say('page-err', { line: String(d).trim().slice(0, 300) }));
check('lobby up', await waitFor(async () => (await fetch(lobby + '/__health')).ok, 15000));
check('page server up', await waitFor(async () => (await fetch(`http://127.0.0.1:${PAGE}/index.html`)).ok, 15000));

// ---- 2. the browser page (CDP) --------------------------------------------------------------------------------
const profile = mkdtempSync(join(tmpdir(), 'gr-e2e-'));
child(BROWSER, ['--headless=new', '--mute-audio', '--remote-debugging-port=' + CDP, '--user-data-dir=' + profile, '--window-size=1280,720',
  '--ignore-gpu-blocklist', '--no-first-run', '--no-default-browser-check', 'about:blank']);
const ver = await waitFor(async () => (await fetch(`http://127.0.0.1:${CDP}/json/version`)).json(), 15000, 150);
if (!ver) { say('fatal', { why: 'no browser' }); process.exit(1); }
const ws = new WebSocket(ver.webSocketDebuggerUrl);
await new Promise((r) => { ws.onopen = r; });
let id = 0;
const wait = new Map(), errors = [];
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.id && wait.has(m.id)) { const w = wait.get(m.id); wait.delete(m.id); if (m.error) w.rej(new Error(m.error.message)); else w.res(m.result); return; }
  if (m.method === 'Runtime.exceptionThrown') errors.push(m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text);
  if (m.method === 'Runtime.consoleAPICalled' && (m.params.type === 'error' || m.params.type === 'warning')) {
    const text = m.params.args.map((a) => a.value ?? a.description).join(' ');
    if (m.params.type === 'error') errors.push(text);
    say('page-console', { type: m.params.type, text: text.slice(0, 300) });
  }
};
const cdp = (method, params = {}, sessionId) => { const msg = { id: ++id, method, params }; if (sessionId) msg.sessionId = sessionId; ws.send(JSON.stringify(msg)); return new Promise((res, rej) => wait.set(msg.id, { res, rej })); };
const { targetId } = await cdp('Target.createTarget', { url: 'about:blank' });
const { sessionId: sid } = await cdp('Target.attachToTarget', { targetId, flatten: true });
await cdp('Runtime.enable', {}, sid); await cdp('Page.enable', {}, sid);
await cdp('Emulation.setFocusEmulationEnabled', { enabled: true }, sid);
const ev = async (expr) => { const r = await cdp('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true }, sid); return r.result ? r.result.value : undefined; };
const click = (sel) => ev(`(() => { const b = document.querySelector(${JSON.stringify(sel)}); if (!b || b.disabled) return false; b.click(); return true; })()`);
const key = async (k, code, vk) => { await cdp('Input.dispatchKeyEvent', { type: 'keyDown', key: k, code, windowsVirtualKeyCode: vk }, sid); await sleep(80); await cdp('Input.dispatchKeyEvent', { type: 'keyUp', key: k, code, windowsVirtualKeyCode: vk }, sid); };
if (SHOTS) mkdirSync(SHOTS, { recursive: true });
const shot = async (name) => {
  if (!SHOTS) return;
  try { const r = await cdp('Page.captureScreenshot', { format: 'png' }, sid); const f = join(SHOTS, name + '.png'); writeFileSync(f, Buffer.from(r.data, 'base64')); say('shot', { f }); } catch (e) { say('shot-fail', { name, e: String(e) }); }
};
const D = () => ev('JSON.stringify((({ phase, lap, place, s, online, onlineResult, events, fps, backend }) => ({ phase, lap, place, s: Math.round(s || 0), online, onlineResult, fps, backend, hits: events["item-hit"] || 0, fires: events["item-fire"] || 0, bumps: events.bump || 0 }))(window.__glassroad || {}))').then((x) => JSON.parse(x || '{}'));

const pageUrl = `http://127.0.0.1:${PAGE}/?bot=1&chart=0&quality=phone&lobby=${encodeURIComponent(lobby)}&uid=e2e-page&name=Page`;
await cdp('Page.navigate', { url: pageUrl }, sid);
check('page boots to the menu', await waitFor(async () => (await ev('window.__glassroad && window.__glassroad.phase')) === 'menu', 90000, 500));

// ---- 3. seat everyone -----------------------------------------------------------------------------------------
const bots = [];
let seatedA;
const roomIdP = new Promise((r) => { seatedA = r; });
const keeps = [];   // keep hand-offs: node players from their log, the page from its session
const common = { lobbyBase: lobby, quiet: false, out: (l) => {
  const o = JSON.parse(l);
  if (o.ev === 'keep' && o.seats.length) keeps.push({ who: o.bot, seats: o.seats });
  if (o.ev !== 'log' && o.ev !== 'seats') say('bot', o);
} };
bots.push(runNetBot({ ...common, uid: 'netbot-a', mode: 'open', open: { seats: 5, laps: LAPS }, pauseAtS: 20, onSeated: (j) => seatedA(j.roomId) }));
const roomId = await Promise.race([roomIdP, sleep(20000).then(() => null)]);
check('A opened a table', !!roomId, roomId);

await click('[data-act=online]');
check('page opens the lobby', await waitFor(async () => (await ev('document.querySelector("#card").dataset.kind')) === 'lobby', 5000));
await click('[data-act=lb-refresh]');
const listed = await waitFor(async () => (await click(`[data-act=lb-join][data-id="${roomId}"]`)) || (await click('[data-act=lb-refresh]')) && false, 20000, 700);
check('page lists the table and joins it', !!listed);
check('page seated in the room', await waitFor(async () => !!(await ev('!!document.querySelector("[data-act=lb-ready]")')), 15000));
await click('[data-act=lb-ready]');
await shot('1-lobby-room');

bots.push(runNetBot({ ...common, uid: 'netbot-b', mode: 'join', roomId }));
await sleep(1500);
bots.push(runNetBot({ ...common, uid: 'netbot-c', mode: 'quick' }));

// ---- 4. countdown and race --------------------------------------------------------------------------------------
check('room starts (auto start)', await waitFor(async () => !!(await D()).online, 70000, 500));
let d = await D();
say('online', { online: d.online });
check('five racers, one kept bot seat', d.online && d.online.seats === 5, d.online);
if (d.online && d.online.keep && d.online.keep.length) keeps.push({ who: 'page', seats: d.online.keep });
say('looks', JSON.parse(await ev('JSON.stringify({ seats: __glassroad.racers.list.map((r) => [r.meta.seat, r.kind, r.meta.look]), karts: __glassroad.world.looks ? __glassroad.world.looks() : null })')));
await shot('2-countdown');
// the remote kart sampler: per frame jump of every remote kart's drawn position (rescues excused)
await ev(`(async () => {
  const { S, RESPAWN } = await import('/sim/state.js');
  const g = window.__glassroad, top = g.feel.get('speed.topMps');
  // a jump counts as a hitch when it is longer than the kart's own drawn speed covers in the wall time since the
  // last frame (x1.5, + 0.3 m slack): the remote clock is wall time, so rAF jitter is not counted against it
  const st = window.__e2e = { frames: 0, samples: 0, maxJumpM: 0, hitches: 0, remote: 0, hits: [], last: {}, lastT: 0 };
  g.session.on('hit', (m) => st.hits.push([m.by, m.victim, m.item, m.verdict]));
  st.keeps = []; g.session.on('keep', (m) => st.keeps.push(m.seats));
  (function loop() {
    requestAnimationFrame(loop);
    const t = Date.now(), rs = g.racers, dt = st.lastT ? (t - st.lastT) / 1000 : 0; st.lastT = t;
    if (!rs || !g.online || g.phase === 'menu') return;
    st.frames++;
    let n = 0;
    for (const r of rs.list) {
      if (r.kind !== 'remote') continue;
      n++;
      const v = r.view, p = [v[S.posX], v[S.posY], v[S.posZ]], sp = Math.abs(v[S.speedPct]) * top, q = st.last[r.i];
      st.last[r.i] = { p, sp };
      if (!q || !dt || v[S.respawnPhase] !== RESPAWN.NONE) continue;
      st.samples++;
      const j = Math.hypot(p[0] - q.p[0], p[1] - q.p[1], p[2] - q.p[2]);
      if (j > st.maxJumpM) st.maxJumpM = j;
      if (j > Math.max(sp, q.sp) * dt * 1.5 + 0.3) st.hitches++;
    }
    st.remote = n;
  })();
  return true;
})()`);
check('race goes', await waitFor(async () => (await D()).phase === 'racing', 30000, 300));
await sleep(6000);
await shot('3-racing');
// the page pauses at about 30 s of race time: the kart keeps moving under the assist, the room never waits
await waitFor(async () => { const x = await D(); return x.phase === 'racing' && (await ev('window.__glassroad.timeS')) > 30; }, 60000, 500);
const sBefore = (await D()).s;
await key('Escape', 'Escape', 27);
check('page pauses', await waitFor(async () => (await D()).phase === 'paused', 3000));
await sleep(3500);
await shot('4-paused');
const sAfter = (await D()).s;
check('paused kart coasts on (the room never waits)', sAfter !== sBefore, { sBefore, sAfter });
await click('[data-act=resume]');
check('page resumes', await waitFor(async () => (await D()).phase === 'racing', 3000));
let midShot = false;
const res = await waitFor(async () => {
  const x = await D();
  if (!midShot && x.lap >= 2) { midShot = true; await shot('5-lap3'); }
  return x.onlineResult ? x : null;
}, 400000, 1000);
check('page gets the room results', !!res, res && res.onlineResult);
await shot('6-results');
const e2e = JSON.parse(await ev('JSON.stringify(Object.assign({}, window.__e2e, { last: undefined }))') || '{}');
for (const k of e2e.keeps || []) if (k.length) keeps.push({ who: 'page', seats: k });
say('remote-karts', { frames: e2e.frames, samples: e2e.samples, remote: e2e.remote, maxJumpM: +(e2e.maxJumpM || 0).toFixed(2), hitches: e2e.hitches });
check('remote karts drawn smoothly (hitches under 1% of drawn kart frames)', e2e.frames > 100 && e2e.remote >= 3 && e2e.hitches <= e2e.samples * 0.01,
  { samples: e2e.samples, hitches: e2e.hitches, maxJumpM: e2e.maxJumpM });
check('items hit across clients', (e2e.hits || []).some((h) => h[0] !== h[1] && h[3] === 'hit'), e2e.hits);

// ---- 5. bots, ledger, board -----------------------------------------------------------------------------------
const outs = await Promise.allSettled(bots);
const rows = outs.map((o) => (o.status === 'fulfilled' ? o.value : { error: String(o.reason) }));
say('bots', { rows: rows.map((r) => ({ uid: r.uid, seat: r.seat, place: r.place, timeMs: r.timeMs, finished: r.finished, error: r.error })) });
check('three node players finished', rows.every((r) => r.finished));
const final = rows.find((r) => r.rows) ? rows.find((r) => r.rows).rows : [];
check('finish order and times agreed by the room (every player got the same rows)', rows.every((r) => JSON.stringify(r.rows) === JSON.stringify(final)));
const bot = final.find((r) => r.bot);
await sleep(1500);
const kv = await (await fetch(lobby + '/__kv?prefix=race:')).json();
const result = kv[`race:result:${roomId}`] ? JSON.parse(kv[`race:result:${roomId}`]) : null;
// the room ends the race when every human is home, so a bot a little slower may stay unfinished; it must have closed
// laps - 1 laps by the room's own ledger (lapsMs of the signed result), kept by a client
const rbot = result && result.rows.find((r) => r.bot);
check('the empty seat raced as a bot kept by a client (laps - 1 laps by the ledger)', bot && rbot && (rbot.finished || rbot.lapsMs.length >= LAPS - 1)
  && keeps.some((k) => k.seats.includes(bot.seat)), { bot: rbot, keeps });
// keeper moves while racing: a stall or idle move means a client stopped driving the bot (time lost at the hand-off)
const moves = roomLog.filter((l) => l.room === roomId && l.ev === 'keeper');
say('keeper-moves', { moves });
if (ownRoom) check('no stall or idle keeper move in a clean run', moves.length > 0 && !moves.some((m) => /^(stall|idle)/.test(m.why)), moves);
const claims = roomLog.filter((l) => l.room === roomId && l.ev === 'claim');
if (claims.length) say('refused-claims', { claims });
check('signed result landed in the local ledger', !!result && result.rows.length === 5, result && result.rows.map((r) => [r.place, r.name, r.timeMs, r.flags.join('|')]));
check('no anti-cheat flags on a clean race', result && result.rows.every((r) => !r.flags.length), result && result.rows.map((r) => r.flags));
const board = await (await fetch(lobby + '/v2/race/board/threshold', { headers: { authorization: 'Bearer jwt-e2e-page' } })).json();
say('board', board);
check('leaderboard has the finishers', board && Array.isArray(board.rows) && board.rows.length >= 4, board && board.rows && board.rows.length);
check('no page errors', errors.length === 0, errors.slice(0, 6));

const failed = checks.filter((c) => !c.ok).map((c) => c.name);
say('verdict', { ok: failed.length === 0, passed: checks.length - failed.length, of: checks.length, failed, roomId, secs: Math.round((Date.now() - t0) / 1000) });
try { ws.close(); } catch { /* closing */ }
cleanup();
await sleep(500);
try { rmSync(profile, { recursive: true, force: true }); } catch { /* the browser may hold it a moment */ }
process.exit(failed.length ? 1 : 0);
