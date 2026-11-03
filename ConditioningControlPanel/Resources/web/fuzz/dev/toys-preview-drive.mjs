import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
const { firefox } = createRequire('C:/wt-fe/e2e/')('playwright');
const base = process.env.FUZZ_URL || 'http://localhost:8798/fuzz/index.html';
const out = fileURLToPath(new URL('./shots/toys-preview/', import.meta.url));
mkdirSync(out, { recursive: true });
const browser = await firefox.launch({ headless: true, executablePath: 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe', firefoxUserPrefs: { 'media.volume_scale': '0.0' } });
const errors = [], checks = [];
try {
  for (const phone of [false, true]) {
    const context = await browser.newContext({ viewport: phone ? { width: 390, height: 844 } : { width: 1366, height: 900 }, hasTouch: phone });
    await context.addInitScript(() => {
      window.__audioStarts = [];
      const events = new WeakMap();
      const connections = new WeakMap(), connectNode = AudioNode.prototype.connect;
      AudioNode.prototype.connect = function (target, ...rest) { connections.set(this, target); return connectNode.call(this, target, ...rest); };
      for (const method of ['setValueAtTime', 'linearRampToValueAtTime', 'exponentialRampToValueAtTime']) {
        const original = AudioParam.prototype[method];
        AudioParam.prototype[method] = function (value, time) {
          if (!events.has(this)) events.set(this, []);
          events.get(this).push({ method, value, time });
          return original.call(this, value, time);
        };
      }
      const create = AudioContext.prototype.createBufferSource;
      AudioContext.prototype.createBufferSource = function () {
        const src = create.call(this), connect = src.connect, start = src.start;
        let gain, gainNode;
        src.connect = function (target, ...rest) { gainNode = target; gain = target.gain; return connect.call(this, target, ...rest); };
        src.start = function (...args) {
          window.__audioStarts.push({ initial: gain?.value, events: events.get(gain) || [], duration: src.buffer?.duration, loop: src.loop, channels: src.buffer?.numberOfChannels, pan: connections.get(gainNode)?.pan?.value ?? 0 });
          return start.apply(this, args);
        };
        return src;
      };
    });
    const page = await context.newPage();
    page.on('pageerror', e => errors.push(e.message));
    await page.goto(`${base}?fresh=1`);
    await page.waitForFunction(() => !!window.fuzz);
    const saved = await page.evaluate(async () => {
      const { serialize, SAVE_KEY } = await import('/fuzz/sim/save.js');
      window.fuzz.add(4321);
      const text = serialize(window.fuzz.state, window.fuzz.life, {});
      localStorage.setItem(SAVE_KEY, text); return text;
    });
    await page.goto(`${base}?preview=toys`);
    const ids = ['quiz', 'toy_box', 'mini_breakout', 'mini_race', 'mini_slots', 'dispenser'];
    await page.waitForFunction(ids => ids.every(id => window.fuzz?.engine.scopes.has(id)), ids);
    const ready = await page.evaluate(() => ({ coins: window.fuzz.state.store.mini_slots.machine.coins,
      race: window.fuzz.state.store.mini_race.race.leftMs, phase: window.fuzz.state.store.dispenser.machine.phase,
      upgrades: !!window.fuzz.state.owned.dispenser_fast && !!window.fuzz.state.owned.dispenser_faster,
      theme: window.fuzz.state.picks.f5 }));
    assert.deepEqual(ready, { coins: 3, race: 0, phase: 'ready', upgrades: true, theme: 'trance' });
    await page.waitForTimeout(2600);
    await page.screenshot({ path: `${out}/${phone ? 'phone' : 'desktop'}.png` });
    await page.locator('.dk[data-fz-owner=quiz] .dk-f').evaluate(el => el.click());
    await page.click('#btn');
    await page.waitForFunction(() => window.__audioStarts.filter(s => s.loop).length >= 3);
    const loops = await page.evaluate(() => window.__audioStarts.filter(s => s.loop));
    assert.deepEqual(loops.filter(s => s.duration === 4).map(s => s.pan).sort(), [-1, 1]);
    assert.ok(loops.some(s => s.duration === 24 && s.channels === 2));
    assert.ok(loops.every(s => s.initial < .002 && s.events.at(-1).time - s.events[0].time >= 1.5));
    assert.equal(await page.evaluate(() => window.fuzz.engine.pictures.source), 'online');
    assert.ok(await page.locator('[data-arcade=mini_slots] img').evaluateAll(imgs => imgs.some(i => i.src.startsWith('data:image/svg+xml') && i.complete && i.naturalWidth > 0)));
    await page.evaluate(() => window.fuzz.tap(80));
    await page.waitForFunction(() => window.__audioStarts.some(s => s.events.some(e => e.method === 'linearRampToValueAtTime' && Math.abs(e.value - .2) < .0001)));
    const moan = await page.evaluate(() => window.__audioStarts.find(s => s.events.some(e => e.method === 'linearRampToValueAtTime' && Math.abs(e.value - .2) < .0001)));
    assert.ok(moan.initial < .02, JSON.stringify(moan));
    assert.ok(Math.abs(moan.events[1].time - moan.events[0].time - .4) < .001);
    assert.equal(moan.events.at(-1).value, 0);
    await page.evaluate(() => window.fuzz.engine.scopes.get('quiz').ret.close());
    const slots = page.locator('[data-arcade=mini_slots]');
    if (!(await slots.isVisible())) await page.locator(phone ? '.dk-chip[data-fz-owner=mini_slots]' : '.dk[data-fz-owner=mini_slots] .dk-f').click();
    await slots.evaluate(el => el.closest('.dk').dispatchEvent(new PointerEvent('pointerdown', { bubbles: true })));
    await slots.getByRole('button', { name: 'Spin', exact: true }).click();
    assert.equal(await page.evaluate(() => window.fuzz.state.store.mini_slots.machine.coins), 2);
    await page.reload();
    await page.waitForFunction(() => window.fuzz?.state.store.mini_slots?.machine.coins === 3);
    assert.equal(await page.evaluate(() => localStorage.getItem('fuzz.save.v2')), saved, 'toy preview must preserve the normal save');
    checks.push({ viewport: phone ? 'phone' : 'desktop', ready, moan, loops, normalSavePreserved: true });
    await context.close();
  }
} finally { await browser.close(); }
writeFileSync(`${out}/checks.json`, JSON.stringify({ checks, errors }, null, 2));
assert.deepEqual(errors, []);
console.log(JSON.stringify({ checks, errors }, null, 2));
