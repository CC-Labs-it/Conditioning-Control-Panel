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
const REQ_OVERRIDE = {
  onemore: ['f5'], outside: ['overlay'],
  /* Owner, 2026-11-02: the theme comes early and colours everything, pictures right after it. No Heartbeat row. */
  f5: ['ticker'], f3: ['f5'], depth: ['f3'], f2: ['depth'], banner: ['f2'], f4: ['banner'], glossy: ['f4'], taste2: ['glossy'], quiz: ['f2'],
};
/* The main path in the order it is walked: E.goal takes the first unowned row, so ITEMS keeps this order. */
const TRUNK_ORDER = ['start', 'stray', 'fpsv', 'f1', 'softw', 'ticker', 'f5', 'f3', 'depth', 'f2', 'banner', 'f4', 'glossy', 'taste2', 'overlay', 'takeover', 'wallpaper', 'tunnel', 'drop'];
const DROPPED = new Set(['heart']);   // owner, 2026-11-02: no heartbeat
/* Rows that came after the design source. A row with `any` needs at least one of those as well as all of `req`. */
const EXTRA_ROWS = [
  { id: 'audio_mixer', emoji: '', name: 'Audio Mixer', what: 'A separate volume slider and mute button for each sound. Your mix is remembered.', line: 'A little less of that one.', cost: 300, lane: 'side', req: ['sound', 'f1'], pay: '-' },
  { id: 'sp_colors', emoji: '🎨', name: 'Spiral Colours', what: 'The spiral slowly drifts through your colours.', line: 'It will not sit still.', cost: 7000, lane: 'look', req: [], any: ['e_spiral', 'a_spiral'], pay: '-' },
  { id: 'sp_shapes', emoji: '🧿', name: 'Spiral Shapes', what: 'The spiral keeps melting into new shapes.', line: 'Follow it anyway.', cost: 20000, lane: 'look', req: ['sp_colors'], pay: '-' },
  { id: 'asmr', emoji: '🎧', name: 'ASMR', what: 'A player for soft ASMR sits at the edge of the screen. Off air for now.', line: 'Closer than you think.', cost: 5000, lane: 'side', req: ['f5'], pay: 'f', fps: 20 },
  { id: 'guide', emoji: '🗺️', name: 'Guide', what: 'Open a map of every unlock and where it leads.', line: 'Now you can see what you skipped.', cost: 9000, lane: 'side', req: ['screen'], pay: '-' },
  { id: 'panel', emoji: '🎛️', name: 'Control Panel', what: 'Turn anything you have unlocked on or off.', line: 'You were always in control. Sure.', cost: 400000, lane: 'side', req: ['overlay'], pay: '-' },
  { id: 'toy_box', emoji: '', name: 'Toy Box', what: 'Opens every 15-30 seconds. Fit the pieces before it closes. Each completed box pays more.', line: 'Everything in its place.', cost: 2500, lane: 'side', req: ['f5'], pay: '-' },
  { id: 'mini_breakout', emoji: '', name: 'Mini Breakout', what: 'An endless little Breakout plays itself. Hover to take the paddle. Clearing a board pays, and playing earns extra.', line: 'Just one more brick.', cost: 4800, lane: 'side', req: ['f2'], pay: '-' },
  { id: 'mini_race', emoji: '', name: 'Racing Thoughts', what: 'Actual Racing Thoughts gameplay loops in a little window. A separate bonus waits for you to collect.', line: 'Watch the road.', cost: 7500, lane: 'side', req: ['f4'], pay: '-' },
  { id: 'mini_slots', emoji: '', name: 'Mini Slots', what: 'A free coin every 45 seconds, with room for three. Spin for pictures, spirals and prizes. Two wins in every six spins.', line: 'The house is feeling generous.', cost: 12500, lane: 'side', req: ['glossy'], pay: '-' },
  { id: 'dispenser', emoji: '', name: 'Prize Dispenser', what: 'Turn the crank, then catch the prize bubble within 2.5 seconds of landing. Ready again after 30 seconds.', line: 'Catch it before it goes.', cost: 22000, lane: 'side', req: ['mini_slots'], pay: '-' },
  { id: 'dispenser_fast', emoji: '', name: 'Quick Refill', what: 'The prize dispenser refills in 22 seconds.', line: 'Less waiting. Same small window.', cost: 40000, lane: 'side', req: ['dispenser'], pay: '-' },
  { id: 'dispenser_faster', emoji: '', name: 'Express Refill', what: 'The prize dispenser refills in 15 seconds.', line: 'Keep a finger free.', cost: 75000, lane: 'side', req: ['dispenser_fast'], pay: '-' },
];

