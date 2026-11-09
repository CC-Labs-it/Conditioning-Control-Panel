// dev/emi-takeover-drive.mjs - drives the EMI Takeover run-up (v3 brief handoff-1109/TC-EMI-TAKEOVER-V3-BRIEF.md) in
// muted headless Chrome (audio is NOT heard here). The go_end take has its own drive: dev/emi-takeover-end-drive.mjs.
//   tx       Transmission: one EMI stays put, her screen alternates a picture and her face (2-3 s each)
//   will     FREE WILL: she walks in short steps, stops, hops; never follows the cursor
//   corp     Founded Startup: her budget fills her guest tile, she walks to it and buys it (Amplifier: bigger)
//   rep      Self Replication: the player's own clicks turn page objects into EMIs or split one off; cap holds
//   hive     HIVE MIND: forced by EMI 150 s after Self Replication (time handed over by the dev door), free for the
//            player; screens run in patterns
// Wake Up is hit-tested at every step. Shots land in $SHOTS (default dev/shots/).
// Env: FUZZ_BASE (default http://127.0.0.1:8861/fuzz/), CHROME_EXE, PW_DIR, W, H, TOUCH=1, SHOTS.
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const require = createRequire('C:/wt-fe/e2e/');
const { chromium } = require(process.env.PW_DIR || 'playwright');
const base = process.env.FUZZ_BASE || 'http://127.0.0.1:8861/fuzz/';
const W = Number(process.env.W || 1280), H = Number(process.env.H || 800);
const touch = !!process.env.TOUCH;
const tag = `${W}x${H}`;
const shots = (process.env.SHOTS || fileURLToPath(new URL('./shots/', import.meta.url))).replace(/\\/g, '/').replace(/\/?$/, '/');
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
const context = await browser.newContext({ viewport: { width: W, height: H }, hasTouch: touch, isMobile: touch });
const page = await context.newPage();
page.on('pageerror', (e) => errors.push(String(e)));
page.on('console', (m) => { if (m.type() === 'error' && !/Failed to load resource|ERR_|navigator\.vibrate/.test(m.text())) errors.push(m.text()); });
const shot = (name, clip) => page.screenshot({ path: `${shots}emi-${name}-${tag}.png`, ...(clip ? { clip } : {}) });
/** A close-up around the first EMI (and her bubble). */
const near = async (pad = 150) => {
  const m = (await state()).main;
  const x = Math.max(0, m.x - pad), y = Math.max(0, m.y - pad);
  return { x, y, width: Math.min(W - x, m.size + pad * 2), height: Math.min(H - y, m.size + pad * 2) };
};
const state = () => page.evaluate(() => window.__emiSwarm.state());
const wakeOk = () => page.evaluate(() => {
  const r = document.getElementById('wake')?.getBoundingClientRect();
  return !!r && r.width > 0 && document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)?.id === 'wake';
});
const mainScreen = () => page.evaluate(() => {
  const s = document.querySelector('#l-over .et-box .et-emi .et-s');
  return s?.dataset.pic ? 'pic' : 'face';
});
let wake = true;
const closeCards = () => page.evaluate(() => {
  for (let i = 0; i < 6; i++) {
    const b = [...document.querySelectorAll('#cards button')].filter((x) => x.offsetParent && /^(close|later|no|not yet|ok|okay|skip|got it)$/i.test(x.textContent.trim()));
    if (!b.length) break;
    b.at(-1).click();
  }
});

/* go_head brings three picture screens; a metronome and a toy give her more to take later */
await page.goto(`${base}index.html?buy=go_tx,a_tick,toy_box&pictures=online${touch ? '&lite=1' : ''}`, { waitUntil: 'load' });
await page.waitForFunction(() => window.__emiSwarm, null, { timeout: 20000 });
await page.evaluate(() => window.fuzz.engine.audio.arm?.());
await wait(1500);
await closeCards();

