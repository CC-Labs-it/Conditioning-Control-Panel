// dev/stage-drive.mjs - the stage (ui/stage.js): buys six look items one at a time on a fresh run and screenshots each
// solo (the paper tag, the others eased down) and then the cast of three with the rest resting. Checks the strengths the
// page wrote, the solo pays (source `stage`) and the Control Panel's stage rows. Desktop 1366x800, then 390x844 touch
// (LITE: a cast of two). Muted Chrome, its own dev server on 8871 unless FUZZ_URL points elsewhere. Exits 1 on a failed
// check or a page error. Screens land in dev/shots/stage-*.png.
// Env: FUZZ_URL, CHROME_EXE, PW_DIR (default the e2e kit in C:/wt-fe/e2e/).
import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const require = createRequire('C:/wt-fe/e2e/');
const { chromium } = require(process.env.PW_DIR || 'playwright');
const PORT = 8871;
let server = null;
if (!process.env.FUZZ_URL) {
  server = spawn(process.execPath, [fileURLToPath(new URL('./serve.mjs', import.meta.url)), String(PORT)], { stdio: 'ignore' });
  await new Promise((r) => setTimeout(r, 700));
}
const base = process.env.FUZZ_URL || `http://127.0.0.1:${PORT}/fuzz/index.html`;
const shots = fileURLToPath(new URL('./shots/', import.meta.url));
mkdirSync(shots, { recursive: true });
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
const LOOKS = ['overlay', 'trail', 'score_heat', 'starburst', 'e_bounce', 'word_comet'];

/** What the stage wrote: { id: strength } per entry, the solo, the stage pays so far. */
const read = (page) => page.evaluate(() => {
  const st = window.fuzz.ui.stage;
  const out = {};
  for (const [id, e] of st.entries) out[id] = Math.round(e.cur * 100) / 100;
  return { s: out, solo: st.solo?.id || null, tag: document.querySelector('.stage-tag')?.textContent || null,
    paid: window.fuzz.state.sources?.stage || 0, all: st.all };
});

try {
  for (const [name, vp, lite] of [['desk', { width: 1366, height: 800 }, 0], ['phone', { width: 390, height: 844 }, 1]]) {
    console.log(`-- ${name} ${vp.width}x${vp.height}`);
    const ctx = await browser.newContext({ viewport: vp, deviceScaleFactor: 1, hasTouch: !!lite });
    const page = await ctx.newPage();
    const errs = [];
    page.on('pageerror', (e) => errs.push(e.message));
    await page.goto(`${base}?fresh=1&pace=20&tips=off&lite=${lite}`);
    await page.waitForFunction(() => window.fuzz?.engine && window.fuzz.ui.stage, null, { timeout: 20000 });
    await wait(1500);
    await page.evaluate(() => window.fuzz.add(1e6));
    for (const [i, id] of LOOKS.entries()) {
      await page.evaluate((x) => window.fuzz.grant(x), id);
      await wait(1700);
      const r = await read(page);
      if (i < 3 || id === 'e_bounce') await page.screenshot({ path: `${shots}stage-${name}-solo-${i + 1}-${id}.png` });
      check(`${id}: solo with a tag`, !!r.solo && /^New: /.test(r.tag || ''), `${r.solo} "${r.tag}"`);
      const others = Object.entries(r.s).filter(([k]) => k !== r.solo && k !== 'wash');
      check(`${id}: the others eased down`, others.every(([, v]) => v < 0.5), JSON.stringify(r.s));
      await wait(3600);
    }
    await wait(2500);
    const r = await read(page);
    const vals = Object.entries(r.s).filter(([k]) => k !== 'wash').map(([, v]) => v);
    const fullN = vals.filter((v) => v > 0.95).length;
    const cap = lite ? 2 : 3;
    check(`cast: ${cap} at full strength`, fullN === cap, JSON.stringify(r.s));
    check('cast: the rest rest at about a quarter', vals.filter((v) => v <= 0.95).every((v) => Math.abs(v - 0.25) < 0.05), JSON.stringify(r.s));
    check('solo pays went through the normal path', r.paid > 0, String(r.paid));
    check('new run: everything at once starts off', r.all === false);
    if (process.env.DEBUG) console.log(JSON.stringify(await page.evaluate(() => window.fuzz.ui.stage.debug())));
    await page.screenshot({ path: `${shots}stage-${name}-cast.png` });

    /* The Control Panel's stage group: pins and the everything switch. */
    await page.evaluate(() => window.fuzz.grant('panel'));
    await wait(5000);
    await page.click('#ctl-panel-btn');
    await wait(400);
    const looks = await page.$('.fz-panel .cp-tab[data-tab=looks]');
    if (looks) await looks.click();
    await wait(300);
    const pins = await page.$$eval('.fz-panel [data-stage-pin]', (n) => n.length);
    check('panel: a pin per effect', pins >= 5, String(pins));
    await page.screenshot({ path: `${shots}stage-${name}-panel.png` });
    await page.click('.fz-panel [data-stage-all]');
    await wait(1600);
    const after = await read(page);
    check('panel: everything at once plays them all', after.all && Object.values(after.s).every((v) => v > 0.95), JSON.stringify(after.s));
    check('no page errors', !errs.length, errs.join(' | '));
    await ctx.close();
  }
} catch (e) {
  failed += 1;
  console.log('FAIL drive threw -', e.message);
} finally {
  await browser.close();
  server?.kill();
}
console.log(failed ? `${failed} failed` : 'all ok');
process.exit(failed ? 1 : 0);
