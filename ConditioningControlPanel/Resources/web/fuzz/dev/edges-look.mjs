// dev/edges-look.mjs - the dock and the pop-up card, by real input, with a whole route on at once, wide and phone.
//   node dev/serve.mjs 8804 &   FUZZ_URL=http://localhost:8804/fuzz/index.html node dev/edges-look.mjs [outDir]
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { pileUp, clearCards } from './pile-check.mjs';

const require = createRequire('C:/wt-fe/e2e/');
const { firefox } = require(process.env.PW_DIR || 'playwright');
const base = process.env.FUZZ_URL || 'http://localhost:8804/fuzz/index.html';
const out = process.argv[2] || 'dev/shots/edges';
const only = process.env.ROUTES ? process.env.ROUTES.split(';') : null;
mkdirSync(out, { recursive: true });
const FF = process.env.FF_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe';
const browser = await firefox.launch({ headless: true, executablePath: FF, firefoxUserPrefs: { 'media.volume_scale': '0.0' } });

const ROUTES = [['eyes', 'sweet', 'slides', 'sell', 'trance'], ['ears', 'strict', 'blind', 'premium', 'locked'], ['ears', 'sweet', 'scroll', 'sell', 'drone']];
const errors = [];

/** The hard rule of the lane: nothing of the dock on the stage or the shop, and the button answers. */
const audit = (page, where) => page.evaluate((w) => {
  const bad = [];
  const hit = (a, b) => a.left < b.right - 1 && b.left < a.right - 1 && a.top < b.bottom - 1 && b.top < a.bottom - 1;
  const btn = document.getElementById('btn').getBoundingClientRect();
  const shop = document.getElementById('shop').getBoundingClientRect();
  const stage = document.getElementById('stage').getBoundingClientRect();
  const slots = [...document.querySelectorAll('.dk')].filter((d) => getComputedStyle(d).display !== 'none');
  for (const d of slots) {
    if (d.classList.contains('open')) continue;
    const r = d.getBoundingClientRect();
    const name = d.querySelector('.dk-n')?.textContent;
    if (hit(r, btn)) bad.push(`${name} on the button`);
    if (hit(r, shop)) bad.push(`${name} on the shop`);
    if (hit(r, stage)) bad.push(`${name} on the stage`);
  }
  const strip = document.getElementById('dock-strip').getBoundingClientRect();
  if (strip.height && hit(strip, btn)) bad.push('strip on the button');
  const c = document.elementFromPoint(btn.left + btn.width / 2, btn.top + btn.height / 2);
  if (c?.closest?.('.dk, #dock-strip')) bad.push(`the dock is on the button: ${c.className}`);
  const full = [...document.querySelectorAll('.rail')].filter((x) => getComputedStyle(x).display !== 'contents' && x.scrollHeight > x.clientHeight);
  if (full.length) bad.push(`${full.length} rails overflow`);
  return { where: w, bad, slots: slots.length, chips: document.querySelectorAll('.dk-chip').length, shopH: Math.round(shop.height), rail: [...document.querySelectorAll('.rail')].map((x) => `${x.scrollHeight}/${x.clientHeight}`) };
}, where);

for (const [w, h, name] of [[1280, 800, 'wide'], [390, 800, 'phone']]) {
  const ctx = await browser.newContext({ viewport: { width: w, height: h }, hasTouch: name === 'phone' });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => errors.push(`${name}: ${e.message}`));
  for (const route of ROUTES) {
    const tag = route[4];
    if (only && !only.includes(tag)) continue;
    await pileUp(page, base, route);
    await page.waitForTimeout(1400);
    await clearCards(page, errors, tag);
    await page.waitForTimeout(800);
    const a = await audit(page, `${name}/${tag}`);
    console.log(JSON.stringify(a));
    if (a.bad.length) errors.push(`${a.where}: ${a.bad.join('; ')}`);
    await page.screenshot({ path: `${out}/${name}-${tag}.png` });
    if (name === 'phone') {
      /* Open a tray by a real tap, look, close it by tapping the chip again, and check the button still answers. */
      const chips = page.locator('.dk-chip');
      const n = await chips.count();
      for (const label of ['Screen Time', 'Podcast', 'ASMR', 'Fake Live Chat']) {
        const c = page.locator('.dk-chip', { hasText: label }).first();
        if (!(await c.count())) { console.log('no chip', label); continue; }
        await c.scrollIntoViewIfNeeded();
        await c.tap();
        await page.waitForTimeout(250);
        const open = await page.locator('.dk.open').count();
        if (open !== 1) errors.push(`${tag}: tap on ${label} opened ${open} trays`);
        await page.screenshot({ path: `${out}/phone-${tag}-tray-${label.replace(/ /g, '')}.png` });
        await c.tap();
        await page.waitForTimeout(120);
        if (await page.locator('.dk.open').count()) errors.push(`${tag}: ${label} did not close`);
      }
      console.log(`${tag}: ${n} chips`);
    }
  }
  await ctx.close();
}
await browser.close();
if (errors.length) { console.log('ERRORS\n' + errors.join('\n')); process.exitCode = 1; } else console.log('ok');
