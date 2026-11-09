// dev/gen-items.mjs - data/items.js and ui/demos/*.js from the design source. Never edit those by hand.
//   node dev/gen-items.mjs            (source: env FUZZ_DESIGN, else the handoff folder below)
// Final names and one-liners come from copy.json; numbers, lanes and req come from rows.json.
import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';
import { COPY_MARKS, markCopy } from './copy-marks.mjs';

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
  onemore: ['f5'], outside: ['overlay'], tr_feed: ['tr_still'],
  /* Owner, 2026-11-02: the theme comes early and colours everything, pictures right after it. No Heartbeat row. */
  f5: ['ticker'], f3: ['f5'], depth: ['f3'], f2: ['depth'], banner: ['f2'], f4: ['banner'], glossy: ['f4'], taste2: ['glossy'], em_head: ['f2'], quiz: ['em_head'],
  /* Owner 2026-11-07: no code-drawn button dress-ups. Their dependents move to what the dropped rows needed. */
  paint: ['trail'], bigbtn: ['corner'], si_feed: ['si_head'], si_curtsy: ['si_feed'],
  /* Owner 2026-11-08: three new Sissy rows fill the gap the dress-ups left between House Rules and Mirror Check. */
  si_mirror: ['si_notes'],
  /* Owner 2026-11-09: Phone Buzz is never offered where nothing can buzz (ui/platform.js), so Fake Inbox opens beside it
   * instead of behind it; otherwise a desktop run could never reach the inbox chain. */
  inbox: ['levels'],
  /* Owner 2026-11-10: Mini Slots come earlier (about minute 11), right after the pictures; the Dispenser still follows it. */
  mini_slots: ['f3'],
};
/* The main path in the order it is walked: E.goal takes the first unowned row, so ITEMS keeps this order. */
const TRUNK_ORDER = ['start', 'stray', 'fpsv', 'f1', 'softw', 'ticker', 'f5', 'f3', 'depth', 'f2', 'banner', 'f4', 'glossy', 'taste2', 'overlay', 'takeover', 'wallpaper', 'tunnel', 'drop'];
/* Rows that only work on some devices (owner 2026-11-09). ui/platform.js decides; a save that owns one keeps it.
 * toys: the host said a toy is connected at run start (caps.toysConnected, only the CCP desktop app; owner 2026-11-10).
 * mouse: a pointer with a right button. */
const DEVICE = { buzz: 'toys', toy: 'toys', dualwield: 'mouse' };
/* remote: Friend Remote is cut (owner 2026-11-10); nothing requires it, and a save that owns it loads without it. */
const DROPPED = new Set(['heart', 'tr_breath', 'watch', 'heartbtn', 'mirror', 'spirbtn', 'si_bow', 'si_ward', 'si_set', 'remote']);
/* Dropped before the theme rows are re-priced, so the rows left behind them close the gap (owner 2026-11-07). */
/* The dropped button looks paid per tap: the look rows left carry that pay, so tapping runs keep their pace. */
const CLICK_AFTER_DROP = { paint: 6, corner: 22, bigbtn: 67, tinybtn: 100 };
const DROPPED_FIRST = new Set(['si_bow', 'si_ward', 'si_set']);
/* Rows added after the theme re-pricing, at their final prices: the re-pricing sorts a theme by design cost and would move
 * them. Owner 2026-11-08: Sissy gets its 85000 / 110000 / 300000 rows back (every other theme has rows there). Pay is the
 * raw design number; planPay brings it to what the other themes' rows at that price pay. */
