/* ui/guide-map.js - the Guide: the whole game as a compact left-to-right map, built from ITEMS, FORKS and LANES. Price runs
 * order along the x axis, the main path through the middle, routes above and below. Marks what is owned, what is on offer, and the
 * routes not picked. Loaded the first time the Guide opens. The layout and the marks are pure and tested (tests/guide-map.test.mjs). */

import { BY_ID, FORKS, FORK_OF_LANE, ITEMS, LANES, LANE_BY_ID } from '../data/items.js';
import { brandText } from '../data/brand.js';
import * as E from '../sim/economy.js';
import { iconFor } from './icons.js';
import { _loadDemos } from './fork.js';

export const NODE_W = 66, NODE_H = 72;
const ROW = 74, STEP = 14, LABEL = 18, TRACK_GAP = 6, PAD = 12;
const ABOVE = new Set(['meta', 'eyes', 'sweet', 'slides', 'blind', 'sell', 'trance', 'bimbo', 'drone', 'sissy', 'side', 'look']);


/** Where every node goes. Lanes that do not overlap along x share a track, so the map stays short. */
export function layoutGuide(items = ITEMS, lanes = LANES) {
  const plan = {}, pos = {};
  const priced = items.filter((r) => r.group === 'run');
  /* Along x by price order, not price: the cheap end is sparse and the middle crowded, so a price scale wastes most of the height. */
  const sorted = priced.map((r) => r.cost).sort((a, b) => a - b);
  const xOf = (cost) => PAD + sorted.indexOf(cost) * STEP;
  const maxX = Math.max(...priced.map((r) => xOf(r.cost)));
  for (const l of lanes) {
    const rows = items.filter((r) => r.lane === l.id);
    if (!rows.length) continue;
    const tracks = [];
    const at = {};
    if (l.id === 'meta' || l.id === 'after') {
      rows.forEach((r, i) => { at[r.id] = { x: (l.id === 'meta' ? PAD : maxX + NODE_W + 80) + i * (NODE_W + 4), row: 0 }; });
      tracks[0] = 1;
    } else {
      [...rows].sort((a, b) => a.cost - b.cost || items.indexOf(a) - items.indexOf(b)).forEach((r) => {
        const x = xOf(r.cost);
        let k = tracks.findIndex((end) => end + 4 <= x);
        if (k < 0) { k = tracks.length; tracks.push(0); }
        tracks[k] = x + NODE_W;
        at[r.id] = { x, row: k };
      });
    }
    const xs = Object.values(at).map((a) => a.x);
    plan[l.id] = { rows: tracks.length, min: Math.min(...xs), max: Math.max(...xs) + NODE_W, at };
  }
  const shelf = { up: [], down: [] };
  for (const l of lanes) {
    const p = plan[l.id];
    if (!p || l.id === 'trunk') continue;
    const list = ABOVE.has(l.id) ? shelf.up : shelf.down;
    let t = list.find((tr) => tr.every((id) => p.min > plan[id].max + 40 || p.max + 40 < plan[id].min));
    if (!t) { t = []; list.push(t); }
    t.push(l.id);
  }
  const heightOf = (tr) => Math.max(...tr.map((id) => plan[id].rows)) * ROW + LABEL + TRACK_GAP;
  const trunkY = PAD + shelf.up.reduce((s, t) => s + heightOf(t), 0) + LABEL;
  let y = trunkY - LABEL;
  for (const t of shelf.up) { y -= heightOf(t); for (const id of t) plan[id].y = y + LABEL; }
  if (plan.trunk) plan.trunk.y = trunkY;
  y = trunkY + (plan.trunk?.rows || 1) * ROW + TRACK_GAP;
  for (const t of shelf.down) { for (const id of t) plan[id].y = y + LABEL; y += heightOf(t); }
  const height = y + PAD;
  let width = 0;
  for (const r of items) {
    const p = plan[r.lane], a = p.at[r.id];
    pos[r.id] = { x: Math.round(a.x), y: Math.round(p.y + a.row * ROW) };
    width = Math.max(width, a.x + NODE_W + PAD);
  }
  const labels = lanes.filter((l) => plan[l.id]).map((l) => ({ lane: l.id, x: Math.round(plan[l.id].min), y: Math.round(plan[l.id].y - LABEL + 2) }));
  return { pos, labels, width: Math.ceil(width), height: Math.ceil(height) };
}

