// rt-linux-spike/bench-server.mjs - zero-dependency server for the Racing Thoughts spike (Node 22+).
// Run: node bench-server.mjs --root <glassroad dir> --out <results dir> [--port 8791]
// Serves the glassroad tree at / and ../arcademy at /arcademy/ (as dev/serve.mjs does), injects spike.js first into
// index.html, and takes the probe's records: POST /__spike/result writes <out>/runs/<run>.json with the engine's
// memory (PSS of every process under the pid in <out>/current.pid, sampled at each /__spike/mark; Linux /proc only).
// GET /__spike/hashes = the onlineCourseHash Node builds for the five courses (what the room's cards carry).
import { createServer } from 'node:http';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { extname, join, normalize, resolve, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const arg = (k, d) => { const i = process.argv.indexOf('--' + k); return i > 0 ? process.argv[i + 1] : d; };
const ROOT = resolve(arg('root', '.'));
const OUT = resolve(arg('out', './spike-out'));
const PORT = Number(arg('port', 8791));
const KIT = fileURLToPath(new URL('.', import.meta.url));
const ARCADEMY = resolve(ROOT, '..', 'arcademy');
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8', '.json': 'application/json; charset=utf-8', '.png': 'image/png', '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg', '.webp': 'image/webp', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary', '.bin': 'application/octet-stream',
  '.ktx2': 'image/ktx2', '.mp3': 'audio/mpeg', '.ogg': 'audio/ogg', '.wav': 'audio/wav', '.mp4': 'video/mp4', '.webm': 'video/webm',
  '.woff2': 'font/woff2', '.woff': 'font/woff', '.ttf': 'font/ttf', '.wasm': 'application/wasm', '.gif': 'image/gif' };

await mkdir(join(OUT, 'runs'), { recursive: true });
const memo = new Map();   // run -> [{ phase, atS, pssMB, procs }]

/** PSS (MB) of the process tree under the engine pid run.sh wrote; null off Linux or with no pid. */
function sampleMemory() {
  if (process.platform !== 'linux') return null;
  let root;
  try { root = Number(readFileSync(join(OUT, 'current.pid'), 'utf8').trim()); } catch (e) { return null; }
  if (!root) return null;
  const kids = new Map();
  for (const d of readdirSyncSafe('/proc')) {
    if (!/^\d+$/.test(d)) continue;
    try {
      const stat = readFileSync('/proc/' + d + '/stat', 'utf8');
      const ppid = Number(stat.slice(stat.lastIndexOf(')') + 2).split(' ')[1]);
      if (!kids.has(ppid)) kids.set(ppid, []);
      kids.get(ppid).push(Number(d));
    } catch (e) { /* gone */ }
  }
  const tree = [], todo = [root];
  while (todo.length) { const p = todo.pop(); tree.push(p); for (const k of kids.get(p) || []) todo.push(k); }
  let kb = 0; const procs = [];
  for (const p of tree) {
    try {
      const roll = readFileSync('/proc/' + p + '/smaps_rollup', 'utf8');
      const m = /^Pss:\s+(\d+)/m.exec(roll);
      const name = readFileSync('/proc/' + p + '/comm', 'utf8').trim();
      if (m) { kb += Number(m[1]); procs.push(name + ':' + Math.round(Number(m[1]) / 1024)); }
    } catch (e) { /* gone or not ours */ }
  }
  return { pssMB: Math.round(kb / 1024), procs };
}
function readdirSyncSafe(d) { try { return readdirSync(d); } catch (e) { return []; } }

let hashCache = null;
async function nodeHashes() {
  if (hashCache) return hashCache;
  const imp = (p) => import(pathToFileURL(join(ROOT, p)).href);
  const [{ onlineCourseHash }, { loadTrack }, { createFeel }] = await Promise.all([imp('net/courseHash.js'), imp('track/load.js'), imp('core/feel.js')]);
  const feel = createFeel(JSON.parse(await readFile(join(ROOT, 'feel.json'), 'utf8'))).toJSON();
  const out = {};
  for (const id of ['threshold', 'keep', 'goonworks', 'boudoir', 'mainframe']) {
    const tj = JSON.parse(await readFile(join(ROOT, 'tracks', id + '.json'), 'utf8'));
    out[id] = await onlineCourseHash(tj, feel, loadTrack(tj, { moving: true }));
  }
  return (hashCache = out);
}

const body = (req) => new Promise((res) => { const b = []; req.on('data', (c) => b.push(c)); req.on('end', () => res(Buffer.concat(b).toString('utf8'))); });
const safeRun = (s) => String(s || 'run').replace(/[^a-zA-Z0-9_.-]/g, '_').slice(0, 80);

createServer(async (req, res) => {
  const url = new URL(req.url, 'http://127.0.0.1');
  const send = (code, type, data) => { res.writeHead(code, { 'content-type': type, 'cache-control': 'no-store' }); res.end(data); };
  try {
    if (url.pathname === '/__spike/spike.js') return send(200, TYPES['.js'], await readFile(join(KIT, 'spike.js')));
    if (url.pathname === '/__spike/hashes') return send(200, TYPES['.json'], JSON.stringify(await nodeHashes()));
    if (url.pathname === '/__spike/mark' && req.method === 'POST') {
      const run = safeRun(url.searchParams.get('run')), m = JSON.parse(await body(req) || '{}');
      const mem = sampleMemory();
      if (!memo.has(run)) memo.set(run, []);
      memo.get(run).push({ phase: m.phase, atS: m.atS, ...(mem || {}) });
      return send(204, 'text/plain', '');
    }
    if (url.pathname === '/__spike/result' && req.method === 'POST') {
      const run = safeRun(url.searchParams.get('run')), rep = JSON.parse(await body(req) || '{}');
      const marks = memo.get(run) || [], pss = marks.map((x) => x.pssMB).filter((x) => x != null);
      rep.memory = pss.length ? { menuMB: (marks.find((x) => x.phase === 'menu') || {}).pssMB ?? null, peakMB: Math.max(...pss),
        raceMB: (marks.filter((x) => x.phase === 'race').pop() || {}).pssMB ?? null, topProcs: (marks.filter((x) => x.phase === 'race').pop() || {}).procs || null } : null;
      await writeFile(join(OUT, 'runs', run + '.json'), JSON.stringify(rep, null, 1));
      console.log('[spike] result ' + run + (rep.failed ? ' FAILED ' + rep.failed : ' fps ' + (rep.race && rep.race.fpsMean)));
      return send(204, 'text/plain', '');
    }
    // static
    let base = ROOT, path = decodeURIComponent(url.pathname);
    if (path.startsWith('/arcademy/')) { base = ARCADEMY; path = path.slice('/arcademy'.length); }
    if (path.endsWith('/')) path += 'index.html';
    const file = normalize(join(base, path));
    if (!file.startsWith(base + sep) && file !== base) return send(403, 'text/plain', 'no');
    if (!existsSync(file)) return send(404, 'text/plain', 'not found');
    let data = await readFile(file);
    if (file === join(ROOT, 'index.html')) data = Buffer.from(String(data).replace('<head>', '<head>\n  <script src="/__spike/spike.js"></script>'));
    return send(200, TYPES[extname(file).toLowerCase()] || 'application/octet-stream', data);
  } catch (e) {
    console.error('[spike] ' + req.url + ': ' + e.message);
    return send(500, 'text/plain', String(e.message));
  }
}).listen(PORT, '127.0.0.1', () => console.log('[spike] serving ' + ROOT + ' on http://127.0.0.1:' + PORT + '/ -> ' + OUT));
