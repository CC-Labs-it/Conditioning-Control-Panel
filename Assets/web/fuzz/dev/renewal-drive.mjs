// dev/renewal-drive.mjs - the Renewal menu driver (brief handoff-1121/TC-ASCENSION-BRIEF.md, lane M).
// Mid-run: pause card -> Renewal (Depth 0, empty line) -> Open the Sky (fogged, browse) -> Escape covers it with the
// pause card -> Renewal uncovers it -> Back to the run. Then a seeded Depth 4 record: the sheet's lane groups, a star
// lit in the Sky for the next run (later bag, not owned, an event fired), the Next run sheet keeps and starts nothing,
// the sheet's From next run group, and Resume closes everything. Desktop 1366x800, then 390x844 touch reduced motion.
// Muted Firefox, its own dev server on 8841. SHOTS=<dir> writes screenshots there. Exits 1 on a failed check.
import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const require = createRequire('C:/wt-fe/e2e/');
const { firefox } = require(process.env.PW_DIR || 'playwright');
const PORT = 8841;
const server = spawn(process.execPath, [fileURLToPath(new URL('./serve.mjs', import.meta.url)), String(PORT)], { stdio: 'ignore' });
console.log('server pid', server.pid);
await new Promise((r) => setTimeout(r, 700));
const url = `http://127.0.0.1:${PORT}/fuzz/index.html?fresh=1&pictures=none`;
const shots = process.env.SHOTS || '';
if (shots) mkdirSync(shots, { recursive: true });
const browser = await firefox.launch({ headless: true, executablePath: process.env.FF_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe', firefoxUserPrefs: { 'media.volume_scale': '0.0' } });
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
let failed = 0;
const check = (name, ok, info = '') => { if (!ok) failed += 1; console.log(`${ok ? 'ok  ' : 'FAIL'} ${name}${info ? ' - ' + info : ''}`); };

try {
  for (const [name, vp] of [['desk', { width: 1366, height: 800 }], ['phone', { width: 390, height: 844 }]]) {
    console.log(`-- ${name} ${vp.width}x${vp.height}`);
    const ctx = await browser.newContext({ viewport: vp, hasTouch: name === 'phone', reducedMotion: name === 'phone' ? 'reduce' : 'no-preference' });
    const page = await ctx.newPage();
    const errs = [];
    page.on('pageerror', (e) => errs.push(e.message));
    const shot = async (n) => { if (shots) await page.screenshot({ path: `${shots}/renewal-${name}-${n}.png` }); };
    const st = () => page.evaluate(() => ({
      paused: window.fuzz.engine.paused, pause: !document.getElementById('pause').hidden,
      sheet: !!document.getElementById('renewal-menu') && !document.getElementById('renewal-menu').hidden,
      sky: !!document.getElementById('ps-sky'), browse: !!document.querySelector('#ps-sky.browse'),
      depth: document.querySelector('#ps-sky [data-k="depth"]')?.textContent ?? null,
    }));

    await page.goto(url);
    await page.waitForFunction(() => window.fuzz?.engine, null, { timeout: 20000 });
    await wait(2500);
    await page.evaluate(() => { window.__changed = 0; document.addEventListener('tc:prestige-changed', () => { window.__changed += 1; }); });

    /* 1. Depth 0 */
    await page.keyboard.press('Escape');
    await wait(300);
    check('pause card has the Renewal door', await page.isVisible('#renewal-door'));
    await shot('1-pause');
    await page.click('#renewal-door');
    await wait(400);
    const s0 = await page.evaluate(() => ({ depth: document.querySelector('.rn-depth b')?.textContent, empty: !!document.querySelector('.rn-empty') }));
    check('Depth 0 sheet opens with the empty line', s0.depth === '0' && s0.empty, JSON.stringify(s0));
    await shot('2-sheet-depth0');
    await page.click('.rn [data-act="sky"]');
    await wait(1200);
    let s = await st();
    check('Open the Sky: browse Sky up, pause card hidden, still paused', s.browse && !s.pause && s.paused && !s.sheet, JSON.stringify(s));
    check('browse Sky shows no Sign and start, has Back to the run', await page.evaluate(() => !!document.querySelector('#ps-sky [data-act="leave"]') && !/Sign and start/.test(document.getElementById('ps-sky').innerHTML)));
    await shot('3-sky-fog');
    await page.keyboard.press('Escape');
    await wait(300);
    s = await st();
    check('Escape over the Sky: pause card above it, the Sky stays', s.pause && s.browse && s.paused, JSON.stringify(s));
    await page.click('#renewal-door');
    await wait(300);
    s = await st();
    check('Renewal again uncovers the Sky', !s.pause && s.browse, JSON.stringify(s));
    await page.click('#ps-sky [data-act="leave"]');
    await wait(400);
    s = await st();
    check('Back to the run: Sky gone, the pause card', !s.sky && !s.sheet && s.pause && s.paused, JSON.stringify(s));

    /* 2. a Depth 4 record with stars, lit endings */
    await page.evaluate(async () => {
      const P = await import('/fuzz/sim/prestige.js');
      const p = P.createPrestige();
      p.depth = 4; p.dust = 1400;
      for (const id of ['pay_1', 'pay_2', 'pay_3', 'head_1', 'head_2', 'idle_1', 'idle_2', 'active_1', 'active_2', 'trig_1', 'trig_2', 'luck_1', 'ghost_1', 'ghost_2', 'fine_1', 'fine_2']) p.own[id] = 1;
      p.lit = { drop: 1, bi_end: 1 };
      p.last = { route: {}, toys: ['wheel', 'toy_box'], ms: 5_400_000 };
      window.fuzz.life.prestige = p;
    });
    await page.click('#renewal-door');
    await wait(400);
    const s4 = await page.evaluate(() => ({ depth: document.querySelector('.rn-depth b')?.textContent, groups: [...document.querySelectorAll('.rn-group h3')].map((h) => h.firstChild.textContent.trim()), rows: document.querySelectorAll('.rn-row').length }));
    check('Depth 4 sheet: hero number, lane groups, rows', s4.depth === '4' && s4.groups.length >= 8 && s4.rows === 18, JSON.stringify(s4));
    await shot('4-sheet-depth4');
    await page.click('.rn [data-act="sky"]');
    await wait(name === 'phone' ? 800 : 2600);
    s = await st();
    check('browse Sky at Depth 4, big number reads 4', s.browse && s.depth === '4', JSON.stringify(s));
    const before = await page.evaluate(() => ({ dust: window.fuzz.life.prestige.dust, ch: window.__changed }));
    await page.click('#ps-sky .ps-arm >> nth=0');   // the Pay lane: its next star (pay_4) opens in the card
    await wait(900);
    check('the card offers Light for next run', (await page.textContent('#ps-sky .ps-card [data-act="buy"]').catch(() => '')) === 'Light for next run');
    await page.click('#ps-sky .ps-card [data-act="buy"]');
    await wait(1500);
    const after = await page.evaluate(() => { const p = window.fuzz.life.prestige; return { later: !!p.later?.own?.pay_4, own: !!p.own.pay_4, dust: p.dust, ch: window.__changed, foot: document.querySelector('#ps-sky .ps-card .lit')?.textContent || '' }; });
    check('buy goes into the next-run bag, not owned, Stardust paid now', after.later && !after.own && after.dust < before.dust, JSON.stringify(after));
    check('tc:prestige-changed after the buy', after.ch > before.ch);
    check('the card says Lit from next run', after.foot === 'Lit from next run', after.foot);
    await shot('5-sky-browse-bought');
    await page.click('#ps-sky [data-act="next"]');
    await wait(400);
    check('Next run sheet keeps, no Sign and start', await page.evaluate(() => { const b = document.querySelector('#ps-sky .ps-sheet [data-act="sign"]'); return b?.textContent === 'Keep for next run'; }));
    await page.click('#ps-sky .ps-sheet [data-act="sign"]');
    await wait(400);
    s = await st();
    check('Keep: the sheet closes, the Sky stays, no reload', s.browse && await page.evaluate(() => !!window.__changed && document.querySelector('#ps-sky .ps-over').hidden), JSON.stringify(s));
    const chBack = await page.evaluate(() => window.__changed);
    await page.click('#ps-sky [data-act="leave"]');
    await wait(300);
    await page.click('#renewal-door');
    await wait(400);
    const sn = await page.evaluate(() => ({ next: document.querySelector('.rn-next')?.textContent || '', ch: window.__changed }));
    check('sheet lists the star under From next run', /From next run/.test(sn.next) && sn.next.length > 20, sn.next.slice(0, 80));
    check('tc:prestige-changed when the Sky closes', sn.ch > chBack);
    await shot('6-sheet-next');
    await page.keyboard.press('Escape');   // the sheet over the pause card: Escape is the pause card's resume
    await wait(400);
    s = await st();
    check('Resume closes the sheet and the Sky with the moment', !s.paused && !s.sheet && !s.sky && !s.pause, JSON.stringify(s));
    check('no page errors', !errs.length, errs.join(' | '));
    await ctx.close();
  }
} finally {
  await browser.close();
  server.kill();
}
console.log(failed ? `${failed} failed` : 'all ok');
process.exit(failed ? 1 : 0);
