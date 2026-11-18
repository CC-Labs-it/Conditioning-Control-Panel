/* ============================================================================
 * smoke/castle-calm-shot.mjs - castling, the Loom and Board motion on the real board.
 *
 * Serves Resources/web itself, loads a hotseat board in headless msedge and
 * dispatches the PointerEvents a mouse would (the smoke/arrows-shot.mjs way):
 *
 *   1. the opening: the king has no castle, no castle marker
 *   2. 1.e4 e5 2.Nf3 Nc6 3.Bc4 Bc5, click the white king: g1 has the castle
 *      marker, h1 the rook ring; click the h1 rook: White castles (Kg1, Rf1)
 *   3. 3...Nf6 4.d3, DRAG the black king onto the h8 rook: Black castles
 *   4. the Loom held at a long think: its opacity is capped
 *   5. Board motion Full / Calm / Still: the camera sway the ramp hands the
 *      board, and whether the Loom keeps turning
 *
 * Prints each step, saves frames, exits non-zero on a page error or a step
 * that did not do what it says.
 *
 *   node smoke/castle-calm-shot.mjs [--out <dir>] [--port <debug port>]
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdirSync, readFileSync, writeFileSync, existsSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';

const EDGE = process.env.PBP_EDGE || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const arg = (n, f) => { const i = process.argv.indexOf('--' + n); return i > -1 && process.argv[i + 1] ? process.argv[i + 1] : f; };
const outDir = arg('out', join(tmpdir(), 'pbp-castle-calm'));
const port = Number(arg('port', '9284'));
const root = fileURLToPath(new URL('../../', import.meta.url));   // Resources/web
const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.woff2': 'font/woff2', '.png': 'image/png' };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const server = createServer((req, res) => {
  const path = normalize(join(root, decodeURIComponent(new URL(req.url, 'http://x').pathname)));
  if (!path.startsWith(normalize(root)) || !existsSync(path) || statSync(path).isDirectory()) { if (/\.m?js$/.test(path)) console.log('  404 ' + req.url); res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': TYPES[extname(path)] || 'application/octet-stream' });
  res.end(readFileSync(path));
}).listen(0);
const web = server.address().port;

const edge = spawn(EDGE, ['--headless=new', '--remote-debugging-port=' + port, '--user-data-dir=' + join(tmpdir(), 'pbp-castle-' + Date.now()),
  '--window-size=1280,860', '--hide-scrollbars', '--no-first-run', '--no-default-browser-check', '--disable-extensions',
  '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'], { stdio: 'ignore' });

let failed = false;
const bad = (w) => { console.log('ERROR ' + w); failed = true; };
const check = (what, cond, got) => { console.log(`  ${cond ? 'ok  ' : 'FAIL'} ${what}${got !== undefined ? ': ' + JSON.stringify(got) : ''}`); if (!cond) failed = true; };

async function open() {
  for (let i = 0; i < 60; i++) {
    try { const r = await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' }); if (r.ok) return (await r.json()).webSocketDebuggerUrl; } catch { /* starting */ }
    await sleep(250);
  }
  throw new Error('headless msedge did not open a debugging port');
}

