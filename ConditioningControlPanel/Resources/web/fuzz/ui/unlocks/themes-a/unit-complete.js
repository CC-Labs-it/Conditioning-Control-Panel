/* Unit Complete runs before the normal end fade: the objects being erased are the player's actual page. */
import { frameRect } from '../../perf.js';
import { bump } from '../../meta-count.js';
import { unitAudio } from './unit-audio.js';
import { scanPlan } from './unit-complete-plan.js';
import { makeRain } from './unit-complete-rain.js';
import { nameTag, makeName } from './unit-complete-name.js';

const limit = (n) => Math.max(0, Math.min(1, n));
let aborted = false;
export const unitCompleteAborted = () => aborted;

const CSS = `
body[data-unit-running] #shop[hidden]:has(.row){display:flex!important;pointer-events:none}
body[data-unit-running] #cards{display:none!important}
body[data-unit-running] #l-over > :not([data-unit-original]){visibility:hidden!important}
body[data-unit-running] #btn-wrap{opacity:var(--unit-button,1)!important;pointer-events:none}
body[data-unit-running] [data-unit-backdrop]{opacity:var(--unit-background,1)!important}
[data-unit-piece]{transition:none!important}
[data-unit-piece=wipe]{clip-path:inset(0 0 var(--unit-cut,0%) 0)!important}
[data-unit-piece=fade]{opacity:var(--unit-ink,1)!important}
[data-unit-scanned]{opacity:0!important}
[data-unit-take]{position:fixed;inset:0;z-index:88;overflow:hidden;pointer-events:auto;touch-action:none}
[data-unit-eye]{position:absolute;left:0;top:0;border-radius:50%;pointer-events:none;
  background:radial-gradient(circle at 44% 30%,#8d9295 0%,#23292c 28%,#060708 58%,#282d30 74%,#020303 85%);
  box-shadow:0 3px 7px #000b,inset 0 1px 1px #d8dddf99,inset 0 -1px 2px #000;display:grid;place-items:center}
.uc-lens{position:absolute;inset:7%;border-radius:50%;border:1px solid #101314;
  background:repeating-radial-gradient(circle,#101315 0 2px,#303335 3px,#090b0c 4px);box-shadow:0 1px 1px #97969a66}
.uc-glass{position:absolute;inset:22%;border-radius:50%;border:1px solid #160200;
  background:radial-gradient(ellipse at 35% 22%,#e8b8a754 0%,#df9d9a22 10%,transparent 30%),radial-gradient(circle,#fffaca 0 3%,#ffb049 6%,#ff2f08 13%,#c40000 25%,#590000 43%,#190202 65%,#090808 76%,#4a3230 95%);
  box-shadow:0 0 0 2px #111,0 0 0 3px #414040,inset 0 -2px 5px #160000}
.uc-core{position:absolute;inset:40%;border-radius:50%;background:#ff3909;box-shadow:0 0 12px 6px #fb1409b8;mix-blend-mode:screen;pointer-events:none}
.uc-iris-glint{position:absolute;left:33%;top:27%;width:20%;height:5%;border-radius:70%;background:#ffe8dc9c;transform:rotate(-23deg)}
.uc-projector{position:absolute;left:50%;top:50%;width:15%;height:15%;border-radius:50%;pointer-events:none;
  background:radial-gradient(circle,#f2fff5 0 15%,#afffc7 23%,#57ff9d 35%,#167852 53%,#0c2119 65%,#64766b 78%,#181d1a 84%);
  box-shadow:0 0 5px #46ff8faa,0 0 12px #53ff8d66;opacity:0}
[data-unit-beams]{position:absolute;inset:0;width:100%;height:100%;overflow:hidden;pointer-events:none}
.uc-terminal{position:absolute;inset:0;display:grid;align-content:center;justify-items:center;padding:clamp(18px,7vw,80px);background:radial-gradient(ellipse at 50% 44%,#07120d,#020504 60%,#000 100%);color:#83ffad}
.uc-terminal pre{width:min(100%,47ch);min-height:8em;margin:0;font:500 clamp(17px,3.2vw,28px)/1.6 Consolas,'Courier New',monospace;white-space:pre-wrap;overflow-wrap:anywhere;text-shadow:0 0 8px #37985680}
.uc-cursor{display:inline-block;width:.55em;height:1em;vertical-align:-.12em;background:#93ffb1;box-shadow:0 0 8px #55d88066}
`;

