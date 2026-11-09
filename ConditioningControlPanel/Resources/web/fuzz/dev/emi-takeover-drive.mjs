// dev/emi-takeover-drive.mjs - drives EMI Takeover in muted headless Chrome (audio is NOT heard here).
//   run-up   index.html?buy=go_hive: real clicks on the button make copies, she walks to her tile and buys it
//   ending   dev/play-ending.html?end=go_end: buy the tile, press "Let her", click during the fill, then watch the
//            phases: full screen, flicker, DROP, the error card (big EMI in its first frame), the face, the ending card
//   panic    a panic suspend during the fill ends the show with no error card
//   reduced  the same ending under prefers-reduced-motion (fades only)
// Wake Up is hit-tested at every poll. Shots land in dev/shots/emi-takeover-*.png.
// Env: FUZZ_BASE (default http://127.0.0.1:8853/fuzz/), CHROME_EXE, PW_DIR, W, H, TOUCH=1, ONLY=runup|ending|panic|reduced.
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const require = createRequire('C:/wt-fe/e2e/');
const { chromium } = require(process.env.PW_DIR || 'playwright');
const base = process.env.FUZZ_BASE || 'http://127.0.0.1:8853/fuzz/';
const W = Number(process.env.W || 1280), H = Number(process.env.H || 800);
const touch = !!process.env.TOUCH;
const tag = `${W}x${H}`;
const only = process.env.ONLY || '';
const shots = fileURLToPath(new URL('./shots/', import.meta.url));
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
async function open(url, opts = {}) {
  const context = await browser.newContext({ viewport: { width: W, height: H }, hasTouch: touch, isMobile: touch, ...opts });
  const page = await context.newPage();
  page.on('pageerror', (e) => errors.push(String(e)));
  page.on('console', (m) => { if (m.type() === 'error' && !/Failed to load resource|ERR_|navigator\.vibrate/.test(m.text())) errors.push(m.text()); });
  await page.goto(url, { waitUntil: 'load' });
  return { context, page };
}
const shot = (page, name) => page.screenshot({ path: `${shots}emi-takeover-${name}-${tag}.png` });
const wakeOk = (frame) => frame.evaluate(() => {
  const r = document.getElementById('wake')?.getBoundingClientRect();
  return !!r && r.width > 0 && document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)?.id === 'wake';
});
const press = (page, x, y) => (touch ? page.touchscreen.tap(x, y) : page.mouse.click(x, y));

/* ---------- run-up ---------- */
async function runup() {
  const { context, page } = await open(`${base}index.html?buy=go_hive&pictures=online${touch ? '&lite=1' : ''}`);
  await page.waitForFunction(() => window.__emiSwarm, null, { timeout: 20000 });
  await wait(2500);
  let s = await page.evaluate(() => window.__emiSwarm.state());
  check('run-up: HIVE MIND stage, the first copy is there', s.stage === 'hive' && s.copies >= 1, JSON.stringify(s));
  const before = s.copies;
  const b = await page.evaluate(() => { const r = document.getElementById('btn').getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 }; });
  let wake = true;
  for (let i = 0; i < 44; i++) { await press(page, b.x, b.y); await wait(260); if (i % 8 === 0) wake = wake && await wakeOk(page); }
  s = await page.evaluate(() => window.__emiSwarm.state());
  check('run-up: the player\'s clicks made copies', s.copies > before, `${before} -> ${s.copies}`);
  await shot(page, 'copies');
  await page.evaluate(() => window.__emiSwarm.tip(500));
  let walked = false, bought = false, maxBub = 0;
  for (let i = 0; i < 260 && !bought; i++) {
    s = await page.evaluate(() => window.__emiSwarm.state());
    maxBub = Math.max(maxBub, s.bubbles);
    if (s.shopping && !walked) { walked = true; await wait(700); await shot(page, 'buying'); }
    bought = s.bought.includes('h_tx');
    wake = wake && await wakeOk(page);
    await wait(200);
  }
  check('run-up: she walked to her tile and bought Transmission', walked && bought, JSON.stringify(s));
  await wait(600);
  await shot(page, 'bought');
  const money = await page.evaluate(() => ({ spent: window.fuzz.state.spent, guest: !!document.querySelector('#rows .et-guest') }));
  check('run-up: her buy took nothing from the bank', !Object.keys(money.spent || {}).some((k) => /emi|tip|h_/.test(k)), JSON.stringify(money));
  await wait(9000);
  s = await page.evaluate(() => window.__emiSwarm.state());
  check('run-up: screens show pictures after Transmission', s.pics >= 1, `${s.pics} screens`);
  const imgs = await page.evaluate(() => [...document.querySelectorAll('.et-p')].every((i) => i.referrerPolicy === 'strict-origin' && !i.closest('canvas')));
  check('run-up: pictures are DOM <img> with referrerPolicy strict-origin', imgs);
  await shot(page, 'transmission');
  check('run-up: never more than 2 bubbles', maxBub <= 2, `max ${maxBub}`);
  check('run-up: Wake Up stayed on top', wake && await wakeOk(page));
  await context.close();
}

