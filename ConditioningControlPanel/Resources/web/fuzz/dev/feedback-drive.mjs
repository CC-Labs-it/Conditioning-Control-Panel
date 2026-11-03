// Owner feedback checks: every tile fills, flat art stays legible, repeated audio stays quiet.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
const require = createRequire('C:/wt-fe/e2e/');
const { firefox } = require('playwright');
const base = process.env.FUZZ_URL || 'http://localhost:8798/fuzz/index.html';
const out = fileURLToPath(new URL('./shots/feedback/', import.meta.url));
mkdirSync(out, { recursive: true });
const browser = await firefox.launch({ headless: true,
  executablePath: process.env.FF_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe',
  firefoxUserPrefs: { 'media.volume_scale': '0.0' } });
const errors = [], notes = [];
try {
  for (const phone of [false, true]) {
    const label = phone ? 'phone' : 'desktop';
    const context = await browser.newContext({ viewport: phone ? { width: 390, height: 844 } : { width: 1280, height: 800 }, hasTouch: phone });
    await context.addInitScript(() => {
      window.__shots = [];
      const original = AudioContext.prototype.createBufferSource;
      AudioContext.prototype.createBufferSource = function (...args) {
        const src = original.apply(this, args), start = src.start, connect = src.connect;
        let gain;
        src.connect = function (target, ...rest) { gain = target; return connect.call(this, target, ...rest); };
        src.start = function (...rest) { window.__shots.push({ at: performance.now(), gain: gain?.gain?.value, duration: this.buffer?.duration }); return start.apply(this, rest); };
        return src;
      };
    });
    const page = await context.newPage();
    page.on('pageerror', (e) => errors.push(`${label}: ${e.message}`));
    await page.goto(`${base}?buy=rounded,float,sound,colour,wallpop,levels,softw&fresh=1`);
    await page.waitForFunction(() => window.fuzz?.engine.scopes.has('wallpop'));
    await page.click('#btn');
    await page.waitForTimeout(1000);
    await page.evaluate(() => { window.fuzz.state.fuzz = 20; window.fuzz.ui.draw(true); });
    const fills = await page.locator('#shop .row').evaluateAll((rows) => rows.map((row) => {
      const id = row.dataset.id, p = window.fuzz.engine.price(id);
      const fill = Number(/scaleY\(([^)]+)\)/.exec(row.querySelector('.bar')?.style.transform || '')?.[1]);
      const cost = row.querySelector('.cost'), priceText = cost.firstElementChild;
      return { id, fill, expected: Math.min(1, window.fuzz.state.fuzz / Math.max(1, p)), svg: !!row.querySelector('svg.ico'),
        priceVisible: cost.clientHeight >= priceText.getBoundingClientRect().height - 1 };
    }));
    assert.ok(fills.length >= 3, JSON.stringify(fills));
    assert.ok(fills.every((r) => r.svg && Math.abs(r.fill - r.expected) < .002), JSON.stringify(fills));
    assert.ok(fills.every((r) => r.priceVisible), JSON.stringify(fills));
    await page.screenshot({ path: `${out}/${label}-shop.png` });
    await page.evaluate(() => { window.fuzz.engine.unmount('softw'); window.fuzz.engine.unmount('levels'); });
    await page.click('#btn'); // arm and warm the recorded click
    await page.waitForTimeout(650);
    await page.evaluate(() => { window.__shots.length = 0; window.fuzz.tap(20); });
    await page.waitForTimeout(60);
    const manual = await page.evaluate(() => [...window.__shots]);
    assert.ok(manual.length > 0 && manual.length <= 2, `rapid manual taps coalesce: ${JSON.stringify(manual)}`);
    assert.ok(manual.every((s) => s.gain <= .041), JSON.stringify(manual));
    await page.waitForTimeout(300);
    const auto = await page.evaluate(async () => {
      window.__shots.length = 0;
      for (let i = 0; i < 40; i++) window.fuzz.engine.tap({ auto: true });
      await new Promise((r) => setTimeout(r, 50));
      return window.__shots.length;
    });
    assert.equal(auto, 0, 'automatic taps are silent');
    const walls = await page.evaluate(async () => {
      const e = window.fuzz.engine;
      e.emit('wall', { x: 0, y: 150, corner: false });
      await new Promise((r) => setTimeout(r, 1200));
      window.__shots.length = 0;
      for (let i = 0; i < 40; i++) {
        e.emit('wall', { x: 0, y: 150, corner: false });
        await new Promise((r) => setTimeout(r, 50));
      }
      return [...window.__shots];
    });
    assert.ok(walls.length <= 3 && walls.every((s) => s.gain <= .022), JSON.stringify(walls));
    notes.push({ label, tiles: fills.length, manual, auto, walls });
    // A representative art sheet, on the same renderer used by actual tiles.
    if (!phone) {
      await page.evaluate(async () => {
        window.fuzz.engine.setPaused(true);
        const { iconFor } = await import('/fuzz/ui/icons.js');
        const { BY_ID } = await import('/fuzz/data/items.js');
        const sheet = document.createElement('div');
        sheet.style.cssText = 'position:fixed;inset:0;z-index:2147483647;background:#f1e8d8;color:#211e28;display:grid;grid-template-columns:repeat(8,1fr);gap:12px;padding:24px;align-content:start;font:13px system-ui;--hue-a:#d68b68;--hue-b:#90a6a0;';
        for (const id of ['stray','f5','softw','confetti','font','sound','colour','bubble','v_frame','a_rain','levels','gold','watch','heartbtn','a_phones','asmr','tr_pod','guide','panel','wallpaper','sp_shapes','boxes','em_head','drop']) {
          const tile = document.createElement('div');
          tile.style.cssText = 'display:grid;justify-items:center;gap:7px;padding:12px;background:#fffaf0;border:2px solid #352a28;border-radius:8px;';
          const icon = iconFor(document, BY_ID[id]); icon.style.width = '64px'; icon.style.height = '64px';
          tile.append(icon, document.createTextNode(BY_ID[id].name)); sheet.append(tile);
        }
        document.body.append(sheet);
      });
      await page.screenshot({ path: `${out}/icon-sheet.png`, clip: { x: 0, y: 0, width: 1280, height: 420 } });
    }
    await context.close();
  }
  assert.deepEqual(errors, []);
  writeFileSync(`${out}/checks.json`, JSON.stringify({ notes, errors }, null, 2));
  console.log(JSON.stringify({ notes, errors }, null, 2));
} finally { await browser.close(); }