/* Foreground objects only. A layer-sized effect can contain individual objects, or just be a backdrop. */
function scanBox(el, clip, w, h) {
  const raw = frameRect(el), mask = clip && frameRect(clip);
  const left = Math.max(0, raw.left, mask?.left ?? 0), right = Math.min(w, raw.right, mask?.right ?? w);
  const top = Math.max(0, raw.top, mask?.top ?? 0), bottom = Math.min(h, raw.bottom, mask?.bottom ?? h);
  if (right - left < 2 || bottom - top < 2) return null;
  return { left, right, top, bottom, width: right - left, height: bottom - top, raw };
}

function openingShot(doc, el, r, eye, w, h) {
  if (r.width < 72 || r.height < 42 || el.closest('#dock-strip')) return 0;
  const nearX = Math.min(r.right, Math.max(r.left, eye.x)), nearY = Math.min(r.bottom, Math.max(r.top, eye.y));
  if (Math.hypot(nearX - eye.x, nearY - eye.y) < eye.size * .65) return 0;
  // The opener needs visible material to cut. Large transparent docks can overlap a different toy.
  let hits = 0;
  for (const [u, v] of [[.5,.5], [.25,.25], [.75,.25], [.25,.75], [.75,.75]]) {
    const hit = doc.elementFromPoint(r.left + u * r.width, r.top + v * r.height);
    if (!hit || !el.contains(hit)) continue;
    for (let node = hit; node && el.contains(node); node = node.parentElement) {
      const style = doc.defaultView.getComputedStyle(node);
      if (node.matches('img,video,canvas,svg,svg *') || style.backgroundImage !== 'none' ||
          (style.backgroundColor !== 'rgba(0, 0, 0, 0)' && style.backgroundColor !== 'transparent')) {
        hits++;
        break;
      }
    }
  }
  if (hits < 3) return 0;
  const area = r.width * r.height, visible = area / (r.raw.width * r.raw.height);
  const margin = Math.min(r.left, w - r.right, r.top, h - r.bottom);
  return hits / 5 * visible * Math.min(1, Math.sqrt(area) / 190) * (.7 + .3 * limit(margin / 64));
}

