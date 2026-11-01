// dev/smoke.mjs - headless boot + ladder smoke. Needs a server (dev/serve.mjs)
// and Playwright with Firefox. Env: FUZZ_URL (default http://localhost:8787/fuzz/index.html?pace=8),
// PW_DIR (a node_modules/playwright folder), FF_EXE (an installed Firefox build when Playwright's pin is missing).
//   node dev/smoke.mjs
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(new URL('.', import.meta.url));
const shots = join(here, 'shots');
mkdirSync(shots, { recursive: true });
const require = createRequire(join(process.env.PW_DIR || 'C:/wt-fe/e2e/node_modules/playwright', 'package.json'));
const { firefox } = require(process.env.PW_DIR || 'C:/wt-fe/e2e/node_modules/playwright');
const url = process.env.FUZZ_URL || 'http://localhost:8787/fuzz/index.html?pace=8';

const launch = { headless: true, firefoxUserPrefs: { 'media.volume_scale': '0.0' } };
if (process.env.FF_EXE) launch.executablePath = process.env.FF_EXE;
const browser = await firefox.launch(launch);
const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
const errors = [];
page.on('pageerror', (e) => errors.push(`pageerror: ${e.message}`));
page.on('console', (m) => { if (m.type() === 'error') errors.push(`console: ${m.text()}`); });
page.on('requestfailed', (r) => errors.push(`request failed: ${r.url()}`));

await page.goto(url, { waitUntil: 'load' });
await page.waitForFunction(() => !!window.fuzz, null, { timeout: 10_000 });
for (let i = 0; i < 30; i++) await page.click('#bubble');
const afterClicks = await page.evaluate(() => window.fuzz.state.fuzz);
await page.screenshot({ path: join(shots, '00-boot.png') });

await page.evaluate(() => window.fuzz.add(20_000_000));   // the whole ladder costs about 3.3M
const ids = await page.evaluate(() => window.fuzz.app.items.map((i) => i.id));
let bought = 0;
for (const id of ids) {
  const r = await page.evaluate((id) => window.fuzz.buy(id), id);
  if (r.ok) bought += 1;
  if (bought % 8 === 0 && r.ok) await page.screenshot({ path: join(shots, `buy-${String(bought).padStart(2, '0')}.png`) });
  await page.waitForTimeout(80);
}
await page.waitForTimeout(1500);
await page.screenshot({ path: join(shots, '99-end.png') });
const ended = await page.evaluate(() => window.fuzz.state.ended);
const phone = await browser.newPage({ viewport: { width: 390, height: 780 } });
await phone.goto(url, { waitUntil: 'load' });
const wide = await phone.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1);
await phone.screenshot({ path: join(shots, 'phone.png') });
await browser.close();

console.log(JSON.stringify({ afterClicks, bought, of: ids.length, ended, phoneScrollsSideways: wide, errors }, null, 2));
process.exit(errors.length || !ended || wide ? 1 : 0);