/* ---------- the ending ---------- */
async function ending({ reduced = false } = {}) {
  const name = reduced ? 'reduced' : 'ending';
  const { context, page } = await open(`${base}dev/play-ending.html?end=go_end&pictures=online${touch ? '&lite=1' : ''}`, reduced ? { reducedMotion: 'reduce' } : {});
  await page.waitForFunction(() => document.getElementById('game')?.contentWindow?.fuzz && /glowing/.test(document.getElementById('note')?.textContent || ''), null, { timeout: 40000 });
  const frame = page.frames().find((f) => /index\.html/.test(f.url()));
  await wait(1500);
  await page.evaluate(() => document.getElementById('note')?.remove());
  await frame.evaluate(() => window.fuzz.engine.audio.arm?.());
  /* the real path: the shop tile, then "Let her" */
  const yes = frame.locator('#cards button', { hasText: 'Let her' });
  for (let tries = 0; tries < 8 && !(await yes.count()); tries++) {
    /* the play door lands a crowded page: close whatever card is up first (a quiz, a remote, a pop-up) */
    for (let i = 0; i < 6; i++) {
      const closed = await frame.evaluate(() => {
        const b = [...document.querySelectorAll('#cards button')].filter((x) => x.offsetParent && /^(close|later|no|not yet|ok|okay|skip)$/i.test(x.textContent.trim()));
        if (b.length) b.at(-1).click();
        return b.length > 0;
      });
      if (!closed) break;
      await wait(350);
    }
    await frame.evaluate(() => document.querySelector('#shop [data-id="go_end"]')?.click());
    await wait(700);
  }
  await yes.waitFor({ timeout: 8000 });
  if (!reduced) await shot(page, 'let-her');
  await yes.click();
  await frame.waitForFunction(() => window.__emiTakeoverProbe, null, { timeout: 8000 });
  /* in the page: the error card's first frame (is the big EMI there, whole, loaded?) and how fast screens change */
  await frame.evaluate(() => {
    const out = (window.__etWatch = { first: null, gaps: [], cells: 0 });
    const root = document.getElementById('l-ending');
    new MutationObserver(() => {
      const card = root.querySelector('.et-err');
      if (card && !out.first) {
        const big = card.previousElementSibling, img = big?.querySelector('.et-b'), r = big?.getBoundingClientRect(), cs = big && getComputedStyle(big);
        out.first = { big: !!big?.classList.contains('et-emi'), loaded: !!img?.complete && img.naturalWidth > 0, opacity: cs?.opacity, anims: big?.getAnimations().length,
          w: r?.width, h: r?.height, ratio: r ? r.width / r.height : 0, logo: !!card.querySelector('.eh img'), text: card.textContent.slice(0, 40), face: big?.querySelector('.et-f')?.getAttribute('src')?.length > 100 };
      }
    }).observe(root, { childList: true, subtree: true });
    const last = new WeakMap();
    new MutationObserver((list) => {
      for (const m of list) {
        if (m.attributeName === 'src' && !m.target.classList.contains('et-p')) continue;   // a face change is not a picture change
        const el = m.target.closest?.('.et-s');
        if (!el) continue;
        const now = performance.now(), was = last.get(el);
        if (was && now - was > 5) out.gaps.push(now - was);
        if (!was) out.cells += 1;
        last.set(el, now);
      }
    }).observe(root, { attributes: true, subtree: true, attributeFilter: ['src', 'data-off'] });
  });
  const seen = {};
  const info = { wake: true, bubblesLate: 0, maxBub: 0, fillS: 0, cells: 0 };
  const t0 = Date.now();
  let p = null, clicksLeft = reduced ? 0 : 14, last = '';
  while (Date.now() - t0 < 120000) {
    p = await frame.evaluate(() => window.__emiTakeoverProbe?.() || null).catch(() => null);
    if (!p) break;
    info.wake = info.wake && await wakeOk(frame);
    info.maxBub = Math.max(info.maxBub, p.bubbles);
    info.cells = p.cells;
    if (['flicker', 'resolve', 'drop', 'error', 'face', 'close', 'gentle'].includes(p.phase)) info.bubblesLate += p.bubbles;
    if (p.phase !== last) { console.log(`   ${((Date.now() - t0) / 1000).toFixed(1)} s  ${p.phase}  filled ${p.filled}/${p.cells}`); last = p.phase; }
    const once = async (key, cond, delay = 0) => { if (!seen[key] && cond) { seen[key] = p; if (delay) await wait(delay); await shot(page, `${name}-${key}`); } };
    if (p.phase === 'fill' && clicksLeft > 0 && p.t > 3) {
      const x = W * (0.15 + Math.random() * 0.7), y = H * (0.3 + Math.random() * 0.6);
      await press(page, x, y); clicksLeft -= 1;
    }
    await once('fill', p.phase === 'fill' && p.filled > p.cells * 0.3);
    if (p.phase === 'settle' && !info.fillS) info.fillS = p.t;
    await once('full', p.phase === 'settle', 900);
    await once('flicker', p.phase === 'flicker', 1500);
    await once('gentle', p.phase === 'gentle', 1500);
    await once('drop', p.phase === 'drop', 1200);
    await once('error', p.phase === 'error');
    if (seen.error && !seen.error2 && p.phase === 'error' && p.t - seen.error.t > 3.2) { seen.error2 = p; await shot(page, `${name}-error-typed`); }
    await once('face', p.phase === 'face', 250);
    await wait(p.phase === 'drop' || p.phase === 'error' ? 40 : 120);
  }
  const watch = await frame.evaluate(() => window.__etWatch);
  const minGap = watch.gaps.length ? Math.min(...watch.gaps) : Infinity;
  check(`${name}: the screen filled`, !!seen.full, `${info.cells} EMIs in ${info.fillS.toFixed(1)} s, grid ${seen.full?.cols}x${seen.full?.rows}${seen.full?.stacked ? ' stacked' : ''}`);
  if (!reduced) check(`${name}: clicks made EMIs during the fill`, (seen.full?.clicks || 0) > 0, `${seen.full?.clicks} clicks counted`);
  check(`${name}: DROP formed: every letter screen lit with one picture, the rest dark`, !!seen.drop && seen.drop.lit === seen.drop.wantLit && seen.drop.dark === seen.drop.cells - seen.drop.wantLit && seen.drop.readable, JSON.stringify(seen.drop && { lit: seen.drop.lit, want: seen.drop.wantLit, dark: seen.drop.dark, cells: seen.drop.cells }));
  if (!reduced) check(`${name}: no screen changed 3 times a second`, minGap >= 334, `fastest change ${Math.round(minGap)} ms over ${watch.gaps.length} changes`);
  else check(`${name}: no flicker phase under reduced motion`, !seen.flicker && !!seen.gentle);
  const f = watch.first;
  check(`${name}: the error card's first frame has the big EMI, loaded, undistorted, and the logo`, !!f && f.big && f.loaded && f.logo && Math.abs(f.ratio - 859 / 869) < 0.01 && (reduced || (f.opacity === '1' && f.anims === 0)), JSON.stringify(f));
  check(`${name}: the uncanny face showed`, seen.face?.face === 'uncanny');
  check(`${name}: no bubble from the pictures on`, info.bubblesLate === 0, `max ${info.maxBub} during the fill`);
  check(`${name}: at most 3 bubbles at once`, info.maxBub <= 3);
  check(`${name}: Wake Up stayed on top the whole take`, info.wake);
  /* after the dark: the goon ending card, then the credits with the recap */
  let card = false, credits = false;
  for (let i = 0; i < 300; i++) {
    const st = await frame.evaluate(() => ({ ending: document.getElementById('l-ending')?.innerText || '', credits: !!document.querySelector('#l-credits *, .credits, [data-rr]'), title: /TAKEOVER/i.test(document.getElementById('l-ending')?.innerText || '') }));
    if (!card && st.ending.trim().length > 20 && !/ERROR SYS/.test(st.ending)) { card = true; check(`${name}: no takeover title screen`, !st.title); await wait(2600); await shot(page, `${name}-card`); await press(page, W / 2, H * 0.3); }
    if (card && st.credits) { credits = true; await wait(4500); await shot(page, `${name}-recap`); break; }
    await wait(200);
  }
  check(`${name}: the normal ending card came after the dark`, card);
  check(`${name}: then the credits and the run recap`, credits);
  check(`${name}: Wake Up still on top at the end`, await wakeOk(frame));
  await context.close();
}

