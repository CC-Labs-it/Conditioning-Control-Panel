// dev/smoke.mjs - plays a whole route headless at high pace and fails on page errors. Needs dev/serve.mjs running.
//   node dev/serve.mjs 8791 &   node dev/smoke.mjs
// Env: FUZZ_URL (default http://localhost:8791/fuzz/index.html), ROUTE (e.g. eyes,sweet,slides,sell,trance),
// PW_DIR (a node_modules/playwright folder), FF_EXE (a Firefox build). Screenshots land in dev/shots/.
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { runPileChecks } from './pile-check.mjs';

const here = fileURLToPath(new URL('.', import.meta.url));
const shots = join(here, 'shots');
mkdirSync(shots, { recursive: true });
const require = createRequire('C:/wt-fe/e2e/');
const { firefox } = require(process.env.PW_DIR || 'playwright');
const base = process.env.FUZZ_URL || 'http://localhost:8791/fuzz/index.html';
const route = (process.env.ROUTE || 'eyes,sweet,slides,sell,trance').split(',');
const FF = process.env.FF_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe';

const browser = await firefox.launch({ headless: true, executablePath: FF, firefoxUserPrefs: { 'media.volume_scale': '0.0' } });
const errors = [];
function watch(page) {
  page.on('pageerror', (e) => errors.push(`pageerror: ${e.message}`));
  page.on('console', (m) => {
    if (m.type() !== 'error') return;
    // data/ads.js comes from the content lane; the page runs on stand-ins until it lands.
    if (/data\/ads\.js/.test(m.text())) return;
    errors.push(`console: ${m.text()}`);
  });
}

async function wakeIsOnTop(page, where) {
  const ok = await page.evaluate(() => {
    const w = document.getElementById('wake').getBoundingClientRect();
    const hit = document.elementFromPoint(w.left + w.width / 2, w.top + w.height / 2);
    return hit && hit.id === 'wake';
  });
  if (!ok) errors.push(`Wake Up is covered at ${where}`);
}

/** The whole contract, the way a stubborn player gets through it: age box, scroll until the second box opens, tick, Accept. */
async function signContract(page) {
  await page.check('#age');
  for (let i = 0; i < 40 && await page.isDisabled('#read'); i++) {
    await page.evaluate(() => { const t = document.getElementById('terms'); t.scrollTop = t.scrollHeight; });
    await page.waitForTimeout(60);
  }
  await page.check('#read');
  await page.click('#accept');
}
/** "Choose your pictures" opens before the contract on a real load: take the no-pictures option, as a player would. */
async function passPictures(page) {
  const first = await page.waitForSelector('.tc [data-pick=none], .tc #terms, .tc #age');
  if (await first.getAttribute('data-pick') !== 'none') return;
  await page.waitForTimeout(400);   // the card's own guard against a stray first press
  await page.click('.tc [data-pick=none]');
  await page.waitForSelector('.tc [data-pick=none]', { state: 'detached' });
}
/** The terms card (ui/clause.js) a hand-made buy now asks first: tick whatever it asks, then accept. */
async function acceptTerms(page) {
  const card = await page.waitForSelector('.tc.clause [data-accept]', { timeout: 3000 }).catch(() => null);
  if (!card) return;
  await page.waitForTimeout(400);
  for (const box of await page.$$('.tc.clause input[data-clause]')) await box.check();
  await page.click('.tc.clause [data-accept]');
  await page.waitForSelector('.tc.clause', { state: 'detached' });
}
/** The short window every later load asks. */
async function acceptUpdate(page) {
  await passPictures(page);
  await page.waitForSelector('.tc #age');
  await page.check('#age');
  await page.click('#accept');
}

/* ---- desktop: the first real minute, by hand ---- */
const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
const page = await ctx.newPage();
watch(page);
await page.goto(base, { waitUntil: 'load' });
await passPictures(page);
await page.waitForSelector('.tc #terms');
await page.screenshot({ path: join(shots, 'a0-contract.png') });
await signContract(page);
await page.waitForFunction(() => !!window.fuzz);
await page.screenshot({ path: join(shots, 'a1-start.png') });
for (let i = 0; i < 12; i++) await page.click('#btn');
await page.waitForTimeout(250);
await page.screenshot({ path: join(shots, 'a2-tapped.png') });
await page.click('#goal .row');   // Bouncing Button
await acceptTerms(page);
await page.waitForTimeout(600);
await page.screenshot({ path: join(shots, 'a3-stray.png') });
const strays = await page.locator('[data-stray]').count();
if (strays !== 1) errors.push(`expected one stray, saw ${strays}`);