/** Every line on the map: parents, `any` parents (one of them will do), and routes that return to the main path. */
export function edgesOf(items = ITEMS, lanes = LANES) {
  const out = [];
  for (const r of items) {
    for (const q of r.req || []) if (BY_ID[q]) out.push({ a: q, b: r.id, cls: r.lane === 'trunk' && BY_ID[q].lane === 'trunk' ? 'trunk' : BY_ID[q].kind === 'fork' ? 'fork' : 'chain' });
    for (const q of r.any || []) if (BY_ID[q]) out.push({ a: q, b: r.id, cls: 'any' });
  }
  for (const l of lanes) {
    if (!l.fork) continue;
    const rows = items.filter((r) => r.lane === l.id).sort((a, b) => a.cost - b.cost);
    const last = rows[rows.length - 1];
    if (!last || last.kind === 'end') continue;
    const back = items.find((r) => r.lane === 'trunk' && r.cost > last.cost && r.kind !== 'fork');
    if (back) out.push({ a: last.id, b: back.id, cls: 'back' });
  }
  return out;
}

/** The rows this run can still reach, given the picks made: lanes not picked are out, and so is anything that needs them. */
export function reachable(run, items = ITEMS) {
  const ok = new Set();
  let grew = true;
  while (grew) {
    grew = false;
    for (const r of items) {
      if (ok.has(r.id)) continue;
      const f = FORK_OF_LANE[r.lane];
      if (f && run.picks[f] && run.picks[f] !== r.lane) continue;
      if ((r.req || []).every((q) => ok.has(q)) && (!r.any || r.any.some((q) => ok.has(q)))) { ok.add(r.id); grew = true; }
    }
  }
  return ok;
}

/** owned, offer (in the shop's reach right now), later (still possible), shut (a route not picked). Moods and After rows have their own rules. */
export function marksOf(run, items = ITEMS) {
  const ok = reachable(run, items), marks = {};
  for (const r of items) {
    if (r.group === 'meta') marks[r.id] = run.mood === r.id ? 'owned' : 'later';
    else if (r.group === 'after') marks[r.id] = 'later';
    else if (E.has(run, r.id)) marks[r.id] = E.isOpen(run, r) ? 'offer' : 'owned';
    else if (!ok.has(r.id)) marks[r.id] = 'shut';
    else marks[r.id] = E.isOpen(run, r) ? 'offer' : 'later';
  }
  return marks;
}

const short = (n) => (n >= 1e6 ? `${+(n / 1e6).toFixed(1)}M` : n >= 1e3 ? `${+(n / 1e3).toFixed(1)}k` : String(n));

const STATUS = { owned: 'You have this.', offer: 'On offer now.', later: 'Not open yet.', shut: 'Not on your route this run.' };
const LEGEND = [['owned', 'Yours'], ['offer', 'On offer'], ['later', 'Later'], ['shut', 'Route not picked']];

