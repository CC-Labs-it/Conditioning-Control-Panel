import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const { chromium } = createRequire('C:/wt-fe/e2e/package.json')('playwright');
const browser = await chromium.launch({ channel: 'chrome', headless: true, args: ['--mute-audio'] });
try {
  for (const mode of ['normal', 'gentle', 'reduced', 'timeout']) {
    const page = await browser.newPage({ viewport: { width: mode === 'reduced' ? 390 : 1120, height: 850 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto((process.env.FUZZ_URL || 'http://localhost:8807/fuzz/index.html') + '?fresh=1&buy=toy_box&pictures=none');
    const root = page.locator('[data-arcade=toy_box]');
    await root.waitFor({ state: 'attached' });
    if (!(await root.isVisible())) await page.locator('.dk-chip[data-fz-owner=toy_box]').click();
    const samples = await page.evaluate(mode => {
      const { engine, ui } = window.fuzz;
      const step = engine.step;
      engine.step = () => {};
      ui.setInit({ gentle: mode === 'gentle', reduced: mode === 'reduced' });
      const state = () => engine.run.store.toy_box.box;
      const read = () => {
        const box = document.querySelector('.tb-box');
        const transform = getComputedStyle(box).transform;
        const matrix = transform === 'none' ? new DOMMatrixReadOnly() : new DOMMatrixReadOnly(transform);
        return { angle: Math.atan2(matrix.m31, matrix.m11) * 180 / Math.PI, width: box.getBoundingClientRect().width };
      };
      while (state().phase === 'closed') step(80);
      if (mode === 'timeout') {
        while (state().phase === 'play') step(10);
      } else {
        for (const piece of state().pieces) {
          document.querySelector('[data-toy-piece=' + piece + ']').click();
          document.querySelector('[data-toy-slot=' + piece + ']').click();
        }
      }
      if (state().phase !== 'closing') throw Error('Round did not close');
      const frames = [read()];
      while (state().phase === 'closing') {
        step(1000 / 60);
        frames.push(read());
        if (frames.length === 37) {
          const transform = getComputedStyle(document.querySelector('.tb-box')).transform;
          engine.setPaused(true);
          step(800);
          engine.setPaused(false);
          engine.switches.set('toy_box', false);
          engine.switches.set('toy_box', true);
          if (getComputedStyle(document.querySelector('.tb-box')).transform !== transform) throw Error('Paused or restored flip moved');
        }
      }
      return { frames, completed: state().completed };
    }, mode);
    const turns = samples.frames.map(frame => frame.angle);
    let totalTurn = 0;
    for (let i = 1; i < turns.length; i++) {
      const degrees = (turns[i] - turns[i - 1] + 540) % 360 - 180;
      assert.ok(degrees >= -.001 && degrees < 17, 'smooth forward rotation on every frame');
      totalTurn += degrees;
    }
    assert.equal(Math.round(totalTurn), mode === 'normal' ? 360 : 0);
    const [lastClosing, firstClosed] = samples.frames.slice(-2);
    assert.ok(Math.abs(firstClosed.width - lastClosing.width) < .1, 'no width jump when the closed state takes over');
    assert.equal(samples.completed, mode === 'timeout' ? 0 : 1);
    assert.deepEqual(errors, []);
    console.log(mode, samples.frames.length, 'frames, total rotation', Math.round(totalTurn));
    await page.close();
  }
} finally {
  await browser.close();
}
