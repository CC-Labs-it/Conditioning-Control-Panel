/* ui/guide-world-model.js - the Guide as a board-game overworld (owner 2026-11-08, v3 mockup acked; replaces The Road).
 * A trunk road runs top to bottom in price order; every fork is a signpost with one plank per route; every route is an island
 * split into typed rows (one step along a row = the next upgrade of that kind); every-run lanes are pockets on a footpath; the
 * two shops are a pocket of their own. Routes not taken stay on the map, dimmed. The layout depends only on the catalog, so it
 * is built once; states come from the run. Pure data, no DOM: tested in tests/guide-world.test.mjs. Source of the layout:
 * handoff-1108/guide-map/gen5.mjs. */

import { BY_ID, FORKS, FORK_OF_LANE, ITEMS, LANES } from '../data/items.js';
import { trailCollection, stagePrices } from '../data/trail-collection.js';
import { ICON_CATALOG } from './icon-catalog.js';
import { THEME_PALETTES } from './palette.js';
import * as E from '../sim/economy.js';
import { gateLine, gateOf, gateOpen } from './shop-gates.js';

/* ---- types: what kind of thing a power-up is. Shape family first, then per-item corrections read off the names. ---- */
export const TYPES = Object.freeze({
  theme: ['Theme', 'tag'], end: ['Ending', 'sunrise'], podcast: ['Podcast', 'mic'],
  button: ['Button', 'button'], cursor: ['Cursor', 'cursor'], page: ['Page', 'palette'], number: ['Numbers', 'numbers'],
  pop: ['Pops', 'burst'], word: ['Words', 'speech'], spiral: ['Spirals', 'spiral'], picture: ['Pictures', 'frame'],
  sound: ['Sounds', 'speaker'], voice: ['Voice', 'mic'], music: ['Music', 'radio'], game: ['Mini games', 'brickgame'],
  prize: ['Prizes', 'wheel'], toy: ['Toys', 'remote'], progress: ['Progress', 'stairs'], social: ['Fake social', 'phone'],
  deep: ['Deeper', 'ink'], mascot: ['EMI moods', 'mascot'], play: ['Play with EMI', 'hand'], ad: ['Ads', 'megaphone'],
  perk: ['Perks', 'crown'], mood: ['Mood', 'moon'], time: ['Time', 'clock'], answer: ['Answers', 'checklist'],
  refuse: ['Saying no', 'hand'], check: ['Checks', 'robot'], tap: ['Tapping', 'hand'], rest: ['Resting', 'moon'],
  feed: ['Feed', 'phone'], story: ['Stories', 'book'], gaze: ['Gaze', 'eye'], rule: ['Rules', 'scroll'], lock: ['Lock', 'lock'],
  video: ['Clips', 'film'], sneaky: ['Sneaky answers', 'magnet'], bug: ['Spots', 'bug'], bar: ['Black bars', 'terminal'], drone: ['Unit', 'robot'], exit: ['Exits', 'sunrise'],
  face: ['Button styles', 'button'], themeface: ['Theme faces', 'tag'], wave: ['Ribbons', 'speaker'], kinetic: ['Kinetic waves', 'metronome'],
  pointer: ['Cursors', 'cursor'], trail: ['Trails', 'cursor'], family: ['Trail families', 'palette'],
});
const SHAPE_TYPE = {
  button: 'button', lipstick: 'button', paddle: 'button', cursor: 'cursor', palette: 'page', numbers: 'number', chart: 'number', meter: 'number',
  burst: 'pop', confetti: 'pop', bubbles: 'pop', speech: 'word', words: 'word', letter: 'word', scroll: 'word', tag: 'word', book: 'story',
  spiral: 'spiral', tunnel: 'spiral', frame: 'picture', gallery: 'picture', film: 'video', phone: 'feed', speaker: 'sound', headphones: 'sound',
  metronome: 'sound', rain: 'sound', mic: 'voice', radio: 'music', brickgame: 'game', box: 'prize', wheel: 'prize', slotmachine: 'prize', dispenser: 'prize',
  remote: 'toy', mascot: 'mascot', hat: 'play', megaphone: 'ad', billboard: 'ad', crown: 'perk', card: 'perk', jewel: 'perk', wallet: 'perk',
  moon: 'mood', lamp: 'mood', clock: 'time', envelope: 'social', ink: 'deep', drop: 'deep', cloud: 'deep', checklist: 'answer', papers: 'answer',
  hand: 'tap', stairs: 'progress', sign: 'progress', heart: 'feed', eye: 'gaze', mirror: 'gaze', robot: 'drone', terminal: 'bar', lock: 'lock', key: 'lock',
  begging: 'lock', bug: 'bug', shield: 'bug', link: 'social', people: 'social', bow: 'theme', dress: 'rule', headset: 'perk', bin: 'ad', sunrise: 'end', infinity: 'end',
  pulse: 'end', shoe: 'answer', shrink: 'answer', eraser: 'refuse', magnet: 'answer', shovel: 'refuse', dice: 'prize', bubblesx: 'pop',
};
const OVERRIDE = {
  title: 'page', font: 'page', colour: 'page', paint: 'page', dark: 'page', sound: 'pop', wallpop: 'pop', confetti: 'pop', corner: 'pop', bubble: 'button',
  sp_colors: 'spiral', sp_shapes: 'spiral', score_heat: 'number', float: 'number', counter: 'number',
  gold: 'progress', screen: 'progress', levels: 'progress', guide: 'progress', panel: 'progress', toy_box: 'toy', toy: 'toy', remote: 'toy',
  buzz: 'social', inbox: 'social', fakechat: 'social', flash: 'deep', drain: 'deep', sparkle: 'deep', triggers: 'deep', opinions: 'deep', listen: 'deep',
  mini_breakout: 'game', mini_race: 'game', mini_deep: 'game', lofi_radio: 'music',
  e_stare: 'gaze', e_pulse: 'gaze', e_last: 'gaze', a_hum: 'sound', a_bin: 'sound', a_rain: 'sound', a_tick: 'sound', a_phones: 'sound', a_close: 'sound',
  a_whisp: 'voice', a_voice: 'voice', a_chant: 'voice', a_spiral: 'spiral', dualwield: 'tap',
  quiz: 'answer', melt: 'answer', swap: 'answer', flee: 'answer', nag: 'answer', crowd: 'sneaky', pbubble: 'sneaky', slider: 'sneaky', shrink: 'sneaky', magnet: 'sneaky',
  fadeno: 'refuse', late: 'refuse', holdno: 'refuse', dragno: 'refuse', stackno: 'refuse', onlyyes: 'refuse',
  robot: 'check', grid: 'check', ttype: 'check', regen: 'check', hijack: 'check',
  em_head: 'mascot', em_jeal: 'mascot', em_mini: 'mascot', em_nap: 'mascot', em_play: 'mascot', em_boss: 'mascot', em_pet: 'play', em_chat: 'play', em_hat: 'play', em_call: 'play',
  s_good: 'word', s_good2: 'word', s_rest: 'rest', s_lull: 'music', s_tuck: 'sound', s_glow: 'mood', s_nap: 'rest', s_blanket: 'page',
  t_orders: 'rule', t_type: 'rule', t_grade: 'rule', t_denied: 'tap', t_count: 'tap', t_faster: 'tap', t_still: 'tap', t_corner: 'button',
  d_scroll: 'feed', d_foryou: 'feed', d_refresh: 'feed', d_autoscroll: 'feed', d_like: 'tap', d_streak: 'tap', d_clips: 'video', d_end: 'number',
  b_frame: 'picture', b_caption: 'word', b_fill: 'word', b_story: 'story', b_memory: 'story', b_radio: 'music', b_ink: 'picture',
  o_skip: 'time', o_emi: 'ad', o_video: 'ad', p_member: 'ad', p_skip: 'ad', p_remove: 'ad', p_badge: 'perk', p_gold: 'perk', p_renew: 'perk', p_line: 'perk',
  fpsv: 'number', depth: 'progress', softw: 'tap', ticker: 'word', banner: 'ad', glossy: 'page', taste2: 'theme', overlay: 'spiral', takeover: 'page', wallpaper: 'picture', tunnel: 'spiral',
  bi_lips: 'button', bi_giggle: 'voice', bi_gloss: 'page', tr_skin: 'prize', tr_still: 'rest', tr_fade: 'page', tr_deep: 'rest', dr_serial: 'drone', dr_term: 'page',
  dr_hex: 'drone', dr_auto: 'tap', dr_hive: 'feed', dr_update: 'check', si_curtsy: 'rule', si_posture: 'rule', si_notes: 'word', si_track: 'social', si_mirror: 'gaze', si_class: 'rule',
  go_hold: 'tap', go_worn: 'tap', go_edge: 'number', go_tabs: 'feed', go_cave: 'mood', go_clock: 'time', go_pals: 'social',
  lo_timer: 'time', lo_beg: 'lock', lo_key: 'lock', lo_pad: 'lock', lo_refuse: 'lock', lo_tease: 'number', lo_wheel: 'prize',
  ce_bar: 'bar', ce_blur: 'picture', ce_nums: 'number', ce_peek: 'gaze', ce_wall: 'perk', ce_text: 'bar', ce_all: 'bar',
  in_spot: 'bug', in_spread: 'bug', in_tile: 'bug', in_cure: 'check', in_fever: 'number', in_share: 'social', in_mut: 'bug',
  x_story: 'story', x_audio: 'voice', x_drill: 'voice', x_listener: 'voice', x_dream: 'deep', x_nothing: 'deep', x_induct: 'spiral', x_gallery: 'picture',
  x_front: 'word', x_what: 'word', x_infinite: 'ad', x_viral: 'ad', x_later: 'perk', x_compl: 'perk', x_manners: 'perk', x_fourhr: 'tap',
  outside: 'exit', onemore: 'exit',
};
export function typeOf(it) {
  const shape = ICON_CATALOG[it.id]?.shape;
  if (it.kind === 'end') return FORK_OF_LANE[it.lane] === 'f5' ? 'theme' : 'end';
  if (/_head$/.test(it.id) && FORK_OF_LANE[it.lane] === 'f5') return 'theme';
  if (/_pod$/.test(it.id)) return FORK_OF_LANE[it.lane] === 'f5' ? 'theme' : 'podcast';
  if (OVERRIDE[it.id]) return OVERRIDE[it.id];
  return SHAPE_TYPE[shape] || 'perk';
}
const TYPE_RANK = { theme: -2, podcast: -1, end: 99, exit: 99, face: 0, themeface: 1, wave: 2, kinetic: 3, pointer: 4, trail: 5, family: 6 };

