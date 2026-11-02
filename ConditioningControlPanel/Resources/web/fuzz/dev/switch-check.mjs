// dev/switch-check.mjs - the Control Panel's promise, tested on real unlocks: with a whole route owned, switch every row off,
// let the game run, see what is left; switch them all on again and compare with the same page reloaded. Then each row alone,
// off and on. Reports errors and anything left behind. Needs dev/serve.mjs running.
//   FUZZ_URL=http://localhost:8805/fuzz/index.html node dev/switch-check.mjs
// Env: ROUTES ("eyes,sweet,slides,sell,trance;..."), ONE=1 also cycles each row alone (slower), QUICK=1 only the first route.
import { createRequire } from 'node:module';

const require = createRequire('C:/wt-fe/e2e/');
const { firefox } = require(process.env.PW_DIR || 'playwright');
const base = process.env.FUZZ_URL || 'http://localhost:8805/fuzz/index.html';
const FF = process.env.FF_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe';
const ROUTES = (process.env.ROUTES || [
  'eyes,sweet,slides,sell,trance', 'ears,strict,scroll,premium,bimbo', 'eyes,strict,blind,sell,drone', 'ears,sweet,slides,premium,sissy',
  'eyes,sweet,scroll,sell,goon', 'ears,strict,blind,premium,locked', 'eyes,strict,slides,premium,censored', 'ears,sweet,scroll,sell,infection',
].join(';')).split(';').map((r) => r.split(','));

const browser = await firefox.launch({ headless: true, executablePath: FF, firefoxUserPrefs: { 'media.volume_scale': '0.0' } });

