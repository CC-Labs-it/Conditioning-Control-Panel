// dev/serve.mjs - zero-dependency static server for the glassroad tree, no caching.
// Run: node dev/serve.mjs [port]   (default 8790, or PORT env). Serves the folder above dev/.
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, normalize, resolve, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = resolve(fileURLToPath(new URL('..', import.meta.url)));
// the app serves Resources/web whole, so glassroad's ../../arcademy imports (the Loom) resolve to /arcademy/
// here; only that tree is mounted beside this one
const ARCADEMY = resolve(ROOT, '..', 'arcademy');
// DEV ONLY: the owner's private song folder (charts + mp3s, handoff-1022/MUSIC-ROAD-BRIEF.md) at /private-audio/.
// It is read in place and never copied into this tree; GLASSROAD_AUDIO_DIR points it elsewhere.
const PRIVATE = resolve(process.env.GLASSROAD_AUDIO_DIR || 'C:/Projects/glassroad-audio');
const MOUNTS = [['/arcademy/', ARCADEMY], ['/private-audio/', PRIVATE]];
const PORT = Number(process.argv[2] || process.env.PORT || 8790);
// A developer may attach a private local provider. Its settings and receipts stay outside this tree.
const providerModule = process.env.GLASSROAD_PREVIEW_PROVIDER;
const liveCards = providerModule
  ? (await import(pathToFileURL(resolve(providerModule)).href)).createLiveCardMedia() : null;
const MIME = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.mjs': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8', '.css': 'text/css; charset=utf-8', '.wasm': 'application/wasm', '.glb': 'model/gltf-binary', '.ktx2': 'image/ktx2',
  '.png': 'image/png', '.jpg': 'image/jpeg', '.svg': 'image/svg+xml', '.webp': 'image/webp', '.gif': 'image/gif',
  '.ogg': 'audio/ogg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.md': 'text/plain; charset=utf-8', '.txt': 'text/plain; charset=utf-8',
};

const server = createServer(async (req, res) => {
  const head = { 'Cache-Control': 'no-store', 'Cross-Origin-Opener-Policy': 'same-origin' };
  if (liveCards && new URL(req.url, 'http://local').pathname.startsWith('/__card-media/')) {
    const path = new URL(req.url, 'http://local').pathname;
    if (path === '/__card-media/start') {
      if (req.method !== 'POST') { res.writeHead(405, head); res.end(); return; }
      if (req.headers.origin !== `http://127.0.0.1:${PORT}` && req.headers.origin !== `http://localhost:${PORT}`) {
        res.writeHead(403, head); res.end(); return;
      }
      try { await liveCards.start(); res.writeHead(204, head); res.end(); }
      catch { res.writeHead(503, head); res.end(); }
      return;
    }
    if (!['GET', 'HEAD'].includes(req.method)) { res.writeHead(405, head); res.end(); return; }
    try {
      if (path === '/__card-media/manifest.json') {
        const body = JSON.stringify(await liveCards.manifest());
        res.writeHead(200, { ...head, 'Content-Type': 'application/json' });
        res.end(req.method === 'HEAD' ? undefined : body); return;
      }
      const row = await liveCards.read(path.slice('/__card-media/'.length));
      if (!row || Date.now() >= row.expiresAt) { res.writeHead(404, head); res.end(); return; }
      res.writeHead(200, { ...head, 'Content-Type': row.type, 'Content-Length': row.bytes.length });
      res.end(req.method === 'HEAD' ? undefined : row.bytes); return;
    } catch { res.writeHead(503, head); res.end(); return; }
  }
  if (req.method === 'POST' && req.url === '/__diag') {   // the page's dev beacon, printed for the session log
    let body = ''; req.on('data', (c) => { if (body.length < 8000) body += c; });
    req.on('end', () => { console.log(new Date().toISOString().slice(11, 19), 'diag', body); res.writeHead(204, head); res.end(); });
    return;
  }
  try {
    let path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
    if (path.endsWith('/')) path += 'index.html';
    const mount = MOUNTS.find(([pre]) => path.startsWith(pre));
    const base = mount ? mount[1] : ROOT;
    const file = normalize(join(base, mount ? path.slice(mount[0].length - 1) : path));
    if (file !== base && !file.startsWith(base + sep)) { res.writeHead(403, head); res.end('forbidden'); return; }
    const st = await stat(file);
    if (!st.isFile()) throw new Error('not a file');
    const body = await readFile(file);
    res.writeHead(200, { ...head, 'Content-Type': MIME[extname(file).toLowerCase()] || 'application/octet-stream', 'Content-Length': body.length });
    res.end(req.method === 'HEAD' ? undefined : body);
  } catch (e) {
    res.writeHead(404, { ...head, 'Content-Type': 'text/plain' });
    res.end('not found');
  }
});
server.listen(PORT, '127.0.0.1', () => console.log('glassroad on http://127.0.0.1:' + PORT + '/'));
server.on('close', () => liveCards?.dispose());
if (liveCards) {
  const stop = () => { liveCards.dispose(); server.close(); };
  process.once('SIGINT', stop); process.once('SIGTERM', stop);
}
