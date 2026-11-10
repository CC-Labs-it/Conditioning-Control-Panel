// node dev/tab-doc-drive.mjs with dev/serve.mjs 8897 running. One muted browser, desktop then phone.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';

const require = createRequire(process.env.PW_ROOT || 'C:/wt-fe/e2e/package.json');
const { chromium } = require('playwright');
const base = process.env.FUZZ_BASE || 'http://127.0.0.1:8897/fuzz/';
const shots = process.env.SHOTS || 'dev/shots/documentary';
mkdirSync(shots, { recursive: true });
const browser = await chromium.launch({ headless: true, executablePath: process.env.CHROME_EXE || 'C:/Program Files/Google/Chrome/Application/chrome.exe', args: ['--mute-audio'] });
let checks = 0;
function check(name, result) { assert.ok(result, name); console.log(`ok ${++checks} ${name}`); }
try {
  for (const phone of [false, true]) {
    const tag = phone ? 'phone' : 'desktop';
    const context = await browser.newContext({ viewport: phone ? { width: 390, height: 844 } : { width: 1440, height: 900 }, hasTouch: phone, isMobile: phone });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(String(error)));
    const open = async (at, extra = '') => {
      await page.goto(`${base}dev/tab-doc.html?manual=1&test=1&at=${at}&lite=${Number(phone)}${extra}`);
      await page.waitForFunction(() => window.__tabDoc?.tunnel.state.zone);
    };
    const pump = () => phone ? page.touchscreen.tap(180, 680) : page.mouse.click(720, 690);
    const wakeClear = () => page.evaluate(() => {
      const button = document.getElementById('wake'), rect = button.getBoundingClientRect();
      return document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2) === button;
    });
    await open('booth', '&name=%3Cimg%20src%3Dx%20onerror%3Dalert(1)%3E');
    await pump(); await page.evaluate(() => __tabDoc.step(2));
    check(`${tag} booth input rattles without motion`, await page.evaluate(() => __tabDoc.tunnel.state.z === 0));
    check(`${tag} user text stays text`, await page.locator('.td-name img').count() === 0);
    await page.evaluate(() => __tabDoc.step(39));
    check(`${tag} silent fallback stamps and unlocks`, await page.evaluate(() => !__tabDoc.tunnel.state.locked && !document.querySelector('.td-stamp').hidden));
    await page.screenshot({ path: join(shots, `${tag}-booth.png`) });
    await pump(); await page.evaluate(() => __tabDoc.step(1));
    check(`${tag} pointer pump moves the cart`, await page.evaluate(() => __tabDoc.tunnel.state.z > 0));
    await page.locator('#wake').click();
    const stoppedAt = await page.evaluate(() => __tabDoc.tunnel.state.z);
    await page.evaluate(() => __tabDoc.step(10));
    check(`${tag} Wake Up freezes ride`, await page.evaluate(() => __tabDoc.tunnel.state.z) === stoppedAt);
    await page.keyboard.press('Escape');
    await page.keyboard.press('Space'); await page.evaluate(() => __tabDoc.step(1));
    check(`${tag} Escape resumes and Space pumps`, await page.evaluate(() => __tabDoc.tunnel.state.z) > stoppedAt);
    check(`${tag} Wake Up stays above the ending`, await wakeClear());
    await open('floorboard', '&pics=online');
    await page.evaluate(() => {
      const root = document.querySelector('.td-root');
      for (let i = 0; i < 600 && __tabDoc.tunnel.state.z < 305; i++) {
        root.dispatchEvent(new PointerEvent('pointerdown', { button: 0, isPrimary: true, bubbles: true }));
        __tabDoc.step(0.2);
      }
    });
    check(`${tag} floor stop lands at 305`, await page.evaluate(() => __tabDoc.tunnel.state.z === 305));
    await page.evaluate(() => __tabDoc.step(2.2));
    check(`${tag} slow replay stays in view at the stop`, await page.locator('.td-replay').isVisible());
    await page.screenshot({ path: join(shots, `${tag}-replay.png`) });
    await page.evaluate(() => __tabDoc.step(7.2));
    check(`${tag} nature-film freeze is visible`, await page.locator('.td-freeze-plate').isVisible());
    await page.evaluate(() => __tabDoc.step(4));
    check(`${tag} fallback releases floor hold`, await page.evaluate(() => __tabDoc.tunnel.state.holds.length === 0));
    await open('wakeup');
    await page.evaluate(() => {
      const root = document.querySelector('.td-root');
      while (__tabDoc.tunnel.state.z < 466) {
        root.dispatchEvent(new PointerEvent('pointerdown', { button: 0, isPrimary: true, bubbles: true }));
        __tabDoc.step(0.2);
      }
    });
    await page.screenshot({ path: join(shots, `${tag}-vitrine.png`) });
    await page.locator('.td-specimen').click();
    check(`${tag} specimen uses the real Wake Up handler`, await page.locator('#pause').isVisible());
    await page.evaluate(() => __tabDoc.panic());
    check(`${tag} panic releases callbacks, media and nodes`, await page.evaluate(() => __tabDoc.stats().callbacks === 0 && __tabDoc.stats().timers === 0 && __tabDoc.stats().subscriptions === 0 && !document.querySelector('.td-root')));
    await open('stage', '&hours=1');
    await page.evaluate(() => __tabDoc.step(1));
    const before = await page.evaluate(() => __tabDoc.stats());
    await page.evaluate(() => { __tabDoc.wall(3 * 3600); __tabDoc.step(600); });
    const after = await page.evaluate(() => __tabDoc.stats());
    check(`${tag} dock has no rendering callback`, after.callbacks === 0 && before.frames === after.frames);
    check(`${tag} three-hour stage advance keeps node count flat`, before.nodes === after.nodes);
    check(`${tag} clock catches up to four hours`, await page.locator('.td-clock-face time').textContent() === '4:00:00');
    await page.evaluate(() => {
      Object.defineProperty(document, 'hidden', { configurable: true, value: true });
      document.dispatchEvent(new Event('visibilitychange'));
      __tabDoc.wall(90);
    });
    check(`${tag} hidden stage does not touch timer text`, await page.locator('.td-clock-face time').textContent() === '4:00:00');
    await page.evaluate(() => { delete document.hidden; document.dispatchEvent(new Event('visibilitychange')); });
    check(`${tag} timer catches up on return`, await page.locator('.td-clock-face time').textContent() === '4:01:30');
    await page.screenshot({ path: join(shots, `${tag}-stage.png`) });
    for (let i = 0; i < 3; i++) await page.locator('.td-stage-ad').click();
    check(`${tag} AD uses the host link gate`, await page.evaluate(() => __tabDoc.stats().openTabs === 2));
    const bounds = await page.locator('.td-stage-new').boundingBox();
    check(`${tag} stage control is at least 44px`, bounds.height >= 44 && bounds.width >= 44);
    await page.locator('.td-stage-new').click();
    check(`${tag} New run disposes scene`, await page.evaluate(() => __tabDoc.stats().disposed && __tabDoc.stats().subscriptions === 0));
    await open('stage', '&plan=prime'); await page.evaluate(() => __tabDoc.step(1));
    check(`${tag} subscriber has no ad button`, !(await page.locator('.td-stage-ad').isVisible()));
    await open('lever', '&reduced=1&gentle=1');
    for (let i = 0; i < 12; i++) await pump();
    await page.evaluate(() => __tabDoc.step(0.1));
    check(`${tag} reduced speed stays at six or below`, await page.evaluate(() => __tabDoc.tunnel.state.v <= 6));
    check(`${tag} Gentle has no speed streaks`, !(await page.locator('.td-streaks').isVisible()));
    check(`${tag} no runtime errors`, errors.length === 0);
    await context.close();
  }
} finally { await browser.close(); }
console.log(`${checks} documentary browser checks passed`);