/* ---- a reload keeps the run and mounts again ---- */
await page.evaluate(() => window.fuzz.engine.save());
await page.reload({ waitUntil: 'load' });
await acceptUpdate(page);
await page.waitForFunction(() => !!window.fuzz);
await page.waitForFunction(() => document.querySelectorAll('[data-stray]').length === 1, null, { timeout: 3000 }).catch(() => errors.push('the stray did not come back after a reload'));
const taps = await page.evaluate(() => window.fuzz.state.taps);
if (taps < 12) errors.push(`the save lost the taps: ${taps}`);

/* ---- the fork card, by hand ---- */
await page.evaluate(() => { window.fuzz.add(200); window.fuzz.buy('fpsv'); window.fuzz.ui.draw(true); });
await page.waitForTimeout(250);
await page.click('#goal .row');
await page.waitForSelector('.opt');
await page.waitForTimeout(1500);
await page.screenshot({ path: join(shots, 'a4-fork.png') });
await page.click(`.opt[data-lane="${route[0]}"]`);

/* ---- a stream on a real media element: two seconds of silent WAV, so it needs no asset ---- */
await page.click('#btn');
await page.evaluate(() => {
  const n = 16000, b = new ArrayBuffer(44 + n * 2), v = new DataView(b);
  const s = (o, t) => [...t].forEach((c, i) => v.setUint8(o + i, c.charCodeAt(0)));
  s(0, 'RIFF'); v.setUint32(4, 36 + n * 2, true); s(8, 'WAVEfmt '); v.setUint32(16, 16, true); v.setUint16(20, 1, true); v.setUint16(22, 1, true);
  v.setUint32(24, 8000, true); v.setUint32(28, 16000, true); v.setUint16(32, 2, true); v.setUint16(34, 16, true); s(36, 'data'); v.setUint32(40, n * 2, true);
  const url = URL.createObjectURL(new Blob([b], { type: 'audio/wav' }));
  const ctx = window.fuzz.engine.scopes.get('stray').ctx;
  window.__ctx = ctx;
  window.__s = ctx.audio.stream(url, { loop: true });
  window.__panned = ctx.audio.stream(url, { loop: true, pan: -1 });
});
await page.waitForFunction(() => !window.__s.el.paused && window.__s.el.currentTime > 0.05 && !window.__panned.el.paused && window.__panned.el.currentTime > 0.05, null, { timeout: 8000 })
  .catch(() => errors.push('a stream did not play'));

/* ---- ctx.parts, the ending layer, ctx.speed in a real page ---- */
const bits = await page.evaluate(async () => {
  const c = window.__ctx, e = window.fuzz.engine;
  const n0 = e.now, r0 = e.real;
  const release = c.speed(0.5);
  await new Promise((r) => setTimeout(r, 500));
  const ratio = (e.now - n0) / (e.real - r0);
  release();
  return { shop: c.parts.shop.id, wrap: c.parts.buttonWrap.id, counter: c.parts.counter.id, stage: c.parts.stage.id, ending: c.layer('ending').id, ratio };
});
if (bits.shop !== 'shop' || bits.wrap !== 'btn-wrap' || bits.counter !== 'counter' || bits.stage !== 'stage' || bits.ending !== 'l-ending') errors.push(`ctx.parts or the ending layer: ${JSON.stringify(bits)}`);
if (bits.ratio > 0.85) errors.push(`ctx.speed(0.5) ran at ${bits.ratio}`);

/* ---- an affordable goal settles into a readable state, without a permanent flash ---- */
await page.evaluate(() => { window.fuzz.add(1e4); window.fuzz.ui.draw(true); });
const ready = await page.evaluate(() => {
  const row = document.querySelector('#goal .row.ready');
  return { visible: !!row && row.getBoundingClientRect().width > 0,
    repeating: !!row?.getAnimations().some((a) => a.effect?.getTiming().iterations === Infinity),
    tag: row?.querySelector('.tag')?.textContent };
});
if (!ready.visible || ready.repeating || !['Ready', 'Your choice', 'The end'].includes(ready.tag)) errors.push(`goal affordance: ${JSON.stringify(ready)}`);

