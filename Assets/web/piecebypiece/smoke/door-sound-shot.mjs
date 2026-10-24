/* ============================================================================
 * smoke/door-sound-shot.mjs - the Ladder board and the Pictures card in a real
 * browser (2026-10-14 polish pass), photographed and checked.
 *
 * Headless Edge over CDP (node's global WebSocket, no packages), a static
 * server on Resources/web, and the same fake desktop host as niches-shot.mjs
 * (so the Pictures card has a state to show). At every size:
 *   1. the Ladder with you at rank 30: the board mounts, three on the podium,
 *      the rest as rows, your row scrolled into view and ringed; a mid-arrival
 *      shot and a settled one; the rating roll and the podium sound (ui-cues
 *      log, after a real click made the audio context)
 *   2. the Ladder with you out of the top: your row pinned under the list
 *   3. the Ladder unrated
 *   4. the Pictures manager: Back top-left, a pill switched on pops, Back closes it
 *   5. the first-start Pictures card
 *   6. reduced motion: the Ladder simply there
 * and zero page errors throughout.
 *
 *   node smoke/door-sound-shot.mjs [--out DIR] [--sizes 1280x720,390x844] [--port 8851] [--cdp 9251]
 * Run it from PowerShell: the Bash sandbox refuses localhost.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdirSync, writeFileSync, readFileSync, existsSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf('--' + n); return i >= 0 && argv[i + 1] ? argv[i + 1] : d; };
const here = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(here, '..', '..');
const out = arg('out', path.join(here, 'out', 'door-sound'));
const sizes = arg('sizes', '1280x720,390x844').split(',').map((s) => s.split('x').map(Number));
const port = Number(arg('port', '8851'));
const cdpPort = Number(arg('cdp', '9251'));
const EDGE = process.env.PBP_EDGE || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
mkdirSync(out, { recursive: true });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.jpg': 'image/jpeg', '.webp': 'image/webp', '.gif': 'image/gif', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg',
  '.woff2': 'font/woff2', '.svg': 'image/svg+xml', '.wasm': 'application/wasm' };
const server = createServer((req, res) => {
  const u = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  const f = path.join(webRoot, u);
  if (!f.startsWith(webRoot) || !existsSync(f) || statSync(f).isDirectory()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': TYPES[path.extname(f).toLowerCase()] || 'application/octet-stream' });
  res.end(readFileSync(f));
}).listen(port, '127.0.0.1');

// the desktop's picture rule, as in niches-shot.mjs (a saved Pink choice, so the menu starts at once)
const FAKE_HOST = `(() => {
  const listeners = [];
  const emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  const first = (() => { try { return !!localStorage.getItem('__firstAsk'); } catch { return false; } })();
  let saved = { flavour: first ? '' : 'pink', custom: {}, online: true, chosen: !first };
  const state = () => ({ type: 'pbp:media-state', flavour: saved.flavour, last: saved.flavour, custom: saved.custom || {}, online: true,
    appWide: false, chosen: saved.chosen, library: true, canOnline: true });
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(t, fn) { if (t === 'message') listeners.push(fn); },
    removeEventListener() {},
    postMessage(m) {
      if (!m || typeof m.type !== 'string') return;
      switch (m.type) {
        case 'ready':
          emit({ type: 'pbp:settings', videoHoldSec: 15, reducedMotion: !!window.__still, whispers: [] });
          emit({ type: 'pbp:identity', unifiedId: '', displayName: 'CodeBambi', appVersion: 'shot', online: false, net: { serverBase: '', authToken: '', viaHost: true } });
          emit(state()); emit({ type: 'pbp:online-media', state: 'ready', subs: [], share: 70, images: [], clips: [], have: 24, want: 36 }); break;
        case 'ping': emit({ type: 'pong' }); break;
        case 'pbp:media-request': emit({ type: 'pbp:media', images: [], gifs: [], videos: [] }); break;
        case 'pbp:media-flavour': saved = { ...saved, flavour: m.flavour, custom: m.custom, chosen: true }; emit(state()); break;
        case 'stake-limits': emit({ type: 'stake', op: 'limits', ok: false, enabled: false }); break;
        case 'pbp:net': emit({ type: 'pbp:net-result', id: m.id, status: 0, body: '' }); break;
        default: break;
      }
    },
  };
})();`;

// STAND-IN for the menu lane's .door-back rules (door.css), so the shots read on this branch alone
const BACK_STANDIN = `.door-back{display:inline-flex;align-items:center;gap:6px;height:34px;padding:0 12px 0 8px;border-radius:10px;
  border:1px solid rgba(245,230,200,.25);background:rgba(245,230,200,.06);color:#E9E2F5;font:12px 'Segoe UI',sans-serif;
  letter-spacing:.12em;text-transform:uppercase;cursor:pointer}.door-back-ico{width:16px;height:16px}`;

const NAMES = ['eggnonymous', 'NarddaBimbo', 'Diana19', 'hanssagich', 'sillygal', 'alilbowlosoup', '.luzy', 'umberto marnickov', 'PinkAirHead',
  'Doubleclick15', 'Sissy Bambi', 'velvet', 'moth', 'sugarcube', 'static', 'lace', 'nightlight', 'pixie', 'hush', 'Rosalind', 'tinsel', 'marzipan'];
const ladderRows = (n, meAt = 0) => Array.from({ length: n }, (_, i) => ({
  rank: i + 1 - (i > 3 && i % 7 === 0 ? 1 : 0), display_name: i + 1 === meAt ? 'CodeBambi' : NAMES[i % NAMES.length] + (i >= NAMES.length ? i : ''),
  rating: 1235 - i * 3, games: 1 + ((i * 7) % 5),
}));
const meAt = (rank, rows) => ({ rated: true, rating: rows[rank - 1].rating, rank: rows[rank - 1].rank, games: 4, best: rows[rank - 1].rating + 6, total: 312 });

const profile = path.join(tmpdir(), 'pbp-door-sound-' + Date.now());
const edge = spawn(EDGE, ['--headless=new', '--remote-debugging-port=' + cdpPort, '--user-data-dir=' + profile, '--window-size=1280,860',
  '--no-first-run', '--no-default-browser-check', '--disable-extensions', '--use-gl=angle', '--use-angle=swiftshader',
  '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required'], { stdio: 'ignore' });

async function target() {
  for (let i = 0; i < 60; i++) {
    try { const r = await fetch(`http://127.0.0.1:${cdpPort}/json/new?about:blank`, { method: 'PUT' }); if (r.ok) return (await r.json()).webSocketDebuggerUrl; } catch { /* starting */ }
    await sleep(250);
  }
  throw new Error('headless Edge did not open a debugging port');
}

