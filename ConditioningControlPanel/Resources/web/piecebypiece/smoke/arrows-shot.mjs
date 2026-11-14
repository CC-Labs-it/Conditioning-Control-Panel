/* ============================================================================
 * smoke/arrows-shot.mjs - the player's arrows and circles on the real board.
 *
 * Serves Resources/web itself, loads a hotseat board in headless msedge and
 * dispatches the PointerEvents a mouse would: right drag e2 -> e4 (an arrow),
 * right click d5 (a circle), the same right click again (gone), a right drag
 * that starts off the board (the camera orbits, nothing drawn), then a left
 * click (all wiped). Prints each step, saves two frames, exits non-zero on a
 * page error or a step that did not do what it says.
 *
 *   node smoke/arrows-shot.mjs [--out <dir>] [--port <debug port>]
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdirSync, readFileSync, writeFileSync, existsSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';

const EDGE = process.env.PBP_EDGE || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const arg = (n, f) => { const i = process.argv.indexOf('--' + n); return i > -1 && process.argv[i + 1] ? process.argv[i + 1] : f; };
const outDir = arg('out', join(tmpdir(), 'pbp-arrows'));
const port = Number(arg('port', '9283'));
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

const edge = spawn(EDGE, ['--headless=new', '--remote-debugging-port=' + port, '--user-data-dir=' + join(tmpdir(), 'pbp-arrows-' + Date.now()),
  '--window-size=1280,860', '--hide-scrollbars', '--no-first-run', '--no-default-browser-check', '--disable-extensions',
  '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'], { stdio: 'ignore' });

let failed = false;
const bad = (w) => { console.log('ERROR ' + w); failed = true; };

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
  await send('Runtime.enable'); await send('Page.enable');
  await send('Page.navigate', { url: `http://127.0.0.1:${web}/piecebypiece/index.html?hotseat=1` });
  for (let i = 0; i < 80 && !(await js('!!(window.PBP && window.PBP.board && window.PBP.board.annotate && window.PBP.board.projectSquare)')); i++) await sleep(250);
  // the front door would hold the board; a hotseat page puts it away
  await js('window.PBP.door?.close?.(); window.PBP.door?.hide?.(); true');
  await sleep(1500);
  await js(`window.__pt = (sq, type, button, buttons) => {
    const c = document.getElementById('board-canvas');
    const p = sq ? window.PBP.board.projectSquare(sq, 0) : { x: 8, y: 8 };
    c.dispatchEvent(new PointerEvent(type, { clientX: p.x, clientY: p.y, button, buttons, bubbles: true, cancelable: true, pointerId: 1, pointerType: 'mouse', isPrimary: true }));
  }; window.__pt2 = (x, y, type, button, buttons) => document.getElementById('board-canvas').dispatchEvent(new PointerEvent(type, { clientX: x, clientY: y, button, buttons, bubbles: true, cancelable: true, pointerId: 1, pointerType: 'mouse', isPrimary: true })); true`);
  const list = () => js('JSON.stringify(window.PBP.board.annotate.list().map(s => s.kind + ":" + s.from + "-" + s.to))').then(JSON.parse);
  const camPos = () => js('(() => { const s = window.PBP.camera.state(); return [s.free, s.theta.toFixed(4), s.phi.toFixed(4)].join(","); })()');
  const step = async (name, fn, want) => {
    await js(fn); await sleep(400);
    const got = await list();
    console.log(`  ${name}: ${JSON.stringify(got)}`);
    if (JSON.stringify(got) !== JSON.stringify(want)) bad(`${name}: wanted ${JSON.stringify(want)}`);
  };
  const doorUp = await js('!!window.PBP.door?.isUp?.()');
  console.log('  door up: ' + doorUp + ', camera ' + await camPos());
  const cam0 = await camPos();
  await step('right drag e2 -> e4', "__pt('e2','pointerdown',2,2); __pt('e3','pointermove',-1,2); __pt('e4','pointermove',-1,2); __pt('e4','pointerup',2,0)", ['arrow:e2-e4']);
  if ((await camPos()) !== cam0) bad('the camera moved during an arrow');
  await step('right click d5', "__pt('d5','pointerdown',2,2); __pt('d5','pointerup',2,0)", ['arrow:e2-e4', 'circle:d5-d5']);
  await step('right drag g1 -> f3 through the men', "__pt('g1','pointerdown',2,2); __pt('f3','pointermove',-1,2); __pt('f3','pointerup',2,0)", ['arrow:e2-e4', 'circle:d5-d5', 'arrow:g1-f3']);
  const meshes = await js('window.PBP.board.annotate.count()');
  console.log('  meshes on the board: ' + meshes);
  if (meshes !== 3) bad('wanted 3 meshes');
  const s1 = await send('Page.captureScreenshot', { format: 'png' });
  writeFileSync(join(outDir, 'arrows-drawn.png'), Buffer.from(s1.data, 'base64'));
  await step('right click d5 again', "__pt('d5','pointerdown',2,2); __pt('d5','pointerup',2,0)", ['arrow:e2-e4', 'arrow:g1-f3']);
  const cam1 = await camPos();
  await step('right drag off the board (orbit)', "__pt(null,'pointerdown',2,2); window.__pt2(60,60,'pointermove',-1,2); window.__pt2(160,90,'pointermove',-1,2); __pt(null,'pointerup',2,0)", ['arrow:e2-e4', 'arrow:g1-f3']);
  const cam2 = await camPos();
  console.log('  camera before/after the off-board right drag: ' + cam1 + ' -> ' + cam2);
  if (cam1 === cam2) bad('the off-board right drag did not orbit');
  await step('left click on the board', "__pt('a4','pointerdown',0,1); __pt('a4','pointerup',0,0)", []);
  if ((await js('window.PBP.board.annotate.count()')) !== 0) bad('meshes left after the wipe');
  const s2 = await send('Page.captureScreenshot', { format: 'png' });
  writeFileSync(join(outDir, 'arrows-wiped.png'), Buffer.from(s2.data, 'base64'));
  console.log('  frames in ' + outDir);
  ws.close();
} catch (e) { bad(e.message); }
finally { edge.kill(); server.close(); }
console.log(failed ? 'FAIL' : 'OK');
process.exit(failed ? 1 : 0);
