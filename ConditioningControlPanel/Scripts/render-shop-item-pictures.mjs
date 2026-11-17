#!/usr/bin/env node
/* ============================================================================
 * render-shop-item-pictures.mjs - the 2D Prize Counter sheet's item pictures: one small still per item that only
 * existed as a 3D shelf model (karts, chess, desk toys, Prime and Back Room prizes), rendered from that approved model.
 *
 *   node ConditioningControlPanel/Scripts/render-shop-item-pictures.mjs [--web <Resources/web>] [--out <dir>]
 *        [--size 256] [--max-kb 40] [--only id,id]
 *
 * Loads prizecounter/assets/items/<id>.glb (LFS: run from a checkout with the real files, or point --web at one) in
 * headless Chrome with the vendored three.js (GLTFLoader + meshopt decoder, as the 3D counter does). The models
 * are never changed: each is shown as it is, from one three-quarter angle above and to the right, one soft key
 * light from the top left, then drawn as a cartoon sticker (a thick ink outline round its silhouette and a hard
 * offset shadow down-right, the sheet's lamp rule), cropped tight and written as
 * prizecounter/sheet/art/items/<id>.webp (long side --size, transparent, quality stepped down under --max-kb).
 * The list of ids below is the same list as sheet/pictures.js (a node test keeps them equal).
 * Offline only. Nothing leaves the machine: Resources/web is served on 127.0.0.1 and the only process this
 * stops is the Chrome it started, by its own handle. CHROME: CHROME_PATH, else the usual install.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { existsSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { PICTURED } from '../Resources/web/prizecounter/sheet/pictures.js';

const opt = (k, d) => { const i = process.argv.indexOf(k); return i > 0 ? process.argv[i + 1] : d; };
const SIZE = Number(opt('--size', 256)), MAX_KB = Number(opt('--max-kb', 40));
const WEB = resolve(opt('--web', resolve(fileURLToPath(import.meta.url), '../../Resources/web')));
const OUT = resolve(opt('--out', join(WEB, 'prizecounter/sheet/art/items')));
const ONLY = opt('--only', '') ? opt('--only', '').split(',') : null;
const IDS = PICTURED.filter((id) => !ONLY || ONLY.includes(id));
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('no chrome at ' + CHROME); process.exit(1); }
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/* Per family: how far round and how high the camera sits. Flat things (banners, frames) turn less so they stay
 * readable; karts and toys get the full three-quarter. */
const VIEW = { flat: [0.32, 0.18], low: [0.62, 0.62], round: [0.62, 0.4] };
/* Chess finishes (chess.finish.*) are not shelf models: they paint the game's own sculpts, so their picture is one
 * game piece in both team palettes side by side (piecebypiece/assets/pieces/<FINISH_PIECE>.glb + board/finishes.js). */
const FINISH_PIECE = opt('--finish-piece', 'knight');
const viewOf = (id) => (/banner|frame|insignia|podium/.test(id) ? VIEW.flat : /^kart\.body|umbrella|^desk\.hat/.test(id) ? VIEW.low : VIEW.round);