/* ---------- Transmission ---------- */
let s = await state();
check('tx: the Transmission stage, one EMI', s.phase === 'tx' && s.count === 1, JSON.stringify(s));
const start = { x: s.main.x, y: s.main.y };
const seenModes = new Set();
let pic = false, face = false;
for (let i = 0; i < 40 && !(pic && face); i++) {
  const m = await mainScreen();
  seenModes.add(m);
  if (m === 'pic' && !pic) { pic = true; await shot('tx-picture', await near()); }
  if (m === 'face' && !face && i > 3) { face = true; await shot('tx-face', await near()); }
  wake = wake && await wakeOk();
  await wait(300);
}
s = await state();
check('tx: her screen alternates a picture and her face', seenModes.has('pic') && seenModes.has('face'), [...seenModes].join(','));
check('tx: she stays put', Math.hypot(s.main.x - start.x, s.main.y - start.y) < 1, `${start.x.toFixed(0)},${start.y.toFixed(0)} -> ${s.main.x.toFixed(0)},${s.main.y.toFixed(0)}`);
const imgs = await page.evaluate(() => [...document.querySelectorAll('.et-p')].every((i) => i.referrerPolicy === 'strict-origin' && !i.closest('canvas')));
check('tx: pictures are DOM <img> with referrerPolicy strict-origin', imgs);
await shot('tx-page');

/* ---------- FREE WILL ---------- */
await page.evaluate(() => window.fuzz.grant('go_will'));
await closeCards();
let moved = 0, jumped = false, walkShot = false, last = (await state()).main, flips = new Set();
/* park the mouse on the page: she must not come to it */
await page.mouse.move(W * 0.5, H * 0.5);
for (let i = 0; i < 160 && !(jumped && walkShot && moved > 120); i++) {
  s = await state();
  moved += Math.hypot(s.main.x - last.x, s.main.y - last.y);
  flips.add(s.main.dir);
  last = s.main;
  if (s.main.state === 'walk' && !walkShot && moved > 20) { walkShot = true; await shot('will-walk', await near()); }
  if (s.main.jumping && !jumped) { jumped = true; await wait(130); await shot('will-jump', await near()); }
  wake = wake && await wakeOk();
  await wait(200);
}
check('will: she walks on her own', moved > 60, `${moved.toFixed(0)} px`);
check('will: she hops now and then', jumped);
const leaning = await page.evaluate(() => [...document.querySelectorAll('.et-emi')].map((e) => e.style.transform).filter((tr) => /scale(?!X\(-1\))|scaleY|rotate|skew|matrix/.test(tr)));
check('will: no stretch, squash or tilt on her, only a flip', leaning.length === 0, leaning.slice(0, 3).join(' | '));

/* ---------- Founded Startup ---------- */
await page.evaluate(() => window.fuzz.grant('go_corp'));
await closeCards();
const bankBefore = await page.evaluate(() => window.fuzz.state.spent);
await page.evaluate(() => window.__emiSwarm.dev.budget(31));
const size0 = (await state()).main.size;
let walkedToTile = false, bought = false;
for (let i = 0; i < 120 && !bought; i++) {
  s = await state();
  if (s.shopping && !walkedToTile) { walkedToTile = true; await wait(600); await shot('corp-buying'); }
  bought = s.bought.includes('h_amp');
  wake = wake && await wakeOk();
  await wait(150);
}
await wait(500);
await shot('corp-bought');
s = await state();
check('corp: she walked to her guest tile and bought Amplifier', walkedToTile && bought, JSON.stringify(s.bought));
check('corp: Amplifier made her bigger', s.main.size > size0, `${size0} -> ${s.main.size}`);
const spent = await page.evaluate(() => window.fuzz.state.spent);
check('corp: her buy took nothing from the bank', JSON.stringify(spent) === JSON.stringify(bankBefore), JSON.stringify(spent));
const guestText = await page.evaluate(() => document.querySelector('#rows .et-guest')?.textContent || '');
check('corp: her guest tile is in the shop', /Amplifier|Overclock/.test(guestText), guestText);

