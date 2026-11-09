// dev/emi-takeover-end-drive.mjs - drives the EMI Takeover ENDING (go_end take) in muted headless Chrome (audio is NOT
// heard here). Opens index.html?buy=go_hive (the swarm is up), ends the run on go_end and watches the beats:
//   fill (grid arriving, back pile), full, shut, DROP, error dialog, frozen, giant EMI (a blink), cheeky face, black,
//   then the credits with the run recap and its poster.
// Wake Up is hit-tested at every poll. Run your own `node dev/serve.mjs 8862` first.
// Env: FUZZ_BASE (default http://127.0.0.1:8862/fuzz/), CHROME_EXE, PW_DIR, W, H, TOUCH=1, REDUCED=1, SHOTS (dir).
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const require = createRequire('C:/wt-fe/e2e/');
const { chromium } = require(process.env.PW_DIR || 'playwright');
const base = process.env.FUZZ_BASE || 'http://127.0.0.1:8862/fuzz/';
const W = Number(process.env.W || 1440), H = Number(process.env.H || 900);
const touch = !!process.env.TOUCH, reduced = !!process.env.REDUCED;
const tag = `${W}x${H}${reduced ? '-reduced' : ''}`;
const shots = process.env.SHOTS || fileURLToPath(new URL('./shots/', import.meta.url));
mkdirSync(shots, { recursive: true });
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const browser = await chromium.launch({
  headless: true,
  executablePath: process.env.CHROME_EXE || 'C:/Program Files/Google/Chrome/Application/chrome.exe',
  args: ['--mute-audio', '--autoplay-policy=no-user-gesture-required'],
});
let failed = 0;
const errors = [];
const check = (name, ok, info = '') => { if (!ok) failed += 1; console.log(`${ok ? 'ok  ' : 'FAIL'} ${name}${info ? ' - ' + info : ''}`); };
const context = await browser.newContext({ viewport: { width: W, height: H }, hasTouch: touch, isMobile: touch, reducedMotion: reduced ? 'reduce' : 'no-preference' });
const page = await context.newPage();
page.on('pageerror', (e) => errors.push(String(e)));
page.on('console', (m) => { if (m.type() === 'error' && !/Failed to load resource|ERR_|navigator\.vibrate/.test(m.text())) errors.push(m.text()); });
await page.goto(`${base}index.html?buy=go_hive&pictures=online${touch ? '&lite=1' : ''}`, { waitUntil: 'load' });
await page.waitForFunction(() => window.fuzz?.engine, null, { timeout: 20000 });
await wait(4000);
const shot = (name) => page.screenshot({ path: `${shots}/end-${name}-${tag}.png` });
const wakeOk = () => page.evaluate(() => {
  const r = document.getElementById('wake')?.getBoundingClientRect();
  return !!r && r.width > 0 && document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)?.id === 'wake';
});
const swarm = await page.evaluate(() => document.querySelectorAll('.et-emi').length);
await page.evaluate(() => window.fuzz.engine.audio?.arm?.());
await page.evaluate(() => window.fuzz.end('go_end'));
await page.waitForFunction(() => window.__emiTakeoverProbe, null, { timeout: 8000 });

