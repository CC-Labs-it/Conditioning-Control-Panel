// dev/spiral-check.mjs - looks at the spiral in a real browser: screenshots through a morph, every pattern, the palette
// reaction (ui/palette.js swapped for a stub the page can set), and what a paint and a frame cost at 1280 and 390.
//   node dev/serve.mjs 8802 &   FUZZ_URL=http://localhost:8802/fuzz/index.html node dev/spiral-check.mjs [out dir]
// Env: NOGL=1 runs with WebGL off (the 2D Loom path). Headless Firefox, muted.
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import http from 'node:http';

const here = fileURLToPath(new URL('.', import.meta.url));
const out = process.argv[2] || join(here, 'shots', 'spiral');
mkdirSync(out, { recursive: true });
const require = createRequire('C:/wt-fe/e2e/');
const { firefox } = require(process.env.PW_DIR || 'playwright');
const base = process.env.FUZZ_URL || 'http://localhost:8802/fuzz/index.html';
const FF = process.env.FF_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe';
const noGl = process.env.NOGL === '1';
const prefs = { 'media.volume_scale': '0.0' };
if (noGl) prefs['webgl.disabled'] = true;
const tag = noGl ? 'nogl' : 'gl';

const STUB = `const NONE = { a: '#8a867d', b: '#8a867d', soft: '#e4e1da', ink: '#000000', spiral: ['#ff4fa8', '#6a3cff'], themes: [] };
export function paletteOf() { return window.__pal || NONE; }`;

/* This Firefox build refuses page.route, so a small proxy in front of the dev server swaps the one module. */
const target = new URL(base);
const proxy = http.createServer((req, res) => {
  if (req.url.split('?')[0].endsWith('/ui/palette.js')) { res.writeHead(200, { 'content-type': 'text/javascript' }); res.end(STUB); return; }
  http.get({ host: target.hostname, port: target.port, path: req.url }, (up) => { res.writeHead(up.statusCode, up.headers); up.pipe(res); }).on('error', () => { res.writeHead(502); res.end(); });
});
await new Promise((r) => proxy.listen(0, r));
const proxied = `http://localhost:${proxy.address().port}${target.pathname}`;

const browser = await firefox.launch({ headless: true, executablePath: FF, firefoxUserPrefs: prefs });
const errors = [];
const report = [];
let fieldNote = null;