/* The design sim paid for 30 minutes with nothing on top. The real game has prizes and modifiers on top (capped in
 * sim/economy.js), so the plan pays 70% of the design's numbers and the whole run lands near 30. Measured: dev/measure.mjs. */
/* Pay a row earns on top of its design numbers, before planPay. Swipe Feed is a screen: it earns per second as well as per swipe. */
const EXTRA_PAY = { d_scroll: { fps: 11 } };
const planPay = (n, lane, cost) => Math.max(1, Math.round(n * (LANE_PAY[lane] ?? 0.7) * (cost >= LATE_FROM ? LATE_PAY : 1)));
/* Tap on Command and Bimbo earn from taps, which a real player does every moment: they run ahead of the Rest route without this.
 * Infection and No Pictures pay less on top of their rows, so they get a little more in them. */
const LANE_PAY = { strict: 0.6, bimbo: 0.45, scroll: 0.65, goon: 0.65, blind: 0.8, infection: 0.9, censored: 0.65 };
/* Everything past the podcast pays this much less: the early game got its rows sooner, and the run still takes 30 minutes. */
const LATE_FROM = 20000, LATE_PAY = 0.82;

/* The order and the minutes (owner, 2026-11-02): theme pick about minute 3, pictures about minute 4, a first screen right after,
 * gifs and clips as later upgrades of that lane, the theme's first rows between the pick and minute 10, its podcast at about
 * minute 10. Measured on the real game: dev/measure.mjs. A row that moves keeps its value for money: pay follows price along
 * the design's own curve. */
const TRUNK_MOVES = {
  f5: 2300, f3: 3300, depth: 3600, f2: 4200, banner: 5000, f4: 6500, glossy: 8000, taste2: 45000, overlay: 150000, takeover: 230000,
  /* The picture lanes, whichever fork: first screen, then a slot each. */
  v_frame: 3500, v_frame2: 5500, v_frame3: 8000, v_gif: 12000, v_wall: 22000, v_fav: 40000, v_video: 65000,
  d_scroll: 3500, d_like: 5500, d_clips: 8000, d_foryou: 12000, d_streak: 22000, d_refresh: 40000, d_end: 65000,
  b_frame: 3500, b_caption: 5500, b_story: 8000, b_radio: 12000, b_fill: 22000, b_ink: 40000, b_memory: 65000,
  /* Rows behind Rest or Tap never sit under it. */
  s_good: 4200, s_rest: 4500, t_orders: 4200, t_denied: 4200, t_count: 4500, quiz: 4500, melt: 5000,
};
/* The last buy: at the rate a run ends on, a bigger one is more than a minute of waiting with nothing else left to buy. */
const END_COST = 1700000;
const THEME_LANES = ['trance', 'bimbo', 'drone', 'sissy', 'goon', 'locked', 'censored', 'infection'];
/* Head just after the pick, then the theme's next rows priced so the pick is felt before the podcast. */
const HEAD_COST = 2500, THEME_EARLY = [3800, 6000, 9000], POD_COST = 12000;
const moved = (r, cost) => {
  const k = (cost / r.cost) ** 0.8;
  return { ...r, cost, ...(r.fps ? { fps: Math.round(r.fps * k) } : {}), ...(r.click ? { click: Math.round(r.click * k) } : {}) };
};
function moveRows(rows) {
  const out = rows.map((r) => (TRUNK_MOVES[r.id] ? moved(r, TRUNK_MOVES[r.id]) : r.kind === 'end' && r.cost === 2000000 ? { ...r, cost: END_COST } : r));
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
    const rest = chain.filter((r) => r !== head && r !== pod).map((r) => byId.get(r.id)).sort((a, b) => a.cost - b.cost);
    THEME_EARLY.forEach((cost, i) => { if (rest[i]) byId.set(rest[i].id, moved(rest[i], cost)); });
  }
  return out.map((r) => byId.get(r.id));
}

