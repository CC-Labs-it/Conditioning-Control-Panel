// dev/gen-items.mjs - data/items.js and ui/demos/*.js from the design source. Never edit those by hand.
//   node dev/gen-items.mjs            (source: env FUZZ_DESIGN, else the handoff folder below)
// Final names and one-liners come from copy.json; numbers, lanes and req come from rows.json.
import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';

const here = fileURLToPath(new URL('.', import.meta.url));
const tree = join(here, '..');
export const DESIGN = process.env.FUZZ_DESIGN || 'C:/Projects/Conditioning-Control-Panel---CSharp-WPF/handoff-1102/fuzz-tree';
const read = (n) => JSON.parse(readFileSync(join(DESIGN, n), 'utf8'));

/** The currency name is a token: only data/brand.js says what it is called. */
const cur = (s) => String(s ?? '').replace(/\bFuzz\b/g, '{cur}');

/* Stacking rows. Same curve as the design sim: 3 x 1.5^owned, 12 at most. */
const GROW = { stray: { ratio: 1.5, max: 12 } };

/* A row whose parent is an ending can never open, because buying the parent ends the run. One Last Go sits
 * behind the theme pick, and its price keeps it late. Go Outside waits for Spiral Overlay: leaving early should be a
 * choice made late in a run, not a row a saver trips over at minute 15. */
const REQ_OVERRIDE = { onemore: ['f5'], outside: ['overlay'] };

/* The design sim paid for 30 minutes with nothing on top. The real game has prizes and modifiers on top (capped in
 * sim/economy.js), so the plan pays 70% of the design's numbers and the whole run lands near 30. Measured: dev/measure.mjs. */
const planPay = (n, lane) => Math.max(1, Math.round(n * (LANE_PAY[lane] ?? 0.7)));
/* Tap on Command earns from taps, which a real player does every moment: it runs ahead of the Rest route without this. */
const LANE_PAY = { strict: 0.6 };

/* The theme pick comes at about minute 8 and its podcast at about minute 10 (owner, 2026-11-02). The trunk up to the pick
 * gets cheaper, each theme opens with its head and then its podcast, and the rows that sat before the podcast each take the
 * price of the row before them, so everything above the podcast's old place keeps its price and no late gap opens. A row
 * that moves keeps its value for money: pay follows price along the design's own curve. */
const TRUNK_MOVES = { f3: 4000, banner: 5000, f4: 6500, glossy: 8000, f5: 9000 };
const THEME_LANES = ['trance', 'bimbo', 'drone', 'sissy', 'goon', 'locked', 'censored', 'infection'];
const HEAD_COST = 10000, POD_COST = 12000;
const moved = (r, cost) => {
  const k = (cost / r.cost) ** 0.8;
  return { ...r, cost, ...(r.fps ? { fps: Math.round(r.fps * k) } : {}), ...(r.click ? { click: Math.round(r.click * k) } : {}) };
};
function moveRows(rows) {
  const out = rows.map((r) => (TRUNK_MOVES[r.id] ? moved(r, TRUNK_MOVES[r.id]) : r));
  const byId = new Map(out.map((r) => [r.id, r]));
  for (const lane of THEME_LANES) {
    const chain = out.filter((r) => r.lane === lane && r.kind !== 'end').sort((a, b) => a.cost - b.cost);
    const p = chain.findIndex((r) => r.id.endsWith('_pod'));
    const [head, pod, after] = [chain[0], chain[p], chain[p + 1]];
    const slots = chain.map((r) => r.cost);
    for (let i = 1; i < p; i++) byId.set(chain[i].id, moved(chain[i], slots[i - 1]));
    byId.set(head.id, moved(head, HEAD_COST));
    byId.set(pod.id, { ...moved(pod, POD_COST), req: [head.id] });
    if (after) byId.set(after.id, { ...after, req: [chain[p - 1].id] });
  }
  return out.map((r) => byId.get(r.id));
}

/* Row text that named a multiple. Modifiers bend past a knee (sim/economy.js), so a row says more, never how many times. */
const WHAT_OVERRIDE = {
  t_denied: 'Some taps are refused, and the next one pays more.',
  t_grade: 'Every two minutes you get a score, and a top score raises earnings for a while.',
  si_set: 'Dressing the button in a full matching set raises its pay.',
  x_later: 'Pick Tap and Chastity: refused taps and purchases pay more.',
};

const groupOf = (r) => (r.lane === 'meta' ? 'meta' : r.lane === 'after' ? 'after' : 'run');

