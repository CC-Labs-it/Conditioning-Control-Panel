// Drives the Bimbodoll ending headless (Firefox, muted): a filled page, the ending, clicks, two screenshots, and the
// absorb check: every swallowed element that has settled has its centre inside the bubble radius; Wake Up's group goes
// in last, never below scale 0.55, and stays clickable (elementFromPoint finds #wake) inside the bubble.
// Env: FUZZ_URL (default the 8817 host), FF_EXE, PW_DIR, W, H, TAG. Shots go to dev/shots/ (bimbodoll-v2-*).
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
const require = createRequire('C:/wt-fe/e2e/');
const { firefox } = require(process.env.PW_DIR || 'playwright');
const base = process.env.FUZZ_URL || 'http://127.0.0.1:8817/fuzz/index.html';
const FF = process.env.FF_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe';
const shots = fileURLToPath(new URL('./shots/', import.meta.url));
mkdirSync(shots, { recursive: true });
const W = Number(process.env.W || 1280), H = Number(process.env.H || 800);
const tag = process.env.TAG || `${W}x${H}`;
const browser = await firefox.launch({ headless: true, executablePath: FF, firefoxUserPrefs: { 'media.volume_scale': '0.0' } });
const page = await browser.newPage({ viewport: { width: W, height: H } });
const errors = [];
page.on('pageerror', (e) => errors.push(String(e)));
page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
const buy = 'colour,font,sound,rounded,softw,a_rain,v_frame,wheel,quiz,toy_box,mini_breakout,mini_slots,word_comet';
await page.goto(`${base}?buy=${buy}&theme=bimbo`, { waitUntil: 'load' });
await page.waitForTimeout(3000);
const btn = await page.$('#btn');
if (btn) await btn.click().catch(() => {});
await page.waitForTimeout(500);
await page.evaluate(() => window.fuzz.end('bi_end'));
const t0 = Date.now();
let shotMid = false, card = false, checks = 0, maxHeld = 0;
const outside = [], wake = { seen: false, last: true, minScale: Infinity, clickable: null };

/** In the page: the probe's view of what the bubble holds, measured. */
const measure = () => page.evaluate(() => {
  const p = window.__bimbodollProbe?.();
  if (!p) return null;
  const held = p.held.map((h) => {
    const r = h.el.getBoundingClientRect();
    const sc = getComputedStyle(h.el).scale;
    const s = sc && sc !== 'none' ? Math.min(...sc.split(' ').map(Number)) : 1;
    let hit = null;
    if (h.wake) { const w = document.getElementById('wake').getBoundingClientRect(); hit = document.elementFromPoint(w.x + w.width / 2, w.y + w.height / 2)?.id === 'wake'; }
    const gone = !h.el.isConnected || (r.width === 0 && r.height === 0);   // put away by its owner meanwhile: nothing to see
    const cs = getComputedStyle(h.el);
    const why = { inl: h.el.style.translate, ct: cs.translate, tf: cs.transform.slice(0, 40), pos: cs.position, anims: h.el.getAnimations().map((a) => Object.keys(a.effect?.getKeyframes?.()[0] || {}).filter((k) => !/^(offset|easing|composite|computedOffset)$/.test(k)).join('+')).join(','), rect: [Math.round(r.left), Math.round(r.top), Math.round(r.width), Math.round(r.height)] };
    return { id: h.id, wake: h.wake, age: h.age, gone, why, d: Math.hypot((r.left + r.right) / 2 - p.cx, (r.top + r.bottom) / 2 - p.cy), s, hit };
  });
  return { r: p.r, t: p.t, popped: p.popped, held };
});

while (Date.now() - t0 < 130000) {
  const m = await measure().catch(() => null);
  if (m && !m.popped) {
    checks++;
    maxHeld = Math.max(maxHeld, m.held.length);
    const others = m.held.filter((h) => !h.wake);
    for (const h of others) if (!h.gone && h.age > 1.8 && h.d > m.r + 2 && !outside.some((o) => o.id === h.id)) outside.push({ id: h.id, d: Math.round(h.d), r: Math.round(m.r), t: m.t.toFixed(1), why: h.why });
    const wk = m.held.find((h) => h.wake);
    if (wk) {
      if (!wake.seen) wake.last = others.every((h) => h.age >= wk.age);   // everything else was in before it
      wake.seen = true;
      if (wk.age > 0.6) { wake.minScale = Math.min(wake.minScale, wk.s); wake.clickable = wake.clickable !== false && wk.hit; }
    }
  }
  /* click the bubble, never Wake Up (that is the panic path) */
  const x = W * (0.2 + Math.random() * 0.6), y = H * (0.2 + Math.random() * 0.6);
  const onWake = await page.evaluate(([x, y]) => !!document.elementFromPoint(x, y)?.closest('#game-controls'), [x, y]).catch(() => true);
  if (!onWake) await page.mouse.click(x, y).catch(() => {});
  await page.waitForTimeout(200);
  const s = (Date.now() - t0) / 1000;
  if (!shotMid && s > 14) { await page.screenshot({ path: shots + `bimbodoll-v2-absorb-${tag}.png` }); shotMid = true; }
  const text = await page.evaluate(() => document.getElementById('l-ending')?.innerText || '');
  if (/BAMBI/.test(text)) { await page.waitForTimeout(1400); await page.screenshot({ path: shots + `bimbodoll-v2-card-${tag}.png` }); card = true; console.log('card at', s.toFixed(1), 's'); break; }
}
const wakeOk = await page.evaluate(() => { const r = document.getElementById('wake').getBoundingClientRect(); return document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)?.id === 'wake'; });
const absorbOk = checks > 5 && outside.length === 0;
const wakeInOk = wake.seen && wake.last && wake.minScale >= 0.549 && wake.clickable === true;
console.log(JSON.stringify({ shotMid, card, wakeOk, absorbOk, checks, maxHeld, outside: outside.slice(0, 10), wake, errors: errors.slice(0, 8) }, null, 1));
await browser.close();
process.exit(card && wakeOk && absorbOk && wakeInOk ? 0 : 1);
