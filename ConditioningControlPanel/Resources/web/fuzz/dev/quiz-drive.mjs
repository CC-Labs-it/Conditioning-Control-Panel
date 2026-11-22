// dev/quiz-drive.mjs - every pop-up trick by real input, wide and phone: the wanted answer, then a wrong one through whatever
// the trick puts in the way. Fails if a card will not close, a wrong answer pays nothing, or the right one does not pay far more.
//   node dev/serve.mjs 8804 &   FUZZ_URL=http://localhost:8804/fuzz/index.html node dev/quiz-drive.mjs [outDir]
import { createRequire } from 'node:module';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { join, resolve } from 'node:path';

const require = createRequire('C:/wt-fe/e2e/');
const { firefox } = require(process.env.PW_DIR || 'playwright');
const base = process.env.FUZZ_URL || 'http://localhost:8804/fuzz/index.html';
const out = resolve(process.argv[2] || 'dev/shots/quiz');
mkdirSync(out, { recursive: true });
const FF = process.env.FF_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe';
const browser = await firefox.launch({ headless: true, executablePath: FF, firefoxUserPrefs: { 'media.volume_scale': '0.0' } });
const { TRICKS, QUESTIONS } = await import(pathToFileURL(resolve('data/content/questions.js')).href);

const errors = [];
const sleep = (page, ms) => page.waitForTimeout(ms);
const isOpen = (page) => page.evaluate(() => !!document.querySelector('.xq:not(.xq-done)'));
const closed = async (page, ms = 2500) => { for (let t = 0; t < ms; t += 100) { if (!(await page.locator('.xq').count())) return true; await sleep(page, 100); } return false; };

/** A real press wherever the thing is right now. Moving targets get several goes. */
async function press(page, loc, holdMs = 0) {
  const b = await loc.boundingBox().catch(() => null);
  if (!b) return false;
  await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2);
  await page.mouse.down();
  if (holdMs) await sleep(page, holdMs);
  await page.mouse.up();
  return true;
}
const answerBtn = (page, label) => page.locator('.xq button', { hasText: new RegExp(`^${label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`) });

async function ask(page, trick) {
  await page.evaluate(() => { const c = window.fuzz.engine.scopes.get('quiz'); c.ret.close?.(); });
  await closed(page, 1500);
  await page.evaluate((t) => { window.fuzz.engine.scopes.get('quiz').ret.ask(t); }, trick);
  await sleep(page, 320);
  const text = await page.locator('.xq-q').textContent();
  const q = QUESTIONS.find((x) => x.text === text && x.trick === trick);
  if (!q) throw new Error(`${trick}: no question reads ${text}`);
  return { q, yes: q.answers.find((a) => a.tone === 'yes'), no: q.answers.find((a) => a.tone === 'no') };
}
const earned = (page) => page.evaluate(() => window.fuzz.state.sources.quiz || 0);

/** The wanted answer, by whatever the trick asks of it. */
async function right(page, trick, { yes, q }) {
  if (trick === 'lean-slider') {
    const track = await page.locator('.xq-slide').boundingBox();
    await page.mouse.move(track.x + track.width - 2, track.y + 3); await page.mouse.down(); await page.mouse.up();
    await press(page, page.locator('.xq button', { hasText: 'That is my answer' }));
    return;
  }
  if (trick === 'verify-checkbox') { await press(page, page.locator('.xq-z div[style*="width: 44px"]').first(), 2500); return; }
  if (trick === 'verify-transcribe') {
    for (const c of q.phrase.replace(/ /g, '')) { await press(page, page.locator('.xq-pad button', { hasText: new RegExp(`^${c}$`) })); await sleep(page, 30); }
    return;   // it answers itself
  }
  if (trick === 'verify-grid') { await press(page, answerBtn(page, yes.label)); await sleep(page, 120); if (await isOpen(page)) await press(page, answerBtn(page, yes.label)); return; }
  for (let i = 0; i < 10 && await isOpen(page); i++) {
    const c = await answerBtn(page, yes.label).count();
    for (let k = 0; k < c && await isOpen(page); k++) { await press(page, answerBtn(page, yes.label).nth(k)); await sleep(page, 90); }
    await sleep(page, 150);
  }
}

/** A wrong answer, through the friction. */
async function wrong(page, trick, { no }) {
  if (trick === 'lean-slider') {
    const track = await page.locator('.xq-slide').boundingBox();
    await page.mouse.move(track.x + 2, track.y + 3); await page.mouse.down(); await page.mouse.up();
    await press(page, page.locator('.xq button', { hasText: 'That is my answer' }));
    return;
  }
  if (trick === 'late-bloom') { for (let t = 0; t < 14000 && !(await answerBtn(page, no.label).count()); t += 250) await sleep(page, 250); }
  if (trick === 'drag-reveal') { for (let i = 0; i < 8; i++) { const s = page.locator('.xq-stick'); if (!(await s.count())) break; await press(page, s.last()); await sleep(page, 280); } }
  if (trick === 'hold-refuse') { await press(page, answerBtn(page, no.label), 3300); return; }
  for (let i = 0; i < 16 && await isOpen(page); i++) {
    const c = await answerBtn(page, no.label).count();
    for (let k = c - 1; k >= 0 && await isOpen(page); k--) { await press(page, answerBtn(page, no.label).nth(k)); await sleep(page, 70); }
    await sleep(page, 200);
  }
}