export const LANE_COLOUR = Object.freeze({
  eyes: '#6fc3ff', ears: '#c39cf3', slides: '#ffcf5a', scroll: '#5fd3b6', blind: '#b9b3ad',
  sweet: '#ffb0a3', strict: '#ff8b5e', sell: '#f6c544', premium: '#8fd49f',
  look: '#ff9fd0', side: '#ffd98a', emi: '#ff6fb5', trouble: '#9fb0ff', combo: '#d9b6ff', exit: '#c9c3bd', shops: '#ffb86b',
  ...Object.fromEntries(Object.entries(THEME_PALETTES).map(([id, p]) => [id, p.a])),
});

/* ---- the Shops pocket: what the Button Shop and Cursor Shop sell, pictured from the shops themselves ---- */
const FACE_ART = 'assets/guide/shops/';
const THEME_FACE_ITEM = { 'bi-lips': 'bi_lips', 'dr-hex': 'dr_hex', 'tr-pendulum': 'tr_skin', worn: 'go_worn', padlock: 'lo_pad', censorbar: 'ce_bar', 'si-parcel': 'si_track' };
const FACE_NAMES = { 'bi-lips': 'Bambi Cloud', 'dr-hex': 'Drone Firmware', 'tr-pendulum': 'Fortune 8-Ball', worn: 'Still Watching', padlock: 'Keyholder Support', censorbar: 'Redacted', 'si-parcel': 'Order Tracking' };
const WAVES = [['classic', 'Classic', 0], ['aurora', 'Velvet Aurora', 1200], ['loom', 'Phase Loom', 2800], ['mercury', 'Mercury Tide', 4800], ['mycelium', 'Neon Mycelium', 7500], ['prism', 'Spectral Shear', 12000]];
const KINETIC = [['siri9', 'iOS 9 SiriWave', 6000], ['harp', 'Resonance Harp', 9000], ['chroma', 'Chroma Cycle', 15000], ['pyramid', 'Swimming Pyramid', 18000]];
const CURSORS = [['system', 'System pointer', 0], ['cursor', 'Pink Cursor', 1200, 'cursor'], ['cursor_prism', 'Prism Cursor', 3600], ['cursor_nova', 'Nova Cursor', 5400]];
const TRAILS = [['trail', 'Stardust', 'trail'], ['starburst', 'Starburst', 'starburst'], ['word_comet', 'Word Comet', 'word_comet']];