const problems = [];
let checks = 0;
const check = (cond, name) => { if (!cond) { problems.push('FAILED: ' + name); console.log('  x ' + name); } else { checks++; console.log('  ok ' + name); } };

async function main() {
  const ws = new WebSocket(await target());
  await new Promise((res, rej) => { ws.addEventListener('open', res, { once: true }); ws.addEventListener('error', rej, { once: true }); });
  let id = 0; const pending = new Map();
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m.result || m); pending.delete(m.id); return; }
    if (m.method === 'Runtime.exceptionThrown') problems.push('exception: ' + (m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text));
    if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') problems.push('console.error: ' + m.params.args.map((a) => a.description || a.value).join(' '));
  });
  const send = (method, params = {}) => new Promise((res) => { const n = ++id; pending.set(n, res); ws.send(JSON.stringify({ id: n, method, params })); });
  const js = async (expression) => {
    const r = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails) throw new Error('eval: ' + (r.exceptionDetails.exception?.description || r.exceptionDetails.text) + '\n' + expression);
    return r.result?.value;
  };
  const until = async (expr, ms = 20000) => { const t0 = Date.now(); while (Date.now() - t0 < ms) { if (await js(expr)) return true; await sleep(120); } return false; };
  const shot = async (name) => { const r = await send('Page.captureScreenshot', { format: 'png' }); const f = path.join(out, name); writeFileSync(f, Buffer.from(r.data, 'base64')); console.log('  shot ' + f); };
  const click = async (x, y) => {
    for (const type of ['mousePressed', 'mouseReleased']) await send('Input.dispatchMouseEvent', { type, x, y, button: 'left', clickCount: 1 });
  };
  const at = (sel) => js(`(() => { const r = document.querySelector(${JSON.stringify(sel)}).getBoundingClientRect(); return [r.x + r.width / 2, r.y + r.height / 2]; })()`);
  const cues = () => js(`import('./audio/ui-cues.js').then((m) => m.ui.log().map((e) => e.name))`);

  await send('Runtime.enable');
  await send('Page.enable');
  await send('Network.enable');
  await send('Network.setBlockedURLs', { urls: ['*scrolller.com*', '*/api/pbp/*'] });
  await send('Page.addScriptToEvaluateOnNewDocument', { source: FAKE_HOST });
  const url = `http://127.0.0.1:${port}/piecebypiece/index.html?seed=7&media=none`;
  // the page's own presentation choices: the opening card off, so the menu is what is shot
  const load = async (flags = '', prefs = {}) => {
    await send('Page.navigate', { url: `http://127.0.0.1:${port}/piecebypiece/smoke/shot.mjs` }).catch(() => {});
    await sleep(200);
    await js(`(() => { try { localStorage.clear(); localStorage.setItem('pbp-presentation-v1', ${JSON.stringify(JSON.stringify({ openIntro: false, ...prefs }))}); ${flags} } catch {} return true; })()`);
    await send('Page.navigate', { url });
    const up = await until(`!!(window.PBP && window.PBP.door && window.PBP.door.screen && window.PBP.door.screen() === 'menu')`, 30000);
    check(up, 'the door opens on the menu');
    await js(`(() => { const s = document.createElement('style'); s.textContent = ${JSON.stringify(BACK_STANDIN)}; document.head.append(s); return true; })()`);
    await sleep(700);
  };
  const ladder = async (top, me) => {
    await js(`(() => { const d = window.PBP.door.debug; d.ladder.fake({ top: ${JSON.stringify(top)}, me: ${JSON.stringify(me)} }); d.act('ladder'); return true; })()`);
    return until(`!!document.querySelector('.ladder-board.has-list, .ladder-board .door-empty')`, 5000);
  };

  for (const [w, h] of sizes) {
    console.log(`\n${w}x${h}`);
    await send('Emulation.setDeviceMetricsOverride', { width: w, height: h, deviceScaleFactor: 1, mobile: w < 600 });
    const tag = `${w}x${h}`;

    // 1. the ladder, you at rank 30
    await load();
    await click(Math.round(w / 2), 8);   // a real gesture: the audio context may start
    const rows = ladderRows(50, 30);
    check(await ladder({ rows, total: 312 }, meAt(30, rows)), 'the ladder board paints');
    check(await js(`document.getAnimations().some((a) => /^ladder-/.test(a.animationName || '') && a.playState === 'running')`), 'the board is arriving (its animations run)');
    await sleep(150);
    await shot(`ladder-arriving-${tag}.png`);
    await sleep(2400);
    await shot(`ladder-${tag}.png`);
    const st = await js(`(() => { const b = document.querySelector('.ladder-board'); const sc = b.querySelector('.ladder-scroll'); const mine = sc && sc.querySelector('.ladder-row.mine');
      const r = mine && mine.getBoundingClientRect(), s = sc && sc.getBoundingClientRect();
      return { mounted: b.dataset.mounted === '1', steps: b.querySelectorAll('.ladder-step').length, rows: b.querySelectorAll('.ladder-row').length,
        back: !!b.querySelector('.ladder-head [data-back]'), esc: /esc\\s*-/i.test(document.querySelector('.door-card').textContent),
        scrolled: sc ? sc.scrollTop : -1, inView: !!(r && s && r.top >= s.top - 2 && r.bottom <= s.bottom + 2), found: !!(mine && mine.classList.contains('found')),
        you: (b.querySelector('.ladder-you-n') || {}).textContent, cardBottom: document.querySelector('.door-card').getBoundingClientRect().bottom, vh: innerHeight }; })()`);
    check(st.mounted && st.steps === 3 && st.rows === 47, `board: 3 on the podium, 47 rows (${st.steps}, ${st.rows})`);
    check(st.back && !st.esc, 'a Back button, no "esc -" small print');
    check(st.scrolled > 0 && st.inView && st.found, `your row is scrolled to and ringed (scrollTop ${st.scrolled})`);
    check(st.you === String(meAt(30, rows).rating), `your rating rolled up and landed (${st.you})`);
    check(st.cardBottom <= st.vh, 'the card fits the screen');
    const heard = await cues();
    check(heard.includes('tick'), `the roll ticks (${heard.join(',') || 'nothing'})`);
    check(heard.includes('stamp'), 'the podium lands with a stamp on the first visit');

    // 2. out of the top
    await load();
    const top = ladderRows(50);
    check(await ladder({ rows: top, total: 312 }, { rated: true, rating: 1031, rank: 117, games: 3, best: 1040, total: 312 }), 'out of the top: the board paints');
    await sleep(2200);
    check(await js(`!!document.querySelector('.ladder-pinned .ladder-row.mine')`), 'your row is pinned under the list');
    await shot(`ladder-pinned-${tag}.png`);

    // 3. unrated
    await load();
    check(await ladder({ rows: top.slice(0, 11), total: 11 }, { rated: false, games: 0 }), 'unrated: the board paints');
    await sleep(1800);
    await shot(`ladder-unrated-${tag}.png`);

    // 4. the Pictures manager
    await load();
    await click(Math.round(w / 2), 8);
    await js(`(() => { window.PBP.door.debug.act('pictures'); return true; })()`);
    check(await until(`!!document.querySelector('.pbp-np-modal.is-in .np-manage')`, 5000), 'Pictures opens the manager');
    check(await js(`!!document.querySelector('.pbp-np-modal .pbp-np-head [data-back]')`), 'the manager has a Back button');
    await sleep(500);
    const off = await js(`(() => { const b = [...document.querySelectorAll('.pbp-np-modal .np-pill:not(.is-on) [data-niche]')][0]; return b ? b.dataset.niche : null; })()`);
    if (off) {
      const [x, y] = await at(`.pbp-np-modal [data-niche="${off}"]`);
      await click(x, y);
      await sleep(80);
      check(await js(`document.querySelector('.pbp-np-modal [data-niche="${off}"]').getAttribute('aria-pressed') === 'true'`), 'a pill switches on');
      check((await cues()).includes('select'), 'and pops with a note');
    }
    await sleep(500);
    await shot(`pictures-${tag}.png`);
    const [bx, by] = await at('.pbp-np-modal .pbp-np-head [data-back]');
    await click(bx, by);
    check(await until(`!document.querySelector('.pbp-np-modal')`, 3000), 'Back closes the card (and it goes, not just vanishes)');
    check((await cues()).includes('back'), 'Back sounds');

    // 5. the first-start card: Play solo with nothing saved asks first
    await load(`localStorage.setItem('__firstAsk', '1');`);
    await js(`(() => { document.querySelector('[data-act=solo]').click(); return true; })()`);
    check(await until(`!!document.querySelector('.pbp-np-modal.is-first.is-in')`, 5000), 'the first start opens the picture card');
    check(await js(`!!document.querySelector('.pbp-np-modal.is-first [data-back]')`), 'the first-start card has a Back button');
    await sleep(500);
    await shot(`pictures-first-${tag}.png`);
    await js(`(() => { document.querySelector('.pbp-np-modal [data-back]').click(); return true; })()`);
    check(await until(`!document.querySelector('.pbp-np-modal') && window.PBP.door.isUp()`, 3000), 'Back leaves the first-start card and starts nothing');

    // 6. reduced motion: the ladder is simply there
    await load('', { reducedMotion: true });
    check(await ladder({ rows, total: 312 }, meAt(30, rows)), 'reduced motion: the board paints');
    await sleep(250);
    const calm = await js(`(() => { const b = document.querySelector('.ladder-board'); const sc = b.querySelector('.ladder-scroll');
      return { landed: b.classList.contains('landed') || !!b.closest('.still'), scrolled: sc ? sc.scrollTop : -1, you: (b.querySelector('.ladder-you-n') || {}).textContent }; })()`);
    check(calm.landed && calm.scrolled > 0 && calm.you === String(meAt(30, rows).rating), 'reduced motion: landed at once, scrolled, the final number');
    await shot(`ladder-still-${tag}.png`);
  }
}

let code = 0;
try { await main(); } catch (e) { problems.push('harness: ' + (e && e.stack || e)); }
finally {
  try { edge.kill(); } catch { /* gone */ }
  server.close();
}
if (problems.length) { console.log('\n' + problems.join('\n')); code = 1; }
console.log(`\ndoor-sound-shot: ${checks} checks, ${problems.length} problems -> ${out}`);
process.exit(code);
