// dev/pile-check.mjs - the hard rules with a whole route on at once: Wake Up, Escape, a host suspend, pop-ups nobody answers,
// and flicker under Gentle and OS reduced motion. dev/smoke.mjs runs it; alone: node dev/serve.mjs 8791 &  node dev/pile-check.mjs
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';

const ROUTES = [
  ['eyes', 'sweet', 'slides', 'sell', 'trance'], ['ears', 'strict', 'scroll', 'premium', 'bimbo'],
  ['eyes', 'strict', 'blind', 'sell', 'drone'], ['ears', 'sweet', 'slides', 'premium', 'sissy'],
  ['eyes', 'sweet', 'scroll', 'sell', 'goon'], ['ears', 'strict', 'blind', 'premium', 'locked'],
  ['eyes', 'strict', 'slides', 'premium', 'censored'], ['ears', 'sweet', 'blind', 'sell', 'infection'],
];

/* Before the page runs: every media element, the audio context, and every element.animate call are counted. */
function spy() {
  window.__media = new Set();
  const A = window.Audio;
  window.Audio = function Audio2(...a) { const e = new A(...a); window.__media.add(e); return e; };
  const ce = document.createElement;
  document.createElement = function (t, ...r) { const e = ce.call(this, t, ...r); if (/^(audio|video)$/i.test(t)) window.__media.add(e); return e; };
  const AC = window.AudioContext;
  window.AudioContext = function AudioContext2(...a) { const c = new AC(...a); window.__ac = c; return c; };
}

export async function pileUp(page, base, route, query = '') {
  await page.goto(`${base}?pace=1&fresh=1${query}`, { waitUntil: 'load' });
  await page.waitForFunction(() => !!window.fuzz);
  await page.evaluate((r) => {
    const f = window.fuzz;
    /* Picks first: a granted fork with no pick takes its first lane, and every pile would wear the first theme. */
    ['f1', 'f2', 'f3', 'f4', 'f5'].forEach((k, i) => { f.state.picks[k] = r[i]; });
    f.engine.emit('page-colour', {});
    for (const it of f.items) if (it.kind !== 'end' && it.group === 'run') f.grant(it.id);
    f.add(1e9);
    /* A real stream and a real loop, so "every stream" is not a claim about silence. */
    const n = 16000, b = new ArrayBuffer(44 + n * 2), v = new DataView(b);
    const s = (o, t) => [...t].forEach((c, i) => v.setUint8(o + i, c.charCodeAt(0)));
    s(0, 'RIFF'); v.setUint32(4, 36 + n * 2, true); s(8, 'WAVEfmt '); v.setUint32(16, 16, true); v.setUint16(20, 1, true); v.setUint16(22, 1, true);
    v.setUint32(24, 8000, true); v.setUint32(28, 16000, true); v.setUint16(32, 2, true); v.setUint16(34, 16, true); s(36, 'data'); v.setUint32(40, n * 2, true);
    const url = URL.createObjectURL(new Blob([b], { type: 'audio/wav' }));
    const scope = f.engine.scopes.get('stray');
    window.__stream = scope.ctx.audio.stream(url, { loop: true });
    window.__scope = scope;
  }, route);
}

/** Presses whatever cards are up. Fails if one will not close. */
export async function clearCards(page, errors, where) {
  for (let i = 0; i < 16; i++) {
    const n = await page.locator('#cards .backdrop').count();
    if (!n) return;
    const b = page.locator('#cards .backdrop:last-child .card-buttons button').last();
    if (!(await b.count())) { await page.waitForTimeout(700); continue; }
    await page.waitForTimeout(380);
    await b.click({ timeout: 1500 }).catch(() => {});
  }
  if (await page.locator('#cards .backdrop').count()) errors.push(`${where}: a card did not close after 16 presses: ${await page.locator('#cards .backdrop h2').first().textContent().catch(() => '?')}`);
}

