import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const { chromium } = createRequire('C:/wt-fe/e2e/package.json')('playwright');
const base = process.env.FUZZ_URL || 'http://localhost:8807/fuzz/index.html';
const browser = await chromium.launch({ channel: 'chrome', args: ['--mute-audio'] });
try {
  for (const phone of [false, true]) {
    const context = await browser.newContext({
      viewport: phone ? {width:390,height:844} : {width:1280,height:850},
      hasTouch: phone, reducedMotion: phone ? 'reduce' : 'no-preference',
    });
    const page = await context.newPage(), errors = [];
    page.on('pageerror', err => errors.push(err.message));
    await page.addInitScript(() => {
      // Take the clock before the first gameplay frame, including on reload.
      // A reduced-motion landing can otherwise finish while navigation waits.
      const raf = window.requestAnimationFrame.bind(window);
      window.requestAnimationFrame = callback => raf(time => {
        const engine = window.fuzz?.engine;
        if (engine && !window.stepWheel) {
          window.stepWheel = engine.step.bind(engine);
          engine.step = () => {};
        }
        callback(time);
      });
      const random = Math.random;
      Math.random = () => {
        if (window.nextWheelRoll == null) return random();
        const value = window.nextWheelRoll; delete window.nextWheelRoll; return value;
      };
    });
    const ready = async () => {
      await page.waitForFunction(() => window.stepWheel && window.fuzz?.engine.scopes.get('wheel')?.ret.show);
      await page.evaluate(() => {
        fuzz.engine.scopes.get('wheel').ret.show();
      });
    };
    const step = ms => page.evaluate(ms => {
      while (ms > 0) { const dt = Math.min(100, ms); window.stepWheel(dt); ms -= dt; }
    }, ms);
    const widget = page.locator('[data-widget="prize-wheel"]');
    const round = () => page.evaluate(() => fuzz.state.store.wheel.spin);
    const earned = () => page.evaluate(() => fuzz.state.sources.wheel || 0);
    const click = r => page.evaluate(sample => {
      window.nextWheelRoll = sample;
      document.querySelector('[data-widget="prize-wheel"] button').click();
    }, r);
    await page.goto(base + '?preview=toys&theme=bimbo&icons=plain&focus=wheel');
    await ready();
    assert.equal(await widget.isVisible(), true);
    const marker = await widget.getAttribute('data-wheel-angle');
    await click(.81);
    const chosen = await round();
    assert.equal(chosen.id, 'jackpot');
    assert.equal(await widget.locator('button').isDisabled(), true);
    assert.equal(await widget.locator('output').textContent(), 'Top prize 10×');
    await step(phone ? 145 : 890);
    assert.equal((await widget.getAttribute('data-wheel-angle')) === marker, phone, 'reduced motion holds the cabinet face');
    const elapsed = (await round()).elapsed;
    await page.evaluate(() => fuzz.ui.pause());
    await step(3000);
    assert.equal((await round()).elapsed, elapsed);
    await page.evaluate(() => fuzz.ui.resume());
    // Pass through the normal save parser during a real reload, then hide the wheel.
    const previous = await earned();
    await page.evaluate(async () => {
      const {serialize,SAVE_KEY} = await import('./sim/save.js');
      localStorage.setItem(SAVE_KEY, serialize(fuzz.state, {...fuzz.life, contract:true}, {again:1}));
    });
    await page.goto(base);
    await ready();
    const restored = await round();
    assert.equal(restored.id, chosen.id);
    assert.equal(restored.base, chosen.base);
    assert.equal(restored.end, chosen.end);
    await page.evaluate(() => document.querySelector('[data-widget="prize-wheel"]').closest('.dk-b').dock.setVisible(false));
    await step(3600);
    assert.equal(await round(), null);
    assert.ok(Math.abs(await earned() - previous - chosen.base * 10) < 1e-6);
    await page.evaluate(() => fuzz.engine.scopes.get('wheel').ret.show());
    assert.ok((await widget.locator('output').textContent()).includes((chosen.base * 10).toLocaleString('en-US')));
    const win = await earned();
    await click(.48);
    assert.equal(await earned(), win);
    assert.equal(await round(), null, 'cooldown survives restoration');
    await step(91000);
    assert.equal(await widget.locator('button').isDisabled(), false);
    await click(.48);
    assert.equal((await round()).id, 'nothing');
    await step(3500);
    assert.equal(await earned(), win);
    assert.equal(await widget.locator('output').textContent(), 'Nothing');
    assert.deepEqual(errors, []);
    await context.close();
    console.log(phone ? 'Phone: reduced motion and both results passed.' : 'Desktop: spin, pause, reload, hidden settlement, cooldown and Nothing passed.');
  }
} finally { await browser.close(); }