const CSS = `
.gm-back { position: fixed; inset: 0; pointer-events: auto; background: rgba(0, 0, 0, .45); animation: fadein .15s ease-out; }
.gm { position: absolute; left: 8px; right: 8px; top: 52px; bottom: 8px; display: flex; flex-direction: column; min-height: 0; overflow: hidden; background: var(--page); color: var(--ink); border: 1px solid var(--line); border-top: 4px solid var(--niche); box-shadow: 2px 2px 0 rgba(0, 0, 0, .35); font: 15px/1.2 var(--font); }
.gm button { color: inherit; font: inherit; touch-action: manipulation; }
.gm-top { flex: none; display: flex; flex-wrap: wrap; align-items: center; gap: 4px 10px; padding: 6px 8px; background: var(--panel); border-bottom: 1px solid var(--line); }
.gm-top h2 { margin: 0; font-size: 20px; line-height: 1; }
.gm-top .sp { flex: 1 1 0; }
.gm-top .tool { min-width: 40px; min-height: 40px; padding: 2px 10px; background: var(--btn); color: var(--btn-ink, inherit); border: 1px solid var(--line); }
.gm-leg { order: 5; flex: 1 1 100%; display: flex; gap: 12px; overflow-x: auto; scrollbar-width: none; white-space: nowrap; font-size: 12px; color: var(--ink-soft); }
.gm-leg i { display: inline-block; width: 12px; height: 12px; margin-right: 4px; vertical-align: -2px; border: 2px solid var(--line); background: var(--panel); }
.gm-leg .owned i { background: var(--niche); border-color: var(--niche); }
.gm-leg .offer i { border-color: var(--niche); border-style: dashed; }
.gm-leg .shut i { opacity: .3; }
.gm-scroll { position: relative; flex: 1 1 auto; min-height: 0; overflow: auto; overscroll-behavior: contain; touch-action: pan-x pan-y; cursor: grab; scrollbar-width: thin; background: var(--page); }
.gm-scroll.drag { cursor: grabbing; user-select: none; -webkit-user-select: none; }
.gm-size { position: relative; }
.gm-canvas { position: absolute; left: 0; top: 0; transform-origin: 0 0; }
.gm-canvas svg { position: absolute; left: 0; top: 0; pointer-events: none; overflow: visible; }
.gm-canvas path { fill: none; stroke: var(--line); stroke-width: 1.25; opacity: .45; }
.gm-canvas path.trunk { stroke-width: 3.5; opacity: .9; }
.gm-canvas path.fork { stroke-width: 2; opacity: .75; }
.gm-canvas path.any { stroke-dasharray: 1 5; stroke-linecap: round; }
.gm-canvas path.back { stroke-dasharray: 5 6; opacity: .3; }
.gm-canvas path.on { stroke: var(--niche); opacity: 1; }
.gm-canvas path.off { opacity: .08; }
.gm-canvas path.hot { stroke: var(--stamp); opacity: 1; stroke-width: 2.5; }
.gm-lane { position: absolute; font-size: 12px; font-weight: 700; line-height: 16px; white-space: nowrap; color: var(--ink-soft); pointer-events: none; }
.gm-lane small { font-size: 11px; font-weight: 400; margin-left: 5px; }
.gm-lane.off { opacity: .4; }
.gm-lane.mine { color: var(--ink); }
.gm-n { position: absolute; width: ${NODE_W}px; height: ${NODE_H}px; padding: 0; display: grid; justify-items: center; align-content: start; gap: 1px; background: none; border: 0; text-align: center; }
.gm-n .box { position: relative; width: 36px; height: 36px; display: grid; place-items: center; background: var(--panel); border: 2px solid var(--line); border-radius: 4px; }
.gm-n .ico { font-size: 21px; line-height: 1; width: auto; height: auto; }
.gm-n .nm { max-width: ${NODE_W}px; max-height: 2.2em; overflow: hidden; font-size: 11px; line-height: 1.05; }
.gm-n .pr { font-size: 10px; line-height: 1; color: var(--ink-soft); font-variant-numeric: tabular-nums; }
.gm-n.fork .box { transform: rotate(45deg) scale(.86); }
.gm-n.fork .ico { transform: rotate(-45deg); }
.gm-n.end .box { border-radius: 50%; }
.gm-n.owned .box { background: var(--niche); border-color: var(--niche); }
.gm-n.owned .box::after { content: ''; position: absolute; right: -4px; top: -4px; width: 11px; height: 11px; background: var(--niche-2); border: 2px solid var(--page); border-radius: 50%; }
.gm-n.offer .box { border-color: var(--niche); border-style: dashed; box-shadow: 0 0 0 2px var(--niche-soft); }
.gm-n.later { opacity: .68; }
.gm-n.shut { opacity: .2; }
.gm-n.sel .box { outline: 3px solid var(--stamp); outline-offset: 1px; }
.gm-card { position: absolute; right: 8px; bottom: 8px; width: 300px; max-height: calc(100% - 16px); overflow-y: auto; overscroll-behavior: contain; padding: 8px 10px 10px; background: var(--panel); color: var(--ink); border: 1px solid var(--line); box-shadow: 2px 2px 0 rgba(0, 0, 0, .35); }
.gm-card .hd { display: grid; grid-template-columns: 34px minmax(0, 1fr) auto; gap: 8px; align-items: center; padding-right: 0; }
.gm-card .hd .ico { font-size: 26px; line-height: 1; width: auto; height: auto; }
.gm-card h3 { margin: 0; font-size: 18px; line-height: 1.05; }
.gm-card .shut { min-width: 40px; min-height: 40px; padding: 0 8px; background: var(--btn); color: var(--btn-ink, inherit); border: 1px solid var(--line); }
.gm-card p { margin: 6px 0 0; font-size: 14px; line-height: 1.2; }
.gm-card .st { font-size: 12px; font-weight: 700; color: var(--ink-soft); }
.gm-card .line { font-style: italic; color: var(--ink-soft); }
.gm-card .demo { margin-top: 6px; }
.gm-card .links { margin-top: 6px; font-size: 13px; line-height: 1.5; color: var(--ink-soft); }
.gm-card .links button { padding: 0 3px; background: none; border: 0; color: var(--ink); text-decoration: underline; text-underline-offset: 2px; }
.gm-card .opt { margin-top: 6px; font-size: 14px; line-height: 1.2; }
.gm-card .opt.mine b { color: var(--ink); text-decoration: underline; text-decoration-color: var(--niche); text-decoration-thickness: 2px; }
.gm-card .opt span { display: block; font-size: 13px; color: var(--ink-soft); }
@media (max-width: 759px) {
  .gm { left: 4px; right: 4px; bottom: 4px; }
  .gm-card { left: 4px; right: 4px; bottom: 4px; width: auto; max-height: 58%; }
  .gm-card .demo { max-width: 240px; }
}
`;