const seen = {};
let wake = true, p = null, last = '', frozenAnims = null, blinkShot = false, pageShows = null;
const t0 = Date.now();
while (Date.now() - t0 < 90000) {
  p = await page.evaluate(() => window.__emiTakeoverProbe?.() || null).catch(() => null);
  if (!p) break;
  wake = wake && await wakeOk();
  if (p.phase !== last) { console.log(`   ${p.t.toFixed(1)} s  ${p.phase}  front ${p.frontIn}/${p.front}  back ${p.backIn}/${p.back} (seeds ${p.seeds})`); last = p.phase; }
  const once = async (key, cond) => { if (!seen[key] && cond) { seen[key] = p; await shot(key); } };
  await once('arriving', p.phase === 'fill' && p.frontIn > p.front * 0.35);
  if (!seen.full && p.phase === 'fill' && p.t > 14.4) {
    /* is any of the page showing? sample points away from Wake Up's corner: each must hit an EMI or the take's own backing */
    pageShows = await page.evaluate(() => {
      const root = document.getElementById('l-ending'), c = document.getElementById('game-controls').getBoundingClientRect();
      /* EMI bodies (the solid part of each sprite box, sim BODY) from the DOM: sprites take no pointer, so no hit test */
      const bodies = [...root.querySelectorAll('.et-emi')].map((e) => e.getBoundingClientRect()).map((r) => ({ l: r.left + r.width * 0.17, r: r.left + r.width * 0.82, t: r.top + r.height * 0.22, b: r.top + r.height * 0.79 }));
      let bad = 0, n = 0, emi = 0;
      for (let x = 5; x < innerWidth; x += 23) for (let y = 5; y < innerHeight - 50; y += 23) {
        if (x > c.left - 250 && y < c.bottom + 250) continue;
        n += 1;
        const el = document.elementFromPoint(x, y);
        if (!el || !root.contains(el)) bad += 1;
        if (bodies.some((q) => x >= q.l && x <= q.r && y >= q.t && y <= q.b)) emi += 1;
      }
      return { bad, n, emi };
    });
    seen.full = p; await shot('full');
  }
  await once('shut', p.phase === 'shut');
  await once('drop', p.phase === 'drop' && p.lit > 0);
  await once('error', p.phase === 'error' || (p.phase === 'frozen' && !seen.error));
  if (p.phase === 'frozen' && !frozenAnims) {
    await wait(300);
    frozenAnims = await page.evaluate(() => {
      const root = document.getElementById('l-ending');
      const a = root.getAnimations({ subtree: true });
      return { running: a.filter((x) => x.playState === 'running').length, all: a.length };
    });
    await shot('frozen');
  }
  if (p.phase === 'giant' && !seen.giant) { seen.giant = p; await wait(350); await shot('giant'); }
  if (p.phase === 'giant' && p.face === '-_-' && !blinkShot) { blinkShot = true; seen.blink = p; await shot('blink'); }
  await once('cheeky', p.phase === 'cheeky');
  if (p.phase === 'close' && !seen.close) { seen.close = p; await wait(Math.round(reduced ? 900 : 450)); await shot('black'); }
  await wait(['giant', 'error', 'shut'].includes(p.phase) ? 15 : 90);
}
check('the swarm was up before the end', swarm > 0, `${swarm} EMIs on the page`);
check('the front grid arrived and the back pile filled', !!seen.full && seen.full.frontIn > seen.full.front * 0.9 && seen.full.backIn === seen.full.back, JSON.stringify(seen.full && { front: `${seen.full.frontIn}/${seen.full.front}`, back: `${seen.full.backIn}/${seen.full.back}`, seeds: seen.full.seeds }));
check('the swarm became the first back-layer EMIs', (seen.full?.seeds || 0) > 0, `${seen.full?.seeds} seeds`);
check('no page shows through at the full screen: every point hits the take and an EMI body', !!pageShows && pageShows.bad === 0 && pageShows.emi === pageShows.n, JSON.stringify(pageShows));
check('shut: every screen dark at once', !!seen.shut, JSON.stringify(seen.shut && { dark: seen.shut.dark, front: seen.shut.front, back: seen.shut.back }));
check('DROP: every lit pixel shows a picture', !!seen.drop && seen.drop.lit === seen.drop.wantLit && seen.drop.readable, JSON.stringify(seen.drop && { lit: seen.drop.lit, want: seen.drop.wantLit }));
check('the error dialog came with the logo', !!seen.error && seen.error.dialog && await page.evaluate(() => true));
check('frozen: no animation running', !!frozenAnims && frozenAnims.running === 0, JSON.stringify(frozenAnims));
check('giant EMI, idle face then a blink', !!seen.giant && seen.giant.big, `blink caught: ${blinkShot}`);
check('cheeky face on body-smug', seen.cheeky?.face === '^_~' && seen.cheeky?.pose === 'body-smug', JSON.stringify(seen.cheeky && { face: seen.cheeky.face, pose: seen.cheeky.pose }));
check('Wake Up stayed on top the whole take', wake);
/* after the dark: no ending card, the credits with the recap and the poster */
let recap = null, cardText = '';
for (let i = 0; i < 200 && !recap; i++) {
  const st = await page.evaluate(() => ({ ending: (document.getElementById('l-ending')?.innerText || '').trim(), rr: !!document.querySelector('[data-rr]'), poster: (document.querySelector('[data-rr="poster"] .rr-frame')?.children.length || 0) > 0, title: (document.querySelector('[data-rr]')?.closest('.rr, [class*="rr-"]')?.innerText || '').slice(0, 90) }));
  if (st.ending && !cardText) cardText = st.ending.slice(0, 80);
  if (st.rr) recap = st;
  await wait(150);
}
await wait(9000);
await shot('recap');
check('no "four hours later" card', !/hours later/i.test(cardText), cardText);
check('the run recap came right after', !!recap, JSON.stringify(recap));
check('the recap carries its poster column (hero tile; the Living Ad poster is Unit Complete only)', !!recap?.poster);
check('Wake Up still on top at the end', await wakeOk());
check('no page errors', errors.length === 0, errors.slice(0, 6).join(' | '));
await browser.close();
console.log(failed ? `${failed} FAILED` : 'all ok');
process.exit(failed ? 1 : 0);