const stillness = (page) => page.evaluate(async () => {
  const f = window.fuzz, world = document.getElementById('world');
  const running = world.getAnimations({ subtree: true }).filter((a) => a.playState === 'running');
  const c0 = f.state.clock, m0 = f.state.fuzz;
  let mutations = 0;
  const seen = {};
  const mo = new MutationObserver((list) => { for (const r of list) { mutations++; const t = r.target; const k = (t.id || t.className || t.tagName || 'text').toString().slice(0, 30); seen[k] = (seen[k] || 0) + 1; } });
  mo.observe(world, { subtree: true, childList: true, attributes: true, characterData: true });
  await new Promise((r) => setTimeout(r, 900));
  mo.disconnect();
  return {
    running: running.slice(0, 6).map((a) => `${a.animationName || 'wa'} on ${a.effect?.target?.id || a.effect?.target?.className || a.effect?.target?.tagName}`), nRunning: running.length,
    clockMoved: f.state.clock !== c0, moneyMoved: f.state.fuzz !== m0, mutations, seen: Object.entries(seen).sort((a, b) => b[1] - a[1]).slice(0, 5),
    media: [...window.__media].filter((e) => !e.paused).map((e) => e.src.slice(0, 60)), audio: window.__ac?.state ?? 'none',
    stream: window.__stream?.el?.paused,
  };
});

function judgeStill(errors, where, s) {
  if (s.nRunning) errors.push(`${where}: ${s.nRunning} animations still run: ${s.running.join(', ')}`);
  if (s.clockMoved) errors.push(`${where}: the clock moved`);
  if (s.moneyMoved) errors.push(`${where}: money moved`);
  if (s.mutations > 2) errors.push(`${where}: the page kept changing, ${s.mutations} mutations: ${JSON.stringify(s.seen)}`);
  if (s.media.length) errors.push(`${where}: media still playing: ${s.media.join(', ')}`);
  if (s.audio === 'running') errors.push(`${where}: the audio context still runs`);
  if (s.stream === false) errors.push(`${where}: a stream still plays`);
}

const onTop = (page, sel) => page.evaluate((s) => { const e = document.querySelector(s), r = e.getBoundingClientRect(); const n = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2); return n === e || e.contains(n); }, sel);

/** Wake Up, Escape and a host suspend, everything on. */
export async function checkStops(page, errors, where) {
  await page.click('#btn', { force: true, timeout: 2000 }).catch(() => {});
  await page.waitForTimeout(200);
  await clearCards(page, errors, where);

  await page.click('#wake');
  await page.waitForTimeout(120);
  if (!(await page.locator('#pause').isVisible())) errors.push(`${where}: Wake Up showed no pause card`);
  if (!(await onTop(page, '#resume'))) errors.push(`${where}: Resume is covered`);
  judgeStill(errors, `${where}, Wake Up`, await stillness(page));
  await page.click('#resume');
  await page.waitForTimeout(250);
  if (await page.evaluate(() => window.fuzz.engine.paused)) errors.push(`${where}: Resume did not resume`);

  await page.keyboard.press('Escape');
  await page.waitForTimeout(120);
  if (!(await page.locator('#pause').isVisible())) errors.push(`${where}: Escape showed no pause card`);
  judgeStill(errors, `${where}, Escape`, await stillness(page));
  await page.keyboard.press('Escape');
  await page.waitForTimeout(250);
  if (await page.evaluate(() => window.fuzz.engine.paused)) errors.push(`${where}: a second Escape did not resume`);

  /* The host's panic, through the real wire, on top of whatever is up. It beats Resume, Escape and the button. */
  const sus = await page.evaluate(async () => {
    const b = await import('/fuzz/bridge.js');
    b._dispatch({ type: 'suspend', on: true, reason: 'panic' });
    return { paused: window.fuzz.engine.paused, suspended: window.fuzz.engine.suspended, streamPaused: window.__stream?.el?.paused };
  });
  if (!sus.suspended || !sus.paused || sus.streamPaused === false) errors.push(`${where}: suspend did not take at once: ${JSON.stringify(sus)}`);
  await page.waitForTimeout(100);
  judgeStill(errors, `${where}, suspend`, await stillness(page));
  await page.click('#resume');
  await page.keyboard.press('Escape');
  const taps = await page.evaluate(() => { const t = window.fuzz.state.taps; window.fuzz.tap(3); return window.fuzz.state.taps - t; });
  const held = await page.evaluate(() => ({ s: window.fuzz.engine.suspended, stream: window.__stream?.el?.paused }));
  if (!held.s || held.stream === false || taps) errors.push(`${where}: something beat the suspend: ${JSON.stringify({ ...held, taps })}`);
  await page.evaluate(async () => { (await import('/fuzz/bridge.js'))._dispatch({ type: 'suspend', on: false, reason: 'panic' }); });
  if (!(await page.locator('#pause').isVisible())) errors.push(`${where}: the pause card went with the suspend`);
  await page.click('#resume');
  await page.waitForTimeout(250);
  if (await page.evaluate(() => window.fuzz.engine.paused || window.fuzz.engine.suspended)) errors.push(`${where}: could not resume after a suspend`);
}

