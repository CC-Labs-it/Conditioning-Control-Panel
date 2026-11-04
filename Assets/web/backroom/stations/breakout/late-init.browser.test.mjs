import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile, existsSync } from 'node:fs';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const require = createRequire(import.meta.url), root = fileURLToPath(new URL('../../../', import.meta.url));
let chromium; try { chromium = require('playwright').chromium; } catch { /* optional browser dependency */ }
const executablePath = process.env.CHROMIUM_PATH || '/usr/bin/chromium';
test('late init repaints the mounted Breakout plan', { skip: !chromium || !existsSync(executablePath) }, async () => {
  const server = createServer((req, res) => {
    const file = path.join(root, new URL(req.url, 'http://localhost').pathname);
    readFile(file, (err, data) => { if (err) { res.writeHead(404).end(); return; }
      res.setHeader('Content-Type', ({ '.js': 'text/javascript', '.html': 'text/html', '.css': 'text/css' })[path.extname(file)] || 'application/octet-stream'); res.end(data); });
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  let browser; try { browser = await chromium.launch({ executablePath, headless: true, args: ['--no-sandbox'] });
    const page = await browser.newPage();
    await page.addInitScript(() => { window.invokeCSharpAction = raw => { if (JSON.parse(raw).type === 'ready') setTimeout(() => window.__ccpRnPush(JSON.stringify({ type: 'init', breakoutStandalone: true, breakout: { storyLimit: 3, demo: true }, plans: { currency: 'USD', prime: { name: 'Prime', monthly: '14.50', patreon_url: 'https://www.patreon.com/checkout/CodeBambi?rid=99' } } })), 4000); }; });
    await page.goto(`http://127.0.0.1:${server.address().port}/backroom/stations/breakout/play.html?desktop=1&demo=1&still&nosub`, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('.bo-demo-plan', { state: 'attached' });
    assert.equal(await page.locator('.bo-demo-plan .br-end-card-price').count(), 0);
    await page.waitForSelector('.bo-demo-plan .br-end-card-price', { state: 'attached', timeout: 6000 });
    assert.equal(await page.locator('.bo-demo-plan .br-end-card-price').textContent(), 'USD 14.50 a month');
    assert.match(await page.locator('.bo-demo-plan a').getAttribute('href'), /rid=99$/);
  } finally { await browser?.close(); server.close(); }
});