function targets(doc, origin, w, h, nameSource) {
  const pieces = [], seen = new Set(), backdrops = [];
  let opener = null, bestShot = 0;
  function add(el, rank) {
    if (!el || el === nameSource || seen.has(el) || el.hidden || /^(SCRIPT|STYLE|LINK|TEMPLATE|NOSCRIPT)$/.test(el.tagName)) return;
    seen.add(el);
    if (el.id === 'l-ticker' && !el.textContent.trim() && !el.childElementCount) return;
    const clip = el.parentElement?.closest('#dock-strip,#shop');
    const r = scanBox(el, clip, w, h);
    if (!r) return;
    const style = doc.defaultView.getComputedStyle(el);
    // A newly painted shop tile starts its entrance at opacity zero. It will be fully visible before the scanner arrives.
    const shopTile = el.matches('#shop .row'), opacity = Number(style.opacity);
    if (style.visibility === 'hidden' || (opacity <= 0.01 && !shopTile)) return;
    if (rank < 4 && r.width * r.height > 0.68 * w * h && !el.matches('.dk,.row')) {
      backdrops.push(el);
      for (const child of el.children) add(child, rank);
      return;
    }
    const distance = Math.hypot((r.left + r.right) / 2 - origin.x, (r.top + r.bottom) / 2 - origin.y);
    const score = Math.sqrt(r.width * r.height) / Math.min(w, h) - distance / Math.hypot(w, h);
    const piece = { el, rank, score, clip, opacity: shopTile ? 1 : opacity };
    pieces.push(piece);
    // A phone can tuck every dock. Let a painted shop tile or owned side toy establish that first shot there.
    const fallback = opener?.rank !== 1 && (shopTile || (rank === 3 && el.dataset.fzOwner));
    if (opacity > .15 && (rank === 1 || fallback)) {
      const shot = openingShot(doc, el, r, origin, w, h);
      if (shot > bestShot) { bestShot = shot; opener = piece; }
    }
  }
  const from = (selector, rank) => doc.querySelectorAll(selector).forEach(el => add(el, rank));
  from('#l-side .rail > *,#l-side > :not(.rail),#l-screens > *,#dock-strip > *', 1);
  from('#shop .row,#shop #more', 2);
  from('#l-floaters > *,#l-over > *,#l-fx > *,#l-toast > *,#l-back > *,#l-wash > *', 3);
  for (const el of doc.body.children) {
    if (/^(world|cards|pause|game-controls|dock-strip)$/.test(el.id) || el.classList.contains('backdrop')) continue;
    if (el.matches('div,button,nav,section,aside') && ['fixed','absolute'].includes(doc.defaultView.getComputedStyle(el).position)) add(el, 3);
  }
  from('#l-ticker,#phrase,#l-banner > *,#l-meters > :not([data-fz-owner="dr_serial"])', 4);
  from('#counter', 5);
  from('#l-meters > [data-fz-owner="dr_serial"]', 6);
  from('#game-controls > *', 7);
  pieces.sort((a, b) => a.rank - b.rank || a.score - b.score);
  if (opener) { pieces.splice(pieces.indexOf(opener), 1); pieces.unshift(opener); }
  // Keep an ancestor visible while its children are scanned. Its remaining paint goes with the eye.
  for (const el of backdrops.filter(el => !pieces.some(p => el.contains(p.el)))) el.dataset.unitBackdrop = '';
  return { pieces, backdrops };
}