/** Every shop good: { id, name, cost, type, pic, what, item?, own(run) }. */
export const SHOP_GOODS = Object.freeze((() => {
  const out = [];
  const bag = (run) => run.store?.bubble || {};
  const kit = (run) => run.store?.cursorKit || {};
  /* A good behind a shop gate (ui/shop-gates.js) says which power-up opens it, and stays 'later' on the map until then. */
  /* Anything sold in the Button Shop also needs its door, the Bubble Button. */
  const good = (o) => out.push(Object.freeze({ lane: 'shops', ...(!o.item && o.cost > 0 && o.what.includes('Button Shop') ? { needs: 'bubble' } : {}), ...o, ...(o.gate ? { what: o.what + ' ' + gateLine(o.gate) + '.' } : {}) }));
  good({ id: 'shop_face_round', name: 'Plain', cost: 0, type: 'face', pic: FACE_ART + 'face-round.webp', what: 'The button as it comes. Button Shop, Styles.', own: () => true });
  good({ id: 'shop_face_bubble', name: 'CCP Bubble', cost: 0, type: 'face', pic: FACE_ART + 'face-bubble.webp', item: 'bubble', what: 'Comes with the Bubble unlock in Looks. Button Shop, Styles.', own: (r) => E.has(r, 'bubble') });
  good({ id: 'shop_face_human-check', name: 'Human Check', cost: 1500, type: 'face', pic: FACE_ART + 'face-human-check.webp', what: 'Tick the box. Prove you are a person, again and again. Button Shop, Styles.', own: (r) => bag(r).faces?.['human-check'] === true });
  good({ id: 'shop_face_emi', name: 'EMI', cost: 3000, type: 'face', gate: 'emi', pic: 'assets/img/emi/body-pet.png', what: 'Pet her with every tap. She likes a steady hand. Button Shop, Styles.', own: (r) => bag(r).faces?.emi === true });
  for (const [face, item] of Object.entries(THEME_FACE_ITEM)) {
    good({ id: 'shop_face_' + face, name: FACE_NAMES[face], cost: BY_ID[item]?.cost || 0, type: 'themeface', pic: FACE_ART + 'face-' + face + '.webp', item, what: `Comes with ${BY_ID[item]?.name || 'its theme item'}. Button Shop, Styles.`, own: (r) => E.has(r, item) });
  }
  for (const [id, name, cost] of WAVES) good({ id: 'shop_wave_' + id, name, cost, type: 'wave', gate: gateOf('wave', id) || undefined, pic: FACE_ART + 'wave-' + id + '.webp', what: 'The ribbon under the button. Button Shop, Wave.', own: (r) => id === 'classic' || bag(r).waveStyles?.[id] === true });
  for (const [id, name, cost] of KINETIC) good({ id: 'shop_wave_' + id, name, cost, type: 'kinetic', gate: 'kinetic', pic: FACE_ART + 'wave-' + id + '.webp', what: 'A kinetic ribbon that answers every press. Button Shop, Wave.', own: (r) => bag(r).waveStyles?.[id] === true });
  for (const [id, name, cost, item] of CURSORS) good({ id: 'shop_cur_' + id, name, cost: item ? BY_ID[item]?.cost ?? cost : cost, type: 'pointer', gate: gateOf('cursor', id) || undefined, needs: item || id === 'system' ? undefined : 'cursor', pic: 'assets/shop/cursors/' + id + '.png', item, what: 'The pointer you tap with. Cursor Shop, Cursors.', own: (r) => id === 'system' || (item ? E.has(r, item) : kit(r).skins?.[id] === true) });
  for (const [id, name, item] of TRAILS) good({ id: 'shop_trail_' + id, name, cost: BY_ID[item]?.cost || 0, type: 'trail', pic: 'assets/shop/trails/' + id + '-1.webp', item, what: 'What follows the pointer. Cursor Shop, Trails.', own: (r) => E.has(r, item) });
  for (const f of trailCollection) good({ id: 'shop_fam_' + f.id, name: f.name, cost: stagePrices[0], type: 'family', gate: 'families', pic: 'assets/shop/trails/' + f.id + '-1.webp', needs: 'trail', what: `A trail family in three stages: ${f.levels.join(', ')}. Cursor Shop, Trails.`, own: (r) => (kit(r).stages?.[f.id] || 0) >= 1 });
  return out;
})());
export const SHOP_BY_ID = Object.freeze(Object.fromEntries(SHOP_GOODS.map((g) => [g.id, g])));

