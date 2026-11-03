import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
const { firefox } = createRequire('C:/wt-fe/e2e/')('playwright');
const base = process.env.FUZZ_URL || 'http://localhost:8798/fuzz/index.html';
const out = fileURLToPath(new URL('./shots/layout-mixer/', import.meta.url));
mkdirSync(out, { recursive: true });
const errors = [], checks = [];
const browser = await firefox.launch({ headless: true, executablePath: 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe', firefoxUserPrefs: { 'media.volume_scale': '0.0' } });
async function front(page, id) {
  const need = await page.evaluate(id => {
    const el = document.querySelector(`.dk[data-fz-owner="${id}"]`);
    return el.classList.contains('min') || +el.style.zIndex < Math.max(...[...document.querySelectorAll('.dk')].map(e => +e.style.zIndex));
  }, id);
  if (need) await page.locator(`.dk-chip[data-fz-owner="${id}"]`).click();
  return page.locator(`.dk[data-fz-owner="${id}"]`);
}
async function drag(page, handle, dx, dy) {
  const r = await handle.boundingBox();
  await page.mouse.move(r.x + r.width / 2, r.y + r.height / 2); await page.mouse.down();
  await page.mouse.move(r.x + r.width / 2 + dx, r.y + r.height / 2 + dy, { steps: 8 }); await page.mouse.up();
}
const layout = (page, id) => page.evaluate(id => structuredClone(window.fuzz.state.store[id]._dock1), id);
async function step(page, ms) { await page.evaluate(ms => { while (ms > 0) { const dt = Math.min(ms, 100); window.fuzz.engine.step(dt); ms -= dt; } }, ms); }
try {
  for (const phone of [false, true]) {
    const context = await browser.newContext({ viewport: phone ? { width: 390, height: 844 } : { width: 1366, height: 900 }, hasTouch: phone });
    await context.addInitScript(() => {
      window.__audio = { sources: [], next: new WeakMap() };
      const connect = AudioNode.prototype.connect;
      AudioNode.prototype.connect = function (target, ...args) { window.__audio.next.set(this, target); return connect.call(this, target, ...args); };
      const create = AudioContext.prototype.createBufferSource;
      AudioContext.prototype.createBufferSource = function () { const src = create.call(this); window.__audio.sources.push(src); return src; };
    });
    const page = await context.newPage(); page.setDefaultTimeout(6000);
    page.on('pageerror', e => errors.push(`${phone ? 'phone' : 'desktop'}: ${e.message}`));
    await page.goto(`${base}?preview=toys`);
    await page.waitForFunction(() => window.fuzz?.engine.scopes.has('audio_mixer'));
    await page.waitForTimeout(2600);
    await page.evaluate(() => localStorage.setItem('fuzz.save.v2', 'untouched normal save'));
    const box = await front(page, 'toy_box');
    const header = await box.locator('.dk-t').boundingBox();
    assert.ok(header.width <= 40 && header.height <= 34, 'expanded chrome should be just a grip');
    assert.equal(await box.locator('.dk-n').isVisible(), false);
    const before = await box.boundingBox();
    await drag(page, box.locator('.dk-t'), phone ? -2 : 340 - before.x, 90 - before.y);
    const moved = await layout(page, 'toy_box');
    await drag(page, box.locator('.dk-resize-corner'), phone ? -40 : 60, 70);
    const sized = await layout(page, 'toy_box');
    assert.ok(sized.w > 180 && sized.h >= 100 && Number.isFinite(sized.h));
    await box.locator('.dk-f').click(); assert.equal(await box.locator('.dk-b').isVisible(), false);
    await front(page, 'toy_box');
    assert.equal((await layout(page, 'toy_box')).h, sized.h);
    await box.locator('.dk-resize-right').focus(); await page.keyboard.press('Shift+ArrowLeft');
    const resized = await layout(page, 'toy_box'); assert.ok(resized.w < sized.w); assert.equal(resized.h, sized.h);
    await page.reload(); await page.waitForFunction(() => window.fuzz?.engine.scopes.has('toy_box')); await page.waitForTimeout(2300);
    const restored = await layout(page, 'toy_box');
    assert.deepEqual({x:restored.x,y:restored.y,w:restored.w,h:restored.h}, {x:resized.x,y:resized.y,w:resized.w,h:resized.h});
    await front(page, 'toy_box'); await step(page, await page.evaluate(() => window.fuzz.state.store.toy_box.box.nextIn * 1000));
    await page.waitForFunction(() => window.fuzz.state.store.toy_box.box.phase === 'play');
    const pieces = await page.evaluate(() => [...window.fuzz.state.store.toy_box.box.pieces]);
    for (const piece of pieces) { await page.locator(`[data-toy-piece=${piece}]`).click(); await page.locator(`[data-toy-slot=${piece}]`).click(); }
    assert.equal(await page.evaluate(() => window.fuzz.state.store.toy_box.box.completed), 1);
    const quiz = await front(page, 'quiz');
    const qr = await quiz.boundingBox(); await drag(page, quiz.locator('.dk-t'), 20 - qr.x, 90 - qr.y);
    await page.evaluate(() => window.fuzz.engine.scopes.get('quiz').ret.ask('none'));
    await quiz.locator('.dk-resize-right').focus();
    for (let i = 0; i < 4; i++) await page.keyboard.press('Shift+ArrowLeft');
    const answer = quiz.locator('.xq:not(.xq-done) .xq-a').nth(1);
    await answer.click(); assert.equal(await quiz.locator('.xq-done').count(), 1);
    const mixer = await front(page, 'audio_mixer');
    const mr = await mixer.boundingBox(); await drag(page, mixer.locator('.dk-t'), phone ? -mr.x + 12 : 690 - mr.x, 90 - mr.y);
    const rain = mixer.locator('[data-audio-source=a_rain]');
    await rain.locator('input').fill('25');
    await page.waitForFunction(() => window.__audio.sources.some(s => s.loop && s.buffer?.duration > 23));
    await page.waitForTimeout(3200);
    const levels = await page.evaluate(() => {
      const src = window.__audio.sources.find(s => s.loop && s.buffer?.duration > 23), n = window.__audio.next, envelope = n.get(src), source = n.get(envelope);
      return { base: envelope.gain.value, source: source.gain.value };
    });
    assert.ok(Math.abs(levels.base - .1) < .002, JSON.stringify(levels));
    assert.ok(Math.abs(levels.source - .25) < .002, JSON.stringify(levels));
    await rain.getByRole('button', { name: 'Mute Rain', exact: true }).click();
    assert.equal(await page.evaluate(() => window.fuzz.engine.audio.mixer.sources().find(s => s.id === 'a_rain').muted), true);
    await page.waitForTimeout(180);
    assert.ok(await page.evaluate(() => { const n=window.__audio.next, s=window.__audio.sources.find(s => s.loop && s.buffer?.duration>23); return n.get(n.get(s)).gain.value < .002; }));
    assert.equal(await page.evaluate(() => window.fuzz.engine.audio.mixer.sources().find(s => s.id === 'binaural').volume), 1);
    await page.screenshot({ path: `${out}/${phone ? 'phone' : 'desktop'}.png` });
    await page.reload(); await page.waitForFunction(() => window.fuzz?.engine.scopes.has('audio_mixer'));
    const pref = await page.evaluate(() => window.fuzz.engine.audio.mixer.sources().find(s => s.id === 'a_rain'));
    assert.equal(pref.volume, .25); assert.equal(pref.muted, true);
    assert.equal(await page.evaluate(() => window.fuzz.state.store.mini_slots.machine.coins), 3);
    assert.equal(await page.evaluate(() => localStorage.getItem('fuzz.save.v2')), 'untouched normal save');
    checks.push({ phone, moved, sized, restored, levels, pref, boxCompleted: true, normalSaveUntouched: true });
    await context.close();
  }
} finally { await browser.close(); }
writeFileSync(`${out}/checks.json`, JSON.stringify({ checks, errors }, null, 2));
assert.deepEqual(errors, []); console.log(JSON.stringify({ checks, errors }, null, 2));
