// dev/measure.mjs - plays the REAL game headless on a stepped clock and reports how long a run really takes. Manual: too
// slow for CI, a 30 minute run takes about a minute of wall time. Needs dev/serve.mjs running.
//   node dev/serve.mjs 8811 &   node dev/measure.mjs --port 8811
// Flags: --routes "eyes,sweet,slides,sell,trance;ears,strict,..."  --profile person|lazy|both  --maxmin 70  --seed 7  --out file.json
// Nothing in the game changes for this: dev/measure-page.js replaces the page's clock from outside and plays by pressing
// what a person would press. Env: PW_DIR, FF_EXE, as dev/smoke.mjs.
import { createRequire } from 'node:module';
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const arg = (k, d) => { const i = process.argv.indexOf(`--${k}`); return i > 0 ? process.argv[i + 1] : d; };
const PORT = arg('port', '8811');
const MAX_MIN = Number(arg('maxmin', 70));
const SEED = Number(arg('seed', 7));
const OUT = arg('out', '');
const FRAME_MS = Number(arg('frame', 200));
const SPREAD = 'eyes,sweet,slides,sell,trance;ears,strict,scroll,premium,bimbo;eyes,strict,blind,premium,drone;ears,sweet,slides,premium,sissy;eyes,sweet,scroll,sell,goon;ears,strict,blind,sell,locked;eyes,strict,slides,sell,censored;ears,sweet,blind,premium,infection';
const routes = arg('routes', SPREAD).split(';').map((r) => r.trim()).filter(Boolean);
const PROFILES = { person: { taps: 4 }, lazy: { taps: 2 } };
const which = arg('profile', 'person');
const profiles = which === 'both' ? ['person', 'lazy'] : [which];

const require = createRequire('C:/wt-fe/e2e/');
const { firefox } = require(process.env.PW_DIR || 'playwright');
const FF = process.env.FF_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe';
const driver = readFileSync(fileURLToPath(new URL('./measure-page.js', import.meta.url)), 'utf8');
const url = `http://localhost:${PORT}/fuzz/index.html?fresh=1`;

const m = (s) => (s == null ? '  -  ' : (s / 60).toFixed(1).padStart(5));
const browser = await firefox.launch({ headless: true, executablePath: FF, firefoxUserPrefs: { 'media.volume_scale': '0.0' } });
const all = [];

for (const profile of profiles) {
  for (const route of routes) {
    const t0 = Date.now();
    const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
    await ctx.route(/\.(mp3|wav|ogg|m4a)(\?|$)/, (r) => r.abort());
    const page = await ctx.newPage();
    const errors = [];
    page.on('pageerror', (e) => errors.push(e.message));
    await page.addInitScript(driver);
    await page.addInitScript(`window.__seed(${SEED})`);
    await page.goto(url, { waitUntil: 'load' });
    // Painting is what costs here, and no money depends on it.
    await page.addStyleTag({ content: '*,*::before,*::after{animation:none!important;transition:none!important;filter:none!important;backdrop-filter:none!important;box-shadow:none!important;}' });
    for (let i = 0; i < 400; i++) {
      if (await page.evaluate(() => { window.__vt.advance(50); return !!window.fuzz; })) break;
      await page.waitForTimeout(25);
    }
    const lanes = route.split(',');
    await page.evaluate(([lanes, taps, frame]) => window.__play.start({ picks: { f1: lanes[0], f2: lanes[1], f3: lanes[2], f4: lanes[3], f5: lanes[4] }, taps, frame }), [lanes, PROFILES[profile].taps, FRAME_MS]);
    let res = null;
    while (true) {
      res = await page.evaluate((s) => window.__play.run(s), 60);
      if (res.done || res.t >= MAX_MIN * 60) break;
    }
    const report = await page.evaluate(() => window.__play.report());
    await ctx.close();
    const row = { route, profile, wallS: Math.round((Date.now() - t0) / 1000), errors: errors.slice(0, 5), ...report };
    all.push(row);
    const top = Object.entries(row.share).sort((a, b) => b[1] - a[1]).slice(0, 9).map(([k, v]) => `${k} ${v}`).join(', ');
    console.log(`\n${route} [${profile}] ended ${row.ended || 'NOT'} at ${m(row.t)} min, ${row.buys} buys, ${row.total.toExponential(2)} total, ${Math.round(row.fps)}/s fps x${row.fpsBoost} click ${Math.round(row.click)} x${row.clickBoost}, ${row.wallS} s wall`);
    console.log(`  forks  ${Object.entries(row.forks).map(([k, v]) => `${k} ${m(v)}`).join(' ')}  podcast ${m(row.pod)}`);
    console.log(`  gaps   longest ${row.longest.gap} s at ${m(row.longest.at)} min (for ${row.longest.next}), over 60 s: ${row.over60}, mean ${row.meanGap} s`);
    console.log(`  ends   ${Object.entries(row.endings).map(([k, v]) => `${k} ${m(v)}`).join(' ')}`);
    console.log(`  money  ${top}`);
    console.log(`  side   lumps ${row.lumpShare}%, mods ${row.modShare}%, pressed ${row.pressed}`);
    if (row.errors.length) console.log(`  ERRORS ${row.errors.join(' | ')}`);
    if (OUT) writeFileSync(OUT, JSON.stringify(all, null, 1));
  }
}
await browser.close();

console.log('\nroute                                     prof    drop  first-alt  gap  f1   f5   pod  lumps mods');
for (const r of all) {
  const alts = Object.entries(r.endings).filter(([k]) => k !== 'drop' && k !== 'onemore').map(([, v]) => v).filter((v) => v != null);
  console.log(`${r.route.padEnd(41)} ${r.profile.padEnd(6)} ${m(r.t)} ${m(alts.length ? Math.min(...alts) : null)}  ${String(r.longest.gap).padStart(4)} ${m(r.forks.f1)} ${m(r.forks.f5)} ${m(r.pod)} ${String(r.lumpShare).padStart(4)}% ${String(r.modShare).padStart(4)}%`);
}
