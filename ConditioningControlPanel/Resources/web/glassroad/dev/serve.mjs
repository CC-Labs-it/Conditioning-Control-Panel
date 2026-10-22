// dev/serve.mjs - zero-dependency static server for the glassroad tree, no caching.
// Run: node dev/serve.mjs [port]   (default 8790, or PORT env). Serves the folder above dev/.
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, normalize, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(fileURLToPath(new URL('..', import.meta.url)));
// the app serves Resources/web whole, so glassroad's ../../arcademy imports (the Loom) resolve to /arcademy/
// here; only that tree is mounted beside this one
const ARCADEMY = resolve(ROOT, '..', 'arcademy');
const PORT = Number(process.argv[2] || process.env.PORT || 8790);
const MIME = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.mjs': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8', '.css': 'text/css; charset=utf-8', '.wasm': 'application/wasm', '.glb': 'model/gltf-binary', '.ktx2': 'image/ktx2',
  '.png': 'image/png', '.jpg': 'image/jpeg', '.svg': 'image/svg+xml', '.webp': 'image/webp', '.gif': 'image/gif',
  '.ogg': 'audio/ogg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.md': 'text/plain; charset=utf-8', '.txt': 'text/plain; charset=utf-8',
};

const server = createServer(async (req, res) => {
  const head = { 'Cache-Control': 'no-store', 'Cross-Origin-Opener-Policy': 'same-origin' };
  if (req.method === 'POST' && req.url === '/__diag') {   // the page's dev beacon, printed for the session log
    let body = ''; req.on('data', (c) => { if (body.length < 8000) body += c; });
    req.on('end', () => { console.log(new Date().toISOString().slice(11, 19), 'diag', body); res.writeHead(204, head); res.end(); });
    return;
  }
  try {
    let path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
    if (path.endsWith('/')) path += 'index.html';
    const base = path.startsWith('/arcademy/') ? ARCADEMY : ROOT;
    const file = normalize(join(base, base === ARCADEMY ? path.slice('/arcademy'.length) : path));
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