/* ---- ctx.openLink: only a real press on a card button, only cclabs.app ---- */
await page.evaluate(() => {
  window.__opened = [];
  window.open = (...a) => { window.__opened.push(a[0]); return null; };
  window.__direct = window.__ctx.openLink('https://cclabs.app/direct');
  window.__ctx.on('tap', () => { window.__onTap = window.__ctx.openLink('https://cclabs.app/tap'); });
  window.__ctx.modal({ title: 'Link', buttons: [
    { label: 'Go', keepOpen: true, onPress: () => { window.__ctx.openLink('https://cclabs.app/ok'); window.__ctx.openLink('https://example.com/no'); } },
    { label: 'Close' },
  ] });
});
await page.waitForTimeout(400);
await page.click('.backdrop .card button.primary');
await page.evaluate(() => document.querySelector('.backdrop .card button.primary').click());  // a script's click is not a press
await page.click('.backdrop .card button:not(.primary)');
await page.click('#btn');
const links = await page.evaluate(() => ({ opened: window.__opened, direct: window.__direct, onTap: window.__onTap }));
if (JSON.stringify(links.opened) !== '["https://cclabs.app/ok"]' || links.direct !== false || links.onTap !== false) errors.push(`ctx.openLink: ${JSON.stringify(links)}`);

/* ---- pause: Wake Up stops the clock and the stream, with nothing polling ---- */
await page.click('#wake');
const heldNow = await page.evaluate(() => ({ s: window.__s.el.paused, p: window.__panned.el.paused }));
if (!heldNow.s || !heldNow.p) errors.push('a stream kept playing through Wake Up');
const c0 = await page.evaluate(() => window.fuzz.state.clock);
await page.waitForTimeout(400);
const c1 = await page.evaluate(() => window.fuzz.state.clock);
if (c0 !== c1) errors.push('the clock ran during pause');
await page.screenshot({ path: join(shots, 'a5-pause.png') });
await page.click('#resume');
await page.waitForFunction(() => !window.__s.el.paused, null, { timeout: 3000 }).catch(() => errors.push('a stream did not come back after resume'));

/* ---- the host's panic: the stream stops in the same call ---- */
const panic = await page.evaluate(() => {
  const heard = [];
  window.__ctx.on('suspend', (e) => heard.push([e.on, e.reason, window.__s.el.paused]));
  window.fuzz.engine.setSuspended(true, 'panic');
  const during = window.__s.el.paused;
  window.__s.el.play();
  window.fuzz.engine.setSuspended(false, 'panic');
  return { during, heard };
});
if (!panic.during || !panic.heard[0]?.[2] || panic.heard[0]?.[1] !== 'panic') errors.push(`panic and a stream: ${JSON.stringify(panic)}`);

/* ---- the rest of the route at speed: a greedy buyer through the real verbs ---- */
const picks = { f2: route[1], f3: route[2], f4: route[3], f5: route[4] };
const played = await page.evaluate(async (picks) => {
  const f = window.fuzz, e = f.engine;
  f.state.pace = 500;
  const t0 = performance.now();
  let n = 0;
  while (!f.state.ended && performance.now() - t0 < 60000) {
    f.tap(5);
    for (const id of Object.keys(picks)) if (f.items.find((i) => i.id === id) && !f.state.owned[id] && f.state.fuzz >= e.price(id)) f.pick(id, picks[id]);
    document.querySelector('.tc [data-pick=none]')?.click();   // the pictures card on Slideshow and Swipe Feed
    const win = e.shop(50);
    for (const it of [...win.rows, win.goal].filter(Boolean)) {
      if (it.kind === 'fork' || it.id === 'go_tx') continue;   // fate seeds: the greedy buyer takes a plain set; EMI has its own drive
      if (f.state.fuzz >= e.price(it.id)) { const r = f.buy(it.id); if (r.ok) n++; }
    }
    await new Promise((r) => requestAnimationFrame(r));
  }
  return { ended: f.state.ended, n, picks: f.state.picks, clock: f.state.clock, order: f.state.order.length };
}, picks);
if (!played.ended) throw new Error('The route did not reach an ending: ' + JSON.stringify(played));
const newRun = page.locator('button.rr-newrun:not(.inf)');
for (let i = 0; i < 120 && !(await newRun.isVisible()); i++) { await page.keyboard.press('Enter'); await page.waitForTimeout(2000); }   // a long show (Go Outside) takes Enter as its skip
await newRun.waitFor({ timeout: 5000 });
// A take may keep the world and its audio until its own cut. The recap must release both streams.
const endHeld = await page.evaluate(() => window.__s.el.paused && window.__panned.el.paused);
if (!endHeld) errors.push('a stream outlived the end of the run');
await page.screenshot({ path: join(shots, 'a6-credits.png') });
await wakeIsOnTop(page, 'the credits');
const seen = await page.evaluate(() => window.fuzz.life.endings);
if (!seen[played.ended.id]) errors.push('the ending was not recorded: ' + played.ended.id);

