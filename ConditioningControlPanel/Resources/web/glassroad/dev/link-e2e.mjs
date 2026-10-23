/* dev/link-e2e.mjs - the race link (NET.md section 13), end to end on this PC with two real pages (no production):
 *   a local lobby + room in PREVIEW mode (CCP-Server proxy/scripts/race-local.mjs --preview u_host), dev/serve.mjs for
 *   the pages, ONE muted headless Edge with up to three pages (?bot=1: the AI drives each kart):
 *     A (u_host, Prime) opens a friends room (1 lap, 4 seats, bots fill) and presses Invite link (the url comes off the
 *       clipboard or the page's fallback line),
 *     C (u_zero, tier 0) opens the link and gets the Prime card, never a seat,
 *     B (u_guest, Prime, not on the preview list) opens the link and is seated in A's room (the claim grants preview),
 *     A starts, both pages race to the finish, both get the room's results, the signed result lands in the ledger.
 *
 *   node dev/link-e2e.mjs --server C:/wt-race-server --node-path <proxy node_modules> [--log file] [--shots dir]
 *        [--page-port 8800] [--cdp-port 8801] [--lobby-port 8813] [--room-port 8812]
 *   node dev/link-e2e.mjs --lobby http://127.0.0.1:8813   (a race-local.mjs --preview u_host already running)
 * Takes C:/wt-glassroad-run/browser.lock when that folder exists (one headless browser at a time; --no-lock skips it).
 * Prints JSON lines (also to --log); the last line is the verdict. Exit 0 when every check passes.
 * Kills only the processes it started.
 */
