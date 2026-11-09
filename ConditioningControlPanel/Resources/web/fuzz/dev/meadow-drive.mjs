// Drives dev/meadow.html in muted headless Chrome at desktop (1280x720) and phone (390x844 touch, lite): frames advance,
// the mouse looks, W walks, a touch hold walks, a touch drag looks, lock + autopilot keep it moving, stop frees it.
// Writes assets/outside/meadow.jpg (1280x720, q 0.8) from the desktop start view unless NO_JPG=1.
// Env: PORT (default 8853), SHOTS (dir for screenshots, default dev/shots). Exits 1 on a failed check or page error.
import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
const require = createRequire('C:/wt-fe/e2e/');
const { chromium } = require('playwright');
const here = (p) => fileURLToPath(new URL(p, import.meta.url));
const PORT = Number(process.env.PORT || 8853), shots = process.env.SHOTS || here('./shots/');
mkdirSync(shots, { recursive: true });
const server = spawn(process.execPath, [here('./serve.mjs'), String(PORT)], { stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 600));
const browser = await chromium.launch({ channel: 'chrome', headless: true, args: ['--mute-audio', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
let failed = 0;
const check = (ok, msg) => { console.log(`${ok ? 'ok  ' : 'FAIL'} ${msg}`); if (!ok) failed += 1; };
const wait = (ms) => new Promise((r) => setTimeout(r, ms));

async function run({ name, w, h, touch, query }) {
  const ctx = await browser.newContext({ viewport: { width: w, height: h }, hasTouch: !!touch, isMobile: !!touch, deviceScaleFactor: 1 });
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(String(e)));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
  await page.goto(`http://127.0.0.1:${PORT}/fuzz/dev/meadow.html${query}`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__meadow || window.__meadowError, null, { timeout: 30000 });
  const err = await page.evaluate(() => window.__meadowError);
  check(!err, `${name}: meadow created ${err || ''}`);
  if (err) { await ctx.close(); return; }
  await page.addStyleTag({ content: '#bar,#info{display:none!important}' });
  const dbg = () => page.evaluate(() => window.__meadow.debug());
  await wait(1200);
  const a = await dbg();
  await wait(800);
  const b = await dbg();
  const fps = (b.frames - a.frames) / 0.8;
  check(b.frames > a.frames + 5, `${name}: frames advance (${fps.toFixed(0)} fps headless, ${b.calls} draw calls, placeholder [${b.placeholder}])`);
  await page.screenshot({ path: `${shots}meadow-${name}-start.png` });
  if (name === 'desktop' && process.env.NO_JPG !== '1') {
    const url = await page.evaluate(() => window.__meadow.still(0.8));
    writeFileSync(here('../assets/outside/meadow.jpg'), Buffer.from(url.split(',')[1], 'base64'));
    console.log('     wrote assets/outside/meadow.jpg');
  }
  const s0 = await page.evaluate(() => window.__meadow.still(0.5));
  if (!touch) {
    await page.mouse.move(w / 2, h / 2);
    await page.mouse.move(w / 2 + 160, h / 2 + 30, { steps: 8 });
    const c = await dbg();
    check(c.yaw < b.yaw - 0.2, `${name}: mouse move looks right (yaw ${b.yaw.toFixed(2)} -> ${c.yaw.toFixed(2)})`);
    await page.keyboard.down('KeyW');
    await wait(900);
    await page.keyboard.up('KeyW');
    const d = await dbg();
    check(Math.hypot(d.x - c.x, d.z - c.z) > 1.2, `${name}: W walks (${Math.hypot(d.x - c.x, d.z - c.z).toFixed(2)} blocks)`);
  } else {
    const cdp = await ctx.newCDPSession(page);
    const touchAt = (type, x, y) => cdp.send('Input.dispatchTouchEvent', { type, touchPoints: type === 'touchEnd' ? [] : [{ x, y, id: 1 }] });
    const c = await dbg();
    await touchAt('touchStart', w / 2, h * 0.7);
    await wait(1100);
    await touchAt('touchEnd');
    const d = await dbg();
    check(Math.hypot(d.x - c.x, d.z - c.z) > 1, `${name}: a touch hold walks (${Math.hypot(d.x - c.x, d.z - c.z).toFixed(2)} blocks)`);
    await touchAt('touchStart', w * 0.3, h / 2);
    for (let k = 1; k <= 8; k += 1) { await touchAt('touchMove', w * 0.3 + k * 18, h / 2); await wait(16); }
    await touchAt('touchEnd');
    const e = await dbg();
    check(Math.abs(e.yaw - d.yaw) > 0.3, `${name}: a touch drag looks (yaw ${d.yaw.toFixed(2)} -> ${e.yaw.toFixed(2)})`);
  }
  await wait(300);
  const s1 = await page.evaluate(() => window.__meadow.still(0.5));
  check(s0 !== s1, `${name}: the picture changed after looking and walking`);
  await page.screenshot({ path: `${shots}meadow-${name}-walked.png` });
  await page.evaluate(() => { window.__meadow.lockControls(); window.__meadow.autopilot(true); });
  const f = await dbg();
  await page.keyboard.down('KeyS');
  await wait(1500);
  await page.keyboard.up('KeyS');
  const g = await dbg();
  check(!g.live && g.auto, `${name}: locked and drifting`);
  check(Math.abs(g.pitch - 0.06) < 0.03, `${name}: eased to a calm view (pitch ${g.pitch.toFixed(3)})`);
  check(Math.hypot(g.x - f.x, g.z - f.z) > 0.5 && (g.x - f.x) * -Math.sin(f.yaw) + (g.z - f.z) * -Math.cos(f.yaw) > 0,
    `${name}: autopilot moves forward and S is ignored (${Math.hypot(g.x - f.x, g.z - f.z).toFixed(2)})`);
  await page.screenshot({ path: `${shots}meadow-${name}-auto.png` });
  await page.evaluate(() => window.__meadow.stop());
  check(await page.evaluate(() => !document.querySelector('canvas')), `${name}: stop removes the canvas`);
  check(!errors.length, `${name}: no page errors ${errors.slice(0, 3).join(' | ')}`);
  await ctx.close();
}

try {
  await run({ name: 'desktop', w: 1280, h: 720, query: '' });
  await run({ name: 'phone', w: 390, h: 844, touch: true, query: '?lite=1' });
} finally {
  await browser.close();
  server.kill();
}
console.log(failed ? `${failed} failed` : 'all passed');
process.exit(failed ? 1 : 0);
