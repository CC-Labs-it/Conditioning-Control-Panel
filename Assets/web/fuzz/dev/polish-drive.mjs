// Real input checks for reward polish. One muted headless browser, desktop then touch.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
const require = createRequire('C:/wt-fe/e2e/');
const { firefox } = require('playwright');
const base = process.env.FUZZ_URL || 'http://localhost:8798/fuzz/index.html';
const shots = fileURLToPath(new URL('./shots/polish/', import.meta.url));
mkdirSync(shots, { recursive: true });
const browser = await firefox.launch({ headless: true,
  executablePath: process.env.FF_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe',
  firefoxUserPrefs: { 'media.volume_scale': '0.0' } });
const errors = [], notes = [];
try {
  for (const phone of [false, true]) {
    const label = phone ? 'phone' : 'desktop';
    const context = await browser.newContext({ viewport: phone ? { width: 390, height: 844 } : { width: 1280, height: 800 }, hasTouch: phone });
    const page = await context.newPage();
    page.on('pageerror', (e) => errors.push(`${label}: ${e.message}`));
    page.on('console', (m) => { if (m.type() === 'error') errors.push(`${label}: ${m.text()}`); });
    await page.goto(`${base}?buy=rounded,colour,sound,levels,softw,v_frame&pictures=none`);
    await page.waitForFunction(() => window.fuzz?.engine.scopes.has('levels'));
    await page.waitForTimeout(4500);
    const press = (selector) => phone ? page.locator(selector).tap() : page.locator(selector).click();
    await press('#btn');
    // A hand-pressed button has contact sparks and one spring, with a steady hit area.
    await page.waitForTimeout(60);
    assert.ok(await page.locator('[data-tap-spark]').count(), `${label}: no tap sparks`);
    await page.screenshot({ path: join(shots, `${label}-tap.png`) });
    for (let i = 0; i < 8; i++) await press('#btn');
    const sparks = await page.locator('[data-tap-spark]').count();
    assert.ok(sparks <= 32, `${label}: unbounded tap particles (${sparks})`);
    // A purchase still follows the real shop interaction, including first-tap preview on touch.
    const buy = await page.evaluate(() => {
      const f = window.fuzz;
      f.add(100000);
      return f.engine.shop(6).rows.find((i) => i.kind !== 'fork' && i.kind !== 'end')?.id;
    });
    assert.ok(buy, `${label}: no purchasable row`);
    await page.waitForTimeout(250);
    const selector = `#shop .row[data-id="${buy}"]`;
    const before = await page.evaluate((id) => window.fuzz.state.owned[id] || 0, buy);
    await press(selector);
    if (phone) {
      assert.equal(await page.evaluate((id) => window.fuzz.state.owned[id] || 0, buy), before, 'first touch opens preview');
      await press(selector);
    }
    assert.equal(await page.evaluate((id) => window.fuzz.state.owned[id], buy), before + 1);
    await page.waitForTimeout(75);
    assert.ok(await page.locator('.purchase-ghost').count(), `${label}: no purchase flight`);
    await page.screenshot({ path: join(shots, `${label}-purchase.png`) });
    await page.waitForTimeout(750);
    assert.equal(await page.locator('.purchase-ghost,.purchase-ring').count(), 0, 'purchase cleans up');
    // Seed a saved bank, remount as a restore, then collect by real input.
    const bank = async () => page.evaluate(() => {
      const e = window.fuzz.engine, ctx = e.scopes.get('levels').ctx;
      ctx.store.set('lvl', 1); ctx.store.set('xp', 350);
      e.unmount('levels'); e.mount('levels', true);
    });
    await bank();
    const count = await page.locator('[data-collect-levels]').textContent();
    assert.ok(!count.endsWith(' 0'), `${label}: no banked levels`);
    await press('[data-collect-levels]');
    await page.waitForTimeout(240);
    assert.equal(await page.locator('[data-collection-receipt]').count(), 1, 'one collection receipt');
    await page.screenshot({ path: join(shots, `${label}-collect.png`) });
    // Freeze while the celebration is in flight; no animation or income clock may advance.
    await press('#wake');
    const frozen = await page.evaluate(() => ({ clock: window.fuzz.state.clock,
      animations: document.querySelector('#world').getAnimations({ subtree: true }).filter((a) => a.playState === 'running').length }));
    assert.equal(frozen.animations, 0, `${label}: running animation behind pause`);
    await page.waitForTimeout(350);
    assert.equal(await page.evaluate(() => window.fuzz.state.clock), frozen.clock);
    await press('#resume');
    await page.waitForTimeout(1700);
    assert.equal(await page.locator('[data-collection-receipt]').count(), 0, 'receipt clears after resume');
    // Continuous earnings have a sparse visible source. They never need collection.
    await page.waitForFunction(() => document.querySelector('.income-packet'), null, { timeout: 10000 });
    await page.screenshot({ path: join(shots, `${label}-income.png`) });
    assert.ok(await page.locator('.income-packet').count() <= 3);
    // Live motion changes cancel new decorations, including a collection already in flight.
    await bank();
    await press('[data-collect-levels]');
    await page.evaluate(() => window.fuzz.ui.setInit({ gentle: true }));
    await page.waitForTimeout(100);
    assert.equal(await page.locator('[data-tap-spark],.purchase-ghost,.income-packet').count(), 0, 'live Gentle clears travel');
    const calmFrames = await page.locator('[data-collection-receipt]').evaluate((el) => el.getAnimations().flatMap((a) => a.effect.getKeyframes()));
    assert.ok(calmFrames.length && calmFrames.every((f) => !('transform' in f) && !('scale' in f)), 'Gentle receipt only fades');
    await page.locator('[data-collection-receipt]').waitFor({ state: 'detached', timeout: 6500 });
    // Switched-off sources clean up immediately and restore without another purchase reveal.
    await page.evaluate(() => {
      const e = window.fuzz.engine;
      e.switches.set('levels', false); e.switches.set('levels', true);
      e.switches.set('v_frame', false); e.switches.set('v_frame', true);
    });
    assert.equal(await page.locator('[data-collect-levels]').count(), 1, 'one level control after remount');
    assert.equal(await page.locator('.purchase-ghost').count(), 0, 'no purchase show on restore');
    await page.screenshot({ path: join(shots, `${label}-gentle.png`) });
    const wakeTop = await page.evaluate(() => {
      const r = document.getElementById('wake').getBoundingClientRect();
      return document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)?.id === 'wake';
    });
    assert.ok(wakeTop, 'Wake Up stays available');
    notes.push({ device: label, purchase: buy, bank: count, boundedSparks: sparks, pause: true, gentle: true, remount: true });
    await context.close();
  }
  assert.deepEqual(errors, []);
  writeFileSync(join(shots, 'checks.json'), JSON.stringify({ notes, errors }, null, 2));
  console.log(JSON.stringify({ notes, errors }, null, 2));
} finally { await browser.close(); }