/* ---- layout ---- */
const RUNG = 88, ROW = 84, STAMP = 112, PAD = 16, TITLE = 26, SPINE = 22, GAP_ISL = 48;
const CX = 1300, LX = CX - 100, RX = CX + 100;
const LAYOUT_LANES = new Set(['look', 'side', 'shops', 'eyes', 'ears', 'bimbo', 'sissy', 'drone', 'trance', 'goon', 'locked', 'censored', 'infection',
  'slides', 'scroll', 'blind', 'sweet', 'strict', 'emi', 'sell', 'premium', 'combo', 'trouble', 'exit']);

let memo = null;
/** The map, built once from the catalog: { W, H, CX, nodes, islands, forks, roads, paths, twigs, trunkD, trunk }. */
export function layoutWorld() {
  if (memo) return memo;
  const order = new Map(ITEMS.map((it, i) => [it.id, i]));
  const run = ITEMS.filter((r) => r.group === 'run').slice().sort((a, b) => a.cost - b.cost || order.get(a.id) - order.get(b.id));
  const byLane = {};
  const nodes = {};
  for (const it of run) {
    const n = { id: it.id, lane: it.lane, cost: it.cost, type: typeOf(it), end: it.kind === 'end', fork: it.kind === 'fork', req: it.req || [], x: null, y: null };
    nodes[it.id] = n;
    (byLane[it.lane] ??= []).push(n);
  }
  byLane.shops = SHOP_GOODS.map((g) => (nodes[g.id] = { id: g.id, lane: 'shops', cost: g.cost, type: g.type, shop: true, pic: g.pic, end: false, fork: false, req: g.item ? [] : [], x: null, y: null }));
  const laneName = Object.fromEntries(LANES.map((l) => [l.id, l.name]));
  laneName.shops = 'Shops';

  const islands = [], trunkNodes = [], forks = [], roads = [], paths = [], twigs = [];
  function ladders(items) {
    const groups = new Map();
    for (const it of items) (groups.get(it.type) || groups.set(it.type, []).get(it.type)).push(it);
    const out = [];
    for (const [type, arr] of groups) {
      arr.sort((a, b) => a.cost - b.cost);
      const ids = new Set(arr.map((a) => a.id));
      const ordered = [];
      const place = (it) => { if (ordered.includes(it)) return; for (const q of it.req) { const p = arr.find((a) => a.id === q); if (p) place(p); } ordered.push(it); };
      arr.forEach(place);
      const rungs = ordered.map((it, i) => ({ it, link: i > 0 ? (it.req.some((q) => ids.has(q)) ? 'solid' : 'dot') : null }));
      out.push({ type, name: TYPES[type]?.[0] || type, shape: TYPES[type]?.[1] || 'box', rungs, min: Math.min(...arr.map((a) => a.cost)), rank: TYPE_RANK[type] ?? 0 });
    }
    out.sort((a, b) => a.rank - b.rank || a.min - b.min);
    return out;
  }
  function island(id, x, y, side, kind, fork, pitch = RUNG) {
    const lads = ladders(byLane[id] || []);
    const maxR = Math.max(1, ...lads.map((l) => l.rungs.length));
    const w = PAD * 2 + STAMP + maxR * pitch;
    const h = PAD * 2 + TITLE + Math.max(1, lads.length) * ROW;
    const isl = { id, name: laneName[id] || id, color: LANE_COLOUR[id] || '#ccc', x, y, w, h, kind, side, fork: fork || null, ladders: [] };
    lads.forEach((l, ri) => {
      const ry = y + PAD + TITLE + ri * ROW + ROW / 2;
      const stampX = side === 'R' ? x + PAD : x + w - PAD - STAMP;
      const lad = { type: l.type, name: l.name, shape: l.shape, x: x + PAD, y: ry - ROW / 2 + 3, w: w - PAD * 2, h: ROW - 6, stampX, cy: ry, side, links: [] };
      l.rungs.forEach((r, i) => {
        r.it.x = side === 'R' ? x + PAD + STAMP + i * pitch + pitch / 2 : x + w - PAD - STAMP - i * pitch - pitch / 2;
        r.it.y = ry;
        if (ri === 0 && i === 0 && kind === 'route') r.it.head = true;
        if (r.link) lad.links.push({ x: side === 'R' ? r.it.x - pitch / 2 : r.it.x + pitch / 2, kind: r.link });
      });
      isl.ladders.push(lad);
    });
    const door = side === 'R' ? x : x + w;
    const spineX = side === 'R' ? door - SPINE : door + SPINE;
    const cy0 = isl.ladders[0]?.cy ?? y + h / 2, cy1 = isl.ladders[isl.ladders.length - 1]?.cy ?? cy0;
    const y0 = kind === 'route' ? y - 14 : cy0;
    isl.spine = { x: spineX, y0, y1: cy1 };
    const tipX = side === 'R' ? x + PAD + 6 : x + w - PAD - 6;
    twigs.push({ lane: id, kind, knots: isl.ladders.map((l) => [spineX, l.cy]), spineX, y0, y1: cy1, tipX });
    islands.push(isl);
    return isl;
  }
  const trunk = (id, y, x = CX) => { const n = nodes[id]; if (!n) return null; n.x = x; n.y = y; trunkNodes.push(n); return n; };
  const footpath = (isl, viaY) => {
    const s = isl.spine;
    paths.push({ lane: isl.id, viaY: viaY ?? null, x: s.x, y0: s.y0, y1: s.y1 });
  };
  function signpost(fid, y, planks) {
    const f = { id: fid, name: FORKS[fid].name, x: CX, y, planks: [] };
    for (const side of ['L', 'R']) {
      planks.filter((p) => p.side === side).forEach((p, i) => {
        const py = y - 6 + i * 32;
        const tipx = side === 'L' ? CX - 138 : CX + 138;
        f.planks.push({ lane: p.lane, name: FORKS[fid].opts[p.lane].name, side, y: py });
        const s = p.island.spine, sx = s.x, sy = s.y0, lx = p.laneX ?? sx, my = py + 14;
        let d = `M${tipx},${my} C${tipx + (lx - tipx) * 0.5},${my} ${lx},${my} ${lx},${Math.min(my + 46, sy - 1)}`;
        d += lx === sx ? ` V${sy}` : ` V${sy - 40} Q${lx},${sy} ${sx},${sy}`;
        roads.push({ lane: p.lane, fork: fid, d });
      });
    }
    forks.push(f);
    trunk(fid, y);
  }
  function shift(isl, nx) {
    const dx = nx - isl.x; isl.x = nx; isl.spine.x += dx;
    for (const l of isl.ladders) { l.x += dx; l.stampX += dx; for (const k of l.links) k.x += dx; }
    for (const n of byLane[isl.id] || []) if (n.x != null) n.x += dx;
    const t = twigs.find((tw) => tw.lane === isl.id);
    t.spineX += dx; t.tipX += dx; t.knots = t.knots.map(([kx, ky]) => [kx + dx, ky]);
  }
  function row(y, left, right) {
    const made = {};
    let x = LX;
    for (const s of left) { const isl = island(s.id, 0, y, 'L', s.kind, s.fork, s.pitch); x -= isl.w + SPINE + 6; shift(isl, x); made[s.id] = isl; x -= GAP_ISL - SPINE; }
    x = RX;
    for (const s of right) { const isl = island(s.id, x + SPINE + 6, y, 'R', s.kind, s.fork, s.pitch); made[s.id] = isl; x += isl.w + SPINE + 6 + GAP_ISL - SPINE; }
    return made;
  }
  function stack(y0, ids, side, fork, door) {
    const made = []; let y = y0;
    ids.forEach((id, k) => {
      let isl;
      if (side === 'L') { isl = island(id, 0, y, 'L', 'route', fork); shift(isl, CX - door - isl.w); }
      else isl = island(id, CX + door, y, 'R', 'route', fork);
      made.push({ lane: id, side, island: isl, laneX: isl.spine.x + (side === 'L' ? 1 : -1) * k * 26 });
      y += isl.h + 70;
    });
    return made;
  }
  const bottomOf = (m) => Math.max(...Object.values(m).map((i) => i.y + i.h));
  const sections = [];

  // 1: the gate. Looks and Side Extras at the entrance; the shops hang under Looks.
  let y = 60;
  trunk('start', 120); trunk('stray', 260); trunk('fpsv', 400);
  let m = row(y, [{ id: 'look', kind: 'pocket' }], [{ id: 'side', kind: 'pocket' }]);
  footpath(m.look); footpath(m.side);
  { const sh = row(m.look.y + m.look.h + 70, [{ id: 'shops', kind: 'pocket', pitch: 118 }], []); footpath(sh.shops); m.shops = sh.shops; }
  sections.push({ id: 'gate', y0: 0 });
  y = bottomOf(m) + 90;
  // 2: Watch or Listen
  sections.push({ id: 'f1', y0: y - 60 });
  m = row(y + 110, [{ id: 'eyes', kind: 'route', fork: 'f1' }], [{ id: 'ears', kind: 'route', fork: 'f1' }]);
  signpost('f1', y, [{ lane: 'eyes', side: 'L', island: m.eyes }, { lane: 'ears', side: 'R', island: m.ears }]);
  y = bottomOf(m) + 80;
  trunk('softw', y); trunk('ticker', y + 120); y += 240;
  // 3: Pick a Theme, eight doors stacked four a side
  sections.push({ id: 'f5', y0: y - 60 });
  {
    const L = ['bimbo', 'sissy', 'drone', 'trance'], R = ['goon', 'locked', 'censored', 'infection'];
    const pl = stack(y + 200, L, 'L', 'f5', 270), pr = stack(y + 200, R, 'R', 'f5', 270);
    signpost('f5', y, [...pl, ...pr]);
    m = Object.fromEntries([...pl, ...pr].map((p) => [p.lane, p.island]));
    y = bottomOf(m) + 100;
  }
  // 4: Pictures
  sections.push({ id: 'f3', y0: y - 60 });
  m = row(y + 140, [{ id: 'slides', kind: 'route', fork: 'f3' }], [{ id: 'scroll', kind: 'route', fork: 'f3' }, { id: 'blind', kind: 'route', fork: 'f3' }]);
  signpost('f3', y, [{ lane: 'slides', side: 'L', island: m.slides }, { lane: 'scroll', side: 'R', island: m.scroll }, { lane: 'blind', side: 'R', island: m.blind }]);
  y = bottomOf(m) + 80;
  trunk('depth', y); y += 120;
  // 5: Rest or Tap, EMI beside Tap
  sections.push({ id: 'f2', y0: y - 60 });
  m = row(y + 110, [{ id: 'sweet', kind: 'route', fork: 'f2' }], [{ id: 'strict', kind: 'route', fork: 'f2' }, { id: 'emi', kind: 'pocket' }]);
  signpost('f2', y, [{ lane: 'sweet', side: 'L', island: m.sweet }, { lane: 'strict', side: 'R', island: m.strict }]);
  { const b = bottomOf(m); footpath(m.emi, b + 40); y = b + 80; }
  trunk('banner', y); y += 120;
  // 6: Ads
  sections.push({ id: 'f4', y0: y - 60 });
  m = row(y + 110, [{ id: 'sell', kind: 'route', fork: 'f4' }], [{ id: 'premium', kind: 'route', fork: 'f4' }]);
  signpost('f4', y, [{ lane: 'sell', side: 'L', island: m.sell }, { lane: 'premium', side: 'R', island: m.premium }]);
  y = bottomOf(m) + 90;
  // 7: the long road down. Combos left; Tricky Questions and Other Exits right.
  sections.push({ id: 'down', y0: y - 60 });
  const t7 = ['glossy', 'taste2', 'overlay', 'takeover', 'wallpaper', 'tunnel', 'drop'];
  t7.forEach((id, i) => trunk(id, y + i * 125 + (id === 'drop' ? 30 : 0)));
  m = row(y, [{ id: 'combo', kind: 'pocket' }], [{ id: 'trouble', kind: 'pocket' }]);
  footpath(m.combo); footpath(m.trouble);
  { const ex = row(m.trouble.y + m.trouble.h + 70, [], [{ id: 'exit', kind: 'pocket' }]).exit; footpath(ex); m.exit = ex; }
  /* Under the Drop the road runs on into a maelstrom: clear of every island near the road. */
  const dropY = nodes.drop?.y ?? y + 6 * 125 + 30;
  const MR = 200;
  const near = islands.filter((i) => i.x < CX + MR + 60 && i.x + i.w > CX - MR - 60);
  const mY = Math.max(dropY + 120 + MR * 0.78, ...near.map((i) => i.y + i.h + 60 + MR * 0.78));
  const H = Math.max(bottomOf(m) + 120, mY + MR + 90);
  const minX = Math.min(...islands.map((i) => i.x)), maxX = Math.max(...islands.map((i) => i.x + i.w));
  const pad = Math.max(0, 120 - minX);
  const W = Math.max(2600, maxX + 120 + pad, CX * 2 + pad);

  /* trunk curve, then every footpath and twig as an SVG path */
  const pts = trunkNodes.slice().sort((a, b) => a.y - b.y).map((t) => [t.x, t.y]);
  let trunkD = `M${pts[0][0]},${pts[0][1]}`;
  for (let i = 1; i < pts.length; i++) {
    const [x0, y0] = pts[i - 1], [x1, y1] = pts[i];
    const wv = (i % 2 ? 1 : -1) * Math.min(26, (y1 - y0) / 12);
    trunkD += ` C${x0 + wv},${y0 + (y1 - y0) * 0.35} ${x1 - wv},${y1 - (y1 - y0) * 0.35} ${x1},${y1}`;
  }
  const pathD = (p) => (p.viaY != null ? `M${p.x},${p.y1} V${p.viaY} H${CX}` : `M${p.x},${p.y0} H${CX}`);
  const twigD = (t) => `M${t.spineX},${t.y0} V${t.y1}` + t.knots.map(([, ky]) => ` M${t.spineX},${ky} H${t.tipX}`).join('');

  const placed = Object.values(nodes).filter((n) => n.x != null && LAYOUT_LANES.has(n.lane) || (n.x != null && n.lane === 'trunk'));
  /* a final pass: shift everything right by pad so nothing sits off the left edge */
  const sx = (v) => v + pad;
  const shiftD = (d) => d.replace(/([MHCQLV ,])(-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)/g, (s, c, a, b) => `${c}${sx(+a)},${b}`).replace(/H(-?\d+(?:\.\d+)?)/g, (s, a) => `H${sx(+a)}`);
  for (const n of placed) n.x = sx(n.x);
  for (const i of islands) { i.x = sx(i.x); i.spine.x = sx(i.spine.x); for (const l of i.ladders) { l.x = sx(l.x); l.stampX = sx(l.stampX); for (const k of l.links) k.x = sx(k.x); } }
  for (const f of forks) f.x = sx(f.x);
  const mTop = mY - MR * 0.78 + 6;
  const sinkD = `M${sx(CX)},${dropY + 44} C${sx(CX) + 30},${dropY + 44 + (mTop - dropY) * 0.4} ${sx(CX) - 30},${mTop - (mTop - dropY) * 0.3} ${sx(CX)},${mTop}`;
  memo = Object.freeze({
    W, H, CX: sx(CX), sections, maelstrom: { x: sx(CX), y: mY, r: MR }, sinkD,
    nodes: placed,
    islands, forks,
    roads: roads.map((r) => ({ ...r, d: shiftD(r.d) })),
    paths: paths.map((p) => ({ lane: p.lane, d: shiftD(pathD(p)) })),
    twigs: twigs.map((t) => ({ lane: t.lane, kind: t.kind, d: shiftD(twigD(t)), knots: t.knots.map(([kx, ky]) => [sx(kx), ky]) })),
    trunkD: shiftD(trunkD),
    trunk: trunkNodes.map((n) => n.id),
  });
  return memo;
}