try {
  mkdirSync(outDir, { recursive: true });
  const ws = new WebSocket(await open());
  await new Promise((r) => ws.addEventListener('open', r));
  let id = 0; const pending = new Map();
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m.result); pending.delete(m.id); }
    else if (m.method === 'Runtime.exceptionThrown') bad('page: ' + (m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text).split('\n')[0]);
    else if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') bad('console: ' + m.params.args.map(a => a.value ?? a.description).join(' ').slice(0, 200));
  });
  const send = (method, params = {}) => { const n = ++id; ws.send(JSON.stringify({ id: n, method, params })); return new Promise((r) => pending.set(n, r)); };
  const js = async (expression) => {
    const r = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result?.value;
  };
  const shot = async (name) => {
    const s = await send('Page.captureScreenshot', { format: 'png' });
    writeFileSync(join(outDir, name), Buffer.from(s.data, 'base64'));
  };
  await send('Runtime.enable'); await send('Page.enable');
  await send('Page.navigate', { url: `http://127.0.0.1:${web}/piecebypiece/index.html?hotseat=1` });
  for (let i = 0; i < 80 && !(await js('!!(window.PBP && window.PBP.board && window.PBP.board.drag && window.PBP.board.projectSquare && window.PBP.board.turnSpiral && window.PBP.ramp)')); i++) await sleep(250);
  await js('window.PBP.door?.close?.(); window.PBP.door?.hide?.(); true');
  await sleep(1500);
  await js(`window.__pt = (sq, type, button, buttons) => {
    const c = document.getElementById('board-canvas');
    const p = window.PBP.board.projectSquare(sq, 0);
    c.dispatchEvent(new PointerEvent(type, { clientX: p.x, clientY: p.y, button, buttons, bubbles: true, cancelable: true, pointerId: 1, pointerType: 'mouse', isPrimary: true }));
  };
  // while a man is in hand the cursor rides the plane he is held on, not the board (smoke/shot.mjs)
  window.__ptHeld = (sq, type, button, buttons) => {
    const c = document.getElementById('board-canvas');
    const p = window.PBP.board.projectSquare(sq, window.PBP.board.drag.holdHeight());
    c.dispatchEvent(new PointerEvent(type, { clientX: p.x, clientY: p.y, button, buttons, bubbles: true, cancelable: true, pointerId: 1, pointerType: 'mouse', isPrimary: true }));
  };
  window.__click = (sq) => { __pt(sq, 'pointerdown', 0, 1); __pt(sq, 'pointerup', 0, 0); };
  window.__pos = (sqs) => { const p = window.PBP.game.rules.position(); return sqs.map(s => p[s] ? p[s].side + p[s].type : '-').join(' '); };
  true`);
  const kinds = () => js('JSON.stringify(window.PBP.board.drag.debug().kinds)').then(JSON.parse);
  const play = (list) => js(`(${JSON.stringify(list)}).every(([f, t]) => !!window.PBP.game.tryMove(f, t))`);

  // 1. the opening: nothing to castle
  await js("window.__pt('e1','pointerdown',0,1); window.__pt('e1','pointerup',0,0); true"); await sleep(300);
  const k0 = await kinds();
  check('opening: the king shows no castle marker', !Object.values(k0).some(k => String(k).startsWith('castle')), k0);
  await js("window.PBP.board.drag.deselect(); true");

  // 2. White castles by clicking the king, then his rook
  check('1.e4 e5 2.Nf3 Nc6 3.Bc4 Bc5 played', await play([['e2', 'e4'], ['e7', 'e5'], ['g1', 'f3'], ['b8', 'c6'], ['f1', 'c4'], ['f8', 'c5']]));
  await sleep(1800);   // the men land
  await js("window.__click('e1'); true"); await sleep(400);
  const k1 = await kinds();
  check('white king selected: g1 castle, h1 castle-rook, f1/e2 plain', k1.g1 === 'castle' && k1.h1 === 'castle-rook' && k1.f1 === 'move' && k1.e2 === 'move' && !k1.c1, k1);
  await sleep(300);
  await shot('castle-hint.png');
  await js("window.__click('h1'); true"); await sleep(1800);
  const p1 = await js("window.__pos(['e1','f1','g1','h1'])");
  check('click the h1 rook: White castled (Rf1, Kg1)', p1 === '- wr wk -', p1);
  check('the move list says O-O', /O-O/.test(await js("JSON.stringify(window.PBP.game.rules.chess.history())")), await js("window.PBP.game.rules.chess.history().slice(-1)[0]"));

  // 3. Black castles by dragging the king onto his rook
  check('3...Nf6 4.d3 played', await play([['g8', 'f6'], ['d2', 'd3']]));
  await sleep(1800);
  await js("__pt('e8','pointerdown',0,1); true"); await sleep(80);
  await js("for (const s of ['f8','g8','h8']) __ptHeld(s,'pointermove',-1,1); true"); await sleep(400);
  const k2 = await kinds();
  check('black king in hand: g8 castle, h8 castle-rook', k2.g8 === 'castle' && k2.h8 === 'castle-rook', k2);
  await js("__ptHeld('h8','pointerup',0,0); true"); await sleep(1800);
  const p2 = await js("window.__pos(['e8','f8','g8','h8'])");
  check('drop the king on the h8 rook: Black castled (Rf8, Kg8)', p2 === '- br bk -', p2);

  // 4. the Loom at a long think
  await js(`(() => {
    const c = window.PBP.game.clock, t0 = performance.now();
    c.isRunning = () => true; c.turnElapsedMs = () => 60000 + (performance.now() - t0);
    const s = c.snapshot.bind(c); c.snapshot = () => ({ ...s(), active: window.PBP.game.turn() });
    return true; })()`);
  await sleep(2500);
  const loom = await js('JSON.stringify(window.PBP.board.turnSpiral.debug())').then(JSON.parse);
  check('the Loom is up and capped at 60%', loom.visible && loom.alpha > .3 && loom.alpha <= .6 + 1e-9, { alpha: loom.alpha, visible: loom.visible });
  await shot('loom-capped.png');

  // 5. Board motion
  await js(`(() => { const b = window.PBP.board, set = b.setCameraSway.bind(b); window.__sway = [];
    b.setCameraSway = v => { window.__sway.push(v); return set(v); }; window.PBP.ramp.debug().setMeter(1); return true; })()`);
  const prefs = "import('/piecebypiece/game/preferences.js')";
  for (const [motion, want] of [['full', 1], ['calm', .35], ['still', 0]]) {
    await js(`${prefs}.then(m => { m.setPresentation({ boardMotion: '${motion}' }); return true; })`);
    await sleep(600);   // the next frame repaints the Loom at the new pace
    await js('window.__sway.length = 0; true');
    const a = await js('window.PBP.board.turnSpiral.debug().paintedMs');
    await sleep(2200);
    const b = await js('window.PBP.board.turnSpiral.debug().paintedMs');
    const sway = await js('window.__sway.slice(-1)[0]');
    const pressed = await js(`document.querySelector('[data-boardmotion="${motion}"]')?.getAttribute('aria-pressed')`);
    check(`Board motion ${motion}: sway ${want}`, typeof sway === 'number' && Math.abs(sway - want) < 1e-6, sway);
    check(`Board motion ${motion}: the Loom ${want ? 'keeps turning' : 'holds still'}`, want ? b !== a : b === a, [a, b]);
    check(`Board motion ${motion}: its Options button reads pressed`, pressed === 'true', pressed);
  }
  await js(`${prefs}.then(m => { m.setPresentation({ boardMotion: 'full' }); return true; })`);
  console.log('  frames in ' + outDir);
  ws.close();
} catch (e) { bad(e.message); }
finally { edge.kill(); server.close(); }
console.log(failed ? 'FAIL' : 'OK');
process.exit(failed ? 1 : 0);