/* ---- New run keeps the lifetime ---- */
await newRun.click();
await page.waitForFunction(() => window.fuzz && window.fuzz.state.clock < 5000 && !window.fuzz.state.ended);
const life = await page.evaluate(() => ({ runs: window.fuzz.life.runs, endings: window.fuzz.life.endings, age: window.fuzz.life.age }));
if (life.runs !== 1 || !life.age) errors.push(`the lifetime did not survive New run: ${JSON.stringify(life)}`);
await ctx.close();

/* ---- every option on every fork plays a demo with no error ---- */
const ctx3 = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const p3 = await ctx3.newPage();
watch(p3);
await p3.goto(`${base}?buy=ticker&fresh=1`, { waitUntil: 'load' });   // the row before the theme pick
await p3.waitForFunction(() => !!window.fuzz);
await p3.evaluate(() => { window.fuzz.add(1e6); window.fuzz.ui.draw(true); });
await p3.waitForTimeout(250);
await p3.click('#goal .row');
await p3.waitForSelector('.opt');
await p3.waitForTimeout(1800);
await p3.screenshot({ path: join(shots, 'a7-themes.png') });
await ctx3.close();

/* ---- phone ---- */
const ctx2 = await browser.newContext({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true });
const phone = await ctx2.newPage();
watch(phone);
await phone.goto(`${base}?pace=1&fresh=1`, { waitUntil: 'load' });
await phone.waitForFunction(() => !!window.fuzz);
await phone.tap('#btn');
await phone.evaluate(() => window.fuzz.add(8));
await phone.waitForTimeout(300);
await phone.screenshot({ path: join(shots, 'b1-phone-start.png') });
await phone.goto(`${base}?buy=f2,stray,sound&fresh=1`, { waitUntil: 'load' });
await phone.waitForFunction(() => !!window.fuzz);
await phone.evaluate(() => window.fuzz.add(3000));
await phone.waitForTimeout(600);
await phone.screenshot({ path: join(shots, 'b2-phone-mid.png') });
const wide = await phone.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1);
if (wide) errors.push('the phone page scrolls sideways');
await wakeIsOnTop(phone, 'the phone');
await phone.goto(`${base}?buy=ticker&fresh=1`, { waitUntil: 'load' });   // the row before the theme pick
await phone.waitForFunction(() => !!window.fuzz);
await phone.evaluate(() => { window.fuzz.add(1e6); window.fuzz.ui.draw(true); });
await phone.waitForTimeout(250);
await phone.tap('#goal .row');   // a choice goes straight to its card
await phone.waitForSelector('.opt');
await phone.waitForTimeout(1200);
await phone.screenshot({ path: join(shots, 'b3-phone-fork.png') });
await ctx2.close();

/* ---- the hard rules with a whole route on at once ---- */
const pile = await runPileChecks(browser, base, { routes: process.env.PILE_ALL ? undefined : [route, ['ears', 'strict', 'scroll', 'premium', 'locked']], seconds: 12 });
errors.push(...pile.errors);

await browser.close();
console.log(JSON.stringify({ played, life, errors }, null, 1));
process.exit(errors.length ? 1 : 0);