// One narrow Mainframe-green ray. Its bright core hits the surface and lights the cut across that object.
function makeBeam(doc, stage) {
  const ns = 'http://www.w3.org/2000/svg', svg = doc.createElementNS(ns, 'svg');
  svg.dataset.unitBeams = ''; svg.setAttribute('aria-hidden', 'true'); svg.style.display = 'none';
  svg.setAttribute('stroke-linecap', 'round'); svg.setAttribute('stroke-linejoin', 'round');
  stage.append(svg);
  const defs = doc.createElementNS(ns, 'defs');
  function gradient(id, stops) {
    const g = doc.createElementNS(ns, 'radialGradient');
    g.id = id;
    for (const [offset, color, alpha] of stops) {
      const stop = doc.createElementNS(ns, 'stop');
      stop.setAttribute('offset', offset); stop.setAttribute('stop-color', color); stop.setAttribute('stop-opacity', alpha);
      g.append(stop);
    }
    defs.append(g);
    return g;
  }
  const light = gradient('uc-cut-light', [[0,'#f3fff4',1],[.27,'#93ffc5',.95],[1,'#32db80',.60]]);
  light.setAttribute('gradientUnits', 'userSpaceOnUse');
  gradient('uc-hit-light', [[0,'#ffffed',1],[.16,'#d4ffdc',1],[.34,'#61ffa4',.74],[1,'#32ef84',0]]);
  svg.append(defs);
  function path(fill, stroke, width, opacity) {
    const el = doc.createElementNS(ns, 'path');
    el.setAttribute('fill', fill); el.setAttribute('opacity', opacity);
    if (stroke) { el.setAttribute('stroke', stroke); el.setAttribute('stroke-width', width); }
    svg.append(el);
    return el;
  }
  const footprint = path('#42f98f', null, 0, .23);
  const aura = path('none', '#22ec78', 12, .13), sheath = path('none', '#46ff9a', 5.5, .64);
  const core = path('none', '#caffd5', 1.7, 1);
  const halo = path('none', '#59ff93', 4.5, .49), contact = path('none', 'url(#uc-cut-light)', 1.7, 1);
  const spark = doc.createElementNS(ns, 'circle');
  spark.setAttribute('r', 12); spark.setAttribute('fill', 'url(#uc-hit-light)'); svg.append(spark);
  contact.dataset.unitContact = '';
  const pt = (x, y) => x.toFixed(1) + ',' + y.toFixed(1);
  return (origin, port, r, sweep, p, soft, acrossName = false) => {
    const lit = !soft && !!r;
    if (lit !== svg.hasAttribute('data-active')) {
      svg.toggleAttribute('data-active', lit); svg.style.display = lit ? '' : 'none';
    }
    if (!lit) { port.style.opacity = '0'; return; }
    const across = .5 + .29 * Math.sin(2 * Math.PI * p - .8);
    const y = r.top + r.height * (acrossName ? across : sweep);
    const x = r.left + r.width * (acrossName ? sweep : across);
    const angle = Math.atan2(y - origin.y, x - origin.x);
    const dx = origin.size * .31 * Math.cos(angle), dy = origin.size * .31 * Math.sin(angle);
    port.style.transform = `translate(-50%,-50%) translate(${dx.toFixed(1)}px,${dy.toFixed(1)}px)`;
    port.style.opacity = '1';
    const source = pt(origin.x + dx, origin.y + dy), tip = pt(x, y);
    const edge = acrossName ? `M${pt(x, r.top)}L${pt(x, r.bottom)}` : `M${pt(r.left, y)}L${pt(r.right, y)}`;
    const ray = `M${source}L${tip}`;
    svg.setAttribute('opacity', (.36 + .64 * Math.min(1, p * 12, (1 - p) * 11)).toFixed(2));
    aura.setAttribute('d', ray); sheath.setAttribute('d', ray); core.setAttribute('d', ray);
    // A small bevel behind the cut gives the target thickness while the ray stays a single straight line.
    const bevel = Math.min(6, r.height * .2, r.width * .04);
    const back = acrossName ? `L${pt(x - bevel, r.bottom - bevel)} ${pt(x - bevel, r.top + bevel)}Z` : `L${pt(r.right - bevel, y - bevel)} ${pt(r.left + bevel, y - bevel)}Z`;
    footprint.setAttribute('d', edge + back);
    light.setAttribute('cx', x); light.setAttribute('cy', y); light.setAttribute('r', Math.max(3, (acrossName ? r.height : r.width) * .66));
    halo.setAttribute('d', edge); contact.setAttribute('d', edge);
    spark.setAttribute('cx', x); spark.setAttribute('cy', y);
  };
}

function lensOrigin(button, width, height) {
  const r = button && frameRect(button);
  if (!r?.width || !r.height) return { x: width / 2, y: height / 2, size: 68 };
  return { x: (r.left + r.right) / 2, y: (r.top + r.bottom) / 2, size: Math.max(48, Math.min(96, r.width, r.height * 1.55)) };
}

function typeTerminal(text, cursor, lines, elapsed, duration, audio, soft) {
  // Allocate time for the player to read each command before the cursor returns to the margin.
  const fractions = [0.04, 0.23, 0.46, 0.60];
  const lengths = [0.13, 0.17, 0.09, 0.29];
  let value = '';
  for (let i = 0; i < lines.length; i++) {
    const phase = (elapsed / duration - fractions[i]) / lengths[i];
    if (phase < 0) break;
    value += (i ? '\n' : '') + lines[i].slice(0, Math.floor(limit(phase) * lines[i].length));
  }
  if (value !== text.data) { text.data = value; if (/\S$/.test(value)) audio.key(); }
  cursor.style.opacity = soft ? '1' : String(0.25 + 0.75 * (0.5 + 0.5 * Math.cos(elapsed * 5.2)));
}

