// dev/serve.mjs - a static server rooted at Resources/web so ../arcademy (the
// Loom) resolves exactly as it does under https://ccp.game/. No dependencies.
//   node dev/serve.mjs            -> http://localhost:8787/fuzz/index.html
//   node dev/serve.mjs 9000
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(fileURLToPath(new URL('.', import.meta.url)), '..', '..');
const PORT = Number(process.argv[2]) || 8787;
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.mjs': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.json': 'application/json', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.svg': 'image/svg+xml', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.woff2': 'font/woff2', '.md': 'text/plain; charset=utf-8' };

createServer(async (req, res) => {
  const url = decodeURIComponent((req.url || '/').split('?')[0]);
  const path = normalize(join(ROOT, url));
  if (!path.startsWith(ROOT)) { res.writeHead(403); return res.end(); }
  try {
    const s = await stat(path);
    const file = s.isDirectory() ? join(path, 'index.html') : path;
    const body = await readFile(file);
    res.writeHead(200, { 'content-type': TYPES[extname(file)] || 'application/octet-stream', 'cache-control': 'max-age=0' });
    res.end(body);
  } catch { res.writeHead(404); res.end('not found'); }
}).listen(PORT, () => console.log(`fuzz: http://localhost:${PORT}/fuzz/index.html  (add ?pace=8 for a 6-minute run)`));
