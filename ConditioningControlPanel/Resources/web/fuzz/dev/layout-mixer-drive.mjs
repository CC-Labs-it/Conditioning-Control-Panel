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
  const object = page.locator(`.dk[data-fz-owner="${id}"]`), tab = page.locator(`.dk-chip[data-fz-owner="${id}"]`);
  await tab.scrollIntoViewIfNeeded();
  if (!await object.evaluate(el => el.classList.contains('min'))) await tab.click();
  await tab.click();
  return object;
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
    await page.waitForFunction(() => window.fuzz?.engine.scopes.has('toy_box'));
    await page.waitForTimeout(2600);
    await page.evaluate(() => localStorage.setItem('fuzz.save.v2', 'untouched normal save'));
    const box = await front(page, 'toy_box');
    assert.equal(await box.locator('.dk-t,.dk-f,.dk-resize').count(), 0, 'objects have no window controls');
    const tab = page.locator('.dk-chip[data-fz-owner="toy_box"]');
    await tab.press('Control+Shift+ArrowRight');
    const moved = await layout(page, 'toy_box');
    assert.equal(moved.h, null, 'the body fits its content');
    await tab.click(); assert.equal(await box.locator('.dk-b').isVisible(), false);
    await front(page, 'toy_box');
    await page.reload(); await page.waitForFunction(() => window.fuzz?.engine.scopes.has('toy_box')); await page.waitForTimeout(2300);
    const restored = await layout(page, 'toy_box');
    assert.deepEqual({x:restored.x,y:restored.y,w:restored.w,h:restored.h}, {x:moved.x,y:moved.y,w:moved.w,h:moved.h});
    await front(page, 'toy_box'); await step(page, await page.evaluate(() => window.fuzz.state.store.toy_box.box.nextIn * 1000));
    await page.waitForFunction(() => window.fuzz.state.store.toy_box.box.phase === 'play');
    const pieces = await page.evaluate(() => [...window.fuzz.state.store.toy_box.box.pieces]);
    for (const piece of pieces) { await page.locator(`[data-toy-piece=${piece}]`).click(); await page.locator(`[data-toy-slot=${piece}]`).click(); }
    assert.equal(await page.evaluate(() => window.fuzz.state.store.toy_box.box.completed), 1);
    const quiz = await front(page, 'quiz');
    await page.evaluate(() => window.fuzz.engine.scopes.get('quiz').ret.ask('none'));
    const question = quiz.locator('.xq:not(.xq-done)').last();
    assert.ok((await question.boundingBox()).width <= (await quiz.boundingBox()).width + 1);
    const answer = question.locator('.xq-a').nth(1);
    await answer.click(); assert.equal(await quiz.locator('.xq-done').count(), 1);
    await page.getByRole('button', {name:'Audio mixer', exact:true}).click();
    const mixer = page.locator('#audio-mixer');
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
    await page.reload(); await page.waitForFunction(() => window.fuzz?.engine.scopes.has('toy_box'));
    const pref = await page.evaluate(() => window.fuzz.engine.audio.mixer.sources().find(s => s.id === 'a_rain'));
    assert.equal(pref.volume, .25); assert.equal(pref.muted, true);
    assert.equal(await page.evaluate(() => window.fuzz.state.store.mini_slots.machine.coins), 3);
    assert.equal(await page.evaluate(() => localStorage.getItem('fuzz.save.v2')), 'untouched normal save');
    checks.push({ phone, moved, restored, levels, pref, boxCompleted: true, normalSaveUntouched: true });
    await context.close();
  }
} finally { await browser.close(); }
writeFileSync(`${out}/checks.json`, JSON.stringify({ checks, errors }, null, 2));
assert.deepEqual(errors, []); console.log(JSON.stringify({ checks, errors }, null, 2));
