// dev/screens-drive.mjs - screens by real input: a real mouse at 1280, a real finger at 390. Needs dev/serve.mjs running.
//   FUZZ_URL=http://localhost:8803/fuzz/index.html SCREENS_MEDIA=<folder> SCREENS_SHOTS=<folder> node dev/screens-drive.mjs
// SCREENS_MEDIA holds small generated test files, never in the repo: one.png two.jpg loop.gif clip.webm clip.mp4 note.txt.
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(new URL('.', import.meta.url));
const shots = process.env.SCREENS_SHOTS || join(here, 'shots');
const media = process.env.SCREENS_MEDIA;
if (!media) throw new Error('set SCREENS_MEDIA');
mkdirSync(shots, { recursive: true });
/* The in-process build: Playwright's API has only a tap, and a real finger needs Firefox's own touch events. */
const require = createRequire('C:/wt-fe/e2e/');
const pw = require('playwright-core/lib/coreBundle').inprocess.createInProcessPlaywright();
const { firefox } = pw;
const base = process.env.FUZZ_URL || 'http://localhost:8803/fuzz/index.html';
const FF = process.env.FF_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe';

const browser = await firefox.launch({ headless: true, executablePath: FF, firefoxUserPrefs: { 'media.volume_scale': '0.0' } });
const errors = [];
const seen = [];
process.on('exit', () => console.log(seen.join('\n')));
const check = (ok, what) => { seen.push(`${ok ? 'ok  ' : 'FAIL'} ${what}`); if (!ok) errors.push(what); };
const watch = (page) => {
  page.on('pageerror', (e) => errors.push(`pageerror: ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error' && !/data\/ads\.js/.test(m.text())) errors.push(`console: ${m.text()}`); });
};
const open = async (ctx, q) => {
  const page = await ctx.newPage();
  watch(page);
  await page.goto(`${base}?${q}`, { waitUntil: 'load' });
  await page.waitForFunction(() => !!window.fuzz);
  await page.waitForTimeout(q.includes('buy=') ? 4200 : 500);   // ?buy picks the first theme, and its Sink moment covers the page at first
  return page;
};
const frames = (page) => page.evaluate(() => [...document.querySelectorAll('#l-screens > .scr')].map((e) => { const r = e.getBoundingClientRect(); return { x: Math.round(r.left), y: Math.round(r.top), w: Math.round(r.width), h: Math.round(r.height) }; }));
const shot = (page, name) => page.screenshot({ path: join(shots, `${name}.png`) });
const f = (...names) => names.map((n) => join(media, n));

async function mouseDrag(page, from, to) {
  await page.mouse.move(from.x, from.y);
  await page.mouse.down();
  await page.mouse.move((from.x + to.x) / 2, (from.y + to.y) / 2, { steps: 4 });
  await page.mouse.move(to.x, to.y, { steps: 4 });
  await page.mouse.up();
  await page.waitForTimeout(250);
}

/** A real finger: Firefox's own touch events through Juggler. Playwright's API has only tap. */
function finger(page) {
  const send = (type, pts) => pw._connection.toImpl(page).delegate._session.send('Page.dispatchTouchEvent', { type, touchPoints: pts.map((p) => ({ x: p.x, y: p.y, radiusX: 8, radiusY: 8, rotationAngle: 0, force: 1 })), modifiers: 0 });
  return {
    async drag(from, to, steps = 6) {
      await send('touchStart', [from]);
      for (let i = 1; i <= steps; i++) {
        await send('touchMove', [{ x: from.x + ((to.x - from.x) * i) / steps, y: from.y + ((to.y - from.y) * i) / steps }]);
        await page.waitForTimeout(16);
      }
      await send('touchEnd', [to]);
      await page.waitForTimeout(250);
    },
  };
}

/* ---- desktop: a real mouse ---- */
{
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await open(ctx, 'buy=v_frame,v_frame2&fresh=1');
  await shot(page, 'scr-01-desk-test-cards');
  const [a0, b0] = await frames(page);
  check(a0 && b0, 'two frames');
  const card = await page.locator('#l-screens .scr-card button').count();
  check(card === 2, `a test card with its button on each frame (${card})`);
  const box = await page.locator('#btn').boundingBox();
  const shop = await page.locator('#shop').boundingBox();
  const off = (r, o) => r.x + r.w <= o.x || r.x >= o.x + o.width || r.y + r.h <= o.y || r.y >= o.y + o.height;
  check(off(a0, box) && off(b0, box) && off(a0, shop) && off(b0, shop), 'the default spots are off the button and the shop');

  const money0 = await page.evaluate(() => window.fuzz.state.fuzz);
  await page.waitForTimeout(1500);
  const money1 = await page.evaluate(() => window.fuzz.state.fuzz);
  check(money1 > money0, `a test card still pays (${money0} -> ${money1})`);

  /* Dragging is not a tap, and the button stays on top of a screen dragged under it. */
  const taps0 = await page.evaluate(() => window.fuzz.state.taps);
  const centre = { x: box.x + box.width / 2, y: box.y + box.height / 2 };
  await mouseDrag(page, { x: a0.x + a0.w / 2, y: a0.y + 12 }, { x: centre.x, y: centre.y - 40 });
  const a1 = (await frames(page))[0];
  check(Math.abs(a1.x + a1.w / 2 - centre.x) < 4, `the frame followed the mouse (${JSON.stringify(a1)})`);
  check(await page.evaluate(() => window.fuzz.state.taps) === taps0, 'a drag across the button is not a tap');
  await shot(page, 'scr-02-desk-under-button');
  const onTop = await page.evaluate(({ x, y }) => document.elementFromPoint(x, y)?.id, centre);
  check(onTop === 'btn', `the button is above the screen (${onTop})`);
  await page.mouse.click(centre.x, centre.y);
  check(await page.evaluate(() => window.fuzz.state.taps) === taps0 + 1, 'the button still takes a press there');
  const wake = await page.evaluate(() => { const r = document.getElementById('wake').getBoundingClientRect(); return document.elementFromPoint(r.left + 4, r.top + 4)?.id; });
  check(wake === 'wake', 'Wake Up on top');

  /* Press through the empty stage to a screen under it, and pull it to Wake Up's corner: Wake Up still wins. */
  await mouseDrag(page, { x: a1.x + 12, y: a1.y + a1.h - 12 }, { x: 1270, y: 6 });
  const a2 = (await frames(page))[0];
  check(a2.x + a2.w <= 1280 && a2.y >= 0, `clamped to the window (${JSON.stringify(a2)})`);
  const wakeHit = await page.evaluate(() => { const r = document.getElementById('wake').getBoundingClientRect(); return document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2)?.id; });
  check(wakeHit === 'wake', 'a screen in the corner leaves Wake Up pressable');
  await page.click('#wake');
  await page.click('#resume');

  /* Deep into the shop: it never stays all the way behind it. */
  await mouseDrag(page, { x: a2.x + a2.w / 2, y: a2.y + a2.h - 30 }, { x: shop.x + shop.width / 2, y: 700 });
  const a3 = (await frames(page))[0];
  const stands = Math.max(shop.y - a3.y, shop.x - a3.x, a3.x + a3.w - (shop.x + shop.width));
  check(stands >= 40, `something stands clear of the shop (${stands}, ${JSON.stringify(a3)})`);
  await shot(page, 'scr-03-desk-shop-clamp');
  const tiles = await page.evaluate(() => { const r = document.querySelector('#rows .row, #goal .row').getBoundingClientRect(); return document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2)?.closest('.row') != null; });
  check(tiles, 'a tile stays pressable over a screen');

  /* Add your files: the real picker from the test card, a non-file first. */
  await mouseDrag(page, { x: a3.x + 8, y: a3.y + 8 }, { x: 130, y: 200 });   // its top left corner: the button sits over the middle
  const a4 = (await frames(page))[0];
  check(a4.x < 200, `a screen pulled back out of the shop (${JSON.stringify(a4)})`);
  let chooser = page.waitForEvent('filechooser', { timeout: 4000 }).catch(() => null);
  await page.locator('#l-screens .scr-card button').first().click();
  let fc = await chooser;
  check(!!fc, 'Add your files opens the picker');
  await fc?.setFiles(f('note.txt'));
  await page.waitForTimeout(200);
  check(await page.evaluate(() => window.fuzz.engine.pictures.source) === 'none', 'a file that is none of those changes nothing');
  chooser = page.waitForEvent('filechooser', { timeout: 4000 }).catch(() => null);
  await page.locator('#l-screens .scr-card button').first().click();
  fc = await chooser;
  await fc?.setFiles(f('one.png', 'two.jpg', 'loop.gif', 'clip.webm', 'clip.mp4'));
  await page.waitForTimeout(900);
  const counts = await page.evaluate(() => ({ source: window.fuzz.engine.pictures.source, counts: window.fuzz.engine.pictures.ownCounts }));
  check(counts.source === 'own' && counts.counts.picture === 2 && counts.counts.gif === 1 && counts.counts.clip === 2, `own files sorted: ${JSON.stringify(counts)}`);
  check(await page.locator('#l-screens .scr-card').count() === 0, 'the test cards gave way');
  const pics = await page.evaluate(() => [...document.querySelectorAll('#l-screens .scr-slot > img')].map((i) => ({ src: i.src.slice(0, 5), ok: i.naturalWidth > 0 })));
  check(pics.length >= 2 && pics.every((p) => p.src === 'blob:' && p.ok), `the frames hold real pictures: ${JSON.stringify(pics)}`);
  await page.waitForTimeout(400);
  await shot(page, 'scr-04-desk-own-pictures');

  /* A real gif and a real clip, each its own unlock. */
  await page.evaluate(() => { window.fuzz.grant('v_frame3'); window.fuzz.grant('v_gif'); window.fuzz.grant('v_fav'); window.fuzz.grant('v_video'); });
  await page.waitForTimeout(1800);
  const live = await page.evaluate(() => {
    const gif = [...document.querySelectorAll('#l-screens img[data-kind=gif]')][0];
    const v = document.querySelector('#l-screens video');
    return { gif: gif && gif.naturalWidth > 0 && gif.src.startsWith('blob:'), video: v && { paused: v.paused, muted: v.muted, loop: v.loop, t: v.currentTime, w: v.videoWidth, ready: v.readyState, src: v.src.slice(0, 5), err: v.error?.message } };
  });
  check(live.gif, 'Corner GIF holds a real gif');
  check(live.video && !live.video.paused && live.video.muted && live.video.loop && live.video.w > 0 && live.video.src === 'blob:', `Mandatory Video plays a real muted looping clip: ${JSON.stringify(live.video)}`);
  await shot(page, 'scr-05-desk-all-media');
  const t0 = await page.evaluate(() => document.querySelector('#l-screens video')?.currentTime);
  await page.waitForTimeout(500);
  const t1 = await page.evaluate(() => document.querySelector('#l-screens video')?.currentTime);
  check(t1 !== t0, `the clip moves (${t0} -> ${t1})`);

  /* The video drags too, with no close button; its fake one does not start a drag. */
  const vbox = await page.evaluate(() => { const s = document.querySelector('#l-screens video').closest('.scr').getBoundingClientRect(); return { x: s.left, y: s.top, w: s.width, h: s.height }; });
  await mouseDrag(page, { x: vbox.x + 20, y: vbox.y + vbox.h - 20 }, { x: vbox.x + 20 + 40, y: vbox.y + vbox.h - 20 - 60 });
  const vbox2 = await page.evaluate(() => { const s = document.querySelector('#l-screens video').closest('.scr').getBoundingClientRect(); return { x: s.left, y: s.top }; });
  check(Math.abs(vbox2.x - vbox.x - 40) < 3 && Math.abs(vbox2.y - vbox.y + 60) < 3, `Mandatory Video drags (${JSON.stringify(vbox2)})`);

  /* Wake Up holds everything, Resume brings it back: the clip stops, the gif goes still. */
  await page.evaluate(() => { window.__gifVideo = document.querySelector('#l-screens video'); });
  await page.click('#wake');
  await page.waitForTimeout(150);
  const heldShot = await page.evaluate(() => ({ v: window.__gifVideo?.paused, conn: window.__gifVideo?.isConnected, still: !!document.querySelector('#l-screens .scr-slot > canvas'), gifHidden: [...document.querySelectorAll('#l-screens img[data-kind=gif]')].every((i) => i.style.visibility === 'hidden') }));
  check(heldShot.v !== false, `Wake Up stops the clip (${JSON.stringify(heldShot)})`);
  check(heldShot.still && heldShot.gifHidden, `Wake Up stills the gif (${JSON.stringify(heldShot)})`);
  await page.click('#resume');
  await page.waitForTimeout(300);
  const back = await page.evaluate(() => ({ canvas: !!document.querySelector('#l-screens .scr-slot > canvas'), gif: [...document.querySelectorAll('#l-screens img[data-kind=gif]')].some((i) => i.style.visibility !== 'hidden') }));
  check(!back.canvas && back.gif, `Resume moves the gif again (${JSON.stringify(back)})`);
  await page.close();
  await ctx.close();
}

/* ---- the save: a spot survives a reload and a resize; own files do not, and the test card and the card say so ---- */
{
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  let page = await open(ctx, 'pace=1');
  await page.evaluate(() => { window.fuzz.add(1e6); window.fuzz.pick('f5', 'bimbo'); window.fuzz.grant('f5'); window.fuzz.pick('f3', 'slides'); });
  await page.waitForTimeout(300);
  await page.evaluate(() => document.querySelector('.tc [data-pick=none]')?.click());
  await page.evaluate(() => { window.fuzz.grant('v_frame'); window.fuzz.grant('v_gif'); });
  await page.waitForTimeout(500);
  const [a] = await frames(page);
  await mouseDrag(page, { x: a.x + a.w / 2, y: a.y + a.h - 30 }, { x: 1100, y: 120 });
  const moved = (await frames(page))[0];
  await page.evaluate(() => window.fuzz.engine.save());
  await page.reload({ waitUntil: 'load' });
  await page.waitForFunction(() => !!window.fuzz);
  await page.waitForTimeout(500);
  const after = (await frames(page))[0];
  check(Math.abs(after.x - moved.x) <= 1 && Math.abs(after.y - moved.y) <= 1, `the spot came back after a reload (${JSON.stringify(moved)} -> ${JSON.stringify(after)})`);
  await page.setViewportSize({ width: 900, height: 620 });
  await page.waitForTimeout(800);
  const small = await frames(page);
  check(small.every((r) => r.x >= 0 && r.y >= 0 && r.x + r.w <= 900 && r.y + r.h <= 620), `clamped on a smaller window (${JSON.stringify(small)})`);
  await shot(page, 'scr-06-resized');
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.waitForTimeout(800);
  const big = (await frames(page))[0];
  check(Math.abs(big.x - moved.x) <= 3 && Math.abs(big.y - moved.y) <= 3, `the same corner on the way back (${JSON.stringify(big)})`);
  await page.close();
  await ctx.close();
}

/* ---- the picked theme's niches feed a screen first (placeholder art names its niche) ---- */
{
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await open(ctx, 'pictures=online&fresh=1');
  await page.evaluate(() => { window.fuzz.add(1e6); window.fuzz.grant('ticker'); window.fuzz.pick('f5', 'bimbo'); window.fuzz.pick('f3', 'slides'); window.fuzz.grant('v_frame'); });
  const niches = await page.evaluate(async () => {
    const out = [];
    const orig = window.fuzz.engine.pictures.next;
    window.fuzz.engine.pictures.next = (o) => { out.push(o.niche); return orig(o); };
    await new Promise((r) => setTimeout(r, 12000));
    window.fuzz.engine.pictures.next = orig;
    return out;
  });
  const themed = niches.filter((n) => ['pink', 'heels', 'gloss', 'curls', 'lashes'].includes(n)).length;
  check(niches.length >= 2 && themed >= niches.length / 2, `theme niches come first: ${JSON.stringify(niches)}`);
  await shot(page, 'scr-07-placeholder-theme');
  await page.close();
  await ctx.close();
}

/* ---- Gentle: no clip runs and no gif moves ---- */
{
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await open(ctx, 'buy=v_frame,v_gif&fresh=1');
  await page.setInputFiles('input[type=file]', f('one.png'), { timeout: 100 }).catch(() => {});
  const chooser = page.waitForEvent('filechooser', { timeout: 4000 }).catch(() => null);
  await page.locator('#l-screens .scr-card button').first().click();
  await (await chooser)?.setFiles(f('one.png', 'loop.gif', 'clip.webm'));
  await page.evaluate(() => window.fuzz.grant('v_video'));
  await page.waitForTimeout(1500);
  await page.click('#wake');
  await page.check('#gentle');
  await page.click('#resume');
  await page.waitForTimeout(1400);
  const g = await page.evaluate(() => ({ playing: [...document.querySelectorAll('video')].filter((v) => !v.paused).length, still: !!document.querySelector('#l-screens .scr-slot > canvas') }));
  check(g.playing === 0 && g.still, `Gentle holds the clip and stills the gif (${JSON.stringify(g)})`);
  await shot(page, 'scr-08-gentle');
  await page.close();
  await ctx.close();
}

/* ---- phone: a real finger ---- */
{
  const ctx = await browser.newContext({ viewport: { width: 390, height: 800 }, hasTouch: true, isMobile: true });
  let page = await open(ctx, 'buy=v_frame,v_frame2&fresh=1');
  await shot(page, 'scr-10-phone-test-cards');
  const [a, b] = await frames(page);
  check(a.x + a.w <= 390 && b.x + b.w <= 390, 'inside a phone');
  const btn = await page.locator('#btn').boundingBox();
  const hit = await page.evaluate(({ x, y }) => document.elementFromPoint(x, y)?.id, { x: btn.x + btn.width / 2, y: btn.y + btn.height / 2 });
  check(hit === 'btn', 'the button is pressable with the frames up');
  const taps0 = await page.evaluate(() => window.fuzz.state.taps);
  const fin = finger(page);
  await fin.drag({ x: a.x + a.w / 2, y: a.y + a.h - 8 }, { x: a.x + a.w / 2 + 120, y: a.y + a.h - 8 + 30 });
  const a2 = (await frames(page))[0];
  check(a2.x > a.x + 100, `a finger drags a frame (${JSON.stringify(a)} -> ${JSON.stringify(a2)})`);
  check(await page.evaluate(() => window.fuzz.state.taps) === taps0, 'a finger drag across the button is not a tap');
  await shot(page, 'scr-11-phone-dragged');
  await fin.drag({ x: a2.x + a2.w / 2, y: a2.y + a2.h - 8 }, { x: 195, y: 700 });
  const a3 = (await frames(page))[0];
  const shopTop = (await page.locator('#shop').boundingBox()).y;
  check(a3.y + 40 <= shopTop, `a finger cannot bury it in the shop (${JSON.stringify(a3)}, shop ${shopTop})`);
  await page.tap('#btn');
  check(await page.evaluate(() => window.fuzz.state.taps) === taps0 + 1, 'the button takes a real tap');
  await page.close();

  /* The feed: the bar moves it, the feed itself swipes. */
  page = await open(ctx, 'buy=d_scroll,d_like,d_clips&fresh=1');
  await shot(page, 'scr-12-phone-feed');
  const fin2 = finger(page);
  const [s] = await frames(page);
  const money = await page.evaluate(() => window.fuzz.rates?.fps ?? window.fuzz.engine.rates().fps);
  check(money > 0, `the feed earns per second (${money})`);
  const t0 = await page.evaluate(() => window.fuzz.state.taps);
  await fin2.drag({ x: s.x + s.w / 2, y: s.y + 10 }, { x: 195, y: s.y + 20 });
  const under = (await frames(page))[0];
  check(under.x === s.x && under.y === s.y, `a bar dropped under the counter goes back, or nothing could grab it (${JSON.stringify(under)})`);
  await fin2.drag({ x: s.x + s.w / 2, y: s.y + 10 }, { x: s.x + s.w / 2 + 272, y: s.y + 10 + 12 });
  const s2 = (await frames(page))[0];
  check(s2.x > s.x + 120, `the bar drags the feed (${JSON.stringify(s)} -> ${JSON.stringify(s2)})`);
  check(await page.evaluate(() => window.fuzz.state.taps) === t0, 'dragging the bar is not a swipe');
  await fin2.drag({ x: s2.x + s2.w / 2, y: s2.y + 10 }, { x: s.x + s.w / 2, y: s.y + 10 });
  const s2b = (await frames(page))[0];
  await fin2.drag({ x: s2b.x + s2b.w / 2, y: s2b.y + s2b.h - 12 }, { x: s2b.x + s2b.w / 2, y: s2b.y + 34 });
  const s3 = (await frames(page))[0];
  check(s3.x === s2b.x && s3.y === s2b.y, 'a swipe on the feed does not move the frame');
  check(await page.evaluate(() => window.fuzz.state.taps) === t0 + 1, 'a swipe on the feed counts as a tap');
  await page.touchscreen.tap(s3.x + 12, s3.y + 30);
  await page.waitForTimeout(300);
  const full = await page.evaluate(() => !!document.querySelector('.routes-full'));
  check(full, 'a press opens the feed full screen');
  await shot(page, 'scr-13-phone-feed-full');
  const wake = await page.evaluate(() => { const r = document.getElementById('wake').getBoundingClientRect(); return document.elementFromPoint(r.left + 4, r.top + 4)?.id; });
  check(wake === 'wake', 'Wake Up above the full feed');
  await page.close();

  /* No Pictures: draggable, no test card. */
  page = await open(ctx, 'buy=b_frame,b_caption,b_story&fresh=1');
  await page.waitForTimeout(1500);
  await shot(page, 'scr-14-phone-blind');
  check(await page.locator('.scr-card').count() === 0, 'No Pictures has no test card: the empty frame is the joke');
  const [c] = await frames(page);
  await finger(page).drag({ x: c.x + c.w / 2, y: c.y + c.h - 8 }, { x: c.x + c.w / 2 + 100, y: c.y + c.h });
  const c2 = (await frames(page))[0];
  check(c2.x > c.x + 80, `the empty frame drags (${JSON.stringify(c2)})`);
  await page.close();

  /* Phone, own files through the real picker. */
  page = await open(ctx, 'buy=v_frame,v_gif&fresh=1');
  const chooser = page.waitForEvent('filechooser', { timeout: 4000 }).catch(() => null);
  await page.locator('#l-screens .scr-card button').last().tap();
  await (await chooser)?.setFiles(f('one.png', 'loop.gif', 'clip.webm'));
  await page.evaluate(() => window.fuzz.grant('v_video'));
  await page.waitForTimeout(1500);
  const phoneVid = await page.evaluate(() => { const v = document.querySelector('#l-screens video'); return v && { paused: v.paused, w: v.videoWidth, muted: v.muted }; });
  check(phoneVid && !phoneVid.paused && phoneVid.w > 0 && phoneVid.muted, `a phone plays a clip (${JSON.stringify(phoneVid)})`);
  await shot(page, 'scr-15-phone-own-media');
  await page.close();
  await ctx.close();
}

/* ---- the rest of the desktop routes, to look at ---- */
{
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  let page = await open(ctx, 'buy=d_scroll,d_like,d_clips,d_foryou,d_streak,d_end&fresh=1');
  const [s] = await frames(page);
  await shot(page, 'scr-20-desk-feed');
  const t0 = await page.evaluate(() => window.fuzz.state.taps);
  await mouseDrag(page, { x: s.x + s.w / 2, y: s.y + 10 }, { x: s.x + s.w / 2 + 30, y: s.y + 10 - 200 });
  const s2 = (await frames(page))[0];
  check(s2.y < s.y - 150, `a mouse moves the feed by its bar (${JSON.stringify(s2)})`);
  check(await page.evaluate(() => window.fuzz.state.taps) === t0, 'the bar is not a swipe');
  await mouseDrag(page, { x: s2.x + s2.w / 2, y: s2.y + s2.h - 40 }, { x: s2.x + s2.w / 2, y: s2.y + 60 });
  check(await page.evaluate(() => window.fuzz.state.taps) === t0 + 1, 'a mouse swipe on the feed is a tap');
  await page.close();
  /* Real clips in the feed: only the one on show plays; Wake Up and a host suspend stop it in the same call. */
  page = await open(ctx, 'buy=d_scroll,d_like,d_clips&fresh=1');
  const fc = page.waitForEvent('filechooser', { timeout: 4000 }).catch(() => null);
  await page.locator('#l-screens .scr-card button').first().click();
  await (await fc)?.setFiles(f('one.png', 'clip.webm'));
  await page.waitForTimeout(600);
  const [feed] = await frames(page);
  let clipUp = false;
  for (let i = 0; i < 24 && !clipUp; i++) {
    await mouseDrag(page, { x: feed.x + feed.w / 2, y: feed.y + feed.h - 50 }, { x: feed.x + feed.w / 2, y: feed.y + 60 });
    await page.waitForTimeout(500);
    clipUp = await page.evaluate(() => [...document.querySelectorAll('#l-screens video')].some((v) => !v.paused));
  }
  check(clipUp, 'a real clip plays in the feed');
  const playing = () => page.evaluate(() => [...document.querySelectorAll('#l-screens video')].filter((v) => !v.paused).length);
  check(await playing() === 1, 'one clip at a time');
  await shot(page, 'scr-23-desk-feed-clip');
  await page.click('#wake');
  check(await playing() === 0, 'Wake Up stops the feed clip');
  await page.click('#resume');
  await page.waitForTimeout(200);
  check(await playing() === 1, 'Resume brings it back');
  const suspended = await page.evaluate(() => { window.fuzz.engine.setSuspended(true, 'panic'); const n = [...document.querySelectorAll('#l-screens video')].filter((v) => !v.paused).length; window.fuzz.engine.setSuspended(false, 'panic'); return n; });
  check(suspended === 0, 'a host suspend stops it in the same call');
  await mouseDrag(page, { x: feed.x + feed.w / 2, y: feed.y + feed.h - 50 }, { x: feed.x + feed.w / 2, y: feed.y + 60 });
  await page.waitForTimeout(400);
  check(await playing() <= 1, 'a swipe leaves at most the new tile playing');
  await page.close();
  page = await open(ctx, 'buy=b_frame,b_caption,b_story&fresh=1');
  await page.waitForTimeout(1500);
  await shot(page, 'scr-21-desk-blind');
  await page.close();
  page = await open(ctx, 'buy=v_frame,v_frame2,v_frame3,v_gif,v_wall&fresh=1&pictures=online');
  await page.waitForTimeout(6000);
  await shot(page, 'scr-22-desk-wall');
  await page.close();
  await ctx.close();
}

await browser.close();
console.log(seen.join('\n'));
console.log(errors.length ? `\n${errors.length} errors:\n${errors.join('\n')}` : '\nall good');
process.exit(errors.length ? 1 : 0);