/* Row text that named a multiple. Modifiers bend past a knee (sim/economy.js), so a row says more, never how many times. */
const WHAT_OVERRIDE = {
  /* Pop-up questions have four answers now, in one fixed spot. */
  quiz: 'A four-answer pop quiz follows your chosen theme. The intended answer pays more, silly answers pay a little. Three right answers complete a lesson.',
  swap: 'Answers trade places when your pointer gets close.',
  flee: 'The wrong answers slide away from your finger.',
  crowd: 'Fake answer buttons multiply across the card, and the real four hide among them.',
  slider: 'A slider picks between the four answers, and it leans toward the top.',
  shrink: 'The right answer grows while the wrong ones shrink.',
  fadeno: 'The wrong answers fade out but can still be tapped.',
  late: 'The other answers appear only after a long wait.',
  holdno: 'Every wrong answer takes a long hold, while the right one takes one tap.',
  dragno: 'Drag cards out of the way to find the other answers.',
  stackno: 'Each wrong answer peels away to reveal itself again underneath.',
  onlyyes: 'Every question still has four answers. All of them are now yes.',
  levels: 'An XP bar keeps filling, and one press collects every level for a bonus.',
  softw: 'Some taps pay extra, a word fades in on the button, and you hear a moan.',
  heartbtn: 'The button becomes a squishy heart.',
  t_denied: 'Some taps are refused, and the next one pays more.',
  t_grade: 'Every two minutes you get a score, and a top score raises earnings for a while.',
  si_set: 'Dressing the button in a full matching set raises its pay.',
  x_later: 'Pick Tap and Chastity: refused taps and purchases pay more.',
  v_frame: 'A framed picture appears and pays every second. Drag it anywhere.',
  d_scroll: 'Swipe through a picture feed. Every swipe counts as a tap, it pays every second, and its bar drags it anywhere.',
  /* Copy the design source promises and the game does not do, or no longer does. */
  remote: 'Preset buttons that poke your own run. No friend is on the other end.',
  z_shuffle: 'A little board of side unlocks that shuffles itself on the credits.',
  tr_head: 'Trance pictures join the page, general hypnosis first.',
  bi_head: 'Bimbo pictures join the page, and CCP ads now mention the Bambi mod.',
  dr_head: 'Drone pictures join the page, and CCP ads now mention Drone mode.',
  si_head: 'Sissy pictures join the page, and CCP ads now mention the Sissy mod.',
  go_head: "Goon pictures join the page, and CCP ads now mention Goon, CCP's game.",
  lo_head: 'Chastity pictures join the page, and CCP ads now mention its chastity features.',
  ce_head: 'Pictures join the page, hidden behind bars.',
};

/* Trunk rows walk in TRUNK_ORDER, in the slots trunk rows already hold; every other row stays where it is. */
function walkOrder(rows) {
  const byId = new Map(rows.map((r) => [r.id, r]));
  const next = TRUNK_ORDER.map((id) => byId.get(id));
  if (next.some((r) => !r)) throw new Error('TRUNK_ORDER names a missing row');
  let i = 0;
  return rows.map((r) => (r.lane === 'trunk' ? next[i++] : r));
}

const groupOf = (r) => (r.lane === 'meta' ? 'meta' : r.lane === 'after' ? 'after' : 'run');

export function buildItems() {
  const rows = walkOrder(moveRows([...read('rows.json').filter((r) => !DROPPED.has(r.id)), ...EXTRA_ROWS])), copy = read('copy.json'), lanes = read('lanes.json');
  const items = rows.map((r) => {
    const c = copy.items[r.id] || r;   // the design rows carry old names too: copy.json wins, and only the rows it does not know speak for themselves
    if (!c) throw new Error(`copy.json has no ${r.id}`);
    const it = { id: r.id, emoji: r.emoji, name: cur(c.name), what: cur(WHAT_OVERRIDE[r.id] || c.what), line: cur(r.line), cost: r.cost, lane: r.lane, req: REQ_OVERRIDE[r.id] || r.req, pay: r.pay };
    const fps = r.fps || EXTRA_PAY[r.id]?.fps;
    if (fps) it.fps = planPay(fps, r.lane, r.cost);
    if (r.click) it.click = planPay(r.click, r.lane, r.cost);
    if (r.wall) it.wall = r.wall;
    if (r.flags) it.flags = r.flags;
    if (r.any) it.any = r.any;
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