/* ---- states from the run ---- */
/** A lane this run cannot reach: its fork went the other way (an undecided fork leaves every option open). */
export function laneShut(run, lane) {
  const f = FORK_OF_LANE[lane];
  return Boolean(f && run.picks?.[f] && run.picks[f] !== lane);
}
/** An island's look: pocket (every run), picked, open (fork still ahead) or shut. */
export function islandState(run, isl) {
  if (isl.kind !== 'route') return 'pocket';
  const pick = run.picks?.[isl.fork];
  return !pick ? 'open' : pick === isl.id ? 'picked' : 'shut';
}
function shutItem(run, id, seen = new Set()) {
  if (seen.has(id)) return false;
  seen.add(id);
  const it = BY_ID[id];
  if (!it) return false;
  if (laneShut(run, it.lane)) return true;
  if ((it.req || []).some((q) => shutItem(run, q, seen))) return true;
  return Boolean(it.any?.length && it.any.every((q) => shutItem(run, q, new Set(seen))));
}
/** The price of the last thing bought: anything open and cheaper than that was walked past. */
function lastPaid(run) {
  for (let i = (run.order || []).length - 1; i >= 0; i--) {
    const it = BY_ID[run.order[i]];
    if (it && it.group === 'run' && it.kind !== 'fork' && it.id !== 'start') return it.cost;
  }
  return 0;
}
/**
 * Every node's state: own, offer (open in the shop now), skip (open, and cheaper than the last buy: walked past), later, off.
 * `canOffer(item)` (ui/platform.js) marks rows this device cannot use as off, and lists them in `device`; owned stays own.
 * Returns { states: {id: state}, here, next, counts, device: {id: true} }.
 */
