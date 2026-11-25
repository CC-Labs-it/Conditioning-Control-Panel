// dev/depth-badge-drive.mjs - the Depth badge driver (brief handoff-1121/TC-ASCENSION-BRIEF.md, lane B).
// Boots ?fresh (saves nothing), checks the badge is hidden at Depth 0, seeds a Depth 4 prestige (stars, two ending
// stars, one star bought for next run), then hovers the badge, hovers an icon, presses Escape (bubble closes, the game
// still pauses), and on a 390x844 touch phone taps the badge and an icon. Muted Firefox, its own dev server.
// Env: FUZZ_URL, FIREFOX_EXE, PW_DIR (default the e2e kit in C:/wt-fe/e2e/), SHOTS=<dir> for screenshots.
import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const require = createRequire('C:/wt-fe/e2e/');
const { firefox } = require(process.env.PW_DIR || 'playwright');
const PORT = 8851;
let server = null;
if (!process.env.FUZZ_URL) {
  server = spawn(process.execPath, [fileURLToPath(new URL('./serve.mjs', import.meta.url)), String(PORT)], { stdio: 'ignore' });
  await new Promise((r) => setTimeout(r, 700));
}
const url = (process.env.FUZZ_URL || `http://127.0.0.1:${PORT}/fuzz/index.html`) + '?fresh=1';
const shots = process.env.SHOTS || '';
const browser = await firefox.launch({
  headless: true,
  executablePath: process.env.FIREFOX_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe',
  firefoxUserPrefs: { 'media.volume_scale': '0.0' },
});
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
let failed = 0;
const check = (name, ok, info = '') => { if (!ok) failed += 1; console.log(`${ok ? 'ok  ' : 'FAIL'} ${name}${info ? ' - ' + info : ''}`); };

const SEED = {
  depth: 4, dust: 1400,
  own: { pay_1: 1, pay_2: 1, pay_3: 1, head_1: 1, head_2: 1, idle_1: 1, active_1: 1, active_2: 1, trig_1: 1, luck_1: 1, ghost_1: 1, fine_1: 1 },
  lit: { drop: 1, bi_end: 1 },
  later: { own: { pay_4: 1 }, core: false },
};
const seed = (page) => page.evaluate((p) => {
  window.fuzz.engine.life.prestige = p;
  document.dispatchEvent(new Event('tc:prestige-changed'));
}, SEED);
const bubbleOpen = (page) => page.evaluate(() => { const b = document.getElementById('depth-bubble'); return !!b && !b.hidden && !b.classList.contains('db-out'); });
const inView = (page) => page.evaluate(() => {
  const r = document.getElementById('depth-bubble').getBoundingClientRect();
  return r.left >= 0 && r.top >= 0 && r.right <= innerWidth && r.bottom <= innerHeight;
});