const sheet = [];
for (const [w, h, name] of [[1280, 800, 'wide'], [390, 800, 'phone']]) {
  const ctx = await browser.newContext({ viewport: { width: w, height: h } });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => errors.push(`${name}: ${e.message}`));
  await page.goto(`${base}?fresh=1&buy=quiz,fpsv`, { waitUntil: 'load' });
  await page.waitForFunction(() => !!window.fuzz?.engine.scopes.get('quiz'));
  await sleep(page, 900);
  for (const trick of Object.keys(TRICKS).filter((k) => !process.env.TRICK || process.env.TRICK === k)) {
    /* Same question pool both ways, so a fair ratio. */
    const a = await ask(page, trick);
    await sleep(page, trick === 'crowd' ? 1500 : 500);
    const file = join(out, `${name}-${trick}.png`);
    const box = await page.locator('.xq').boundingBox();
    await page.locator('.xq').screenshot({ path: file });
    const spot = `${Math.round(box.x)},${Math.round(box.y + box.height)}`;
    sheet.push({ name, trick, file, spot });
    const m0 = await earned(page);
    await right(page, trick, a);
    if (!(await closed(page, 5000))) errors.push(`${name}/${trick}: right answer did not close`);
    const m1 = await earned(page);
    const b = await ask(page, trick);
    if (trick !== 'mono') await wrong(page, trick, b); else await right(page, trick, b);
    let shut = await closed(page, 2500);
    if (!shut && trick === 'melt-away') { await right(page, trick, b); shut = await closed(page, 2500); }
    if (!shut) errors.push(`${name}/${trick}: wrong answer did not close`);
    const m2 = await earned(page);
    const payR = m1 - m0, payW = m2 - m1;
    console.log(`${name.padEnd(5)} ${trick.padEnd(18)} right ${payR.toFixed(0).padStart(7)}  wrong ${payW.toFixed(0).padStart(7)}  at ${spot}`);
    if (payR <= 0) errors.push(`${name}/${trick}: the right answer paid nothing`);
    if (payW <= 0) errors.push(`${name}/${trick}: a wrong answer paid nothing`);
    if (trick !== 'mono' && trick !== 'melt-away' && payR < payW * 3) errors.push(`${name}/${trick}: right ${payR} is not far above wrong ${payW}`);
  }
  /* Only Yes: four ways to say yes on an ordinary question, and any of them pays as right. */
  await page.evaluate(() => { const f = window.fuzz; f.grant('onlyyes'); });
  await sleep(page, 900);
  const o = await ask(page, 'none');
  const labels = await page.locator('.xq-z button').allTextContents();
  if (new Set(labels).size !== 4) errors.push(`${name}: Only Yes shows ${labels.join(' / ')}`);
  void o;
  await page.locator('.xq').screenshot({ path: join(out, `${name}-onlyyes.png`) });
  sheet.push({ name, trick: 'onlyyes on none', file: join(out, `${name}-onlyyes.png`), spot: '' });
  await press(page, page.locator('.xq-z button').first());
  if (!(await closed(page))) errors.push(`${name}: Only Yes did not close`);

  /* Wake Up goes over a card and comes back to it. */
  await ask(page, 'none');
  await page.locator('#wake').click();
  if (!(await page.locator('#pause').isVisible())) errors.push(`${name}: Wake Up did not pause over a card`);
  await page.locator('#resume').click();
  await press(page, page.locator('.xq-z button').first());
  if (!(await closed(page))) errors.push(`${name}: a card did not close after a pause`);

  /* Left alone it goes away by itself. */
  if (name === 'wide' && !process.env.TRICK) {
    await ask(page, 'none');
    await sleep(page, 31500);
    if (await page.locator('.xq').count()) errors.push('a card nobody answered did not leave');
  }
  /* A whole-page look with a card up. */
  await ask(page, 'none');
  await page.screenshot({ path: join(out, `${name}-page.png`) });
  await ctx.close();
}

const rows = sheet.map((s) => `<figure><img src="${pathToFileURL(s.file).href}"><figcaption>${s.name} ${s.trick}</figcaption></figure>`).join('');
const html = join(out, 'sheet.html');
writeFileSync(html, `<!doctype html><body style="margin:0;background:#888;font:12px sans-serif;display:flex;flex-wrap:wrap;gap:6px;padding:6px;align-items:flex-start;">${rows}<style>figure{margin:0}figcaption{color:#fff}img{display:block;max-width:330px}</style>`);
const sheetPage = await (await browser.newContext({ viewport: { width: 1400, height: 900 } })).newPage();
await sheetPage.goto(pathToFileURL(html).href);
await sheetPage.screenshot({ path: join(out, 'sheet.png'), fullPage: true });
await browser.close();
void readFileSync;
if (errors.length) { console.log(`ERRORS\n${errors.join('\n')}`); process.exitCode = 1; } else console.log('ok');