import { spawn, execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, writeFileSync, appendFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('..', import.meta.url));
const argv = process.argv.slice(2);
const arg = (k, d) => { const i = argv.indexOf('--' + k); return i >= 0 && argv[i + 1] && !argv[i + 1].startsWith('--') ? argv[i + 1] : d; };
const PAGE = Number(arg('page-port', 8800)), CDP = Number(arg('cdp-port', 8801));
const LOBBY_PORT = Number(arg('lobby-port', 8813)), ROOM_PORT = Number(arg('room-port', 8812));
const OWNER = [8790, 8791, 8792, 8793, 8794, 8795, 8796, 8810, 8811];
if ([PAGE, CDP, LOBBY_PORT, ROOM_PORT].some((p) => OWNER.includes(p))) { console.error('link-e2e: owner ports are off limits'); process.exit(2); }
const LOG = arg('log', null), SHOTS = arg('shots', null);
const BROWSER = arg('browser', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe');
const RUN_DIR = 'C:/wt-glassroad-run', LOCK = RUN_DIR + '/browser.lock';
const USE_LOCK = !argv.includes('--no-lock') && existsSync(RUN_DIR);
const TOKEN = /\/r\/([A-Za-z0-9_-]{16,64})/;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const t0 = Date.now();
if (LOG) writeFileSync(LOG, '');
const say = (ev, o = {}) => { const line = JSON.stringify({ t: +((Date.now() - t0) / 1000).toFixed(1), ev, ...o }); console.log(line); if (LOG) appendFileSync(LOG, line + '\n'); };
const checks = [];
const check = (name, ok, info) => { checks.push({ name, ok: !!ok }); say('check', { name, ok: !!ok, info }); return !!ok; };

const kids = [];
function child(cmd, args, opts = {}) {
  const p = spawn(cmd, args, { stdio: ['ignore', 'pipe', 'pipe'], ...opts });
  kids.push(p);
  return p;
}
let locked = false;
function cleanup() {
  for (const p of kids) {
    if (p.exitCode !== null) continue;
    try { execFileSync('taskkill', ['/PID', String(p.pid), '/T', '/F'], { stdio: 'ignore' }); } catch { try { p.kill(); } catch { /* gone */ } }
  }
  if (locked) { try { rmSync(LOCK, { recursive: true, force: true }); } catch { /* gone */ } locked = false; }
}
process.on('exit', cleanup);
process.on('SIGINT', () => { cleanup(); process.exit(130); });

async function waitFor(fn, ms, step = 200) {
  const end = Date.now() + ms;
  for (;;) { try { const v = await fn(); if (v) return v; } catch { /* not yet */ } if (Date.now() > end) return null; await sleep(step); }
}
function finish() {
  const failed = checks.filter((c) => !c.ok).map((c) => c.name);
  say('verdict', { ok: failed.length === 0, passed: checks.length - failed.length, of: checks.length, failed, secs: Math.round((Date.now() - t0) / 1000) });
  cleanup();
  process.exit(failed.length ? 1 : 0);
}

// ---- 1. lobby + room (preview gate on), page server -----------------------------------------------------------
let lobby = arg('lobby', null);
if (lobby && !/^http:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/.test(lobby)) { console.error('link-e2e: local lobbies only'); process.exit(2); }
if (!lobby) {
  const server = arg('server', null);
  if (!server) { console.error('link-e2e: --server <CCP-Server checkout> or --lobby <url>'); process.exit(2); }
  const env = { ...process.env, ...(arg('node-path', null) ? { NODE_PATH: arg('node-path') } : {}) };
  const rl = child(process.execPath, [join(server, 'proxy/scripts/race-local.mjs'), '--port', String(LOBBY_PORT), '--room-port', String(ROOM_PORT), '--preview', 'u_host'],
    { env, cwd: join(server, 'proxy') });
  rl.stdout.on('data', (d) => { for (const l of String(d).split('\n')) if (l.trim()) say('lobby-out', { line: l.trim().slice(0, 240) }); });
  rl.stderr.on('data', (d) => say('lobby-err', { line: String(d).trim().slice(0, 300) }));
  lobby = `http://127.0.0.1:${LOBBY_PORT}`;
}
const pageSrv = child(process.execPath, [join(ROOT, 'dev/serve.mjs'), String(PAGE)], { cwd: ROOT });
pageSrv.stderr.on('data', (d) => say('page-err', { line: String(d).trim().slice(0, 300) }));
check('lobby up', await waitFor(async () => (await fetch(lobby + '/__health')).ok, 15000));
check('page server up', await waitFor(async () => (await fetch(`http://127.0.0.1:${PAGE}/index.html`)).ok, 15000));
// the preview gate: a signed in stranger (not listed, no link claimed) is refused with `preview`
const stranger = await fetch(lobby + '/v2/race/open', { headers: { authorization: 'Bearer jwt-u_stranger', 'x-dev-name': 'Stranger', 'x-dev-tier': '2' } });
check('preview gate refuses a stranger', stranger.status === 403 && (await stranger.json().catch(() => ({}))).error === 'preview', stranger.status);

// ---- 2. one browser, pages over CDP -----------------------------------------------------------------------------
if (USE_LOCK) {
  for (let i = 0; i < 24 && !locked; i++) {
    try { mkdirSync(LOCK); locked = true; writeFileSync(join(LOCK, 'owner-link-e2e'), String(process.pid)); } catch { say('lock-wait'); await sleep(15000); }
  }
  if (!locked) { check('browser.lock taken', false); finish(); }
}
const watchdog = setTimeout(() => { say('fatal', { why: 'watchdog (6 min lock budget)' }); checks.push({ name: 'watchdog', ok: false }); finish(); }, 5.75 * 60 * 1000);
const profile = mkdtempSync(join(tmpdir(), 'gr-link-'));
child(BROWSER, ['--headless=new', '--mute-audio', '--remote-debugging-port=' + CDP, '--user-data-dir=' + profile, '--window-size=1280,720',
  '--ignore-gpu-blocklist', '--no-first-run', '--no-default-browser-check', '--disable-background-timer-throttling',
  '--disable-renderer-backgrounding', '--disable-backgrounding-occluded-windows', 'about:blank']);
const ver = await waitFor(async () => (await fetch(`http://127.0.0.1:${CDP}/json/version`)).json(), 15000, 150);
if (!ver) { say('fatal', { why: 'no browser' }); checks.push({ name: 'browser', ok: false }); finish(); }
const ws = new WebSocket(ver.webSocketDebuggerUrl);
await new Promise((r) => { ws.onopen = r; });
let id = 0;
const wait = new Map(), errors = {}, sessions = {};
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.id && wait.has(m.id)) { const w = wait.get(m.id); wait.delete(m.id); if (m.error) w.rej(new Error(m.error.message)); else w.res(m.result); return; }
  const who = sessions[m.sessionId];
  if (!who) return;
  if (m.method === 'Runtime.exceptionThrown') errors[who].push(m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text);
  if (m.method === 'Runtime.consoleAPICalled' && (m.params.type === 'error' || m.params.type === 'warning')) {
    const text = m.params.args.map((a) => a.value ?? a.description).join(' ');
    if (m.params.type === 'error') errors[who].push(text);
    say('page-console', { who, type: m.params.type, text: text.slice(0, 240) });
  }
};
const cdp = (method, params = {}, sessionId) => { const msg = { id: ++id, method, params }; if (sessionId) msg.sessionId = sessionId; ws.send(JSON.stringify(msg)); return new Promise((res, rej) => wait.set(msg.id, { res, rej })); };
const origin = `http://127.0.0.1:${PAGE}`;
try { await cdp('Browser.grantPermissions', { origin, permissions: ['clipboardReadWrite', 'clipboardSanitizedWrite'] }); } catch (e) { say('no-clipboard-grant', { e: String(e) }); }

