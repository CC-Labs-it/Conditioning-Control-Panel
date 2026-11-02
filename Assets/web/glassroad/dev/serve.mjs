// dev/serve.mjs - zero-dependency static server for the glassroad tree, no caching.
// Run: node dev/serve.mjs [port]   (default 8790, or PORT env). Serves the folder above dev/.
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, normalize, resolve, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { MAINFRAME_CHART, MAINFRAME_AUDIO, MAINFRAME_SHA256, verseChart, mixEnvelope } from './verseChart.mjs';
import { KEEP_CHART, lockedChunks, lockedChart } from './lockedChart.mjs';
import { GOON_CHART, GOON_AUDIO, GOON_SHA256, goonChart } from './goonChart.mjs';
import { BOUDOIR_CHART, BOUDOIR_AUDIO, BOUDOIR_SHA256, boudoirChart, remixEvents } from './boudoirChart.mjs';
import { KEEP_REMIX_CHART, KEEP_REMIX_AUDIO, KEEP_REMIX_SHA256, keepRemixChart } from './keepRemixChart.mjs';
import { verifiedRecording, sendRecording } from './privateRecording.mjs';
import { withPrivateCues } from './cueFiles.mjs';

const ROOT = resolve(fileURLToPath(new URL('..', import.meta.url)));
// the app serves Resources/web whole, so glassroad's ../../arcademy imports (the Loom) resolve to /arcademy/
// here; only that tree is mounted beside this one
const ARCADEMY = resolve(ROOT, '..', 'arcademy');
// DEV ONLY: the owner's private song folder (charts + mp3s, handoff-1022/MUSIC-ROAD-BRIEF.md) at /private-audio/.
// It is read in place and never copied into this tree; GLASSROAD_AUDIO_DIR points it elsewhere.
const PRIVATE = resolve(process.env.GLASSROAD_AUDIO_DIR || 'C:/Projects/glassroad-audio');
const MOUNTS = [['/arcademy/', ARCADEMY], ['/private-audio/', PRIVATE]];
const PORT = Number(process.argv[2] || process.env.PORT || 8790);
let mainframeChart, mainframeRecording;
let keepChart;
let keepRemixRecording, keepRemixChartBody;
async function readKeepRemixRecording() {
  if (!keepRemixRecording) keepRemixRecording = verifiedRecording([
    process.env.GLASSROAD_KEEP_RECORDING || 'C:/Users/Admin/Downloads/Locked - remix v5.mp3',
    join(PRIVATE, 'tracks-2026-10-24/keep/song/remix/out/locked_remix_v5.mp3'),
  ], KEEP_REMIX_SHA256).catch(e => { keepRemixRecording = null; throw e; });
  return keepRemixRecording;
}
async function readKeepRemixChart() {
  if (!keepRemixChartBody) keepRemixChartBody = (async () => {
    const recording = await readKeepRemixRecording(), dir = join(PRIVATE, 'tracks-2026-10-24/keep/song/remix');
    const [beat, words, arrangement, events] = await Promise.all([
      readFile(join(dir, 'work/beat.json'), 'utf8'), readFile(join(dir, 'work/words.json'), 'utf8'),
      readFile(join(dir, 'work/remix.py'), 'utf8'), readFile(join(dir, 'out/locked_remix_v5_events.csv'), 'utf8'),
    ]);
    const { stdout } = await promisify(execFile)('ffmpeg', ['-v', 'error', '-threads', '2',
      '-i', recording.file, '-vn', '-ac', '1', '-ar', '8000', '-threads', '2', '-f', 'f32le', 'pipe:1'],
    { encoding: 'buffer', maxBuffer: 16 * 1024 * 1024, windowsHide: true });
    const pcm = new Float32Array(stdout.buffer.slice(stdout.byteOffset, stdout.byteOffset + stdout.byteLength));
    return JSON.stringify(keepRemixChart(JSON.parse(beat), JSON.parse(words), arrangement,
      remixEvents(events), pcm.length / 8000, mixEnvelope(pcm)));
  })().catch(e => { keepRemixChartBody = null; throw e; });
  return keepRemixChartBody;
}
let boudoirRecording, boudoirChartBody;
async function readBoudoirRecording() {
  if (!boudoirRecording) boudoirRecording = verifiedRecording([
    process.env.GLASSROAD_BOUDOIR_RECORDING || 'C:/Users/Admin/Downloads/Think Pink - remix v5.mp3',
    join(PRIVATE, 'tracks-2026-10-24/sissy/remix/out/think-pink_remix_v5.mp3'),
  ], BOUDOIR_SHA256).catch(e => { boudoirRecording = null; throw e; });
  return boudoirRecording;
}
async function readBoudoirChart() {
  if (!boudoirChartBody) boudoirChartBody = (async () => {
    const recording = await readBoudoirRecording(), dir = join(PRIVATE, 'tracks-2026-10-24/sissy/remix');
    const [beat, words, arrangement, events] = await Promise.all([
      readFile(join(dir, 'work/beat.json'), 'utf8'), readFile(join(dir, 'work/words.json'), 'utf8'),
      readFile(join(dir, 'work/remix.py'), 'utf8'), readFile(join(dir, 'out/think-pink_remix_v5_events.csv'), 'utf8'),
    ]);
    const { stdout } = await promisify(execFile)('ffmpeg', ['-v', 'error', '-threads', '2',
      '-i', recording.file, '-vn', '-ac', '1', '-ar', '8000', '-threads', '2', '-f', 'f32le', 'pipe:1'],
    { encoding: 'buffer', maxBuffer: 16 * 1024 * 1024, windowsHide: true });
    const pcm = new Float32Array(stdout.buffer.slice(stdout.byteOffset, stdout.byteOffset + stdout.byteLength));
    return JSON.stringify(boudoirChart(JSON.parse(beat), JSON.parse(words), arrangement,
      remixEvents(events), pcm.length / 8000, mixEnvelope(pcm)));
  })().catch(e => { boudoirChartBody = null; throw e; });
  return boudoirChartBody;
}
let goonRecording, goonChartBody;
async function readGoonRecording() {
  if (!goonRecording) goonRecording = verifiedRecording([
    process.env.GLASSROAD_GOON_RECORDING || 'C:/Users/Admin/Downloads/Keep Watching - remix v4.mp3',
    join(PRIVATE, 'tracks-2026-10-24/goon/remix/out/keep-watching_remix_v4.mp3'),
  ], GOON_SHA256).catch(e => { goonRecording = null; throw e; });
  return goonRecording;
}
async function readGoonChart() {
  if (!goonChartBody) goonChartBody = (async () => {
    const recording = await readGoonRecording(), dir = join(PRIVATE, 'tracks-2026-10-24/goon/remix/work');
    const [beat, words, arrangement, events] = await Promise.all([
      readFile(join(dir, 'beat.json'), 'utf8'), readFile(join(dir, 'words.json'), 'utf8'), readFile(join(dir, 'remix.py'), 'utf8'),
      readFile(join(dir, '../out/keep-watching_remix_v4_events.csv'), 'utf8'),
    ]);
    const { stdout } = await promisify(execFile)('ffmpeg', ['-v', 'error', '-threads', '2',
      '-i', recording.file, '-vn', '-ac', '1', '-ar', '8000', '-threads', '2', '-f', 'f32le', 'pipe:1'],
    { encoding: 'buffer', maxBuffer: 16 * 1024 * 1024, windowsHide: true });
    const pcm = new Float32Array(stdout.buffer.slice(stdout.byteOffset, stdout.byteOffset + stdout.byteLength));
    return JSON.stringify(goonChart(JSON.parse(beat), JSON.parse(words), arrangement, pcm.length / 8000, mixEnvelope(pcm), remixEvents(events)));
  })().catch(e => { goonChartBody = null; throw e; });
  return goonChartBody;
}
async function readKeepChart() {
  if (!keepChart) keepChart = (async () => {
    const dir = join(PRIVATE, 'locked-cock');
    const script = await readFile(join(dir, 'script.md'), 'utf8');
    const mixSource = await readFile(join(dir, 'mix_v3.py'), 'utf8');
    const block = mixSource.match(/MANTRAS\s*=\s*\{([\s\S]*?)\}/)?.[1] || '';
    const mantras = [...block.matchAll(/"([^"]+)"/g)].map(m => m[1]);
    if (!mantras.length) throw new Error('Missing recorded mantra selection');
    const notes = JSON.parse(await readFile(join(dir, 'out/v3/NOTES.json'), 'utf8'));
    const takes = [], samples = [];
    for (let i = 0; i < lockedChunks(script).length; i++) {
      const name = 's' + String(i).padStart(2, '0');
      takes.push(JSON.parse(await readFile(join(dir, 'work/voice', name + '.json'), 'utf8')));
      const { stdout } = await promisify(execFile)('ffmpeg', ['-v', 'error', '-threads', '2',
        '-i', join(dir, 'work/voice', name + '.mp3'), '-vn', '-ac', '1', '-ar', '44100',
        '-threads', '2', '-f', 's16le', 'pipe:1'], { encoding: 'buffer', maxBuffer: 32 * 1024 * 1024, windowsHide: true });
      samples.push(stdout.length / 2);
    }
    return JSON.stringify(lockedChart(script, takes, samples, notes, mantras));
  })().catch(e => { keepChart = null; console.warn('[keep chart]', e.message); throw e; });
  return keepChart;
}
async function readMainframeChart() {
  if (!mainframeChart) mainframeChart = (async () => {
    const dir = join(PRIVATE, 'tracks-2026-10-24/mainframe/out/v5');
    const recording = await readMainframeRecording();
    const raw = JSON.parse(await readFile(join(dir, 'chart.json'), 'utf8'));
    const { stdout } = await promisify(execFile)('ffmpeg', ['-v', 'error', '-threads', '2',
      '-i', recording.file, '-vn', '-ac', '1', '-ar', '8000',
      '-threads', '2', '-f', 'f32le', 'pipe:1'], { encoding: 'buffer', maxBuffer: 16 * 1024 * 1024, windowsHide: true });
    const pcm = new Float32Array(stdout.buffer.slice(stdout.byteOffset, stdout.byteOffset + stdout.byteLength));
    return JSON.stringify({ ...verseChart(raw, mixEnvelope(pcm)), audio: MAINFRAME_AUDIO });
  })().catch(e => { mainframeChart = null; throw e; });
  return mainframeChart;
}
async function readMainframeRecording() {
  if (!mainframeRecording) mainframeRecording = verifiedRecording([
    process.env.GLASSROAD_MAINFRAME_RECORDING || join(PRIVATE, 'tracks-2026-10-24/mainframe/out/v5/mainframe_phone.mp3'),
  ], MAINFRAME_SHA256).catch(e => { mainframeRecording = null; throw e; });
  return mainframeRecording;
}
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
  const pathname = new URL(req.url, 'http://local').pathname;
  if (pathname === KEEP_REMIX_CHART || pathname === KEEP_REMIX_AUDIO) {
    if (!['GET', 'HEAD'].includes(req.method)) { res.writeHead(405, head); res.end(); return; }
    try {
      if (pathname === KEEP_REMIX_AUDIO) sendRecording(req, res, (await readKeepRemixRecording()).bytes, head);
      else {
        const body = await readKeepRemixChart();
        res.writeHead(200, { ...head, 'Content-Type': 'application/json' });
        res.end(req.method === 'HEAD' ? undefined : body);
      }
    } catch { res.writeHead(503, head); res.end('Private Keep remix unavailable'); }
    return;
  }
  if (pathname === '/private-audio/pink-road/chart.json') {
    if (!['GET', 'HEAD'].includes(req.method)) { res.writeHead(405, head); res.end(); return; }
    try {
      const chart = JSON.parse(await readFile(join(PRIVATE, 'pink-road/chart.json'), 'utf8'));
      const body = JSON.stringify(await withPrivateCues(PRIVATE, 'threshold', chart));
      res.writeHead(200, { ...head, 'Content-Type': 'application/json' });
      res.end(req.method === 'HEAD' ? undefined : body);
    } catch { res.writeHead(503, head); res.end('Private recording unavailable'); }
    return;
  }
  if (pathname === BOUDOIR_CHART || pathname === BOUDOIR_AUDIO) {
    if (!['GET', 'HEAD'].includes(req.method)) { res.writeHead(405, head); res.end(); return; }
    try {
      if (pathname === BOUDOIR_AUDIO) sendRecording(req, res, (await readBoudoirRecording()).bytes, head);
      else {
        const body = JSON.stringify(await withPrivateCues(PRIVATE, 'boudoir', JSON.parse(await readBoudoirChart())));
        res.writeHead(200, { ...head, 'Content-Type': 'application/json' });
        res.end(req.method === 'HEAD' ? undefined : body);
      }
    } catch { res.writeHead(503, head); res.end('Private Boudoir recording unavailable'); }
    return;
  }
  if (pathname === GOON_CHART || pathname === GOON_AUDIO) {
    if (!['GET', 'HEAD'].includes(req.method)) { res.writeHead(405, head); res.end(); return; }
    try {
      if (pathname === GOON_AUDIO) sendRecording(req, res, (await readGoonRecording()).bytes, head);
      else {
        const body = JSON.stringify(await withPrivateCues(PRIVATE, 'goonworks', JSON.parse(await readGoonChart())));
        res.writeHead(200, { ...head, 'Content-Type': 'application/json' });
        res.end(req.method === 'HEAD' ? undefined : body);
      }
    } catch { res.writeHead(503, head); res.end('Private GOONWORKS recording unavailable'); }
    return;
  }
  if (new URL(req.url, 'http://local').pathname === KEEP_CHART) {
    if (!['GET', 'HEAD'].includes(req.method)) { res.writeHead(405, head); res.end(); return; }
    try { const body = await readKeepChart(); res.writeHead(200, { ...head, 'Content-Type': 'application/json' }); res.end(req.method === 'HEAD' ? undefined : body); }
    catch { res.writeHead(503, head); res.end('Private Keep recording unavailable'); }
    return;
  }
  if (pathname === MAINFRAME_CHART || pathname === MAINFRAME_AUDIO) {
    if (!['GET', 'HEAD'].includes(req.method)) { res.writeHead(405, head); res.end(); return; }
    try {
      if (pathname === MAINFRAME_AUDIO) { sendRecording(req, res, (await readMainframeRecording()).bytes, head); return; }
      const body = JSON.stringify(await withPrivateCues(PRIVATE, 'mainframe', JSON.parse(await readMainframeChart())));
      res.writeHead(200, { ...head, 'Content-Type': 'application/json' });
      res.end(req.method === 'HEAD' ? undefined : body);
    } catch { res.writeHead(503, head); res.end('Private Mainframe recording unavailable'); }
    return;
  }
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
server.listen(PORT, '127.0.0.1', () => console.log('glassroad on http://127.0.0.1:' + server.address().port + '/'));
server.on('close', () => liveCards?.dispose());
if (liveCards) {
  const stop = () => { liveCards.dispose(); server.close(); };
  process.once('SIGINT', stop); process.once('SIGTERM', stop);
}
