// dev/next-drive.mjs - the Next up strip under the shop, in a real (muted, headless) browser: desktop 1280 and a 390 px
// touch phone, the Journey shop and the plain shop. Checks one row of at most three ghosts, no buy on them, the card on
// hover / first tap without a Buy button, and no sideways page scroll. Screenshots in dev/shots/next/.
//   node dev/next-drive.mjs        (starts its own dev server on 8841; FUZZ_URL to use another)
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const require = createRequire('C:/wt-fe/e2e/');
const { firefox } = require('playwright');
const here = fileURLToPath(new URL('.', import.meta.url));
const shots = join(here, 'shots', 'next');
mkdirSync(shots, { recursive: true });
let server = null;
const base = process.env.FUZZ_URL || 'http://localhost:8841/fuzz/index.html';
if (!process.env.FUZZ_URL) {
  server = spawn(process.execPath, [join(here, 'serve.mjs'), '8841'], { stdio: 'ignore' });
  await new Promise((r) => setTimeout(r, 700));
}
const browser = await firefox.launch({ headless: true,
  executablePath: process.env.FF_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe',
  firefoxUserPrefs: { 'media.volume_scale': '0.0' } });
const errors = [], made = [];
try {
  for (const shop of ['journey', 'plain']) {
    for (const phone of [false, true]) {
      const label = `${shop}-${phone ? 'phone' : 'desktop'}`;
      const context = await browser.newContext({ viewport: phone ? { width: 390, height: 844 } : { width: 1280, height: 800 }, hasTouch: phone });
      const page = await context.newPage();
      page.on('pageerror', (e) => errors.push(`${label}: ${e.message}`));
      const q = shop === 'journey' ? '?preview=journey&slot=nextdrive&fresh&pictures=none&tips=off' : '?buy=rounded,title&pictures=none&tips=off';
      await page.goto(base + q);
      await page.waitForFunction(() => window.fuzz?.engine);
      await page.waitForTimeout(1500);
      const press = (sel) => phone ? page.locator(sel).first().tap() : page.locator(sel).first().click();
      for (let i = 0; i < 3; i++) await press('#btn');
      // Buy a few goals so the strip has something past the opening rows.
      await page.evaluate(() => {
        const f = window.fuzz;
        for (let n = 0; n < 12; n++) {
          const g = f.engine.shop(6).goal;
          if (!g || g.kind === 'end') break;
          f.add(f.engine.price(g.id) + 1);
          if (g.kind === 'fork') f.engine.choose(g.id, 'eyes'); else f.engine.buy(g.id, g.options?.[0]?.[0]);
        }
      });
      await page.waitForFunction(() => { const n = document.getElementById('next'); return n && !n.hidden && n.querySelector('.ghost'); }, null, { timeout: 8000 });
      await page.waitForTimeout(500);
      const info = await page.evaluate(() => {
        const n = document.getElementById('next'), g = [...n.querySelectorAll('.ghost')];
        return { count: g.length, tops: g.map((x) => Math.round(x.getBoundingClientRect().top)), label: n.querySelectorAll('.nx-label').length,
          prices: n.querySelectorAll('.cost').length, sideways: document.documentElement.scrollWidth - innerWidth, right: Math.max(...g.map((x) => x.getBoundingClientRect().right)), w: innerWidth,
          ids: g.map((x) => x.dataset.next), shown: [...document.querySelectorAll('#goal .row,#rows .row')].map((x) => x.dataset.id) };
      });
      assert.ok(info.count >= 1 && info.count <= 3, `${label}: ${info.count} ghosts`);
      assert.equal(new Set(info.tops).size, 1, `${label}: ghosts wrap onto two rows`);
      assert.equal(info.label, 1, `${label}: one next label`);
      assert.equal(info.prices, 0, `${label}: a ghost shows a price`);
      assert.ok(info.sideways <= 0 && info.right <= info.w, `${label}: the strip scrolls the page sideways`);
      for (const id of info.ids) assert.ok(!info.shown.includes(id), `${label}: ${id} is a ghost and a tile`);
      const strip = join(shots, `${label}-strip.png`);
      await page.screenshot({ path: strip });
      made.push(strip);
      // Hover / first tap reads the card; no Buy button; nothing bought.
      const buys = await page.evaluate(() => window.fuzz.engine.run.buys);
      if (phone) await page.locator('#next .ghost').first().tap(); else await page.locator('#next .ghost').first().hover();
      await page.waitForTimeout(400);
      const tip = await page.evaluate(() => { const t = document.getElementById('tip'); return { open: !t.hidden, buy: !t.querySelector('.tip-buy').hidden, name: t.querySelector('b')?.textContent || '' }; });
      assert.ok(tip.open, `${label}: no card on the ghost`);
      assert.ok(!tip.buy, `${label}: the ghost card offers Buy`);
      assert.equal(await page.evaluate(() => window.fuzz.engine.run.buys), buys, `${label}: a ghost bought something`);
      const card = join(shots, `${label}-card.png`);
      await page.screenshot({ path: card });
      made.push(card);
      console.log(label, 'ghosts', info.ids.join(','), '| card', tip.name);
      await context.close();
    }
  }
} finally {
  await browser.close();
  server?.kill();
}
if (errors.length) { console.log('page errors:\n' + errors.join('\n')); process.exitCode = 1; }
console.log(made.join('\n'));
