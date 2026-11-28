// dev/prestige-drive.mjs - the Renewal Sky regression driver (brief handoff-1109/TC-PRESTIGE-WIRE-BRIEF.md, wave 2).
// Ends a run, presses Renew, reloads in the middle of the Sky (the renewal must stay applied once and the Sky reopen),
// lights a star, checks the Looks shelf (locked before the core, a buy after it), signs a Next run with rules, a word and
// a keepsake, then reloads the renewed run and checks the run stamp, the pay, the pstore bags, the ghost recording and
// the bought look. Desktop 1366x800, then 390x844 touch with reduced motion. Muted Chrome, its own dev server on 8839
// (started here unless FUZZ_URL points elsewhere). Exits 1 on any failed check or page error.
// Env: FUZZ_URL, CHROME_EXE, PW_DIR (default the e2e kit in C:/wt-fe/e2e/), SHOTS=1 for screenshots in dev/shots/.
import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const require = createRequire('C:/wt-fe/e2e/');
const { chromium } = require(process.env.PW_DIR || 'playwright');
const PORT = 8839;
let server = null;
if (!process.env.FUZZ_URL) {
  server = spawn(process.execPath, [fileURLToPath(new URL('./serve.mjs', import.meta.url)), String(PORT)], { stdio: 'ignore' });
  await new Promise((r) => setTimeout(r, 700));
}
const base = process.env.FUZZ_URL || `http://127.0.0.1:${PORT}/fuzz/index.html`;
const url = `${base}?pace=50`;
const shots = fileURLToPath(new URL('./shots/', import.meta.url));
if (process.env.SHOTS) mkdirSync(shots, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  executablePath: process.env.CHROME_EXE || 'C:/Program Files/Google/Chrome/Application/chrome.exe',
  args: ['--mute-audio', '--autoplay-policy=no-user-gesture-required'],
});
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
let failed = 0;
const check = (name, ok, info = '') => {
  if (!ok) failed += 1;
  console.log(`${ok ? 'ok  ' : 'FAIL'} ${name}${info ? ' - ' + info : ''}`);
};

/** The saved game, as the page stores it. */
const saved = (page) => page.evaluate(() => {
  const k = Object.keys(localStorage).find((x) => /^fuzz/.test(x) && !/prefs|tips|sky|preview/.test(x));
  try { return JSON.parse(localStorage.getItem(k) || 'null'); } catch { return null; }
});
const booted = (page) => page.waitForFunction(() => window.fuzz?.engine, null, { timeout: 20000 });