const PAGE = `<!doctype html><meta charset="utf-8"><body style="margin:0;background:transparent">
<script type="importmap">{"imports":{"three":"/vendor/three/three.module.min.js","three/addons/":"/vendor/three/addons/"}}</script>
<script type="module">
import * as T from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/addons/libs/meshopt_decoder.module.js';
const R = 640;                                            // the 3D pass, drawn down to the output size
const canvas = document.createElement('canvas'); canvas.width = canvas.height = R; document.body.append(canvas);
const renderer = new T.WebGLRenderer({ canvas, antialias: true, alpha: true, preserveDrawingBuffer: true });
renderer.setPixelRatio(1); renderer.setSize(R, R, false); renderer.setClearColor(0x000000, 0);
renderer.outputColorSpace = T.SRGBColorSpace; renderer.toneMapping = T.NeutralToneMapping; renderer.toneMappingExposure = 1.3;
const scene = new T.Scene();
const pm = new T.PMREMGenerator(renderer), studio = new T.Scene();   // a soft warm studio for the shiny bits
studio.background = new T.Color('#5a4466');
for (const p of [[-3, 4, 2], [-4, 1, 1], [4, 1, -1], [0, 1, 4]]) {
  const panel = new T.Mesh(new T.PlaneGeometry(3, 3), new T.MeshBasicMaterial({ color: new T.Color(2, 1.7, 1.8), side: T.DoubleSide }));
  panel.position.set(...p); panel.lookAt(0, 0, 0); studio.add(panel);
}
scene.environment = pm.fromScene(studio, 0.04).texture; scene.environmentIntensity = 0.55;
scene.add(new T.HemisphereLight(0xfff0f6, 0x4a3858, 1.6));
const key = new T.DirectionalLight(0xfff2e4, 2.6);        // one lamp, top left
scene.add(key, key.target);
const camera = new T.PerspectiveCamera(24, 1, 0.01, 50);
const loader = new GLTFLoader(); loader.setMeshoptDecoder(MeshoptDecoder);
const INK = '#1d0c26';

function sticker(src, size, line) {
  // tight crop of the 3D pass, then: hard shadow down-right, ink outline round the silhouette, the picture on top
  const a = src.getContext ? src : null;
  const w = src.width, h = src.height, c0 = document.createElement('canvas'); c0.width = w; c0.height = h;
  const x0 = c0.getContext('2d'); x0.drawImage(src, 0, 0);
  const d = x0.getImageData(0, 0, w, h).data; let l = w, t = h, r = -1, b = -1;
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) if (d[(y * w + x) * 4 + 3] > 8) { if (x < l) l = x; if (x > r) r = x; if (y < t) t = y; if (y > b) b = y; }
  if (r < 0) return null;
  const bw = r - l + 1, bh = b - t + 1, pad = line * 2 + 6;
  const k = (size - pad * 2) / Math.max(bw, bh);
  const W = Math.round(bw * k) + pad * 2, H = Math.round(bh * k) + pad * 2;
  const pic = document.createElement('canvas'); pic.width = W; pic.height = H;
  const xp = pic.getContext('2d'); xp.imageSmoothingQuality = 'high';
  xp.drawImage(c0, l, t, bw, bh, pad - line * 0.4, pad - line * 0.4, bw * k, bh * k);
  const sil = document.createElement('canvas'); sil.width = W; sil.height = H;
  const xs = sil.getContext('2d'); xs.drawImage(pic, 0, 0); xs.globalCompositeOperation = 'source-in'; xs.fillStyle = INK; xs.fillRect(0, 0, W, H);
  const out = document.createElement('canvas'); out.width = W; out.height = H;
  const xo = out.getContext('2d');
  xo.globalAlpha = 0.55; xo.drawImage(sil, line * 1.1, line * 1.4); xo.globalAlpha = 1;          // the hard shadow
  for (let i = 0; i < 24; i++) { const an = i / 24 * Math.PI * 2; xo.drawImage(sil, Math.cos(an) * line, Math.sin(an) * line); }
  for (let i = 0; i < 12; i++) { const an = i / 12 * Math.PI * 2; xo.drawImage(sil, Math.cos(an) * line * 0.5, Math.sin(an) * line * 0.5); }
  xo.drawImage(pic, 0, 0);
  return out;
}

window.render = async (id, size, maxKb, yaw, pitch) => {
  const gltf = await loader.loadAsync('/prizecounter/assets/items/' + id + '.glb');
  return shoot(gltf.scene, size, maxKb, yaw, pitch);
};

/* A chess finish: the same game sculpt twice, the white side's palette on the left and the black side's on the
 * right, painted by the game's own finishes.js exactly as the board paints it (pattern clock at 0). */
const TYPE = { pawn: 'p', knight: 'n', bishop: 'b', rook: 'r', queen: 'q', king: 'k' };
window.renderFinish = async (finish, name, size, maxKb, yaw, pitch) => {
  const { applyFinish } = await import('/piecebypiece/board/finishes.js');
  const gltf = await loader.loadAsync('/piecebypiece/assets/pieces/' + name + '.glb');
  const pair = new T.Group();
  const w = new T.Box3().setFromObject(gltf.scene).getSize(new T.Vector3()).x;
  for (const side of ['w', 'b']) {
    const piece = gltf.scene.clone(true);
    piece.userData.type = TYPE[name]; piece.userData.side = side;
    piece.traverse((o) => {
      if (!o.isMesh) return;
      if (o.geometry.attributes.color) {
        o.material = new T.MeshPhysicalMaterial({ color: 0xffffff, roughness: 0.35, clearcoat: 0.3, emissiveIntensity: 0 });
        o.material.userData.pbpPatch = 'skin';
      } else o.material = o.material.clone();
    });
    if (!applyFinish(piece, finish, side)) return { err: 'finish did not take: ' + finish };
    piece.position.x = (side === 'w' ? -0.56 : 0.56) * w;
    piece.rotation.y = side === 'w' ? 0.3 : -0.3;
    pair.add(piece);
  }
  return shoot(pair, size, maxKb, yaw, pitch);
};

function shoot(g, size, maxKb, yaw, pitch) {
  scene.add(g); g.updateMatrixWorld(true);
  const box = new T.Box3().setFromObject(g), c = box.getCenter(new T.Vector3()), s = box.getSize(new T.Vector3());
  const rad = s.length() * 0.5, dist = rad / Math.sin(T.MathUtils.degToRad(camera.fov / 2)) * 1.02;
  const dir = new T.Vector3(Math.sin(yaw), pitch, Math.cos(yaw)).normalize();
  camera.position.copy(c).addScaledVector(dir, dist); camera.near = dist / 20; camera.far = dist * 4;
  camera.lookAt(c); camera.updateProjectionMatrix();
  key.position.copy(c).add(new T.Vector3(-2.2, 3.2, 2.4).multiplyScalar(rad * 2)); key.target.position.copy(c);
  renderer.render(scene, camera);
  scene.remove(g);
  const out = sticker(canvas, size, Math.max(3, Math.round(size / 48)));
  if (!out) return { err: 'empty render' };
  let q = 0.9, url = out.toDataURL('image/webp', q);
  while (url.length * 0.75 > maxKb * 1024 && q > 0.3) { q -= 0.08; url = out.toDataURL('image/webp', q); }
  return { data: url.slice(url.indexOf(',') + 1), q: Math.round(q * 100), w: out.width, h: out.height };
}
window.ready = true;
</script>`;