export function buildItems() {
  const rows = moveRows(read('rows.json')), copy = read('copy.json'), lanes = read('lanes.json');
  const items = rows.map((r) => {
    const c = copy.items[r.id];
    if (!c) throw new Error(`copy.json has no ${r.id}`);
    const it = { id: r.id, emoji: r.emoji, name: cur(c.name), what: cur(WHAT_OVERRIDE[r.id] || c.what), line: cur(r.line), cost: r.cost, lane: r.lane, req: REQ_OVERRIDE[r.id] || r.req, pay: r.pay };
    if (r.fps) it.fps = planPay(r.fps, r.lane);
    if (r.click) it.click = planPay(r.click, r.lane);
    if (r.wall) it.wall = r.wall;
    if (r.flags) it.flags = r.flags;
    it.kind = r.kind || 'item';
    it.group = groupOf(r);
    if (GROW[r.id]) it.grow = GROW[r.id];
    return it;
  });
  const outLanes = lanes.map((l) => {
    const c = copy.lanes[l.id] || {};
    const o = { id: l.id, name: cur(c.name || l.name), what: cur(c.what || l.note || '') };
    if (l.fork) o.fork = l.fork;
    return o;
  });
  const forks = {};
  for (const [id, f] of Object.entries(copy.forks)) forks[id] = { name: cur(f.name), ask: cur(f.ask), opts: Object.fromEntries(Object.entries(f.opts).map(([k, v]) => [k, { name: cur(v.name), what: cur(v.what) }])) };
  return { items, lanes: outLanes, forks };
}

export function itemsSource() {
  const { items, lanes, forks } = buildItems();
  const line = (o) => '  ' + JSON.stringify(o) + ',';
  return [
    '// GENERATED by dev/gen-items.mjs from the design source. Do not edit. Copy uses the tokens {cur} and {game}: see data/brand.js.',
    'export const ITEMS = Object.freeze([',
    ...items.map(line),
    ']);',
    'export const LANES = Object.freeze([',
    ...lanes.map(line),
    ']);',
    'export const FORKS = Object.freeze({',
    ...Object.entries(forks).map(([k, v]) => `  ${k}: ${JSON.stringify(v)},`),
    '});',
    'export const BY_ID = Object.freeze(Object.fromEntries(ITEMS.map((i) => [i.id, i])));',
    'export const LANE_BY_ID = Object.freeze(Object.fromEntries(LANES.map((l) => [l.id, l])));',
    '/** Lane id -> the fork that opens it. Lanes without one are always open. */',
    'export const FORK_OF_LANE = Object.freeze(Object.fromEntries(LANES.filter((l) => l.fork).map((l) => [l.id, l.fork])));',
    '',
  ].join('\n');
}

/* The demo kit and demos become ES modules. Each file keeps its own helpers; only DEMOS is shared. */
export const DEMO_FILES = ['basics', 'routes', 'themes1', 'themes2', 'extras'];

function demoCur(src) {
  // Every use in the source sits inside a single-quoted string or a comment.
  return src.replace(/\bFuzz\b/g, "' + CUR + '");
}

export function demoSources() {
  const out = {};
  const kit = readFileSync(join(DESIGN, 'kit.js'), 'utf8')
    .replace("o.label != null ? ' ' + o.label : ' Fuzz'", "' ' + (o.label != null ? o.label : CUR)")
    .replace('const DEMOS = {};', 'export const DEMOS = {};')
    .replace('const Demo = (() => {', 'export const Demo = (() => {');
  if (/\bFuzz\b/.test(kit.replace(/^\s*\/\*.*$/gm, ''))) throw new Error('kit.js still names the currency');
  out['kit.js'] = `// GENERATED by dev/gen-items.mjs from the design source's kit.js. Do not edit.\nimport { BRAND } from '../../data/brand.js';\nconst CUR = BRAND.currency;\n${kit}`;
  for (const f of DEMO_FILES) {
    const src = demoCur(readFileSync(join(DESIGN, 'demos', `${f}.js`), 'utf8').replace(/^\s*\/\/.*$/gm, ''));
    out[`${f}.js`] = `// GENERATED by dev/gen-items.mjs from the design source's demos. Do not edit.\nimport { BRAND } from '../../data/brand.js';\nimport { DEMOS } from './kit.js';\nconst CUR = BRAND.currency;\n${src}`;
  }
  return out;
}

if (process.argv[1] && fileURLToPath(import.meta.url).replace(/\\/g, '/') === process.argv[1].replace(/\\/g, '/')) {
  if (!existsSync(DESIGN)) throw new Error(`no design source at ${DESIGN}`);
  writeFileSync(join(tree, 'data', 'items.js'), itemsSource());
  mkdirSync(join(tree, 'ui', 'demos'), { recursive: true });
  for (const [name, src] of Object.entries(demoSources())) writeFileSync(join(tree, 'ui', 'demos', name), src);
  console.log('items.js + demos written');
}