async function open(width, height, ids) {
  const ctx = await browser.newContext({ viewport: { width, height } });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => errors.push(`${width}: ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error' && !/data\/ads\.js/.test(m.text())) errors.push(`${width} console: ${m.text()}`); });
  await page.goto(`${proxied}?fresh=1&buy=${ids}`);
  await page.waitForFunction(() => window.fuzz?.engine);
  await page.waitForTimeout(500);
  return { ctx, page };
}

/** Frames and paints over a stretch of real time. */
async function measure(page, seconds) {
  return page.evaluate(async (seconds) => {
    const bus = (await import('/fuzz/fx/spiral-bus.js')).spiralBus;
    const before = bus.sites.map((s) => ({ p: s.stats.paints, ms: s.stats.ms }));
    const gaps = [];
    let last = performance.now();
    const t0 = last;
    await new Promise((res) => {
      const k = (now) => { gaps.push(now - last); last = now; if (now - t0 < seconds * 1000) requestAnimationFrame(k); else res(); };
      requestAnimationFrame(k);
    });
    gaps.sort((a, b) => a - b);
    return {
      fps: Math.round(gaps.length / seconds),
      p50: +gaps[Math.floor(gaps.length * 0.5)].toFixed(1),
      p95: +gaps[Math.floor(gaps.length * 0.95)].toFixed(1),
      sites: bus.sites.map((s, i) => {
        const paints = s.stats.paints - (before[i]?.p || 0), ms = s.stats.ms - (before[i]?.ms || 0);
        return { site: s.stats.label, gl: s.stats.gl, size: s.size, paintsPerS: +(paints / seconds).toFixed(1), msPerPaint: paints ? +(ms / paints).toFixed(2) : 0 };
      }),
    };
  }, seconds);
}

const busState = (page) => page.evaluate(async () => {
  const b = (await import('/fuzz/fx/spiral-bus.js')).spiralBus;
  return { from: b.clock.from, to: b.clock.to, t: +b.clock.t.toFixed(2), colours: b.colours(), fading: b.fading };
});

for (const [w, h] of [[1280, 800], [390, 800]]) {
  /* the page before any of this: the still path */
  let { ctx, page } = await open(w, h, 'e_spiral');
  report.push({ at: `${w} e_spiral only`, ...(await measure(page, 3)) });
  fieldNote = await page.evaluate(async () => (await import('/fuzz/fx/spiral-bus.js')).fieldInfo);
  await page.screenshot({ path: join(out, `${tag}-${w}-0-still.png`) });

  /* the palette event, with a stubbed return: must fade, not snap */
  await page.evaluate(() => { window.__pal = { a: '#3cc8ff', b: '#b79cff', soft: '#dff', ink: '#012', spiral: ['#3cc8ff', '#b79cff'], themes: ['trance'] }; window.fuzz.engine.emit('palette', { themes: ['trance'] }); });
  const f0 = await busState(page);
  await page.waitForTimeout(1500);
  const f1 = await busState(page);
  await page.screenshot({ path: join(out, `${tag}-${w}-1-fading.png`) });
  await page.waitForTimeout(2200);
  const f2 = await busState(page);
  await page.screenshot({ path: join(out, `${tag}-${w}-2-faded.png`) });
  report.push({ at: `${w} palette fade`, start: f0.colours, mid: f1.colours, end: f2.colours, fadingAtEnd: f2.fading });
  await ctx.close();

  /* Spiral Colours */
  ({ ctx, page } = await open(w, h, 'e_spiral,sp_colors'));
  report.push({ at: `${w} sp_colors`, ...(await measure(page, 4)) });
  await page.screenshot({ path: join(out, `${tag}-${w}-3-colours.png`) });
  await ctx.close();

  /* Spiral Shapes: frames through a morph */
  ({ ctx, page } = await open(w, h, 'e_spiral,sp_colors,sp_shapes'));
  let n = 0;
  const seen = new Set();
  for (let i = 0; i < 90 && n < 9; i++) {
    const s = await busState(page);
    seen.add(s.from);
    if (s.to !== null && Math.floor(s.t * 8) >= n % 8 && n < 8) {
      await page.screenshot({ path: join(out, `${tag}-${w}-4-morph-${n}.png`) });
      report.push({ at: `${w} morph frame ${n}`, ...s });
      n += 1;
    }
    await page.waitForTimeout(60);
    if (n >= 8) break;
  }
  report.push({ at: `${w} sp_shapes`, ...(await measure(page, 10)) });
  await ctx.close();

  /* what the pile costs with neither unlock: the number to beat */
  ({ ctx, page } = await open(w, h, 'e_spiral,overlay'));
  report.push({ at: `${w} overlay + button, no unlocks`, ...(await measure(page, 6)) });
  await page.screenshot({ path: join(out, `${tag}-${w}-5-pile-still.png`) });
  await ctx.close();

  /* the whole pile of spirals together: page, overlay, button */
  ({ ctx, page } = await open(w, h, 'e_spiral,sp_colors,sp_shapes,overlay'));
  report.push({ at: `${w} sp_shapes + overlay + button`, ...(await measure(page, 10)) });
  await page.screenshot({ path: join(out, `${tag}-${w}-5-pile.png`) });
  for (let i = 0; i < 6; i++) {
    const r = await page.evaluate(() => { const b = document.getElementById('btn').getBoundingClientRect(); return { x: b.left - 8, y: b.top - 8, width: b.width + 16, height: b.height + 16 }; });
    await page.screenshot({ path: join(out, `${tag}-${w}-6-btn-${i}.png`), clip: r });
    await page.waitForTimeout(1500);
  }
  await ctx.close();
}

/* every pattern, held still, in a grid: what each one looks like at rest */
{
  const { ctx, page } = await open(1280, 800, 'e_spiral');
  await page.evaluate(async () => {
    const { paintLoom } = await import('/fuzz/fx/spiral-bus.js');
    const { PATTERNS, morphFrame, groundOf } = await import('/fuzz/fx/spiral-patterns.js');
    const grid = document.createElement('div');
    grid.style.cssText = 'position:fixed;inset:0;z-index:99999;background:#000;display:grid;grid-template-columns:repeat(5,1fr);gap:4px;padding:4px;color:#fff;font:12px sans-serif;';
    const pair = ['#ff4fa8', '#6a3cff'];
    PATTERNS.forEach((p, i) => {
      const box = document.createElement('div');
      const c = document.createElement('canvas');
      c.width = c.height = 240; c.style.cssText = 'width:100%;display:block;';
      const g = c.getContext('2d', { alpha: false });
      paintLoom(g, morphFrame(i, null, 0, pair, groundOf(pair[0])), 0.13, 240, 240, { cover: false });
      box.append(c, `${i} ${p.id}`);
      grid.append(box);
    });
    document.body.append(grid);
  });
  await page.waitForTimeout(200);
  await page.screenshot({ path: join(out, `${tag}-patterns.png`) });
  await ctx.close();
}

await browser.close();
proxy.close();
console.log('field made', fieldNote);
console.log(JSON.stringify(report, null, 0).split('},{').join('},\n{'));
if (errors.length) { console.log('ERRORS', errors); process.exit(1); }