/** Nobody answers anything. The button and the goal row may not stay blocked for long. Game clock x4 by ctx.speed. */
export async function checkIgnored(page, errors, where, seconds = 20, maxMs = 8000) {
  const res = await page.evaluate(async ({ seconds }) => {
    const f = window.fuzz, btn = document.getElementById('btn');
    const release = window.__scope.ctx.speed(4);
    const hit = (el) => { if (!el) return null; const r = el.getBoundingClientRect(); if (!r.width) return null; const n = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2); return !n || n === el || el.contains(n) ? null : n; };
    const name = (n) => { const lay = n.closest('.layer,#cards,#shop,#stage'); let box = n; while (box.parentElement && box.parentElement !== lay) box = box.parentElement; return `${lay?.id || '?'}: ${(box.innerText || box.className || box.tagName).replace(/\s+/g, ' ').slice(0, 44)}`; };
    const long = {};
    const cur = { btn: null, goal: null };
    const t0 = performance.now();
    let maxOver = 0, cards = {};
    while (performance.now() - t0 < seconds * 1000) {
      const now = performance.now();
      for (const [k, el] of [['btn', btn], ['goal', document.querySelector('#goal .row')]]) {
        const b = hit(el);
        if (b) { const id = name(b); if (!cur[k] || cur[k].id !== id) cur[k] = { id, at: now }; long[`${k} | ${id}`] = Math.max(long[`${k} | ${id}`] || 0, Math.round((now - cur[k].at) * 4)); } else cur[k] = null;
      }
      const c = document.querySelector('#cards .backdrop h2');
      if (c) cards[c.textContent] = (cards[c.textContent] || 0) + 1;
      maxOver = Math.max(maxOver, [...document.getElementById('l-over').querySelectorAll('button')].filter((b) => b.offsetWidth).length);
      await new Promise((r) => setTimeout(r, 250));
    }
    release();
    return { long, maxOver, cards, clock: f.state.clock };
  }, { seconds });
  for (const [k, ms] of Object.entries(res.long)) if (ms > (k.includes('cards:') ? 18000 : maxMs)) errors.push(`${where}: ${k} held for ${Math.round(ms / 1000)} s of game time`);
  if (res.maxOver > 40) errors.push(`${where}: ${res.maxOver} buttons piled up in the over layer with nobody pressing`);
  return res;
}

/** Nothing may flicker above 3 Hz. A flicker here is a value that goes A, B, A: opacity, visibility, colour, on anything big. */
export async function checkFlicker(page, errors, where, seconds = 5) {
  const res = await page.evaluate(async (seconds) => {
    const tooFast = [];
    const big = (e) => { const r = e.getBoundingClientRect?.(); return r && r.width * r.height > 1600 && r.bottom > 0 && r.right > 0 && r.top < innerHeight && r.left < innerWidth; };
    const scan = () => {
      for (const a of document.getAnimations()) {
        const t = a.effect?.getComputedTiming?.(); const el = a.effect?.target;
        if (!t || !el || a.playState !== 'running' || !(t.iterations > 2)) continue;
        const cycle = (t.duration || 0) * (String(t.direction).includes('alternate') ? 2 : 1) / (a.playbackRate || 1);
        if (cycle > 0 && cycle < 333) tooFast.push(`${a.animationName || 'wa'} on ${el.id || el.className || el.tagName}: ${Math.round(cycle)} ms`);
      }
    };
    /* Every frame: a,b,a on opacity or visibility or background colour of big things in the world. */
    const hist = new Map(), flips = new Map();
    const world = document.getElementById('world');
    const t0 = performance.now();
    let frames = 0;
    await new Promise((res) => {
      const k = () => {
        frames++;
        const els = [...world.querySelectorAll('*')].filter((e) => !e.closest('svg') && big(e)).slice(0, 500);
        const now = performance.now() - t0;
        for (const e of els) {
          const s = getComputedStyle(e);
          const v = [Number(s.opacity), s.visibility, s.backgroundColor, s.display].join('|');
          const h = hist.get(e) || [];
          if (h[h.length - 1] !== v) {
            if (h.length >= 2 && h[h.length - 2] === v) { const f = flips.get(e) || []; f.push(now); flips.set(e, f); }
            h.push(v); if (h.length > 3) h.shift(); hist.set(e, h);
          }
        }
        if (now < seconds * 1000) requestAnimationFrame(k); else res();
      };
      requestAnimationFrame(k);
    });
    scan();
    const out = [];
    for (const [e, f] of flips) {
      /* more than 3 full cycles in any one second */
      for (let i = 0; i + 3 < f.length; i++) if (f[i + 3] - f[i] < 1000) { out.push(`${e.id || e.className || e.tagName} ${(e.innerText || '').slice(0, 20)} x${f.length}`); break; }
    }
    return { tooFast: [...new Set(tooFast)].slice(0, 10), out: out.slice(0, 10), frames };
  }, seconds);
  for (const t of res.tooFast) errors.push(`${where}: loop above 3 Hz: ${t}`);
  for (const t of res.out) errors.push(`${where}: flicker: ${t}`);
  return res;
}