async function openPage(who, query) {
  const { targetId } = await cdp('Target.createTarget', { url: 'about:blank', newWindow: true });
  const { sessionId: sid } = await cdp('Target.attachToTarget', { targetId, flatten: true });
  sessions[sid] = who; errors[who] = [];
  await cdp('Runtime.enable', {}, sid); await cdp('Page.enable', {}, sid);
  await cdp('Emulation.setFocusEmulationEnabled', { enabled: true }, sid);
  const ev = async (expr, gesture = false) => { const r = await cdp('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true, userGesture: gesture }, sid); return r.result ? r.result.value : undefined; };
  const click = (sel) => ev(`(() => { const b = document.querySelector(${JSON.stringify(sel)}); if (!b || b.disabled) return false; b.click(); return true; })()`, true);
  const text = (sel) => ev(`(() => { const e = document.querySelector(${JSON.stringify(sel)}); return e ? e.textContent : null; })()`);
  const D = () => ev('JSON.stringify((({ phase, lap, place, online, onlineResult }) => ({ phase, lap, place, online, onlineResult }))(window.__glassroad || {}))').then((x) => JSON.parse(x || '{}'));
  const shot = async (name) => {
    if (!SHOTS) return;
    try { const r = await cdp('Page.captureScreenshot', { format: 'png' }, sid); const f = join(SHOTS, name + '.png'); writeFileSync(f, Buffer.from(r.data, 'base64')); say('shot', { f }); } catch (e) { say('shot-fail', { name, e: String(e) }); }
  };
  const url = `${origin}/?bot=1&chart=0&quality=phone&lobby=${encodeURIComponent(lobby)}&${query}`;
  await cdp('Page.navigate', { url }, sid);
  const close = () => cdp('Target.closeTarget', { targetId }).catch(() => {});
  return { who, sid, ev, click, text, D, shot, close };
}
if (SHOTS) mkdirSync(SHOTS, { recursive: true });
const kind = (p) => p.ev('document.querySelector("#card") && document.querySelector("#card").dataset.kind');
const note = (p) => p.text('#card-note');
const seatNames = (p) => p.ev('[...document.querySelectorAll(".lb-seats .n")].map((e) => e.textContent).join("|")');

// ---- 3. A hosts a friends room and makes the link -----------------------------------------------------------------
const A = await openPage('A', 'uid=u_host&name=Host&tier=2');
check('A boots to the menu', await waitFor(async () => (await A.ev('window.__glassroad && window.__glassroad.phase')) === 'menu', 90000, 500));
await A.click('[data-act=online]');
check('A opens the lobby', await waitFor(async () => (await kind(A)) === 'lobby', 8000));
await A.click('[data-act=lb-host][data-id=friends]');
check('A opens the friends room form', await waitFor(async () => (await A.text('#card .card-title')) === 'Friends room', 5000));
for (let i = 0; i < 7; i++) { await A.click('[data-act=lb-hstep][data-key=laps]'); await sleep(40); }      // 3 -> 1 lap (wraps at 9)
for (let i = 0; i < 3; i++) { await A.click('[data-act=lb-hstep][data-key=seats]'); await sleep(40); }     // 8 -> 4 seats
const form = await A.ev('[...document.querySelectorAll("[data-act=lb-hstep]")].map((b) => b.dataset.key + "=" + b.textContent).join(" ")');
check('A sets 1 lap, 4 seats, bots fill', /laps=1\b/.test(form) && /seats=4\b/.test(form) && /botFill=On/.test(form), form);
await A.click('[data-act=lb-open]');
check('A seated in the friends room', await waitFor(async () => !!(await A.ev('!!document.querySelector("[data-act=lb-ready]")')), 15000));
check('A sees the Invite link button (access host)', await waitFor(async () => A.click('[data-act=lb-link]'), 10000, 300));
const linkNote = await waitFor(async () => { const n = await note(A); return n && /Link/.test(n) ? n : null; }, 10000);
let linkUrl = await A.text('.lb-url');
const clip = linkUrl ? null : await A.ev('navigator.clipboard.readText().catch((e) => "ERR " + e.message)', true);
if (!linkUrl) linkUrl = clip;
const token = (TOKEN.exec(linkUrl || '') || [])[1] || null;
check('A copied the link (clipboard or fallback line)', !!token, { note: linkNote, from: clip === null ? 'fallback line' : 'clipboard', url: linkUrl });
await A.shot('link-1-host-room');
if (!token) finish();
const look = await (await fetch(`${lobby}/v2/race/link/${token}`)).json();
check('public lookup names the host, 8 seats left', look && look.live === true && look.name === 'Host' && look.left === 8, look);

// ---- 4. C (tier 0) opens the link: the Prime card --------------------------------------------------------------
const C = await openPage('C', `uid=u_zero&name=Zero&tier=0&r=${token}`);
const cNote = await waitFor(async () => { const n = await note(C); return n && /Prime/.test(n) ? n : null; }, 90000, 500);
check('tier 0 guest sees the Prime card', cNote === 'Racing a friend needs Prime.', cNote);
check('the address bar drops r after the claim', (await C.ev('new URLSearchParams(location.search).has("r")')) === false, await C.ev('location.search'));
await C.shot('link-2-tier0-prime-card');
await C.close();

// ---- 5. B (Prime, not on the preview list) opens the link: seated in A's room ---------------------------------
const B = await openPage('B', `uid=u_guest&name=Guest&tier=2&r=${token}`);
check('B seated in A\'s room through the link', await waitFor(async () => !!(await B.ev('!!document.querySelector("[data-act=lb-ready]")')), 90000, 500));
const bSeats = await waitFor(async () => { const s = await seatNames(B); return /Host/.test(s) && /Guest/.test(s) ? s : null; }, 8000);
check('B sees Host and itself in the room', !!bSeats, bSeats);
const aSeats = await waitFor(async () => { const s = await seatNames(A); return /Guest/.test(s) ? s : null; }, 8000);
check('A sees Guest join', !!aSeats, aSeats);
const lookAfter = await (await fetch(`${lobby}/v2/race/link/${token}`)).json();
check('the claim used one seat of the link', lookAfter && lookAfter.left === 7, lookAfter);
await B.click('[data-act=lb-ready]');
await B.shot('link-3-guest-seated');

// ---- 6. A starts, both race to the finish ---------------------------------------------------------------------------
check('A starts the room', await waitFor(async () => A.click('[data-act=lb-start]'), 8000, 300));
check('both pages race', await waitFor(async () => (await A.D()).phase === 'racing' && (await B.D()).phase === 'racing', 45000, 400));
const dA = await A.D(), dB = await B.D();
check('same room, four seats', dA.online && dB.online && dA.online.roomId === dB.online.roomId && dA.online.seats === 4, { A: dA.online, B: dB.online });
await sleep(5000);
await A.shot('link-4-racing-host'); await B.shot('link-4-racing-guest');
const res = await waitFor(async () => { const a = await A.D(), b = await B.D(); return a.onlineResult && b.onlineResult ? { a: a.onlineResult, b: b.onlineResult } : null; }, 200000, 1000);
check('both pages get the room results', !!res, res);
// each page names its own row "You": swap in the player's name before comparing
const named = (rows, me) => (rows || []).map((r) => [r[0], r[1] === 'You' ? me : r[1], r[2]]);
check('both results agree', res && JSON.stringify(named(res.a, 'Host')) === JSON.stringify(named(res.b, 'Guest')), res && named(res.a, 'Host'));
await A.shot('link-5-results');
await sleep(1500);
const kv = await (await fetch(lobby + '/__kv?prefix=race:')).json();
const result = dA.online && kv[`race:result:${dA.online.roomId}`] ? JSON.parse(kv[`race:result:${dA.online.roomId}`]) : null;
check('signed result in the ledger with Host and Guest', !!result && ['Host', 'Guest'].every((n) => result.rows.some((r) => r.name === n)),
  result && result.rows.map((r) => [r.place, r.name, r.timeMs, r.bot ? 'bot' : 'human', r.flags.join('|')]));
check('Host and Guest finished', !!result && result.rows.filter((r) => !r.bot).every((r) => r.finished));
check('no anti-cheat flags', !!result && result.rows.every((r) => !r.flags.length), result && result.rows.map((r) => r.flags));
check('no page errors', !errors.A.length && !errors.B.length && !errors.C.length, { A: errors.A.slice(0, 4), B: errors.B.slice(0, 4), C: errors.C.slice(0, 4) });

clearTimeout(watchdog);
try { ws.close(); } catch { /* closing */ }
cleanup();
await sleep(500);
try { rmSync(profile, { recursive: true, force: true }); } catch { /* the browser may hold it a moment */ }
finish();