/* ---------- Self Replication ---------- */
await page.evaluate(() => window.__emiSwarm.dev.tiles(4));
await wait(400);
s = await state();
check('rep: Self Replication is hers', s.phase === 'rep', s.phase);
const btn = await page.evaluate(() => { const r = document.getElementById('btn').getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 }; });
const before = s.count;
let convertShot = false;
for (let i = 0; i < 26; i++) {
  if (touch) await page.touchscreen.tap(btn.x, btn.y); else await page.mouse.click(btn.x, btn.y);
  await wait(460);
  s = await state();
  if (s.gone >= 1 && !convertShot) { convertShot = true; await wait(200); await shot('rep-converted'); }
  if (i % 6 === 0) wake = wake && await wakeOk();
}
s = await state();
check('rep: the player\'s clicks made EMIs', s.count > before + 5, `${before} -> ${s.count}`);
check('rep: some page objects became EMIs', s.gone >= 1, `${s.gone} taken`);
await page.evaluate(() => window.fuzz.engine.tap({ auto: true }));
const autoCount = (await state()).count;
for (let i = 0; i < 5; i++) { await page.evaluate(() => window.fuzz.engine.tap({ auto: true })); await wait(450); }
check('rep: auto taps make nobody', (await state()).count === autoCount);
await shot('rep-crowd');

/* ---------- HIVE MIND, forced ---------- */
const fuzzBefore = await page.evaluate(() => window.fuzz.state.fuzz);
await page.evaluate(() => window.__emiSwarm.dev.sinceRep(149));
await page.waitForFunction(() => window.fuzz.state.owned.go_hive > 0, null, { timeout: 8000 }).catch(() => {});
s = await state();
const fuzzAfter = await page.evaluate(() => window.fuzz.state.fuzz);
check('hive: EMI bought HIVE MIND herself', s.phase === 'hive', s.phase);
check('hive: it was free for the player', fuzzAfter >= fuzzBefore - 1, `${Math.round(fuzzBefore)} -> ${Math.round(fuzzAfter)}`);
const toast = await page.evaluate(() => document.getElementById('l-toast')?.textContent || '');
check('hive: a toast says she did', /HIVE MIND/.test(toast), toast);
const patterns = new Set();
let litMax = 0;
for (let i = 0; i < 90 && patterns.size < 3; i++) {
  s = await state();
  if (s.pattern && !patterns.has(s.pattern)) { patterns.add(s.pattern); await wait(900); await shot(`hive-${s.pattern}`); }
  litMax = Math.max(litMax, s.pics);
  wake = wake && await wakeOk();
  await wait(300);
}
check('hive: patterns rotate', patterns.size >= 2, [...patterns].join(','));
check('hive: screens light together', litMax >= 3, `${litMax} lit at once`);

/* every screen change under 3 a second, measured on the page */
const gaps = await page.evaluate(async () => {
  const last = new WeakMap(), out = [];
  const mo = new MutationObserver((list) => {
    for (const m of list) {
      const el = m.target.closest?.('.et-s');
      if (!el || (m.attributeName === 'src' && !m.target.classList.contains('et-p'))) continue;
      const now = performance.now(), was = last.get(el);
      if (was && now - was > 5) out.push(now - was);
      last.set(el, now);
    }
  });
  mo.observe(document.getElementById('l-over'), { attributes: true, subtree: true, attributeFilter: ['data-pic', 'src'] });
  await new Promise((r) => setTimeout(r, 12000));
  mo.disconnect();
  return out;
});
const minGap = gaps.length ? Math.min(...gaps) : Infinity;
check('hive: no screen changed 3 times a second', minGap >= 334, `fastest ${Math.round(minGap)} ms over ${gaps.length} changes`);
check('Wake Up stayed on top', wake && await wakeOk());

/* switching Transmission off puts every taken object back */
await page.evaluate(() => window.fuzz.engine.switches.set('go_tx', false));
await wait(500);
const back = await page.evaluate(() => ({ hidden: document.querySelectorAll('[data-et-gone]').length, emis: document.querySelectorAll('.et-emi').length, swarm: !!window.__emiSwarm }));
check('switch off: every taken object is back, no EMI left', back.hidden === 0 && back.emis === 0 && !back.swarm, JSON.stringify(back));
await page.evaluate(() => window.fuzz.engine.switches.set('go_tx', true));
await wait(800);
s = await page.evaluate(() => window.__emiSwarm?.state());
check('switch on: the story resumes where it was', s?.phase === 'hive' && s.count > 1, JSON.stringify(s && { phase: s.phase, count: s.count }));

check('no page errors', errors.length === 0, errors.slice(0, 6).join(' | '));
await browser.close();
console.log(failed ? `${failed} FAILED` : 'all ok');
process.exit(failed ? 1 : 0);