export async function runPileChecks(browser, base, { routes = ROUTES, seconds = 16 } = {}) {
  const errors = [], notes = {};
  for (const [i, route] of routes.entries()) {
    const phone = i % 2 === 1, where = `${route[4]} ${phone ? '390' : '1280'}`;
    const c = await browser.newContext(phone ? { viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true } : { viewport: { width: 1280, height: 800 } });
    await c.addInitScript(spy);
    const page = await c.newPage();
    page.on('pageerror', (e) => errors.push(`${where}: pageerror ${e.message}`));
    page.on('console', (m) => { if (m.type() === 'error') errors.push(`${where}: console ${m.text().slice(0, 200)}`); });
    await pileUp(page, base, route);
    await page.waitForTimeout(800);
    await clearCards(page, errors, where);
    await checkStops(page, errors, where);
    notes[where] = await checkIgnored(page, errors, where, seconds);
    await c.close();
  }
  /* Gentle by the switch, and by the OS. */
  for (const [mode, route] of routes.map((r, i) => [i % 2 ? 'reduced' : 'gentle', r])) {
    const where = `${route[4]} ${mode}`;
    const c = await browser.newContext({ viewport: { width: 1280, height: 800 }, reducedMotion: mode === 'reduced' ? 'reduce' : 'no-preference' });
    await c.addInitScript(spy);
    const page = await c.newPage();
    page.on('pageerror', (e) => errors.push(`${where}: pageerror ${e.message}`));
    await page.goto(`${base}?pace=1&fresh=1`, { waitUntil: 'load' });
    await page.waitForFunction(() => !!window.fuzz);
    if (mode === 'gentle') await page.evaluate(() => window.fuzz.ui.setInit({ gentle: true }));
    await pileUp2(page, route);
    await page.waitForTimeout(800);
    await clearCards(page, errors, where);
    notes[where] = await checkFlicker(page, errors, where);
    await c.close();
  }
  return { errors, notes };
}

/* The pile without a reload, so a Gentle set before it stays on. */
async function pileUp2(page, r) {
  await page.evaluate((route) => {
    const f = window.fuzz;
    ['f1', 'f2', 'f3', 'f4', 'f5'].forEach((k, i) => { f.state.picks[k] = route[i]; });
    f.engine.emit('page-colour', {});
    for (const it of f.items) if (it.kind !== 'end' && it.group === 'run') f.grant(it.id);
    f.add(1e9);
  }, r);
}

if (process.argv[1] && fileURLToPath(import.meta.url).replace(/\\/g, '/') === process.argv[1].replace(/\\/g, '/')) {
  const require = createRequire('C:/wt-fe/e2e/');
  const { firefox } = require(process.env.PW_DIR || 'playwright');
  const FF = process.env.FF_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe';
  const browser = await firefox.launch({ headless: true, executablePath: FF, firefoxUserPrefs: { 'media.volume_scale': '0.0' } });
  const base = process.env.FUZZ_URL || 'http://localhost:8791/fuzz/index.html';
  const only = process.env.PILE_ROUTES ? process.env.PILE_ROUTES.split(';').map((s) => s.split(',')) : undefined;
  const { errors, notes } = await runPileChecks(browser, base, { routes: only || ROUTES, seconds: Number(process.env.PILE_SECONDS) || 16 });
  console.log(JSON.stringify({ errors, notes }, null, 1));
  await browser.close();
  process.exit(errors.length ? 1 : 0);
}
