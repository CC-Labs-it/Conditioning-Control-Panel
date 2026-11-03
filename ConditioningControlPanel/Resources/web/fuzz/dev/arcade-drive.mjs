// Real desktop/touch input for the small cabinets. One muted headless browser; source modules stay unchanged.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
const require = createRequire('C:/wt-fe/e2e/');
const { firefox } = require('playwright');
const base = process.env.FUZZ_URL || 'http://localhost:8798/fuzz/index.html';
const out = fileURLToPath(new URL('./shots/arcade/', import.meta.url));
mkdirSync(out, { recursive: true });
const errors = [], checks = [];
const browser = await firefox.launch({ headless: true, executablePath: process.env.FF_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe', firefoxUserPrefs: { 'media.volume_scale': '0.0' } });

async function step(page, ms) {
  await page.evaluate((ms) => { const e = window.fuzz.engine; while (ms > 0) { const dt = Math.min(100, ms); e.step(dt); ms -= dt; } window.fuzz.ui.draw(true); }, ms);
}
async function state(page, id, key = 'machine') { return page.evaluate(({ id, key }) => structuredClone(window.fuzz.state.store[id]?.[key]), { id, key }); }
async function source(page, id) { return page.evaluate((id) => window.fuzz.state.sources[id] || 0, id); }
async function expose(page, id, phone) {
  const root = page.locator(`[data-arcade="${id}"]`);
  if (phone && !(await root.isVisible())) {
    const chip = page.locator(`.dk-chip[data-fz-owner="${id}"]`);
    await chip.scrollIntoViewIfNeeded(); await chip.click();
  }
  if (!(await root.isVisible())) await page.locator(`.dk[data-fz-owner="${id}"] .dk-t`).click();
  await root.waitFor({ state: 'visible' });
  await page.waitForTimeout(180);
  return root;
}
async function open(page, id, phone) {
  await page.goto(`${base}?buy=${id},colour,sound&fresh=1`);
  await page.waitForFunction((id) => window.fuzz?.engine.scopes.has(id), id);
  await page.evaluate((id) => {
    const e = window.fuzz.engine;
    for (const key of [...e.scopes.keys()]) if (![id, 'colour', 'sound', 'fpsv'].includes(key)) e.unmount(key);
  }, id);
  await page.waitForTimeout(200);
  return expose(page, id, phone);
}
async function common(page, root, name) {
  const result = await root.evaluate((el) => {
    const r = el.getBoundingClientRect(), w = document.getElementById('wake').getBoundingClientRect();
    return { width: r.width, height: r.height, left: r.left, right: r.right, viewport: innerWidth,
      wake: document.elementFromPoint(w.x + w.width / 2, w.y + w.height / 2)?.id,
      images: [...el.querySelectorAll('img')].map((i) => ({ src: i.getAttribute('src'), loaded: i.complete && i.naturalWidth > 0 })) };
  });
  assert.equal(result.wake, 'wake', `${name}: Wake Up covered`);
  assert.ok(result.width > 100 && result.height > 50, `${name}: cabinet hidden`);
  assert.ok(result.left >= -1 && result.right <= result.viewport + 1, `${name}: horizontal clipping ${JSON.stringify(result)}`);
  assert.ok(result.images.every((i) => i.loaded), `${name}: broken art ${JSON.stringify(result.images)}`);
  await page.screenshot({ path: `${out}/${name}.png` });
  checks.push({ name, ...result });
}
async function run(name, fn) {
  try { await fn(); console.log(`PASS ${name}`); }
  catch (e) { errors.push(`${name}: ${e.stack || e}`); console.log(`FAIL ${name}: ${e.message}`); }
}

try {
  for (const phone of [false, true]) {
    const label = phone ? 'phone' : 'desktop';
    const context = await browser.newContext({ viewport: phone ? { width: 390, height: 844 } : { width: 1280, height: 800 }, hasTouch: phone });
    const page = await context.newPage(); page.setDefaultTimeout(5000);
    page.on('pageerror', (e) => errors.push(`${label} pageerror: ${e.message}`));

    await run(`${label} toy-box`, async () => {
      const root = await open(page, 'toy_box', phone);
      await page.waitForFunction(() => [...document.querySelectorAll('[data-arcade=toy_box] img')].every((i) => i.complete && i.naturalWidth));
      const s = await state(page, 'toy_box', 'box'), before = await source(page, 'toy_box');
      const first = s.pieces[0];
      if (phone) { await page.locator(`[data-toy-piece=${first}]`).tap(); await page.locator(`[data-toy-slot=${first}]`).tap(); }
      else { await page.locator(`[data-toy-piece=${first}]`).click(); await page.locator(`[data-toy-slot=${first}]`).click(); }
      assert.ok((await state(page, 'toy_box', 'box')).placed.includes(first));
      const second = s.pieces[1], from = await page.locator(`[data-toy-piece=${second}]`).boundingBox(), to = await page.locator(`[data-toy-slot=${second}]`).boundingBox();
      await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2); await page.mouse.down();
      await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, { steps: 12 }); await page.mouse.up();
      assert.equal((await state(page, 'toy_box', 'box')).phase, 'closing');
      assert.ok(await source(page, 'toy_box') > before);
      const paid = await source(page, 'toy_box');
      await page.evaluate(() => { for (const e of document.querySelectorAll('[data-toy-slot]')) e.click(); });
      assert.equal(await source(page, 'toy_box'), paid);
      await common(page, root, `${label}-toy-packed`);
      await step(page, 1300); assert.equal((await state(page, 'toy_box', 'box')).round, 1);
      await common(page, root, `${label}-toy-next`);
    });

    await run(`${label} breakout`, async () => {
      const root = await open(page, 'mini_breakout', phone), canvas = root.locator('canvas');
      if (phone) {
        await root.getByRole('button', { name: 'Take control' }).tap();
        assert.ok((await root.innerText()).includes('Your paddle'));
        await canvas.tap({ position: { x: 45, y: 120 } });
        await root.getByRole('button', { name: 'Autoplay', exact: true }).tap();
      } else {
        await canvas.hover({ position: { x: 35, y: 80 } });
        assert.ok((await root.innerText()).includes('Your paddle'));
        await page.mouse.move(600, 400);
      }
      assert.ok((await root.innerText()).includes('Autoplay'));
      await step(page, 1200);
      await page.click('#wake'); const a = await state(page, 'mini_breakout', 'game');
      await step(page, 2000); await page.waitForTimeout(200);
      assert.deepEqual(await state(page, 'mini_breakout', 'game'), a);
      await page.click('#resume');
      await expose(page, 'mini_breakout', phone);
      await common(page, root, `${label}-breakout`);
    });

    await run(`${label} race`, async () => {
      const root = await open(page, 'mini_race', phone), video = root.locator('video');
      await page.waitForFunction(() => document.querySelector('[data-arcade=mini_race] video')?.readyState >= 2, null, { timeout: 12000 });
      const t0 = await video.evaluate((v) => v.currentTime); await page.waitForTimeout(650);
      const t1 = await video.evaluate((v) => v.currentTime); assert.ok(t1 > t0 + .2, `video did not advance ${t0}/${t1}`);
      assert.equal(await video.evaluate((v) => v.muted), true);
      await page.click('#wake'); const held = await video.evaluate((v) => v.currentTime); await page.waitForTimeout(350);
      assert.ok(Math.abs(await video.evaluate((v) => v.currentTime) - held) < .05);
      await page.click('#resume');
      await expose(page, 'mini_race', phone);
      await root.evaluate((r) => { r.style.display = 'none'; }); await page.waitForTimeout(650);
      assert.equal(await video.evaluate((v) => v.paused), true);
      await root.evaluate((r) => { r.style.display = ''; }); await page.waitForTimeout(650);
      await step(page, 36000); const before = await source(page, 'mini_race');
      await root.locator('.mini-race-collect').click(); const paid = await source(page, 'mini_race'); assert.ok(paid > before);
      await root.locator('.mini-race-collect').evaluate((b) => b.click()); assert.equal(await source(page, 'mini_race'), paid);
      await common(page, root, `${label}-race`);
      checks.push({ name: `${label}-race-decoded`, t0, t1, width: await video.evaluate((v) => v.videoWidth) });
    });

    await run(`${label} slots`, async () => {
      let root = await open(page, 'mini_slots', phone); const results = [];
      for (let i = 0; i < 6; i++) {
        if (!(await state(page, 'mini_slots')).coins) await step(page, 45000);
        await root.locator('.mini-slot-spin').click();
        const pending = await state(page, 'mini_slots'); assert.ok(pending.pending);
        if (i === 0) {
          // Serialize a genuine in-flight spin, reload the page, then resume that saved result.
          await page.evaluate(async () => {
            const { serialize } = await import('/fuzz/sim/save.js');
            window.fuzz.life.age = true; window.fuzz.life.contract = true;
            localStorage.setItem('fuzz.save.v2', serialize(window.fuzz.state, window.fuzz.life));
          });
          await page.goto(base); await page.locator('.tc #age').check(); await page.locator('.tc #accept').click();
          await page.waitForFunction(() => window.fuzz?.engine.scopes.has('mini_slots'));
          await page.evaluate(() => { for (const id of [...window.fuzz.engine.scopes.keys()]) if (!['mini_slots','colour','sound','fpsv'].includes(id)) window.fuzz.engine.unmount(id); });
          root = await expose(page, 'mini_slots', phone);
          const restored = await state(page, 'mini_slots');
          assert.deepEqual(restored.pending?.reels || restored.last, pending.pending.reels);
          assert.equal(restored.coins, pending.coins);
        }
        await step(page, 2500);
        const s = await state(page, 'mini_slots'); assert.equal(s.pending, null);
        const matching = s.last.every((n) => n === s.last[0]);
        assert.equal(matching, pending.pending.win);
        assert.equal(await root.getAttribute('data-win'), String(matching));
        assert.equal(await root.locator('.mini-slot-symbol').count(), 3);
        const symbols = await root.locator('.mini-slot-symbol').evaluateAll((nodes) => nodes.map((n) => n.innerHTML));
        if (matching) assert.equal(new Set(symbols).size, 1); else assert.ok(new Set(symbols).size > 1);
        results.push({ win: matching, reels: s.last, result: s.result });
      }
      assert.equal(results.filter((r) => r.win).length, 2);
      const paid = await source(page, 'mini_slots');
      await page.evaluate(() => { const e = window.fuzz.engine; e.switches.set('mini_slots', false); e.switches.set('mini_slots', true); });
      root = await expose(page, 'mini_slots', phone); await step(page, 500);
      assert.equal(await source(page, 'mini_slots'), paid);
      await common(page, root, `${label}-slots`); checks.push({ name: `${label}-six-spins`, results });
    });

    await run(`${label} dispenser`, async () => {
      const root = await open(page, 'dispenser', phone);
      await step(page, 60000); assert.equal((await state(page, 'dispenser')).phase, 'ready');
      await root.locator('.prize-crank').click(); assert.equal((await state(page, 'dispenser')).phase, 'drop');
      await page.click('#wake'); const held = await state(page, 'dispenser'); await step(page, 5000);
      assert.deepEqual(await state(page, 'dispenser'), held); await page.click('#resume'); await expose(page, 'dispenser', phone);
      await step(page, 800); assert.equal((await state(page, 'dispenser')).phase, 'catch');
      await common(page, root, `${label}-dispenser-catch`);
      const before = await source(page, 'dispenser'); await root.locator('.prize-bubble').click();
      const paid = await source(page, 'dispenser'); assert.ok(paid > before);
      await root.locator('.prize-bubble').evaluate((b) => b.click()); assert.equal(await source(page, 'dispenser'), paid);
      await step(page, 31000); assert.equal((await state(page, 'dispenser')).phase, 'ready');
      await root.locator('.prize-crank').click(); await step(page, 3400);
      assert.equal((await state(page, 'dispenser')).missed, 1);
      assert.equal(await source(page, 'dispenser'), paid);
      await step(page, 31000); assert.equal((await state(page, 'dispenser')).phase, 'ready');
      for (const [id, expected] of [['dispenser_fast', 22000], ['dispenser_faster', 15000]]) {
        await page.evaluate((id) => window.fuzz.grant(id), id);
        await root.locator('.prize-crank').click();
        const cooldown = (await state(page, 'dispenser')).cooldown;
        assert.ok(cooldown <= expected && cooldown > expected - 500, `${id}: ${cooldown}`);
        await step(page, expected + 1000);
      }
      await common(page, root, `${label}-dispenser-ready`);
    });
    await context.close();
  }
} finally {
  await browser.close();
  writeFileSync(`${out}/checks.json`, JSON.stringify({ checks, errors }, null, 2));
  console.log(JSON.stringify({ checks: checks.length, errors }, null, 2));
}
assert.deepEqual(errors, []);