export function worldStates(run, world = layoutWorld(), { canOffer = null } = {}) {
  const states = {}, device = {};
  const paid = lastPaid(run);
  const fuzz = run.fuzz || 0;
  for (const n of world.nodes) {
    if (n.shop) {
      const g = SHOP_BY_ID[n.id];
      if (g.own(run)) states[n.id] = 'own';
      else if (g.item) states[n.id] = shutItem(run, g.item) ? 'off' : 'later';
      else if (g.needs && !E.has(run, g.needs)) states[n.id] = 'later';
      else if (g.gate && !gateOpen(run, g.gate)) states[n.id] = 'later';
      else states[n.id] = fuzz >= g.cost ? 'offer' : 'later';
      continue;
    }
    if (n.fork) { states[n.id] = run.picks?.[n.id] ? 'own' : E.isOpen(run, BY_ID[n.id]) ? 'offer' : 'later'; continue; }
    if (n.id === 'start' || E.has(run, n.id)) { states[n.id] = 'own'; continue; }
    if (canOffer && BY_ID[n.id] && !canOffer(BY_ID[n.id])) { states[n.id] = 'off'; device[n.id] = true; continue; }
    if (shutItem(run, n.id)) { states[n.id] = 'off'; continue; }
    if (E.isOpen(run, BY_ID[n.id])) { states[n.id] = paid && n.cost < paid ? 'skip' : 'offer'; continue; }
    states[n.id] = 'later';
  }
  let here = null;
  for (let i = (run.order || []).length - 1; i >= 0 && !here; i--) if (states[run.order[i]] === 'own' && world.nodes.some((n) => n.id === run.order[i])) here = run.order[i];
  here ||= 'start';
  const next = world.nodes.filter((n) => !n.shop && !n.fork && states[n.id] === 'offer').sort((a, b) => a.cost - b.cost)[0]?.id || null;
  const counts = {};
  for (const n of world.nodes) if (!n.fork) counts[states[n.id]] = (counts[states[n.id]] || 0) + 1;
  return { states, here, next, counts, device };
}