const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.glb': 'model/gltf-binary', '.webp': 'image/webp', '.png': 'image/png', '.wasm': 'application/wasm' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path === '/__render.html') { res.writeHead(200, { 'content-type': 'text/html' }); return res.end(PAGE); }
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
const PORT = Number(process.env.RENDER_PORT || 8913), DEBUG_PORT = PORT + 500;
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));
const prof = mkdtempSync(join(tmpdir(), 'shop-pics-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`, '--no-first-run',
  '--no-default-browser-check', '--mute-audio', '--ignore-gpu-blocklist', '--enable-unsafe-swiftshader', 'about:blank'], { stdio: 'ignore' });
console.log('chrome pid ' + chrome.pid);
async function done(code) { try { chrome.kill(); } catch { /* our own child only */ } server.close(); await sleep(500); try { rmSync(prof, { recursive: true, force: true }); } catch { /* noop */ } process.exit(code); }

let target = null;
for (let i = 0; i < 60 && !target; i++) { await sleep(250); try { target = (await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/list`)).json()).find((t) => t.type === 'page'); } catch { /* not up */ } }
if (!target) { console.error('chrome never answered'); await done(1); }
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((r) => { ws.onopen = r; });
let msgId = 0;
const waits = new Map();
ws.onmessage = (e) => { const m = JSON.parse(e.data); if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Runtime.exceptionThrown') console.error('page: ' + (m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text)); };
const cdp = (method, params) => new Promise((r) => { const i = ++msgId; waits.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => { const r = (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result; if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text); return r.result.value; };
await cdp('Runtime.enable');
await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/__render.html` });
for (let i = 0; i < 100 && !(await ev('!!window.ready').catch(() => false)); i++) await sleep(100);

try {
  await mkdir(OUT, { recursive: true });
  let total = 0;
  for (const id of IDS) {
    const [yaw, pitch] = viewOf(id);
    const finish = id.startsWith('chess.finish.') ? id.slice('chess.finish.'.length) : null;
    const r = finish
      ? await ev(`window.renderFinish(${JSON.stringify(finish)}, ${JSON.stringify(FINISH_PIECE)}, ${SIZE}, ${MAX_KB}, 0, 0.22)`)
      : await ev(`window.render(${JSON.stringify(id)}, ${SIZE}, ${MAX_KB}, ${yaw}, ${pitch})`);
    if (!r || r.err) throw new Error(`${id}: ${r ? r.err : 'no answer'}`);
    const buf = Buffer.from(r.data, 'base64');
    if (buf.length > MAX_KB * 1024) throw new Error(`${id}.webp is ${buf.length} bytes, over ${MAX_KB} KB`);
    await writeFile(join(OUT, `${id}.webp`), buf);
    total += buf.length;
    console.log(`  ${id}.webp  ${r.w}x${r.h}  ${(buf.length / 1024).toFixed(1)} KB  q${r.q}`);
  }
  console.log(`${IDS.length} pictures, ${(total / 1024).toFixed(1)} KB`);
  await done(0);
} catch (e) { console.error('render failed: ' + e.message); await done(1); }