try {
  for (const [name, vp] of [['desk', { width: 1366, height: 800 }], ['phone', { width: 390, height: 844 }]]) {
    console.log(`-- ${name} ${vp.width}x${vp.height}`);
    const touch = name === 'phone';
    const ctx = await browser.newContext({ viewport: vp, deviceScaleFactor: touch ? 2 : 1, hasTouch: touch, isMobile: false, reducedMotion: touch ? 'reduce' : 'no-preference' });
    const page = await ctx.newPage();
    const errs = [];
    page.on('pageerror', (e) => errs.push(e.message));
    const shot = async (n) => { if (shots) await page.screenshot({ path: `${shots}/badge-${name}-${n}.png` }); };
    await page.goto(url);
    await page.waitForFunction(() => window.fuzz?.engine && document.getElementById('depth-badge'), null, { timeout: 30000 });
    await wait(1500);
    check('hidden at Depth 0 with nothing owned', await page.evaluate(() => document.getElementById('depth-badge').hidden));
    await seed(page);
    await wait(200);
    check('shown after tc:prestige-changed', await page.evaluate(() => !document.getElementById('depth-badge').hidden));
    check('numeral reads 4', (await page.textContent('#depth-badge .db-icon b')) === '4');
    check('in #game-controls before the audio controls', await page.evaluate(() => {
      const w = document.getElementById('depth-badge'); return w.parentNode.id === 'game-controls' && w.nextElementSibling?.id === 'audio-controls';
    }));
    await shot('1-badge');

    if (!touch) {
      await page.hover('#depth-badge .db-icon');
      await wait(300);
      check('hover opens the bubble', await bubbleOpen(page));
      const cells = await page.$$eval('#depth-bubble .db-cell', (c) => c.map((x) => ({ id: x.dataset.id, next: x.classList.contains('db-next') })));
      check('every bonus and the next one as icons', cells.length === 15 && cells.filter((c) => c.next).map((c) => c.id).join() === 'pay_4', JSON.stringify(cells.map((c) => c.id)));
      check('bubble inside the screen', await inView(page));
      await shot('2-bubble');
      await page.hover('#depth-bubble .db-cell[data-id="pay_4"]');
      await wait(200);
      const tip = await page.evaluate(() => { const t = document.getElementById('depth-tip'); return t.hidden ? '' : t.innerText; });
      check('icon tooltip with the next-run line', /From next run/.test(tip), JSON.stringify(tip));
      await shot('3-tooltip');
      await page.hover('#depth-bubble .db-cell[data-id="drop"]');
      await wait(150);
      await shot('3b-tooltip-ending');
      /* leave: closes after a short delay, not at once */
      await page.mouse.move(600, 500);
      await wait(80);
      check('still open just after leaving', await bubbleOpen(page));
      await wait(600);
      check('closed after the delay', !(await bubbleOpen(page)));
      /* keyboard: focus opens, Escape closes and the game still pauses */
      await page.focus('#depth-badge .db-icon');
      await wait(200);
      check('focus opens', await bubbleOpen(page));
      await page.keyboard.press('Tab');
      await wait(100);
      check('Tab reaches an icon with its tooltip', await page.evaluate(() => document.activeElement?.classList.contains('db-cell') && !document.getElementById('depth-tip').hidden));
      await page.keyboard.press('Escape');
      await wait(300);
      check('Escape closes the bubble', !(await bubbleOpen(page)));
      check('Escape still pauses the game', await page.evaluate(() => !document.getElementById('pause').hidden));
    } else {
      await page.tap('#depth-badge .db-icon');
      await wait(300);
      check('tap opens the bubble', await bubbleOpen(page));
      check('bubble inside the phone screen', await inView(page));
      await shot('2-bubble');
      await page.tap('#depth-bubble .db-cell[data-id="pay_4"]');
      await wait(200);
      const tip = await page.evaluate(() => { const t = document.getElementById('depth-tip'); return t.hidden ? '' : t.innerText; });
      check('tap on an icon shows its tooltip', /From next run/.test(tip), JSON.stringify(tip));
      check('still open after the icon tap', await bubbleOpen(page));
      await shot('3-tooltip');
      await page.tap('body', { position: { x: 200, y: 600 } });
      await wait(300);
      check('tap outside closes', !(await bubbleOpen(page)));
      /* a full sky: the grid scrolls inside a bubble that stays on screen */
      await page.evaluate(async () => {
        const { LANES, ENDING_STARS } = await import('./data/prestige.js');
        const p = { depth: 10, dust: 98765, own: {}, lit: {}, core: false, later: { own: {}, core: true } };
        for (const l of LANES) for (const s of l.stars) p.own[s.id] = 1;
        for (const e of ENDING_STARS) p.lit[e.id] = 1;
        window.fuzz.engine.life.prestige = p;
        document.dispatchEvent(new Event('tc:prestige-changed'));
      });
      await page.tap('#depth-badge .db-icon');
      await wait(300);
      check('numeral reads 10', (await page.textContent('#depth-badge .db-icon b')) === '10');
      check('full sky bubble inside the phone screen', await inView(page));
      check('the grid scrolls', await page.evaluate(() => { const s = document.querySelector('#depth-bubble .db-scroll'); s.scrollTop = 99999; return s.scrollHeight > s.clientHeight + 20 && s.scrollTop > 0; }));
      await shot('4-full-sky-scrolled');
      await page.tap('body', { position: { x: 200, y: 800 } });
      await wait(300);
    }
    /* back to Depth 0: hidden again */
    await page.evaluate(() => { window.fuzz.engine.life.prestige = null; document.dispatchEvent(new Event('tc:prestige-changed')); });
    await wait(100);
    check('hidden again when the record empties', await page.evaluate(() => document.getElementById('depth-badge').hidden));
    check('no page errors', errs.length === 0, errs.join(' | '));
    await ctx.close();
  }
} finally {
  await browser.close();
  if (server) server.kill();
}
console.log(failed ? `${failed} FAILED` : 'all ok');
process.exit(failed ? 1 : 0);
