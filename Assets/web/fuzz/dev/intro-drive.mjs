// dev/intro-drive.mjs - the contract and the pictures card by real input, desktop and 390 px. Needs dev/serve.mjs running.
//   node dev/serve.mjs 8791 &   node dev/intro-drive.mjs
// Env: FUZZ_URL (default http://localhost:8791/fuzz/index.html). Screenshots land in dev/shots/intro-*.png: look at them.
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(new URL('.', import.meta.url));
const shots = join(here, 'shots');
mkdirSync(shots, { recursive: true });
const require = createRequire('C:/wt-fe/e2e/');
const { firefox } = require(process.env.PW_DIR || 'playwright');
const base = process.env.FUZZ_URL || 'http://localhost:8791/fuzz/index.html';
const FF = process.env.FF_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe';

const errors = [];
const check = (ok, what) => { if (!ok) errors.push(what); };
const browser = await firefox.launch({ headless: true, executablePath: FF, firefoxUserPrefs: { 'media.volume_scale': '0.0' } });
const SAVE = 'fuzz.save.v2';

async function open(size, opts = {}) {
  const ctx = await browser.newContext({ viewport: size, ...opts });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => errors.push(`pageerror: ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(`console: ${m.text()}`); });
  return { ctx, page };
}
const saved = (page) => page.evaluate((k) => localStorage.getItem(k), SAVE);
const toast = (page) => page.locator('.tc-toast').innerText();
const scrollTerms = (page, to) => page.evaluate((v) => { const t = document.getElementById('terms'); t.scrollTop = v === 'end' ? t.scrollHeight : v; }, to);
const eggBack = async (page, sel) => { await page.locator(sel).scrollIntoViewIfNeeded(); await page.click(sel); };

/* ---- first visit, desktop: everything in the window ---- */
{
  const { ctx, page } = await open({ width: 1280, height: 800 });
  await page.goto(base, { waitUntil: 'load' });
  await page.waitForSelector('.tc #terms');
  await page.screenshot({ path: join(shots, 'intro-1-first.png') });

  check(await page.isDisabled('#accept'), 'Accept must start disabled');
  check(await page.isDisabled('#read'), 'the second box must start disabled');
  check((await page.locator('#terms > p').count()) === 97, 'the contract lost a section');
  const sizes = await page.evaluate(() => { const p = [...document.querySelectorAll('#terms > p')]; return [parseFloat(getComputedStyle(p[0]).fontSize), parseFloat(getComputedStyle(p[p.length - 1]).fontSize)]; });
  check(sizes[0] > 15 && sizes[1] < 7, `text does not shrink: ${sizes}`);

  /* The dead X, Wake Up, Escape: harmless. */
  await page.click('.fakex');
  check(/Section 8/.test(await toast(page)), 'the X line');
  await page.click('#wake');
  check(/Nothing is running/.test(await page.locator('.tc-wake').innerText()), 'Wake Up note');
  check(await page.locator('#pause').isHidden(), 'Wake Up opened the pause card during the intro');
  await page.keyboard.press('Escape');
  check(await page.locator('#pause').isHidden(), 'Escape opened the pause card during the intro');
  const wakeHit = await page.evaluate(() => { const r = document.getElementById('wake').getBoundingClientRect(); return document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)?.id; });
  check(wakeHit === 'wake', 'Wake Up is covered by the window');
  check(await page.evaluate(() => document.getElementById('world').inert), 'the page behind must be inert');
  check(await saved(page) === null, 'a save appeared before Accept');

  /* The arrow: four messages, then the wrong way. */
  await scrollTerms(page, 400);
  await page.waitForTimeout(80);
  for (const re of [/later update/, /Still coming/, /with legal/, /assigned to a llama/]) { await page.click('#jump'); check(re.test(await toast(page)), `jump ${re}`); }
  await page.click('#jump');
  await page.waitForTimeout(80);
  check(await page.evaluate(() => document.getElementById('terms').scrollTop) === 0, 'the fifth press goes to the top');
  check(/Wrong way/.test(await toast(page)) && /1 of 8/.test(await toast(page)), 'jump egg');

  /* The other hidden things, by real clicks. */
  await eggBack(page, '#llama');
  check(/75 /.test(await toast(page)), `llama pays 50 on top of 25: ${await toast(page)}`);
  await eggBack(page, '[data-egg=exit]');
  await eggBack(page, '[data-egg=dont]');
  await eggBack(page, '#redact');
  check(!(await page.locator('#redact').getAttribute('class')).includes('bar'), 'the bar stays on');
  await eggBack(page, '#flip');
  await eggBack(page, '#ghost');
  await page.locator('#optout').scrollIntoViewIfNeeded();
  await page.click('#optout');
  check(!(await page.isChecked('#optout')), 'the opt-out must spring back');
  await page.click('#optout');
  check((await page.locator('.lln').innerText()) === 'five (5)', 'the llamas should count up on every opt-out');
  check(/five llamas/.test(await toast(page)), 'the llama line');
  check((await page.locator('#eggs').innerText()) === '8', 'all 8 found');
  check((await page.locator('#count').innerText()) === '225', `the counter behind should read the points: ${await page.locator('#count').innerText()}`);
  await page.screenshot({ path: join(shots, 'intro-2-eggs.png') });

  /* Slow scroll to the end, by the wheel: no skim. */
  await scrollTerms(page, 0);
  await page.waitForTimeout(800);
  const skimmedBefore = await page.locator('#terms .again').count();   // playwright's jumps to the hidden things count as skimming, as they should
  await page.locator('#terms').hover();
  for (let i = 0; i < 500 && await page.isDisabled('#read'); i++) { await page.mouse.wheel(0, 90); await page.waitForTimeout(130); }
  check(!(await page.isDisabled('#read')), 'slow scrolling must unlock the box');
  check((await page.locator('#terms .again').count()) === skimmedBefore, 'slow scrolling must not skim');
  check(/allegedly|100%/.test(await page.locator('#pct').innerText()), 'the Read bar');
  await page.check('#read');
  check(await page.isDisabled('#accept'), 'Accept needs the age box too');
  check(/age box/.test(await page.locator('#status').innerText()), 'status asks for the age box');
  await page.check('#age');
  await page.screenshot({ path: join(shots, 'intro-3-ready.png') });
  await page.click('#accept');
  await page.waitForFunction(() => !!window.fuzz?.state);
  await page.waitForTimeout(700);
  await page.screenshot({ path: join(shots, 'intro-4-page.png') });
  check(await page.evaluate(() => window.fuzz.state.fuzz) >= 225, 'the points did not carry into the run');
  check(await page.evaluate(() => window.fuzz.life.contract && window.fuzz.life.age), 'the contract was not remembered');
  check(await page.evaluate(() => document.activeElement?.id) === 'btn', 'focus should land on the button');
  check(await page.locator('.tc').count() === 0, 'the window stayed');
  check(await page.evaluate(() => !document.getElementById('world').inert), 'the world stayed inert');
  await page.click('#btn');
  await page.click('#wake');
  check(await page.locator('#pause').isVisible(), 'Wake Up must pause once the game runs');
  check((await page.locator('#pics-now').innerText()).includes('empty'), 'pause row should say empty frames');
  await page.waitForTimeout(250);
  await page.screenshot({ path: join(shots, 'intro-5-pause.png') });
  await page.click('#resume');

  /* Later visits: a new line each. */
  await page.evaluate(() => window.fuzz.engine.save());
  const lines = [];
  for (let i = 0; i < 3; i++) {
    await page.reload({ waitUntil: 'load' });
    await page.waitForSelector('.tc #age');
    lines.push(await page.locator('.tc .intro').innerText());
    check(await page.isDisabled('#accept'), 'later visit: Accept needs the age box');
    check(await page.evaluate(() => document.activeElement?.id) === 'age', 'later visit: focus on the age box');
    if (i === 0) await page.screenshot({ path: join(shots, 'intro-6-back.png') });
    await page.check('#age');
    await page.click('#accept');
    await page.waitForFunction(() => window.fuzz?.state?.taps >= 1);
    await page.evaluate(() => window.fuzz.engine.save());
  }
  check(new Set(lines).size === 3 && lines.every((l) => /llama/i.test(l)), `update lines: ${lines}`);
  check(await page.evaluate(() => window.fuzz.state.taps) >= 1, 'a later visit lost the run');

  /* Decline on a later visit keeps the save exactly as it was. */
  const before = await saved(page);
  await page.reload({ waitUntil: 'load' });
  await page.waitForSelector('.tc #age');
  const unchanged = await saved(page);
  await page.click('#decline');
  await page.reload({ waitUntil: 'load' });
  check(await saved(page) === unchanged && !!before, 'declining changed the save');
  await ctx.close();
}

/* ---- decline on a first visit: no game, no audio, nothing saved ---- */
{
  const { ctx, page } = await open({ width: 1280, height: 800 });
  await page.goto(base, { waitUntil: 'load' });
  await page.waitForSelector('.tc #decline');
  await page.click('#decline');
  await page.waitForSelector('#again');
  await page.waitForTimeout(250);
  await page.screenshot({ path: join(shots, 'intro-7-decline.png') });
  check(/You declined/.test(await page.locator('.tc h1').innerText()), 'decline screen');
  check(await page.evaluate(() => document.activeElement?.id) === 'again', 'decline: focus on Read it again');
  const quiet = await page.evaluate(() => ({ mounted: window.fuzz.engine.scopes.size, taps: window.fuzz.state.taps, clock: window.fuzz.state.clock, shopHidden: document.getElementById('world').inert }));
  check(quiet.mounted === 0 && quiet.clock === 0 && quiet.shopHidden, `declined but running: ${JSON.stringify(quiet)}`);
  await page.evaluate(() => { window.dispatchEvent(new Event('pagehide')); document.dispatchEvent(new Event('visibilitychange')); });
  check(await saved(page) === null, 'declining saved something');
  await page.click('#again');
  await page.waitForSelector('.tc #terms');
  check((await page.locator('#count').innerText()) === '0', 'Read it again starts the count over');
  await page.reload({ waitUntil: 'load' });
  await page.waitForSelector('.tc #terms');
  check(await saved(page) === null, 'a reload after declining saved something');
  await ctx.close();
}

/* ---- hosted: the host's suspend and close win during the intro, through the real bridge, with a stand-in webview ---- */
{
  const { ctx, page } = await open({ width: 1280, height: 800 });
  await page.addInitScript(() => {
    const handlers = [];
    window.__sent = [];
    window.__host = (frame) => handlers.forEach((fn) => fn({ data: frame }));
    window.chrome = { webview: { postMessage: (f) => window.__sent.push(f), addEventListener: (_, fn) => handlers.push(fn) } };
  });
  await page.goto(base, { waitUntil: 'load' });
  await page.evaluate(() => window.__host({ type: 'init', protocol: 1, hosted: true, caps: {}, mod: 'default' }));
  await page.waitForSelector('.tc #terms');
  await page.evaluate(() => window.__host({ type: 'suspend', on: true, reason: 'panic' }));
  check(await page.locator('#pause').isVisible(), 'the pause card must show over the window on a host stop');
  check(await page.locator('#pics-change').isHidden(), 'no pictures press during the intro');
  check(await page.evaluate(() => document.querySelector('.tc').inert && window.fuzz.engine.suspended), 'the window must take no press during a host stop');
  await page.waitForTimeout(250);
  await page.screenshot({ path: join(shots, 'intro-8-suspend.png') });
  await page.click('#resume');
  check(await page.locator('#pause').isVisible(), 'Resume must not lift a host stop');
  await page.evaluate(() => window.__host({ type: 'suspend', on: false, reason: 'panic' }));
  await page.click('#resume');
  check(await page.locator('.tc #terms').isVisible(), 'the window should be back after the stop');
  check(await page.evaluate(() => !document.querySelector('.tc').inert), 'the window should take presses again');
  check(await saved(page) === null, 'a host stop saved a player who has not accepted');

  /* Decline in a host says window, not tab, and has a way out. */
  await page.click('#decline');
  check(/close this window/.test(await page.locator('.tc .intro').innerText()), 'hosted decline copy');
  await page.click('#leave-intro');
  check(await page.evaluate(() => window.__sent.some((f) => f.type === 'exit')), 'Leave must tell the host');
  await page.click('#again');
  await page.waitForSelector('.tc #terms');

  /* Close: exit-done goes out, nothing is written, Accept is dead. */
  await page.evaluate(() => window.__host({ type: 'close' }));
  const closed = await page.evaluate(() => ({ done: window.__sent.some((f) => f.type === 'exit-done'), inert: document.querySelector('.tc').inert }));
  check(closed.done && closed.inert, `host close: ${JSON.stringify(closed)}`);
  check(await saved(page) === null, 'a host close saved a player who has not accepted');
  await ctx.close();
}

/* ---- phone, 390 px, touch: fling skimming, then everything by tap ---- */
{
  const { ctx, page } = await open({ width: 390, height: 844 }, { hasTouch: true });
  await page.goto(base, { waitUntil: 'load' });
  await page.waitForSelector('.tc #terms');
  await page.screenshot({ path: join(shots, 'intro-9-phone.png') });
  check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'the phone page scrolls sideways');
  const win = await page.evaluate(() => { const r = document.querySelector('.tc .win').getBoundingClientRect(); const a = document.getElementById('accept').getBoundingClientRect(); const w = document.getElementById('wake').getBoundingClientRect(); return { top: r.top, bottom: r.bottom, left: r.left, right: r.right, acceptBottom: a.bottom, wakeBottom: w.bottom, h: innerHeight, w: innerWidth }; });
  check(win.top >= win.wakeBottom, `the window covers Wake Up: ${JSON.stringify(win)}`);
  check(win.bottom <= win.h && win.right <= win.w && win.left >= 0 && win.acceptBottom <= win.h, `the window overflows: ${JSON.stringify(win)}`);
  await page.tap('.fakex');
  await page.tap('#jump');
  /* A fling: a decelerating burst of scroll, the shape a finger leaves. Firefox headless cannot synthesise a real one. */
  const fling = await page.evaluate(async () => {
    const t = document.getElementById('terms');
    let v = 5200;   // px per second
    let last = performance.now();
    while (v > 200 && t.scrollTop < t.scrollHeight - t.clientHeight - 2) {
      await new Promise((r) => requestAnimationFrame(r));
      const now = performance.now();
      t.scrollTop += v * (now - last) / 1000;
      last = now;
      v *= 0.97;
    }
    return document.querySelectorAll('#terms .again').length;
  });
  check(fling > 0, `a fling should skim: ${fling}`);
  check(/Skimming detected/.test(await toast(page)), 'skim line on the phone');
  await page.waitForTimeout(150);
  await page.screenshot({ path: join(shots, 'intro-10-skim.png') });
  /* The cap: keep flinging, it always gets through. */
  for (let i = 0; i < 80 && await page.isDisabled('#read'); i++) {
    await page.evaluate(() => { const t = document.getElementById('terms'); t.scrollTop = t.scrollHeight; });
    await page.waitForTimeout(50);
  }
  check(!(await page.isDisabled('#read')), 'skimming must never stop the player getting through');
  const again = await page.locator('#terms .again').count();
  check(again <= 14 * 3 && again > 0, `the cap: ${again}`);
  await page.tap('#read');
  await page.tap('#age');
  await page.waitForTimeout(50);
  check(/A joke contract/.test(await page.locator('#status').innerText()), 'status when ready');
  await page.screenshot({ path: join(shots, 'intro-11-phone-ready.png') });
  await page.tap('#accept');
  await page.waitForFunction(() => !!window.fuzz?.state);
  await page.waitForTimeout(400);
  await page.screenshot({ path: join(shots, 'intro-12-phone-page.png') });

  /* Own files on a route that needs pictures: the real fork press, a real PNG and GIF through the picker. */
  const png = Buffer.from(await page.evaluate(() => { const c = document.createElement('canvas'); c.width = 480; c.height = 640; const x = c.getContext('2d'); const g = x.createLinearGradient(0, 0, 480, 640); g.addColorStop(0, '#ff2a90'); g.addColorStop(1, '#2a6bff'); x.fillStyle = g; x.fillRect(0, 0, 480, 640); x.fillStyle = '#fff'; x.font = '64px serif'; x.fillText('MINE', 120, 320); return c.toDataURL('image/png').split(',')[1]; }), 'base64');
  const gif = Buffer.from('R0lGODlhAQABAIAAAP8AAP///yH5BAAAAAAALAAAAAABAAEAAAICRAEAOw==', 'base64');
  const ready = await page.evaluate(() => {
    const f = window.fuzz, row = f.items.find((i) => i.id === 'f3');
    for (const id of [row.req].flat().filter(Boolean)) f.grant(id);
    f.add(1e7);
    f.ui.draw(true);
    const r = f.pick('f3', 'slides');
    return { ok: r.ok, why: r.why || r.reason, pick: f.state.picks.f3, req: row.req };
  });
  check(ready.pick === 'slides', `the route did not take: ${JSON.stringify(ready)}`);
  await page.waitForSelector('.tc [data-pick=files]');
  await page.waitForTimeout(200);
  await page.screenshot({ path: join(shots, 'intro-13-pictures.png') });
  check(/Slideshow/.test(await page.locator('.tc .intro').innerText()), 'the card names the route');
  await page.tap('[data-pick=signin]');
  check(/website build/.test(await page.locator('#pic-status').innerText()), 'the sign-in stub');
  check(await page.locator('.tc [data-pick=signin]').isVisible(), 'the stub returns to the card');
  await page.setInputFiles('#pic-files', [{ name: 'note.txt', mimeType: 'text/plain', buffer: Buffer.from('no') }]);
  check(/not pictures/.test(await page.locator('#pic-status').innerText()), 'a non-picture is refused');
  const chooser = page.waitForEvent('filechooser', { timeout: 3000 }).catch(() => null);
  await page.tap('[data-pick=files]');
  const fc = await chooser;
  check(!!fc, 'Use my own files must open the picker');
  await fc?.setFiles([{ name: 'mine.png', mimeType: 'image/png', buffer: png }, { name: 'mine.gif', mimeType: 'image/gif', buffer: gif }]);
  await page.waitForFunction(() => !document.querySelector('.tc'));
  const pics = await page.evaluate(async () => { const p = window.fuzz.engine.pictures; return { source: p.source, n: p.ownCount, url: (await p.next()).url, saved: JSON.parse(localStorage.getItem('fuzz.save.v2')).extras.pictures }; });
  check(pics.source === 'own' && pics.n === 2 && pics.url.startsWith('blob:'), `own files: ${JSON.stringify(pics)}`);
  check(pics.saved === 'own', 'the choice, not the files, is what the save keeps');
  check(!(await saved(page)).includes('blob:'), 'a blob url reached the save');
  await page.evaluate(() => { const f = window.fuzz; for (const it of f.engine.shop(50).rows) if (it.lane === 'slides' && it.kind !== 'fork') f.buy(it.id); });
  await page.waitForTimeout(3800);
  await page.screenshot({ path: join(shots, 'intro-14-slideshow.png') });
  const shown = await page.evaluate(() => [...document.querySelectorAll('#world *')].filter((e) => (e.style?.background || '').includes('blob:') || (e.tagName === 'IMG' && e.src.startsWith('blob:') && e.naturalWidth > 0)).length);
  check(shown > 0, 'no frame is showing the file');

  /* The pause row changes it. */
  await page.tap('#wake');
  check(/2 on this visit/.test(await page.locator('#pics-now').innerText()), 'pause row counts the files');
  await page.tap('#pics-change');
  await page.waitForSelector('.tc.above #pic-back');
  await page.screenshot({ path: join(shots, 'intro-15-change.png') });
  await page.tap('#pic-back');
  check(await page.locator('.tc').count() === 0, 'Back closes the card');
  await page.tap('#resume');

  /* A reload: the files are gone, and the card offers to pick again rather than pretending. */
  await page.evaluate(() => window.fuzz.engine.save());
  await page.reload({ waitUntil: 'load' });
  await page.waitForSelector('.tc #age');
  await page.tap('#age');
  await page.tap('#accept');
  await page.waitForSelector('.tc [data-pick=files]');
  await page.waitForTimeout(200);
  await page.screenshot({ path: join(shots, 'intro-16-lost.png') });
  check(/only here for that visit/.test(await page.locator('.tc .intro').innerText()), 'the card after a reload');
  check(await page.evaluate(async () => (await window.fuzz.engine.pictures.next()) === null), 'frames must stay empty meanwhile');
  await page.tap('[data-pick=none]');
  check(await page.evaluate(() => window.fuzz.engine.pictures.source) === 'none', 'Leave the frames empty sets none');
  await ctx.close();
}

/* ---- Gentle: no shake on the X ---- */
{
  const { ctx, page } = await open({ width: 800, height: 700 }, { reducedMotion: 'reduce' });
  await page.goto(base, { waitUntil: 'load' });
  await page.waitForSelector('.tc #terms');
  await page.click('.fakex');
  check(!(await page.locator('.tc .win').getAttribute('class')).includes('nudge'), 'reduced motion must not shake');
  await ctx.close();
}

/* ---- the dev door: placeholder art only with ?pictures=online ---- */
{
  const { ctx, page } = await open({ width: 800, height: 700 });
  await page.goto(`${base}?fresh=1`, { waitUntil: 'load' });
  await page.waitForFunction(() => !!window.fuzz);
  check(await page.evaluate(async () => (await window.fuzz.engine.pictures.next()) === null), 'no door: empty frames');
  await page.goto(`${base}?fresh=1&pictures=online`, { waitUntil: 'load' });
  await page.waitForFunction(() => !!window.fuzz);
  check(await page.evaluate(async () => (await window.fuzz.engine.pictures.next()).url.startsWith('data:image/svg')), 'the dev door shows placeholder art');
  await ctx.close();
}

await browser.close();
console.log(JSON.stringify({ errors }, null, 1));
process.exit(errors.length ? 1 : 0);