/** Build the map once. Returns { show, hide, select }: closing only hides it, so it opens again where it was, at once. Everything goes when the unlock does. */
export function createGuide(ctx) {
  const memo = {};
  const doc = ctx.button.el.ownerDocument;
  const phone = () => ctx.bounds().w < 760;
  const { pos, labels, width, height } = layoutGuide();
  const edges = edgesOf();

  ctx.css(CSS);
  const back = ctx.el('div', '', null, doc.getElementById('cards'));
  back.className = 'gm-back';
  doc.getElementById('cards').prepend(back);   // under any card that comes up, which stays the host's last child
  const root = ctx.el('section', '', null, back);
  root.className = 'gm';
  root.setAttribute('role', 'dialog');
  root.setAttribute('aria-label', 'Guide');

  const mk = (tag, cls, parent, text) => {
    const e = doc.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = brandText(text);
    if (parent) parent.appendChild(e);
    return e;
  };

  const top = mk('div', 'gm-top', root);
  mk('h2', '', top, 'Guide');
  mk('span', 'sp', top);
  const tool = (label, aria, fn) => {
    const b = mk('button', 'tool', top, label);
    b.type = 'button';
    b.setAttribute('aria-label', aria);
    b.addEventListener('click', fn);
    return b;
  };
  tool('-', 'Smaller', () => zoomBy(-0.14));
  tool('+', 'Bigger', () => zoomBy(0.14));
  const closeBtn = tool('Close', 'Close the guide', () => hide());
  const legend = mk('div', 'gm-leg', top);
  for (const [k, label] of LEGEND) { const s = mk('span', k, legend); mk('i', '', s); s.append(label); }

  const scroll = mk('div', 'gm-scroll', root);
  const sizer = mk('div', 'gm-size', scroll);
  const canvas = mk('div', 'gm-canvas', sizer);
  canvas.style.width = `${width}px`;
  canvas.style.height = `${height}px`;

  /* ---- lines ---- */
  const NS = 'http://www.w3.org/2000/svg';
  const svg = doc.createElementNS(NS, 'svg');
  svg.setAttribute('width', width);
  svg.setAttribute('height', height);
  canvas.appendChild(svg);
  const lines = edges.map((e) => {
    const A = pos[e.a], B = pos[e.b];
    const x1 = A.x + NODE_W / 2 + 18, y1 = A.y + 18, x2 = B.x + NODE_W / 2 - 18, y2 = B.y + 18;
    const dx = Math.max(24, Math.abs(x2 - x1) * 0.45);
    const p = doc.createElementNS(NS, 'path');
    p.setAttribute('d', `M${x1} ${y1}C${x1 + dx} ${y1} ${x2 - dx} ${y2} ${x2} ${y2}`);
    svg.appendChild(p);
    return { ...e, p };
  });

  /* ---- lane names and nodes ---- */
  const laneEls = {};
  for (const l of labels) {
    const row = LANE_BY_ID[l.lane];
    const e = mk('div', 'gm-lane', canvas, row.name);
    e.style.left = `${l.x}px`;
    e.style.top = `${l.y}px`;
    laneEls[l.lane] = { el: e, note: mk('small', '', e) };
  }
  const nodes = {};
  for (const it of ITEMS) {
    const b = mk('button', `gm-n${it.kind === 'fork' ? ' fork' : it.kind === 'end' ? ' end' : ''}`, canvas);
    b.type = 'button';
    b.dataset.id = it.id;
    b.style.left = `${pos[it.id].x}px`;
    b.style.top = `${pos[it.id].y}px`;
    const box = mk('span', 'box', b);
    box.appendChild(iconFor(doc, it));
    mk('span', 'nm', b, it.name);
    mk('span', 'pr', b, it.group === 'run' && it.cost ? short(it.cost) : '');
    nodes[it.id] = b;
  }

  /* ---- marks ---- */
  let marks = {};
  function paint() {
    marks = marksOf(ctx.state);
    for (const it of ITEMS) {
      const mark = marks[it.id];
      const n = nodes[it.id];
      if (n.dataset.mark !== mark) {
        if (n.dataset.mark) n.classList.remove(n.dataset.mark);
        n.classList.add(mark);
        n.dataset.mark = mark;
      }
    }
    const taken = (id) => marks[id] === 'owned' || marks[id] === 'offer' && E.has(ctx.state, id);
    for (const l of lines) {
      l.p.setAttribute('class', `${l.cls}${taken(l.a) && taken(l.b) ? ' on' : ''}${marks[l.a] === 'shut' || marks[l.b] === 'shut' ? ' off' : ''}`);
    }
    for (const [lane, { el, note }] of Object.entries(laneEls)) {
      const f = FORK_OF_LANE[lane];
      if (!f) continue;
      const picked = ctx.state.picks[f];
      el.classList.toggle('off', !!picked && picked !== lane);
      el.classList.toggle('mine', picked === lane);
      note.textContent = picked === lane ? 'your route' : picked ? 'not picked' : 'if picked';
    }
  }
  paint();

  /* ---- the card ---- */
  let card = null, selected = null, stopDemo = null, demoToken = 0;
  function dropDemo() { demoToken += 1; stopDemo?.(); stopDemo = null; }
  function startDemo(host, id) {
    dropDemo();
    /* Previews are not checked against the 3 Hz rule, so Gentle goes without them. */
    if (doc.body.classList.contains('gentle') || ctx.paused) { host.hidden = true; return; }
    const mine = demoToken;
    _loadDemos().then((Demo) => {
      if (mine !== demoToken || !card) return;
      if (!Demo.has(id)) { host.hidden = true; return; }
      stopDemo = Demo.play(host, id);
    }).catch((e) => console.error('demos failed', e));
  }
  function shutCard() {
    dropDemo();
    card?.remove();
    card = null;
    if (selected) nodes[selected].classList.remove('sel');
    selected = null;
    for (const l of lines) l.p.classList.remove('hot');
  }
  function focusNode(id) {
    const P = pos[id], z = zoom;
    scroll.scrollTo({ left: P.x * z - scroll.clientWidth / 2 + (NODE_W / 2) * z, top: P.y * z - scroll.clientHeight / 3, behavior: ctx.gentle ? 'auto' : 'smooth' });
  }
  function linkRow(parent, label, ids) {
    if (!ids.length) return;
    const row = mk('div', 'links', parent);
    row.append(`${label} `);
    for (const id of ids) {
      const b = mk('button', '', row, BY_ID[id].name);
      b.type = 'button';
      b.addEventListener('click', () => select(id, true));
    }
  }
  function select(id, move = false) {
    shutCard();
    selected = id;
    const it = BY_ID[id];
    nodes[id].classList.add('sel');
    for (const l of lines) if (l.a === id || l.b === id) l.p.classList.add('hot');
    card = mk('div', 'gm-card', root);
    const hd = mk('div', 'hd', card);
    hd.appendChild(iconFor(doc, it));
    mk('h3', '', hd, it.name);
    const x = mk('button', 'shut', hd, 'X');
    x.type = 'button';
    x.setAttribute('aria-label', 'Close this card');
    x.addEventListener('click', shutCard);
    const f = FORK_OF_LANE[it.lane];
    const price = it.group === 'run' && it.cost ? ` Costs ${ctx.fmt(E.price(ctx.state, id))}.` : '';
    const where = f ? `Only on the ${LANE_BY_ID[it.lane].name} route. ` : '';
    const mark = it.group === 'after' ? 'After an ending.' : it.group === 'meta' ? (marks[id] === 'owned' ? 'This run has this mood.' : 'A mood some runs start with.') : `${STATUS[marks[id]]}`;
    mk('p', 'st', card, `${where}${mark}${marks[id] === 'owned' ? '' : price}`);
    if (it.kind === 'fork') {
      mk('p', '', card, FORKS[id].ask);
      for (const [lane, opt] of Object.entries(FORKS[id].opts)) {
        const o = mk('div', `opt${ctx.state.picks[id] === lane ? ' mine' : ''}`, card);
        mk('b', '', o, opt.name);
        mk('span', '', o, opt.what);
      }
    } else {
      if (it.what) mk('p', '', card, it.what);
      if (it.line) mk('p', 'line', card, it.line);
      startDemo(mk('div', 'demo', card), id);
    }
    linkRow(card, 'Needs', [...(it.req || []), ...(it.any || [])]);
    linkRow(card, 'Opens', ITEMS.filter((r) => r.req?.includes(id) || r.any?.includes(id)).map((r) => r.id));
    if (move) focusNode(id);
  }

  /* ---- moving: a finger scrolls natively, a mouse drags, the wheel runs out of height and carries on sideways ---- */
  let zoom = phone() ? 0.8 : 1, lastZoom = zoom;
  function applyZoom(keep = true) {
    const cx = (scroll.scrollLeft + scroll.clientWidth / 2) / lastZoom, cy = (scroll.scrollTop + scroll.clientHeight / 2) / lastZoom;
    canvas.style.transform = `scale(${zoom})`;
    sizer.style.width = `${Math.ceil(width * zoom)}px`;
    sizer.style.height = `${Math.ceil(height * zoom)}px`;
    if (keep) { scroll.scrollLeft = cx * zoom - scroll.clientWidth / 2; scroll.scrollTop = cy * zoom - scroll.clientHeight / 2; }
    lastZoom = zoom;
  }
  function zoomBy(d) { zoom = Math.max(0.42, Math.min(1.4, +(zoom + d).toFixed(2))); applyZoom(); }
  applyZoom(false);

  let press = null, dragged = false;
  scroll.addEventListener('pointerdown', (e) => {
    dragged = false;
    if (e.pointerType === 'touch' || e.button > 0) return;
    press = { id: e.pointerId, x: e.clientX, y: e.clientY, left: scroll.scrollLeft, top: scroll.scrollTop, moving: false };
  });
  scroll.addEventListener('pointermove', (e) => {
    if (!press || e.pointerId !== press.id) return;
    const dx = e.clientX - press.x, dy = e.clientY - press.y;
    if (!press.moving && Math.hypot(dx, dy) < 5) return;
    if (!press.moving) { press.moving = true; dragged = true; scroll.classList.add('drag'); scroll.setPointerCapture?.(e.pointerId); }
    scroll.scrollLeft = press.left - dx;
    scroll.scrollTop = press.top - dy;
  });
  const letGo = () => { press = null; scroll.classList.remove('drag'); };
  scroll.addEventListener('pointerup', letGo);
  scroll.addEventListener('pointercancel', letGo);
  scroll.addEventListener('wheel', (e) => {
    if (e.ctrlKey || e.shiftKey || e.deltaX) return;
    const atEnd = e.deltaY > 0 ? scroll.scrollTop + scroll.clientHeight >= scroll.scrollHeight - 1 : scroll.scrollTop <= 0;
    if (!atEnd) return;
    scroll.scrollLeft += e.deltaY * (e.deltaMode === 1 ? 24 : 1);
    e.preventDefault();
  }, { passive: false });
  scroll.addEventListener('click', (e) => {
    if (dragged) { dragged = false; return; }
    const n = e.target.closest?.('.gm-n');
    if (n) select(n.dataset.id);
    else if (e.target.closest?.('.gm-size')) shutCard();
  });

  /* ---- living alongside the game ---- */
  ctx.on('buy', () => { if (shown) paint(); });
  ctx.on('fork', () => { if (shown) paint(); });
  ctx.on('pause', dropDemo);
  ctx.on('suspend', ({ on }) => { if (on) dropDemo(); });
  const replay = () => { if (shown && selected && !stopDemo) { const h = card?.querySelector('.demo'); if (h) { h.hidden = false; startDemo(h, selected); } } };
  ctx.on('resume', replay);

  let shown = false;
  function show() {
    if (shown) return;
    shown = true;
    back.hidden = false;
    paint();
    /* The first time, start where the player is: the next step on the main path. After that, where they left it. */
    if (memo.left != null) { scroll.scrollLeft = memo.left; scroll.scrollTop = memo.top; }
    else {
      const here = pos[E.goal(ctx.state)?.id] || pos.start;
      scroll.scrollLeft = here.x * zoom - scroll.clientWidth / 2;
      scroll.scrollTop = here.y * zoom - scroll.clientHeight / 2;
    }
    closeBtn.focus?.({ preventScroll: true });
  }
  function hide() {
    if (!shown) return;
    shown = false;
    memo.left = scroll.scrollLeft;
    memo.top = scroll.scrollTop;
    shutCard();
    back.hidden = true;
  }
  back.hidden = true;
  back.addEventListener('click', (e) => { if (e.target === back) hide(); });
  return { show, hide, select, get shown() { return shown; }, get scroll() { return scroll; }, get root() { return root; } };
}
