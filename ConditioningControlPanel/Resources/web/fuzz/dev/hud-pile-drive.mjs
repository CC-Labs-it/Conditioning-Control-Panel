// Final combined HUD check. One muted hidden browser, real answers and motion changes.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { clearCards } from './pile-check.mjs';
const { firefox } = createRequire('C:/wt-fe/e2e/')('playwright');
const base = process.env.FUZZ_URL || 'http://localhost:8798/fuzz/index.html';
const out = fileURLToPath(new URL('./shots/hud-pile/', import.meta.url));
mkdirSync(out, { recursive: true });
const checks = [], errors = [];
const ids = ['toy_box', 'mini_breakout', 'mini_race', 'mini_slots', 'dispenser', 'quiz'];
const browser = await firefox.launch({ headless: true, executablePath: process.env.FF_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe', firefoxUserPrefs: { 'media.volume_scale': '0.0' } });
async function expose(page, id, phone) {
  const root = page.locator(id === 'quiz' ? '.dk[data-fz-owner="quiz"] .dk-b' : `[data-arcade="${id}"]`);
  const chip = page.locator(`.dk-chip[data-fz-owner="${id}"]`);
  const needsFocus = await page.evaluate(id => {
    const el = document.querySelector(`.dk[data-fz-owner="${id}"]`);
    return el.classList.contains('min') || +el.style.zIndex < Math.max(...[...document.querySelectorAll('.dk')].map(w => +w.style.zIndex));
  }, id);
  if (needsFocus) { await chip.scrollIntoViewIfNeeded(); if (phone) await chip.tap(); else await chip.click(); }
  const dock = page.locator(`.dk[data-fz-owner="${id}"]`);
  let point;
  for (let attempt = 0; attempt < 40; attempt++) {
    point = await page.evaluate((id) => {
      const target = document.querySelector(`.dk[data-fz-owner="${id}"]`), head = target.querySelector('.dk-t'), r = head.getBoundingClientRect();
      for (let y = r.top + 8; y < r.bottom - 3; y += 8) for (let x = r.left + 10; x < r.right - 38; x += 16) {
        const hit = document.elementFromPoint(x, y);
        if (head.contains(hit) && !hit.closest('button')) return { x, y, target: true };
      }
      for (const other of [...document.querySelectorAll('.dk:not(.min)')].reverse()) {
        if (other === target) continue;
        const b = other.querySelector('.dk-f'), s = b.getBoundingClientRect(), x = s.x + s.width / 2, y = s.y + s.height / 2;
        if (document.elementFromPoint(x, y) === b) return { x, y, target: false };
      }
      return null;
    }, id);
    assert.ok(point, `${id}: no reachable title or covering-window control`);
    if (point.target) break;
    if (phone) await page.touchscreen.tap(point.x, point.y); else await page.mouse.click(point.x, point.y);
  }
  assert.ok(point?.target, `${id}: could not expose title`);
  await page.mouse.move(point.x, point.y); await page.mouse.down(); await page.mouse.up();
  if (await dock.evaluate((el) => el.classList.contains('min'))) await dock.locator('.dk-f').click();
  await root.waitFor({ state: 'visible' });
  return root;
}
async function nextToyOpening(page) {
  await page.evaluate(() => {
    let ms = window.fuzz.state.store.toy_box.box.nextIn * 1000;
    while (ms > 0) { const dt = Math.min(100, ms); window.fuzz.engine.step(dt); ms -= dt; }
  });
  await page.waitForFunction(() => window.fuzz.state.store.toy_box.box.phase === 'play');
  await page.waitForFunction(() => [...document.querySelectorAll('[data-arcade=toy_box] img')].every((i) => i.complete && i.naturalWidth));
  await page.evaluate(() => window.fuzz.engine.scopes.get('quiz')?.ret.close());
}
async function gentle(page, on) { await page.evaluate((on) => window.fuzz.ui.setInit({ gentle: on }), on); await page.waitForTimeout(80); }
async function step(page, ms) { await page.evaluate((ms) => { while (ms > 0) { const d = Math.min(ms, 100); window.fuzz.engine.step(d); ms -= d; } }, ms); }
async function check(name, fn) {
  try { const detail = await fn(); checks.push({ name, ...detail }); console.log(`PASS ${name}`); }
  catch (e) { errors.push(`${name}: ${e.stack}`); console.log(`FAIL ${name}: ${e.message}`); }
}
async function screenshot(page, name) {
  const layout = await page.evaluate(() => {
    const r = document.getElementById('wake').getBoundingClientRect();
    return { wake: document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)?.id,
      width: innerWidth, pageWidth: document.documentElement.scrollWidth,
      images: [...document.querySelectorAll('[data-arcade] img')].map(i => ({ src: i.src, ok: i.complete && i.naturalWidth > 0 })) };
  });
  assert.equal(layout.wake, 'wake'); assert.ok(layout.pageWidth <= layout.width + 1, JSON.stringify(layout));
  assert.ok(layout.images.every(i => i.ok), JSON.stringify(layout.images));
  await page.screenshot({ path: `${out}/${name}.png` }); return layout;
}
async function pressMovingControl(page, control) {
  await control.scrollIntoViewIfNeeded();
  const point = await control.evaluate(el => {
    const r = el.getBoundingClientRect(), x = r.x + r.width / 2, y = r.y + r.height / 2;
    return { x, y, reachable: el.contains(document.elementFromPoint(x, y)) };
  });
  assert.ok(point.reachable, 'moving control must remain hit-testable');
  await page.mouse.click(point.x, point.y);
}
async function crowdedHud(page) {
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto(`${base}?pace=1&fresh=1`);
  await page.waitForFunction(() => !!window.fuzz);
  await page.evaluate(async () => {
    const f = window.fuzz, route = ['eyes', 'sweet', 'slides', 'sell', 'trance'];
    ['f1', 'f2', 'f3', 'f4', 'f5'].forEach((key, i) => { f.state.picks[key] = route[i]; });
    // Restore a ready bonus and two completed boxes before their normal mounts.
    f.state.store.mini_race = { race: { leftMs: 0 } };
    const { createToyBox } = await import('/fuzz/ui/unlocks/arcade/toy-box-model.js');
    f.state.store.toy_box = { box: { ...createToyBox(f.engine.rng), completed: 2, round: 2 } };
    f.engine.emit('page-colour', {});
    for (const item of f.items) if (item.group === 'run' && item.kind !== 'end') f.grant(item.id);
    f.add(1e9);
  });
  await page.waitForTimeout(1400);
  const cardErrors = [];
  await clearCards(page, cardErrors, 'crowded HUD'); assert.deepEqual(cardErrors, []);
  await page.evaluate(() => window.fuzz.engine.scopes.get('quiz')?.ret.close());
  await page.waitForTimeout(400);
  await page.evaluate(() => {
    // Reproduce two permitted player placements. Keep the real screens and their
    // dimensions; automatic rescue must not move this layout fixture mid-check.
    const screens = [...document.querySelectorAll('.scr')].filter(el => el.getBoundingClientRect().width > 0).slice(0, 2);
    const css = document.createElement('style');
    css.textContent = screens.map((el, i) => {
      el.dataset.hudCorner = String(i);
      const r = el.getBoundingClientRect(), x = i ? innerWidth - r.width - 8 : 8, y = innerHeight - r.height - 8;
      return `[data-hud-corner="${i}"]{transform:translate3d(${x}px,${y}px,0)!important}`;
    }).join('\n');
    document.head.appendChild(css);
    document.dispatchEvent(new Event('fz-screens'));
  });
  await page.waitForTimeout(120);
  const fixture = await page.evaluate(() => {
    const screens = [...document.querySelectorAll('.scr')].map(el => el.getBoundingClientRect()).filter(r => r.width > 0 && r.height > 0);
    const windows = [...document.querySelectorAll('.dk:not(.min)')].map(el => el.getBoundingClientRect());
    let overlaps = 0;
    for (let i = 0; i < windows.length; i++) for (let j = i + 1; j < windows.length; j++) {
      const a = windows[i], b = windows[j];
      if (Math.min(a.right, b.right) - Math.max(a.left, b.left) > 12 && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 12) overlaps++;
    }
    return { docks: document.querySelectorAll('.dk').length, screens: screens.length, overlaps, autoFolded: document.querySelectorAll('.dk.auto').length };
  });
  assert.ok(fixture.docks >= 12 && fixture.screens >= 2, `route is not crowded: ${JSON.stringify(fixture)}`);
  assert.ok(fixture.overlaps > 0, `fixture has no overlapping windows: ${JSON.stringify(fixture)}`);
  assert.equal(fixture.autoFolded, 0);
  const played = [];
  for (const id of ['mini_slots', 'mini_race', 'dispenser', 'toy_box']) {
    await page.evaluate(() => window.fuzz.engine.scopes.get('quiz')?.ret.close());
    const dock = page.locator(`.dk[data-fz-owner="${id}"]`), head = dock.locator('.dk-t');
    const body = dock.locator('.dk-b'), root = await expose(page, id, false);
    const otherMin = await page.evaluate(id => [...document.querySelectorAll('.dk')].filter(el => el.dataset.fzOwner !== id).map(el => [el.dataset.fzOwner, el.classList.contains('min')]), id);
    const from = await head.boundingBox(), beforePosition = await dock.boundingBox();
    const grab = { x: from.x + 20, y: from.y + from.height / 2 };
    await page.mouse.move(grab.x, grab.y); await page.mouse.down();
    await page.mouse.move(grab.x + 70 - beforePosition.x, grab.y + 80 - beforePosition.y, { steps: 10 }); await page.mouse.up();
    const moved = await dock.boundingBox();
    const position = await dock.evaluate(el => ({ x: el.style.left, y: el.style.top }));
    assert.ok(Math.hypot(moved.x - beforePosition.x, moved.y - beforePosition.y) > 12, `${id}: title drag did not move the window`);
    // Picture changes must preserve player positions and must never auto-fold another window.
    await page.evaluate(() => document.dispatchEvent(new Event('fz-screens')));
    await page.waitForTimeout(250);
    assert.deepEqual(await dock.evaluate(el => ({ x: el.style.left, y: el.style.top })), position, `${id}: player position changed`);
    assert.deepEqual(await page.evaluate(id => [...document.querySelectorAll('.dk')].filter(el => el.dataset.fzOwner !== id).map(el => [el.dataset.fzOwner, el.classList.contains('min')]), id), otherMin);
    assert.equal(await body.isVisible(), true, `${id}: moved window body hidden`);
    assert.equal(await dock.evaluate(el => el.matches('.min,.auto')), false, `${id}: selected panel was folded`);
    if (id === 'toy_box') { await nextToyOpening(page); await page.evaluate(() => window.fuzz.engine.scopes.get('quiz')?.ret.close()); }
    let control;
    if (id === 'mini_slots') control = root.getByRole('button', { name: 'Spin', exact: true });
    else if (id === 'mini_race') control = root.getByRole('button', { name: 'Collect bonus', exact: true });
    else if (id === 'dispenser') control = root.getByRole('button', { name: 'Turn crank', exact: true });
    else control = root.locator('[data-toy-piece]').first();
    await control.scrollIntoViewIfNeeded();
    assert.equal(await control.isEnabled(), true, `${id}: playable control disabled`);
    const before = await page.evaluate(id => window.fuzz.state.sources[id] || 0, id);
    await pressMovingControl(page, control);
    if (id === 'mini_slots') assert.ok(await page.evaluate(() => !!window.fuzz.state.store.mini_slots.machine.pending));
    else if (id === 'mini_race') assert.ok(await page.evaluate(() => window.fuzz.state.sources.mini_race) > before);
    else if (id === 'dispenser') assert.notEqual(await root.getAttribute('data-phase'), 'ready');
    else {
      const piece = await control.getAttribute('data-toy-piece'), hole = root.locator(`[data-toy-slot="${piece}"]`);
      await pressMovingControl(page, hole);
      assert.ok(await page.evaluate(piece => window.fuzz.state.store.toy_box.box.placed.includes(piece), piece));
      assert.equal((await page.evaluate(() => window.fuzz.state.store.toy_box.box.pieces)).length, 4);
    }
    assert.equal(await body.isVisible(), true, `${id}: interaction hid the window`);
    // The explicit minimize button is the only fold gesture; restoring keeps the position.
    await pressMovingControl(page, dock.locator('.dk-f')); assert.equal(await body.isVisible(), false);
    await pressMovingControl(page, dock.locator('.dk-f')); assert.equal(await body.isVisible(), true);
    assert.deepEqual(await dock.evaluate(el => ({ x: el.style.left, y: el.style.top })), position, `${id}: restore moved the window`);
    played.push({ id, clicked: true, moved: { x: moved.x, y: moved.y }, preservedOthers: otherMin.length });
    await page.screenshot({ path: `${out}/desktop-crowded-${id}.png` });
  }
  return { fixture, played };
}
try {
  if (process.env.ONLY_CROWDED === '1') {
    const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
    const page = await context.newPage(); page.setDefaultTimeout(5000);
    page.on('pageerror', e => errors.push(`desktop pageerror: ${e.message}`));
    await check('desktop crowded late-run controls', () => crowdedHud(page));
    await context.close();
  } else for (const phone of [false, true]) {
    const label = phone ? 'phone' : 'desktop';
    const context = await browser.newContext({ viewport: phone ? { width: 390, height: 844 } : { width: 1366, height: 900 }, hasTouch: phone });
    const page = await context.newPage(); page.setDefaultTimeout(5000);
    page.on('pageerror', e => errors.push(`${label} pageerror: ${e.message}`));
    await page.goto(`${base}?buy=${ids.join(',')},colour,font&fresh=1`);
    await page.waitForFunction(ids => ids.every(id => window.fuzz?.engine.scopes.has(id)), ids);
    await page.waitForTimeout(1000);
    await page.evaluate(() => { window.fuzz.state.picks.f5 = 'sissy'; window.fuzz.engine.emit('fork', { fork: 'f5', lane: 'sissy' }); window.fuzz.engine.scopes.get('quiz').ret.close(); });
    await page.waitForTimeout(500);
    await check(`${label} combined`, async () => {
      const mounted = await page.evaluate(ids => ids.map(id => ({ id, mounted: window.fuzz.engine.scopes.has(id), errors: window.fuzz.engine.scopes.get(id)?.errors })), ids);
      assert.ok(mounted.every(m => m.mounted && !m.errors));
      return { mounted, ...await screenshot(page, `${label}-combined`) };
    });
    await check(`${label} compact-tray-flip`, async () => {
      const root = await expose(page, 'toy_box', phone);
      await nextToyOpening(page);
      const pieces = await page.evaluate(() => [...window.fuzz.state.store.toy_box.box.pieces]);
      const before = await page.evaluate(() => window.fuzz.state.sources.toy_box || 0);
      for (const id of pieces) {
        if (phone) { await page.locator(`[data-toy-piece=${id}]`).tap(); await page.locator(`[data-toy-slot=${id}]`).tap(); }
        else { await page.locator(`[data-toy-piece=${id}]`).click(); await page.locator(`[data-toy-slot=${id}]`).click(); }
      }
      const paid = await page.evaluate(() => window.fuzz.state.sources.toy_box || 0);
      assert.ok(paid > before);
      await step(page, 740); await page.waitForTimeout(40);
      const flip = await root.locator('.tb-box').evaluate(el => ({ transform: getComputedStyle(el).transform, animations: el.getAnimations().map(a => a.playState) }));
      assert.notEqual(flip.transform, 'none', 'box must rotate after lid closes');
      assert.ok(flip.animations.includes('running'), JSON.stringify(flip));
      await page.screenshot({ path: `${out}/${label}-box-flip.png` });
      await gentle(page, true);
      assert.equal(await root.locator('.tb-box').evaluate(el => getComputedStyle(el).transform), 'none');
      await step(page, 1600); await gentle(page, false);
      assert.equal(await page.evaluate(() => window.fuzz.state.store.toy_box.box.phase), 'closed');
      assert.equal(await page.evaluate(() => window.fuzz.state.sources.toy_box || 0), paid);
      await nextToyOpening(page);
      const tray = await root.locator('.tb-tray').evaluate(el => ({ columns: getComputedStyle(el).gridTemplateColumns.split(' ').length, height: el.getBoundingClientRect().height }));
      assert.equal(tray.columns, 4); assert.ok(tray.height < 85, JSON.stringify(tray));
      await screenshot(page, `${label}-toy-compact`); return { flip, tray };
    });
    await check(`${label} live-gentle`, async () => {
      const root = await expose(page, 'dispenser', phone);
      await root.getByRole('button', { name: 'Turn crank' }).click(); await page.waitForTimeout(90);
      const rotate = await root.locator('.prize-crank').evaluate(el => getComputedStyle(el).rotate);
      assert.notEqual(rotate, 'none');
      await gentle(page, true);
      assert.equal(await root.locator('.prize-crank').evaluate(el => el.getAnimations().filter(a => a.playState === 'running').length), 0);
      const race = await expose(page, 'mini_race', phone); await page.waitForTimeout(100);
      assert.equal(await race.locator('video').evaluate(v => v.paused && v.hidden), true);
      assert.equal(await race.locator('img').isVisible(), true);
      await screenshot(page, `${label}-gentle-poster`); await gentle(page, false); return { crankRotateBeforeGentle: rotate };
    });
    await check(`${label} eight-themed-quizzes`, async () => {
      await expose(page, 'quiz', phone);
      const themes = await page.evaluate(async () => Object.keys((await import('/fuzz/data/content/themed-questions.js')).QUIZ_THEMES));
      const rows = [];
      for (const theme of themes) {
        const q = await page.evaluate(theme => {
          window.fuzz.state.picks.f5 = theme; window.fuzz.engine.emit('fork', { fork: 'f5', lane: theme });
          const o = window.fuzz.engine.scopes.get('quiz').ret.ask('none');
          const right = [...o.buttons].find(([a]) => a.tone === 'yes');
          return { theme: o.card.dataset.quizTheme, question: o.card.dataset.quizQuestion, right: right[1].textContent, before: window.fuzz.state.sources.quiz || 0 };
        }, theme);
        assert.equal(q.theme, theme); assert.ok(q.question.startsWith(`theme_${theme}_`));
        await page.waitForTimeout(350);
        const card = page.locator(`[data-quiz-question="${q.question}"]`).last();
        assert.equal(await card.locator('button').count(), 4);
        const bounds = await card.boundingBox(); assert.ok(bounds.x >= -1 && bounds.x + bounds.width <= (phone ? 390 : 1366) + 1);
        await screenshot(page, `${label}-quiz-${theme}`);
        await card.getByRole('button', { name: q.right, exact: true }).click();
        const paid = await page.evaluate(() => window.fuzz.state.sources.quiz);
        assert.ok(paid > q.before); const once = paid;
        await card.getByRole('button', { name: q.right, exact: true }).evaluate(el => el.click());
        assert.equal(await page.evaluate(() => window.fuzz.state.sources.quiz), once);
        rows.push({ ...q, paid: paid - q.before, lesson: await card.locator('.xq-lesson').innerText() });
        await page.evaluate(() => window.fuzz.engine.scopes.get('quiz').ret.close()); await page.waitForTimeout(350);
      }
      assert.equal(rows[2].lesson, 'Lesson complete. Bonus included.');
      assert.equal(rows[5].lesson, 'Lesson complete. Bonus included.');
      return { rows };
    });
    await check(`${label} ending-pauses-at-fade-start`, async () => {
      await expose(page, 'mini_race', phone); await page.waitForFunction(() => !document.querySelector('[data-arcade=mini_race] video').paused, null, { timeout: 12000 });
      const ended = await page.evaluate(() => { const v = document.querySelector('[data-arcade=mini_race] video'); window.fuzz.end('drop'); return { paused: v.paused, fade: !!document.querySelector('.endfade'), time: v.currentTime }; });
      assert.equal(ended.paused, true); assert.equal(ended.fade, true);
      await page.waitForTimeout(250);
      const time = await page.locator('[data-arcade=mini_race] video').evaluate(v => v.currentTime);
      assert.ok(Math.abs(time - ended.time) < .05); await page.screenshot({ path: `${out}/${label}-ending-start.png` }); return ended;
    });
    if (!phone) await check('desktop crowded late-run controls', () => crowdedHud(page));
    await context.close();
  }
} finally {
  await browser.close();
  writeFileSync(`${out}/checks.json`, JSON.stringify({ checks, errors }, null, 2));
  console.log(JSON.stringify({ checks: checks.length, errors }, null, 2));
}
assert.deepEqual(errors, []);