/* ---------- panic ---------- */
async function panic() {
  const { context, page } = await open(`${base}index.html?buy=go_hive&pictures=online`);
  await page.waitForFunction(() => window.__emiSwarm, null, { timeout: 20000 });
  await wait(1500);
  await page.evaluate(() => { window.fuzz.end('go_end'); });
  await page.waitForFunction(() => window.__emiTakeoverProbe, null, { timeout: 8000 });
  await wait(3000);
  await page.evaluate(() => window.fuzz.engine.setSuspended(true, 'panic'));
  await wait(600);
  const gone = await page.evaluate(() => !window.__emiTakeoverProbe);
  await page.evaluate(() => window.fuzz.engine.setSuspended(false, 'panic'));
  await wait(5000);
  const after = await page.evaluate(() => ({ err: !!document.querySelector('.et-err'), text: document.getElementById('l-ending')?.innerText || '' }));
  check('panic: the take ended at once', gone);
  check('panic: no error card and no ending show', !after.err && !/ERROR SYS/.test(after.text), after.text.slice(0, 60));
  await context.close();
}

if (!only || only === 'runup') await runup();
if (!only || only === 'ending') await ending();
if (!only || only === 'panic') await panic();
if (!only || only === 'reduced') await ending({ reduced: true });
check('no page errors', errors.length === 0, errors.slice(0, 6).join(' | '));
await browser.close();
console.log(failed ? `${failed} FAILED` : 'all ok');
process.exit(failed ? 1 : 0);