/** Returns at the end of the terminal. Leave its last frame in place until the engine completes its fade. */
export function unitCompleteTake(ctx) {
  aborted = false;
  const count = { value: null };
  bump('tc:end:dr_end').then(n => { count.value = n; }).catch(() => {});
  const doc = ctx.world.ownerDocument, body = doc.body, button = ctx.button.el;
  const { w, h } = ctx.bounds(), audio = unitAudio(ctx);
  ctx.css(CSS);
  // A completed toast animation can enqueue another after the engine freezes. Only scan the original foreground.
  const original = [...doc.querySelectorAll('#l-over > *')];
  for (const el of original) el.dataset.unitOriginal = '';
  body.dataset.unitRunning = '';
  const identity = nameTag(ctx);
  const { pieces, backdrops } = targets(doc, lensOrigin(button, w, h), w, h, identity.source);
  // The name's own source stays on screen until the last shot lifts it (the Name Tag card lives in #l-over).
  const included = new Set([...backdrops, ...pieces.map(piece => piece.el), identity.source].filter(Boolean));
  for (const el of original) if (!included.has(el)) delete el.dataset.unitOriginal;
  const plan = scanPlan(pieces.length), muffled = new Set();
  const stage = ctx.el('div', '', null, body); stage.dataset.unitTake = ''; stage.dataset.phase = 'eye';
  const darkness = ctx.el('div', 'position:absolute;inset:0;background:#020504;opacity:0;pointer-events:none', null, stage);
  const rain = makeRain(ctx, stage, pieces), beam = makeBeam(doc, stage);
  const name = makeName(ctx, stage, rain.canvas, identity);
  const eye = ctx.el('div', '', null, stage); eye.dataset.unitEye = ''; eye.setAttribute('aria-hidden', 'true');
  for (const cls of ['uc-lens','uc-glass','uc-core','uc-iris-glint']) ctx.el('i', '', null, eye).className = cls;
  const port = ctx.el('i', '', null, eye); port.className = 'uc-projector';
  const core = eye.querySelector('.uc-core');
  const veil = ctx.el('div', 'position:absolute;inset:0;background:#020303;opacity:0;', null, stage);
  const screen = ctx.el('div', '', null, stage); screen.className = 'uc-terminal'; screen.hidden = true;
  const type = ctx.el('pre', '', null, screen); type.dataset.unitTerminal = ''; type.setAttribute('role', 'status'); type.setAttribute('aria-live', 'off');
  const text = doc.createTextNode(''); type.append(text);
  const cursor = ctx.el('span', '', null, type); cursor.className = 'uc-cursor'; cursor.setAttribute('aria-hidden', 'true');
  let time = 0, elapsed = 0, shotIndex = 0, active = false, erased = 0, lines = null, done = false, cleared = false;
  let namePass = 0, nameCut = 0;
  let previous = doc.defaultView.performance.now(), stopFrame = () => {}, resolve;
  const finished = new Promise(r => { resolve = r; });

  function clear() {
    if (cleared) return;
    cleared = true; audio.stop(); rain.clear(); name.clear(); stage.remove();
    delete body.dataset.unitRunning;
    for (const el of original) delete el.dataset.unitOriginal;
    body.style.removeProperty('--unit-button'); body.style.removeProperty('--unit-background');
    for (const { el } of pieces) {
      delete el.dataset.unitPiece; delete el.dataset.unitScanned;
      el.style.removeProperty('--unit-cut'); el.style.removeProperty('--unit-ink');
    }
    for (const el of backdrops) delete el.dataset.unitBackdrop;
    for (const id of muffled) ctx.audio.muffle(id, false);
  }
  function finish() {
    if (done) return;
    done = true; stopFrame(); audio.stop(); resolve();
  }
  ctx.cleanup(() => { finish(); clear(); });
  ctx.on('suspend', event => {
    previous = doc.defaultView.performance.now();
    if (event?.on && event.reason === 'panic') { aborted = true; finish(); clear(); }
  });
  ctx.on('resume', () => { previous = doc.defaultView.performance.now(); });
  const resetClock = () => { previous = doc.defaultView.performance.now(); };
  doc.addEventListener('visibilitychange', resetClock);
  ctx.cleanup(() => doc.removeEventListener('visibilitychange', resetClock));

  function erase(piece) {
    piece.el.dataset.unitScanned = '1';
    delete piece.el.dataset.unitPiece;
    const owners = new Set([piece.el.dataset.fzOwner, ...Array.from(piece.el.querySelectorAll('[data-fz-owner]'), el => el.dataset.fzOwner)]);
    for (const id of owners) if (id && !muffled.has(id)) {
      muffled.add(id);
      ctx.audio.muffle(id, true, { hz: 500, cut: 0.65, fadeOutMs: 340 });
    }
  }

  function evaporate(rect, fraction, width, soft) {
    if (fraction <= erased) return;
    if (rect) {
      if (erased === 0) audio.dissolve(Math.max(-1, Math.min(1, (rect.left + rect.right) / width - 1)));
      if (!soft) rain.evaporate(rect, erased, fraction, time);
    }
    erased = fraction;
  }

  function frame() {
    const now = doc.defaultView.performance.now();
    if (!doc.hidden) {
      const delta = Math.max(0, (now - previous) / 1000);
      elapsed += delta;
      // Recover a little of a missed frame on subsequent sweeps while keeping each cut visible.
      time = Math.min(elapsed, time + (shotIndex === pieces.length && time < plan.black ? Math.min(.08, delta) : 1.25 * delta));
    }
    previous = now;
    const soft = ctx.gentle || ctx.reduced;
    if (shotIndex === pieces.length && time >= plan.terminal) {
      if (!lines) {
        const repeated = Number.isSafeInteger(count.value) && count.value > 0
          ? `this message has been repeated for ${count.value} ${count.value === 1 ? 'unit' : 'units'}`
          : 'this message has been repeated before';
        lines = ['unit complete', 'booting system...', 'reset', repeated];
        stage.dataset.phase = 'terminal'; screen.hidden = false; eye.hidden = true; rain.canvas.hidden = true; audio.boot();
      }
      typeTerminal(text, cursor, lines, time - plan.terminal, plan.end - plan.terminal, audio, soft);
      if (time >= plan.end) { type.setAttribute('aria-live', 'polite'); type.setAttribute('aria-label', lines.join('. ')); finish(); }
      return;
    }

    const { w: width, h: height } = ctx.bounds(), origin = lensOrigin(button, width, height);
    const boxOf = piece => scanBox(piece.el, piece.clip, width, height);
    let shot = plan.shots[shotIndex];
    if (shot && time >= shot.start + shot.duration) {
      // A dropped frame must never take two objects, or skip straight past a shot's visible cutting pass.
      if (erased > 0) {
        const finished = pieces[shotIndex];
        evaporate(boxOf(finished), 1, width, soft);
        erase(finished);
        audio.release(); active = false; erased = 0;
        shot = plan.shots[++shotIndex];
      }
      const latest = shot ? shot.start + shot.duration * .68 : plan.nameHold;
      time = Math.min(time, latest);
      elapsed = shot ? Math.min(elapsed, time + .45) : time;
    }
    const progress = limit(time / plan.nameHold), intro = limit(time / 0.24);
    const scanning = shot && time >= shot.start;
    const piece = scanning ? pieces[shotIndex] : null, box = piece && boxOf(piece);
    if (scanning && !active) {
      active = true;
      const first = plan.shots[0].duration, last = plan.shots.at(-1).duration;
      audio.scan((first - shot.duration) / Math.max(.01, first - last));
    }
    const p = scanning ? limit((time - shot.start) / shot.duration) : 0;
    // The opening shots acquire downwards before cutting up. A fly-by goes straight into the cut,
    // so even a phone drawing one frame of this target shows it shedding pixels under the beam.
    const turn = shot ? .36 * limit((shot.duration - .10) / .18) : .36;
    const half = p < turn ? p / turn : (p - turn) / (1 - turn);
    const eased = half * half * (3 - 2 * half);
    const wipe = p < turn ? 0 : eased, sweep = p < turn ? eased : 1 - wipe;
    if (scanning) {
      stage.dataset.phase = 'scan';
      const { el, rank, opacity } = piece, fade = soft || rank === 7;
      el.dataset.unitPiece = fade ? 'fade' : 'wipe';
      if (fade) el.style.setProperty('--unit-ink', (opacity * (1 - p)).toFixed(3));
      else if (box) {
        const hiddenBottom = box.raw.bottom - box.bottom;
        const cut = wipe ? (hiddenBottom + box.height * wipe) / box.raw.height : 0;
        el.style.setProperty('--unit-cut', `${(100 * cut).toFixed(2)}%`);
      }
      evaporate(box, wipe, width, soft);
    }
    if (time < plan.nameHold) beam(origin, port, box, sweep, p, soft);
    body.style.setProperty('--unit-button', (1 - intro).toFixed(3));
    body.style.setProperty('--unit-background', (1 - progress * progress).toFixed(3));
    eye.style.width = eye.style.height = `${origin.size}px`;
    eye.style.transform = `translate(${origin.x - origin.size / 2}px,${origin.y - origin.size / 2}px) scale(${soft ? 1 : 0.72 + intro * 0.28})`;
    eye.style.opacity = intro.toFixed(3); core.style.opacity = (soft ? 0.25 : 0.3 + 0.22 * Math.sin(time * 2)).toFixed(3);
    const hold = time - plan.nameHold, focus = limit(hold / .23);
    if (hold >= 0) {
      darkness.style.opacity = focus.toFixed(3);
      const first = limit((time - plan.firstCut) / 1.03), second = limit((time - plan.secondCut) / 1.27);
      const r = name.show(hold, first, second, origin, width, height, soft);
      const pass = time >= plan.secondCut ? 2 : time >= plan.firstCut ? 1 : 0;
      const cut = pass === 2 ? second : first;
      if (pass !== namePass) {
        namePass = pass; nameCut = 0;
        audio.scan(.18); audio.dissolve(0);
      }
      if (pass && cut > nameCut) {
        if (!soft) rain.evaporate(r, nameCut, cut, time, name.letters, pass === 2);
        if (cut === 1) audio.release();
        nameCut = cut;
      }
      stage.dataset.phase = pass ? 'name-cut-' + pass : 'name-hold';
      beam(origin, port, pass && cut < 1 ? r : null, pass === 2 ? 1 - cut : cut, cut, soft, true);
      if (time >= plan.tracking) {
        if (!name.tag.hasAttribute('data-unit-scanned')) {
          name.tag.dataset.unitScanned = '1'; ctx.audio.solo(true); audio.off();
        }
        const black = soft ? plan.tracking : plan.black;
        name.tracking(r, soft ? -1 : (time - plan.tracking) / (plan.black - plan.tracking));
        stage.dataset.phase = time < black ? 'static' : 'black';
        veil.style.opacity = limit((time - black) / .72).toFixed(3);
      }
    }
    rain.draw(time, progress, soft, width, height, 1 - focus);
  }

  frame(); stopFrame = ctx.frame(frame);
  return finished;
}