const LATE_ROWS = [
  { after: 'si_rules', id: 'si_track', emoji: '📦', name: 'Order Tracking', what: 'The button becomes a parcel tracker for one frilly uniform. Every tap moves it one step closer to your door.', line: 'Signed for, by you.', cost: 85000, lane: 'sissy', req: ['si_rules'], pay: 'c', click: 80 },
  { after: 'si_track', id: 'si_posture', emoji: '🪑', name: 'Posture Check', what: 'Now and then a card asks you to sit up straight. Hold still for three seconds and it pays.', line: 'Shoulders back. Stay.', cost: 110000, lane: 'sissy', req: ['si_track'], pay: 'f', fps: 366 },
  { after: 'si_posture', id: 'si_notes', emoji: '💌', name: 'Thank-You Notes', what: 'A big purchase brings a small handwritten note thanking you for being so good.', line: 'With gratitude, and a little lace.', cost: 300000, lane: 'sissy', req: ['si_posture'], pay: 'f', fps: 566 },
];
function addLate(rows) {
  const out = [...rows];
  for (const r of LATE_ROWS) {
    const { after, ...row } = r;
    const at = out.findIndex((x) => x.id === after);
    if (at < 0) throw new Error('LATE_ROWS: no row ' + after);
    out.splice(at + 1, 0, row);
  }
  return out;
}
/* Rows that came after the design source. A row with `any` needs at least one of those as well as all of `req`. */
const EXTRA_ROWS = [
  { id: 'd_autoscroll', emoji: '📲', name: 'Auto-Scroll', what: 'The open Swipe Feed turns to the next post every 10 to 15 seconds. Swipe at any time or pause it on the feed.', line: 'One more, hands free.', cost: 4000, lane: 'scroll', req: ['d_scroll'], pay: '-' },
  { id: 'score_heat', emoji: '🔥', name: 'Score Heat', what: 'A streak swells the score and lights a fire behind it in your theme colors. A big payout sends a shimmer through the digits.', line: 'That one counted.', cost: 6200, lane: 'look', req: ['f3'], pay: '-' },
  { id: 'starburst', emoji: '🌟', name: 'Starburst', what: 'Stardust grows into a denser trail with larger, brighter stars.', line: 'Your cursor has a following.', cost: 6000, lane: 'look', req: ['trail'], pay: '-' },
  { id: 'word_comet', emoji: '☄️', name: 'Word Comet', what: 'Words in your theme colors follow soft gold and cyan sparks. Stop moving and the words coast to a fade.', line: 'The words follow you home.', cost: 12000, lane: 'look', req: ['starburst'], pay: '-' },
  /* Owner 2026-11-08: a mid-game power-up. Right-click presses the button too (ui/hands.js). Right after Second Theme, about minute 27. */
  { id: 'dualwield', emoji: '🖱️', name: 'Double Wielding', what: 'The right mouse button taps too. Press both at once for two taps.', line: 'Two fingers. Zero thoughts.', cost: 60000, lane: 'look', req: ['taste2'], pay: '-' },
  { id: 'sp_colors', emoji: '🎨', name: 'Spiral Colours', what: 'The spiral slowly drifts through your colours.', line: 'It will not sit still.', cost: 7000, lane: 'look', req: [], any: ['e_spiral', 'a_spiral'], pay: '-' },
  { id: 'sp_shapes', emoji: '🧿', name: 'Spiral Shapes', what: 'The spiral keeps melting into new shapes.', line: 'Follow it anyway.', cost: 20000, lane: 'look', req: ['sp_colors'], pay: '-' },
  { id: 'lofi_radio', emoji: '📻', name: 'Lo-fi Radio', what: 'A little desk radio plays every track, starting with your chosen theme.', line: 'Your side of the dial.', cost: 5000, lane: 'side', req: ['f5'], pay: '-' },
  { id: 'guide', emoji: '🗺️', name: 'Guide', what: 'Open a map of every unlock and where it leads.', line: 'Now you can see what you skipped.', cost: 9000, lane: 'side', req: ['screen'], pay: '-' },
  /* Owner 2026-11-07: the Control Panel comes much sooner (an icon beside the volume button), not after Spiral Overlay. */
  { id: 'panel', emoji: '🎛️', name: 'Control Panel', what: 'Turn anything you have unlocked on or off.', line: 'You were always in control. Sure.', cost: 12000, lane: 'side', req: ['guide'], pay: '-' },
  { id: 'toy_box', emoji: '', name: 'Toy Box', what: 'Opens every 15-30 seconds. Fit the pieces before it closes. Each completed box pays more.', line: 'Everything in its place.', cost: 2500, lane: 'side', req: ['f5'], pay: '-' },
  { id: 'mini_breakout', emoji: '', name: 'Mini Breakout', what: 'An endless little Breakout plays itself. Hover to take the paddle. Clearing a board pays, and playing earns extra.', line: 'Just one more brick.', cost: 4800, lane: 'side', req: ['f2'], pay: '-' },
  { id: 'mini_race', emoji: '', name: 'Racing Thoughts', what: 'Watch the kart race on five courses in rotation. Collect a bonus when the timer is ready.', line: 'Watch the road.', cost: 7500, lane: 'side', req: ['f3'], pay: '-' },
  /* Owner 2026-11-07: Racing Thoughts arrives earlier (req f3, was f4) and The Deep End joins the arcade on the pictures already loaded. */
  { id: 'mini_deep', emoji: '', name: 'The Deep End', what: 'A small grid game. Match the pictures. Big scores set off flashes.', line: 'Further in.', cost: 9000, lane: 'side', req: ['f3'], pay: '-' },
  { id: 'mini_slots', emoji: '', name: 'Mini Slots', what: 'A free coin every 45 seconds, with room for three. Spin for pictures, spirals and prizes. Two wins in every six spins.', line: 'The house is feeling generous.', cost: 12500, lane: 'side', req: ['glossy'], pay: '-' },
  { id: 'dispenser', emoji: '', name: 'Prize Dispenser', what: 'Catch a colored capsule: red Flashes or cyan Subliminals (45% each), purple Mind Drain (8%, 4x prize), or gold Jackpot (2%, 10x, both bursts). Catch within 2.5 seconds; crank every 30 seconds.', line: 'Catch it before it goes.', cost: 22000, lane: 'side', req: ['mini_slots'], pay: '-' },
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
  s_good: 4200, s_rest: 4500, t_orders: 4200, t_denied: 4200, t_count: 4500, em_head: 4300, quiz: 4500, melt: 5000,
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
const BUTTON_NAMES = {
  bi_lips:'Bambi Cloud Button', dr_hex:'Firmware Download', tr_skin:'Fortune 8-Ball', go_worn:'Still Watching',
  lo_pad:'Keyholder Support', ce_bar:'Redacted Button',
};
const WHAT_OVERRIDE = {
  bi_lips:'Sign in with Bambi Cloud. The button already knows your name.',
  dr_hex:'Tap to download drone firmware. Tap again to install the user.',
  tr_skin:'A fortune ball swings on its chain. Tap on center for a reading and extra click income.',
  go_worn:'The player asks if you are still watching. Every tap starts one more episode.',
  lo_pad:'Tap for a keyholder support ticket and its reply.',
  ce_bar:'Tap the redaction to uncover a fresh comment about the person clicking.',
  bubble:'Wear the CCP bubble and open the Button Shop. Choose main-button and bouncing artwork separately.',
  stray: 'Each wall bounce earns its usual {cur} plus 1% of one second of base passive income.',
  rounded: 'Rounds the button and adds 1 per tap. Manual taps also earn 0.5% of one second of base passive income.',
  font: 'Fuller, rounded letters across the game. Headlines and controls pick up the colors of your mods.',
  trail: 'Fine dust and tiny sparkles follow your cursor. Opens Starburst.',
  a_tick: 'Drag the gold weight to set the tempo. Up slows it down; down speeds it up.',
  /* Pop-up questions have four answers now, in one fixed spot. */
  em_head: 'EMI moves onto your desk. Drag her to pick a spot.',
  quiz: 'EMI asks four-answer questions about your chosen theme. The intended answer pays more, silly answers pay a little. Three right answers complete a lesson.',
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
  wheel: 'Spin free every 90 seconds. Wider slices are more likely to land. Prizes range from 0.5x to 10x, with a 20% chance of Nothing.',
  softw: 'Some taps pay extra, a word fades in on the button, and you hear a moan.',
  t_denied: 'Some taps are refused, and the next one pays more.',
  t_grade: 'Every two minutes you get a score, and a top score raises earnings for a while.',
  x_later: 'Pick Tap and Chastity: refused taps and purchases pay more.',
  v_frame: 'A framed picture appears and pays every second. Drag it anywhere.',
  d_scroll: 'Swipe through a picture feed. Every swipe counts as a tap, it pays every second, and its bar drags it anywhere.',
  /* Copy the design source promises and the game does not do, or no longer does. */
  remote: 'Preset buttons that poke your own run. No friend is on the other end.',
  /* TOYS lane (owner 2026-11-10): both toy rows are offered only with a toy connected in the CCP desktop app; Big grows in place. */
  buzz: 'Your connected toy buzzes on lucky taps.',
  toy: 'A toy you connected in the CCP desktop app follows how fast you earn.',
  bigbtn: 'The button grows to fill its whole spot. Nothing else on the page moves.',
  z_shuffle: 'A little board of side unlocks that shuffles itself on the credits.',
  tr_head: 'Trance pictures join the page, general hypnosis first.',
  bi_head: 'Bimbo pictures join the page, and CCP ads now mention the Bambi mod.',
  dr_head: 'Drone pictures join the page, and CCP ads now mention Drone mode.',
  si_head: 'Sissy pictures join the page, and CCP ads now mention the Sissy mod.',
  go_head: "Goon pictures join the page, and CCP ads now mention Goon, CCP's game.",
  lo_head: 'Chastity pictures join the page, and CCP ads now mention its chastity features.',
  ce_head: 'Pictures join the page, hidden behind bars.',
};

/* ---- INTAKE lane (owner 2026-11-10, late game wave): the question tricks sell as three stages. ----
 * Each stage is one purchase: its lead (renamed) brings the rows listed in `fold` (data/folds.js reads the field), and
 * `qstage` tells the quiz how often to ask and how big the per-answer bonus is (ui/unlocks/extras/quiz-stages.js).
 * `total` is the stage's price before CONFIG.PRICE_SCALE: its rows' design costs are scaled to it, keeping their order,
 * so on the 4-tap reference stage 1 lands about minute 22-25, stage 2 about 31-35, stage 3 about 46-49.
 * Per-second pay (fps) stays each row's own. Other lanes: keep your overrides out of this table. */
const INTAKE_STAGES = {
  melt: {
    qstage: 1, total: 45000, fold: ['swap', 'flee', 'nag', 'crowd', 'pbubble'],
    name: 'Softer Answers',
    what: 'Questions come about once a minute, and every answer pays a bonus. Wrong answers start to melt, swap places and run.',
    line: 'Easier to agree with.',
  },
  slider: {
    qstage: 2, total: 450000, fold: ['shrink', 'fadeno', 'magnet', 'late', 'holdno', 'dragno', 'stackno', 'robot'],
    name: 'Leaning Answers',
    what: 'Questions come about every 45 seconds, and the answer bonus grows. The card leans, pulls and hides the other answers.',
    line: 'It tilts your way.',
  },
  grid: {
    qstage: 3, total: 1600000, fold: ['ttype', 'regen', 'onlyyes', 'hijack'],
    name: 'Nothing But Yes',
    what: 'Questions come about every 35 seconds, with the biggest answer bonus. Picture checks, wobbly text, then every answer is yes.',
    line: 'One answer, four ways.',
  },
};
function intakeStages(items) {
  const by = new Map(items.map((it) => [it.id, it]));
  for (const [lead, st] of Object.entries(INTAKE_STAGES)) {
    const rows = [lead, ...st.fold].map((id) => by.get(id));
    if (rows.some((r) => !r)) throw new Error('INTAKE_STAGES names a missing row under ' + lead);
    const sum = rows.reduce((t, r) => t + r.cost, 0);
    /* Never cheaper than the row before it (tests/items.test.mjs), so a stage costs at least its rows at the parent price. */
    let floor = by.get(rows[0].req[0])?.cost || 0;
    for (const r of rows) { r.cost = Math.max(floor, Math.round((r.cost * st.total) / sum / 100) * 100); floor = r.cost; r.qstage = st.qstage; }
    Object.assign(by.get(lead), { name: st.name, what: st.what, line: st.line, fold: st.fold });
  }
  return items;
}

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
  const rows = walkOrder(addLate(moveRows([...read('rows.json'), ...EXTRA_ROWS].filter(r => !DROPPED_FIRST.has(r.id))).filter(r => !DROPPED.has(r.id)))), copy = read('copy.json'), lanes = read('lanes.json');
  const items = rows.map((r) => {
    const c = copy.items[r.id] || r;   // the design rows carry old names too: copy.json wins, and only the rows it does not know speak for themselves
    if (!c) throw new Error(`copy.json has no ${r.id}`);
    const it = { id: r.id, emoji: r.emoji, name: BUTTON_NAMES[r.id] || (r.id === 'trail' ? 'Stardust' : cur(c.name)), what: cur(WHAT_OVERRIDE[r.id] || c.what), line: cur(r.line), cost: r.cost, lane: r.lane, req: REQ_OVERRIDE[r.id] || r.req, pay: r.pay };
    const fps = r.fps || EXTRA_PAY[r.id]?.fps;
    if (fps) it.fps = planPay(fps, r.lane, r.cost);
    if (r.click) it.click = planPay(r.click, r.lane, r.cost);
    if (CLICK_AFTER_DROP[r.id]) { it.click = CLICK_AFTER_DROP[r.id]; if (it.pay === '-') it.pay = 'c'; }
    if (r.wall) it.wall = r.wall;
    if (r.flags) it.flags = r.flags;
    if (r.any) it.any = r.any;
    /* Keywords and diegetic colours (owner 2026-11-08): laid on here, so the design source stays plain. */
    const mk = COPY_MARKS[r.id];
    if (mk) { it.what = markCopy(it.what, mk.what, `${r.id}.what`); it.line = markCopy(it.line, mk.line, `${r.id}.line`); }
    it.kind = r.kind || 'item';
    it.group = groupOf(r);
    if (GROW[r.id]) it.grow = GROW[r.id];
    if (DEVICE[r.id]) it.device = DEVICE[r.id];
    return it;
  });
  intakeStages(items);
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

/* A dropped row's demo goes with it: the design source still has it, the game never shows it. */
function dropDemos(src) {
  const lines = src.split('\n'), out = [];
  let skipping = false;
  for (const line of lines) {
    const key = /^  ([a-z0-9_]+): \(k\)/.exec(line);
    if (key) skipping = DROPPED.has(key[1]);
    else if (/^\S/.test(line)) skipping = false;
    if (!skipping) out.push(line);
  }
  return out.join('\n');
}

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
    const src = dropDemos(demoCur(readFileSync(join(DESIGN, 'demos', `${f}.js`), 'utf8').replace(/^\s*\/\/.*$/gm, '')));
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