try {
  for (const [name, vp] of [['desk', { width: 1366, height: 800 }], ['phone', { width: 390, height: 844 }]]) {
    console.log(`-- ${name} ${vp.width}x${vp.height}`);
    const ctx = await browser.newContext({ viewport: vp, deviceScaleFactor: 1, hasTouch: name === 'phone', reducedMotion: name === 'phone' ? 'reduce' : 'no-preference' });
    const page = await ctx.newPage();
    const errs = [];
    page.on('pageerror', (e) => errs.push(e.message));
    page.on('console', (m) => { if (m.type() === 'error' && !/Failed to load resource/.test(m.text())) console.log('   console error:', m.text()); });
    const shot = async (n) => { if (process.env.SHOTS) await page.screenshot({ path: `${shots}prestige-${name}-${n}.png` }); };

    /* 1. a finished run, then Renew */
    await page.goto(url);
    await booted(page);
    await wait(1200);
    await page.evaluate(() => { window.fuzz.add(5e7); window.fuzz.end('drop'); });
    await page.waitForSelector('.card.credits button:has-text("Renew")', { timeout: 20000 });
    await wait(600);
    await page.click('.card.credits button:has-text("Renew")');
    await page.waitForSelector('#ps-sky', { timeout: 10000 });
    await wait(900);
    const r1 = await page.evaluate(() => ({ depth: window.fuzz.life.prestige.depth, dust: window.fuzz.life.prestige.dust }));
    const s1 = await saved(page);
    check('renew applied', r1.depth === 1 && r1.dust > 0, JSON.stringify(r1));
    check('renew saved with no run', !s1?.run && s1?.life?.prestige?.depth === 1 && s1?.extras?.sky, `run=${!!s1?.run} sky=${JSON.stringify(s1?.extras?.sky)}`);
    await shot('1-sky');

    /* 2. reload in the middle of the Sky: Renew stays applied once, the Sky reopens */
    await page.reload();
    await booted(page);
    await page.waitForSelector('#ps-sky', { timeout: 10000 }).catch(() => {});
    await wait(1500);
    const r2 = await page.evaluate(() => ({ sky: !!document.getElementById('ps-sky'), depth: window.fuzz.life.prestige.depth, dust: window.fuzz.life.prestige.dust }));
    check('reload mid-Sky reopens the Sky', r2.sky);
    check('reload mid-Sky keeps the renewal once', r2.depth === r1.depth && r2.dust === r1.dust, JSON.stringify(r2));
    check('reload mid-Sky saves no run', !(await saved(page))?.run);

    /* 3. light a star (the Pay lane's first) */
    await page.click('.ps-arm[aria-label="Pay"]');
    await wait(1000);
    const buy = await page.$('.ps-card [data-act="buy"]:not([disabled])');
    if (buy) { await buy.click(); await wait(1500); }
    const own = await page.evaluate(() => window.fuzz.life.prestige.own);
    const s3 = await saved(page);
    check('star lit and saved', !!own.pay_1 && !!s3?.life?.prestige?.own?.pay_1, JSON.stringify(own));

    /* 4. Looks shelf: locked before the core, a buy after it */
    await page.click('#ps-sky [data-act="looks"]');
    await wait(500);
    const lockedState = await page.evaluate(() => {
      const bs = [...document.querySelectorAll('#ps-sky .ps-shelf [data-look]')];
      return { n: bs.length, allLocked: bs.every((b) => b.disabled && /Locked/.test(b.textContent)) };
    });
    check('looks locked before the core', lockedState.n > 0 && lockedState.allLocked, JSON.stringify(lockedState));
    await page.click('#ps-sky .ps-shelf .acts [data-act="back"]');
    await page.evaluate(() => { const p = window.fuzz.life.prestige; p.core = true; p.dust += 50000; });
    await page.click('#ps-sky [data-act="looks"]');
    await wait(400);
    await page.click('#ps-sky .ps-shelf [data-look="wave:harp"]');
    await wait(400);
    const looks = await page.evaluate(() => window.fuzz.life.prestige.store.looks);
    check('look bought after the core', looks?.own?.['wave:harp'] === 1, JSON.stringify(looks));
    check('look saved', (await saved(page))?.life?.prestige?.store?.looks?.own?.['wave:harp'] === 1);
    await shot('2-looks');
    await page.click('#ps-sky .ps-shelf .acts [data-act="back"]');
    await page.evaluate(() => { const p = window.fuzz.life.prestige; p.core = false; });   // keep the run timing plain

    /* 5. Next run with rules, a word and a keepsake */
    await page.evaluate(() => {
      const p = window.fuzz.life.prestige;
      Object.assign(p.own, { fine_1: 1, fine_2: 1, fine_4: 1, trig_1: 1, head_7: 1 });
      p.last = { ...(p.last || {}), toys: ['toy_box', 'wheel'] };
    });
    await page.click('#ps-sky [data-act="next"]');
    await wait(700);
    for (const id of ['silent', 'no_hands', 'sink', 'wheel']) await page.click(`.ps-chip[data-id="${id}"]`);
    await shot('3-next');
    await page.click('#ps-sky [data-act="sign"]');
    await page.waitForFunction(() => window.fuzz?.engine && !window.fuzz.state.ended && !document.getElementById('ps-sky'), null, { timeout: 20000 });
    await wait(1500);
    const run1 = await page.evaluate(() => {
      const s = window.fuzz.state, p = window.fuzz.life.prestige;
      p.store.trig.__drive = 7;   // a pstore bag must ride the life save
      return { stamp: s.prestige, pace: s.pace, harp: s.store.bubble?.waveStyles?.harp === true, wheel: !!s.owned.wheel, depth: p.depth,
        ghost: JSON.stringify(Object.entries(s.store).filter(([k]) => /ghost/.test(k))) };
    });
    check('stamp carries rules, word, keepsake', JSON.stringify(run1.stamp?.rules) === '["silent","no_hands"]' && JSON.stringify(run1.stamp?.words) === '["sink"]' && JSON.stringify(run1.stamp?.keep) === '["wheel"]', JSON.stringify(run1.stamp && { rules: run1.stamp.rules, words: run1.stamp.words, keep: run1.stamp.keep }));
    check('keepsake owned, look owned from the first second', run1.wheel && run1.harp);
    check('sky visit cleared', !(await saved(page))?.extras?.sky);
    await wait(5500);   // the ghost samples every 5 s of game time
    const bought = await page.evaluate(() => {   // a real buy: the ghost writes its recording on every buy
      const f = window.fuzz, it = f.engine.shop(8).goal;
      if (!it || it.kind === 'fork' || it.kind === 'end') return null;
      f.add(f.engine.price(it.id) + 10);
      return f.buy(it.id)?.ok ? it.id : null;
    });
    check('a buy in the renewed run', !!bought, String(bought));

    /* 6. reload the renewed run: everything stays */
    const before = await page.evaluate(() => ({ stamp: JSON.stringify(window.fuzz.state.prestige), pace: window.fuzz.state.pace,
      ghost: JSON.stringify(Object.entries(window.fuzz.state.store).filter(([k]) => /ghost/.test(k))) }));
    await page.reload();
    await booted(page);
    await wait(1500);
    const after = await page.evaluate(() => ({ stamp: JSON.stringify(window.fuzz.state.prestige), pace: window.fuzz.state.pace, depth: window.fuzz.life.prestige.depth,
      trig: window.fuzz.life.prestige.store.trig?.__drive, harp: window.fuzz.state.store.bubble?.waveStyles?.harp === true, sky: !!document.getElementById('ps-sky'),
      ghost: JSON.stringify(Object.entries(window.fuzz.state.store).filter(([k]) => /ghost/.test(k))) }));
    check('reload keeps the stamp', after.stamp === before.stamp);
    check('reload keeps the pay', Math.abs(after.pace - before.pace) < 1e-9, `${before.pace} -> ${after.pace}`);
    check('reload renews nothing', after.depth === run1.depth && !after.sky, `depth ${after.depth}`);
    check('pstore survives', after.trig === 7);
    check('ghost recording survives', after.ghost === before.ghost && after.ghost !== '[]', after.ghost.slice(0, 80));
    check('look still owned', after.harp);
    await shot('4-reloaded');
    check('no page errors', errs.length === 0, errs.join(' | '));
    await ctx.close();
  }
} finally {
  await browser.close();
  server?.kill();
}
console.log(failed ? `${failed} check(s) failed` : 'all checks passed');
process.exit(failed ? 1 : 0);