/* What the page looks like to a player, as data. Runs in the page. */
const SNAP = () => {
  const count = (id) => document.getElementById(id)?.childElementCount ?? -1;
  const rails = document.querySelectorAll('#l-side .rail');
  const styled = (id) => document.getElementById(id)?.getAttribute('style') || '';
  const btn = document.getElementById('btn');
  const root = {};
  for (const s of document.documentElement.style) if (s.startsWith('--')) root[s] = document.documentElement.style.getPropertyValue(s);
  return {
    layers: { back: count('l-back'), wash: count('l-wash'), meters: count('l-meters'), banner: count('l-banner'), rails: rails[0].childElementCount + rails[1].childElementCount },
    root,
    rootOther: [...document.documentElement.style].filter((s) => !s.startsWith('--')),
    body: [...document.body.classList].sort(),
    bodyKids: [...document.body.children].map((e) => e.id || e.tagName.toLowerCase()).sort(),
    btn: { skin: btn.dataset.skin || '', cls: [...btn.classList].filter((c) => c !== 'down').sort() },
    world: styled('world'), page: styled('page'), stage: styled('stage'), counter: styled('counter'), wrap: styled('btn-wrap'), shop: styled('shop'),
    media: document.querySelectorAll('audio,video').length,
    mods: window.fuzz.engine.mods.length,
    scopes: window.fuzz.engine.scopes.size,
  };
};
const RESIDUE = () => {
  const label = (e) => `${e.tagName.toLowerCase()}${e.id ? `#${e.id}` : ''}${e.className ? `.${String(e.className).split(' ').join('.')}` : ''}[${(e.textContent || '').trim().slice(0, 24)}]`;
  const names = (id) => [...(document.getElementById(id)?.children || [])].map(label);
  const rails = document.querySelectorAll('#l-side .rail');
  const out = {};
  for (const id of ['l-back', 'l-wash', 'l-floaters', 'l-meters', 'l-banner', 'l-over', 'l-fx', 'cards']) { const n = names(id); if (n.length) out[id] = n.slice(0, 14); }
  const r = [...rails[0].children, ...rails[1].children].map(label);
  if (r.length) out.rails = r;
  out.scopes = [...window.fuzz.engine.scopes.keys()];
  out.btn = document.getElementById('btn').getAttribute('style') || '';
  out.shopRows = [...document.querySelectorAll('#rows .row,#goal .row')].filter((e) => e.getAttribute('style')).map((e) => `${e.dataset.id}:${e.getAttribute('style')}`).slice(0, 8);
  out.shopTextOdd = [...document.querySelectorAll('#rows .row b,#goal .row b')].map((b) => b.textContent).filter((t, i, a) => /[^A-Za-z0-9 &'.,:-]/.test(t)).slice(0, 8);
  out.stray = document.querySelectorAll('[data-stray]').length;
  out.btnText = document.getElementById('btn').textContent;
  out.cur = document.getElementById('cur').textContent;
  out.title = document.title;
  out.ticker = document.getElementById('l-ticker').getAttribute('style') || '';
  out.count = document.getElementById('count').getAttribute('style') || '';
  out.rowsChanged = [...document.querySelectorAll('#rows .row,#goal .row')].flatMap((e) => {
    const it = window.fuzz.items.find((x) => x.id === e.dataset.id), got = e.querySelector('b')?.textContent;
    const cls = [...e.classList].filter((c) => !['row', 'in', 'out', 'short', 'ready', 'no', 'peek'].includes(c));
    return [...(it && got !== it.name ? [`${it.id}: ${got}`] : []), ...(cls.length ? [`${e.dataset.id}.${cls.join('.')}`] : [])];
  }).slice(0, 8);
  out.rootKeys = [...document.documentElement.style].filter((k) => !['--page', '--ink', '--ink-soft', '--ink-faint', '--btn', '--panel', '--panel-short', '--line', '--fill', '--stamp', '--ready', '--font', '--shop-font', '--btn-ink', '--btn-edge'].includes(k));
  out.styles = [...document.head.querySelectorAll('style')].map((t) => t.id || (t.dataset.fzSeq ? 's' : 'raw:' + t.textContent.slice(0, 40)));
  out.styles = out.styles.filter((x) => x !== 's');
  return out;
};

function diff(a, b, path = '') {
  const out = [];
  if (a && b && typeof a === 'object' && typeof b === 'object') {
    for (const k of new Set([...Object.keys(a), ...Object.keys(b)])) out.push(...diff(a[k], b[k], path ? `${path}.${k}` : k));
  } else if (JSON.stringify(a) !== JSON.stringify(b)) out.push(`${path}: ${JSON.stringify(a)} -> ${JSON.stringify(b)}`);
  return out;
}

const fails = [];
for (const route of process.env.QUICK ? ROUTES.slice(0, 1) : ROUTES) {
  const name = route.join(',');
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await ctx.newPage();
  const errs = [];
  page.on('pageerror', (e) => errs.push(`pageerror: ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error' && !/data\/ads\.js/.test(m.text())) errs.push(m.text().slice(0, 240)); });

  /* Own every row on the route, by price the way a run does, then mount the lot once more: what a reload would show. */
  await page.goto(`${base}?fresh=1`, { waitUntil: 'load' });
  await page.waitForFunction(() => !!window.fuzz);
  const owned = await page.evaluate((r) => {
    const fz = window.fuzz, picked = new Set(r), laneFork = {};
    for (const [f, def] of Object.entries(fz.forks)) for (const lane of Object.keys(def.opts)) laneFork[lane] = f;
    fz.engine.unmountAll();
    const rows = fz.items.filter((it) => it.group === 'run' && it.kind === 'item' && (!laneFork[it.lane] || picked.has(it.lane))).sort((a, b) => a.cost - b.cost);
    for (const f of Object.keys(fz.forks)) { const lane = r.find((x) => fz.forks[f].opts[x]); fz.state.picks[f] = lane; }
    for (const it of rows) fz.grant(it.id);
    fz.engine.unmountAll();
    fz.engine.mountAll();
    return fz.state.order.length;
  }, route);
  await page.waitForTimeout(1500);
  const before = await page.evaluate(SNAP);
  const baseBefore = await page.evaluate(() => JSON.stringify(window.fuzz.state.base));
  const list = await page.evaluate(() => window.fuzz.engine.switches.groups().flatMap((g) => g.rows.map((r) => r.id)));

  /* ---- all off ---- */
  const offMs = await page.evaluate((l) => { const t = performance.now(); for (const id of l) window.fuzz.engine.switches.set(id, false); return Math.round(performance.now() - t); }, list);
  await page.waitForTimeout(1500);
  const off = await page.evaluate(SNAP);
  const residue = await page.evaluate(RESIDUE);
  if (await page.evaluate(() => JSON.stringify(window.fuzz.state.base)) !== baseBefore) fails.push(`${name}: the row pay moved`);
  const offErrs = errs.length;

  /* ---- all on ---- */
  const onMs = await page.evaluate((l) => { const t = performance.now(); for (const id of l) window.fuzz.engine.switches.set(id, true); return Math.round(performance.now() - t); }, list);
  await page.waitForTimeout(1500);
  const on = await page.evaluate(SNAP);
  const back = diff(before, on);

  /* ---- each row alone ---- */
  const one = [];
  if (process.env.ONE) {
    for (const id of list) {
      const n = errs.length;
      const s0 = await page.evaluate(SNAP);
      await page.evaluate((x) => window.fuzz.engine.switches.set(x, false), id);
      await page.waitForTimeout(120);
      await page.evaluate((x) => window.fuzz.engine.switches.set(x, true), id);
      await page.waitForTimeout(120);
      const d = diff(s0, await page.evaluate(SNAP));
      if (d.length) one.push(`${id}: ${d.slice(0, 3).join(' | ')}`);
      if (errs.length > n) one.push(`${id}: ERR ${errs[errs.length - 1]}`);
    }
  }

  console.log(`\n=== ${name}: owned ${owned}, switches ${list.length}, all off ${offMs} ms, all on ${onMs} ms`);
  console.log('off state:', JSON.stringify({ ...off, root: Object.keys(off.root).length }));
  console.log('residue:', JSON.stringify(residue));
  console.log('errors while off:', offErrs, 'total:', errs.length);
  for (const e of [...new Set(errs)].slice(0, 12)) console.log('  ERR', e);
  console.log('back to before:', back.length ? back.slice(0, 16) : 'yes');
  if (one.length) { console.log('one at a time:'); for (const f of one.slice(0, 40)) console.log('  ', f); }
  await ctx.close();
}
for (const s of fails) console.log('FAIL', s);
await browser.close();
