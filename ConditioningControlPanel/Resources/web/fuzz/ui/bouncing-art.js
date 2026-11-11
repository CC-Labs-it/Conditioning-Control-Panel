import { drawFace, FACE_CSS } from './unlocks/extras/emi-face.js';
import { FACE_HTML } from './button-face-art.js';

export const COPY_CHOICES = {auto:'Match button', dvd:'DVD logo', bubble:'CCP bubble', emi:'Little EMIs'};
export const EMI_CHOICES = {mix:'Mixed faces', happy:'Happy', annoyed:'Smug', rage:'Shocked'};
/** Size of the little bouncing copies against their first draft (owner 2026-11-08: Normal is twice the first draft). */
export const COPY_SIZES = {small:'Small', normal:'Normal', big:'Big'};
const SIZE_SCALE = {small:1, normal:2, big:3};
export const COPY_SCALE = SIZE_SCALE.normal;
/* Owner 2026-11-08: only Match button, Mixed faces and Normal are free. Prices sit near Human Check (1500) at the low end
 * and under the Wave ribbons (6000-18000) at the top. Stored as bag.copyOwned['field:value'] = true. */
export const COPY_PRICES = Object.freeze({
  bouncer: {auto:0, dvd:2500, bubble:2500, emi:5000},
  emiPose: {mix:0, happy:1200, annoyed:1200, rage:1200},
  copySize: {normal:0, small:800, big:4000},
});
const DEFAULTS = {bouncer:'auto', emiPose:'mix', copySize:'normal'};
const LISTS = {bouncer:COPY_CHOICES, emiPose:EMI_CHOICES, copySize:COPY_SIZES};
const POSES = {happy:'body-idle',annoyed:'body-smug',rage:'body-shock'};
export { FACE_CSS };

/** Long triggers keep their wording but share two balanced lines on the DVD face. */
export function bounceTextLayout(text) {
  const words = text.split(' ');
  let lines = [text];
  if (text.length > 14 && words.length > 1) {
    let best = Infinity;
    for (let i = 1; i < words.length; i++) {
      const pair = [words.slice(0, i).join(' '), words.slice(i).join(' ')];
      const longest = Math.max(...pair.map(line => line.length));
      if (longest < best) { best = longest; lines = pair; }
    }
  }
  return {text: lines.join('\n'), columns: Math.max(...lines.map(line => line.length)), rows: lines.length};
}
function setBounceLabel(node, text) {
  const layout = bounceTextLayout(text);
  node.querySelector('strong').textContent = layout.text;
  node.dataset.bounceLines = String(layout.rows);
}

export function copyPrice(field, value) { return COPY_PRICES[field]?.[value] ?? 0; }
export function ownedCopy(bag, field, value) {
  return Object.hasOwn(LISTS[field] || {}, value) && (copyPrice(field, value) === 0 || bag?.copyOwned?.[field + ':' + value] === true);
}
/** The choice the copies wear: the stored pick when it is owned, else the free default. */
export function chosenCopy(bag, field) {
  const v = bag?.[field];
  return ownedCopy(bag, field, v) ? v : DEFAULTS[field];
}
/** A save that picked a copy look while they were free keeps it owned. Returns the new map, or null when nothing changes. */
export function grandfatherCopies(bag) {
  if (!bag) return null;
  const add = {};
  for (const field of Object.keys(DEFAULTS)) {
    const v = bag[field];
    if (v && v !== DEFAULTS[field] && Object.hasOwn(LISTS[field], v) && bag.copyOwned?.[field + ':' + v] !== true) add[field + ':' + v] = true;
  }
  return Object.keys(add).length ? {...bag.copyOwned, ...add} : null;
}
export function copyScale(bag) { return SIZE_SCALE[chosenCopy(bag, 'copySize')]; }

/** Match button: 'face' = an exact little copy of the button's own face, the bubble skin = the bubble, the plain button
 *  keeps the DVD logo with its words. `hasFace` covers a face the live button wears that is not in FACE_HTML. */
export function bouncingLook(prefs, skin, isWord = false, hasFace = false) {
  const choice = chosenCopy(prefs, 'bouncer');
  if (choice !== 'auto') return choice;
  if (isWord) return 'dvd';
  if (skin === 'bubble') return 'bubble';
  return (skin && Object.hasOwn(FACE_HTML, skin)) || (skin && hasFace) ? 'face' : 'dvd';
}
export function emiPose(prefs, index) {
  const choice = chosenCopy(prefs, 'emiPose');
  return Object.hasOwn(POSES,choice) ? choice : ['happy','annoyed','rage'][index % 3];
}
/** A still copy of the live button face (any face, registry or not), else the registry's artwork for that skin. */
function faceCopy(doc, button, skin) {
  const live = button?.dataset?.artFace === skin ? button.querySelector(':scope > .fz-face') : null;
  let face;
  if (live) face = live.cloneNode(true);
  else { face = doc.createElement('span'); face.className = 'fz-face'; face.innerHTML = FACE_HTML[skin] || ''; }
  for (const n of [face, ...face.querySelectorAll('[id],[aria-label]')]) { n.removeAttribute('id'); n.removeAttribute('aria-label'); }
  /* The swing is mirrored per frame by a bound copy (bindBounce); a pet heart mid-flight would freeze in a clone. */
  face.querySelector('.tc-pend')?.style.removeProperty('transform');
  for (const n of face.querySelectorAll('.tc-emi-heart')) n.remove();
  /* A cloned canvas comes back blank: copy its pixels, and give an EMI face a painter of its own so it can react. */
  if (live) {
    const from = live.querySelectorAll('canvas'), to = face.querySelectorAll('canvas');
    to.forEach((c, i) => { try { c.width = from[i].width; c.height = from[i].height; c.getContext('2d')?.drawImage(from[i], 0, 0); } catch {} });
  }
  for (const f of face.querySelectorAll('.xe-f')) { const host = f.parentNode, m = f.dataset.m || 'happy'; f.remove(); drawFace(host, m); }
  face.style.cssText = 'position:absolute;left:0;top:0;width:max-content;transform-origin:0 0;';
  return face;
}
export function renderBounce(node, kind, mood, word, caption = '', button = null, skin = '') {
  const doc = node.ownerDocument;
  node.replaceChildren();
  node.dataset.bounceArt = kind;
  node.setAttribute('aria-hidden','true');
  if (kind === 'bubble') return;
  if (kind === 'face') { node.append(faceCopy(doc, button, skin)); return; }
  if (kind === 'emi') {
    const img = doc.createElement('img');
    img.src = 'assets/img/emi/'+POSES[mood]+'.png'; img.alt = ''; img.draggable = false;
    node.append(img);
    drawFace(node,mood);
    return;
  }
  const label = doc.createElement('strong');
  node.append(label,doc.createElement('i'));
  setBounceLabel(node, word || caption || 'T&C');
  if (word) {
    const sub = doc.createElement('small');
    sub.textContent = 'V I D E O'; node.append(sub);
  }
}
/** Fit a face copy inside 28k x 75k px (k = the size pick), keeping its proportions. */
export function faceFit(natW, natH, k) {
  if (!(natW > 0 && natH > 0)) return {z:0, w:Math.round(60 * k), h:Math.round(24 * k)};
  const z = Math.min(28 * k / natH, 75 * k / natW);
  return {z, w:Math.round(natW * z), h:Math.round(natH * z)};
}

/* Exact little copies (owner 2026-11-08): one watcher per live face counts its content changes (a tap's new text, a
 * deck card, a reset), and every copy re-clones on the next frame it sees a new count. The pendulum's per-frame swing is
 * left out of the count and mirrored instead, as are EMI's blink and mood picture. */
const faceWatch = new WeakMap();
function watchFace(live) {
  let w = faceWatch.get(live);
  if (w) return w;
  w = {v:0};
  const Obs = live.ownerDocument.defaultView?.MutationObserver;
  if (Obs) new Obs((records) => {
    const moved = (r) => r.type === 'attributes' ? !r.target.closest?.('.tc-pend')
      : r.type !== 'childList' || ![...r.addedNodes, ...r.removedNodes].every((n) => n.classList?.contains('tc-emi-heart'));
    if (records.some(moved)) w.v += 1;
  }).observe(live, {subtree:true, childList:true, characterData:true, attributes:true, attributeFilter:['class','style']});
  faceWatch.set(live, w);
  return w;
}
/** The live parts a copy follows every frame: [live, copy, what]. Built once per clone, so a frame only compares strings. */
function mirrorPairs(live, copy) {
  const pairs = [];
  const add = (sel, what) => { const a = live.querySelector(sel), b = copy.querySelector(sel); if (a && b) pairs.push([a, b, what]); };
  add('.tc-pend', 'rot');
  add('.tc-emi-art > img', 'src');
  add('.xe-f', 'expr');
  return pairs;
}
const RECLONE_MS = 150;

// Apply only on a preference, size or skin change. Wall hits use the same painted bounds.
export function bindBounce(ctx, node, {index = 0, word = '', scale = 1, caption = ''} = {}) {
  let kind, mood, skin, width, height, k0, measured = false;
  let textLayout = bounceTextLayout(word || caption || 'T&C');
  let live = null, watch = null, seen = 0, clonedAt = -1e9, pairs = [];
  const size = {w:0,h:0};
  const button = ctx.button.el;
  function follow() {
    live = button.dataset.artFace === skin ? button.querySelector(':scope > .fz-face') : null;
    watch = live ? watchFace(live) : null;
    seen = watch?.v ?? 0;
    clonedAt = ctx.state.clock;
    pairs = live && node.firstElementChild ? mirrorPairs(live, node.firstElementChild) : [];
  }
  /** Follow the live face: re-clone on a content change (at most every RECLONE_MS), mirror the swing and EMI's face. */
  function mirror() {
    const current = button.dataset.artFace === skin ? button.querySelector(':scope > .fz-face') : null;
    if (current !== live || (watch && watch.v !== seen && ctx.state.clock - clonedAt >= RECLONE_MS)) {
      renderBounce(node,kind,mood,word,caption,button,skin);
      measured = false;
      follow();
      return;
    }
    if (node.dataset.flinch) return;
    const still = ctx.gentle || ctx.reduced;
    for (const [a, b, what] of pairs) {
      if (still && what !== 'src') continue;
      if (what === 'rot') { if (b.style.transform !== a.style.transform) b.style.transform = a.style.transform; }
      else if (what === 'src') { const s = a.getAttribute('src'); if (s && b.getAttribute('src') !== s) b.setAttribute('src', s); }
      else if (a.dataset.expression) b.paint?.(a.dataset.expression);
    }
  }
  function sync() {
    const prefs = ctx.state.store.bubble;
    const worn = button.dataset.skin;
    const next = bouncingLook(prefs,worn,Boolean(word),Boolean(button.dataset.artFace));
    const pose = next === 'emi' ? emiPose(prefs,index) : '';
    const face = next === 'face' ? worn : '';
    if (kind !== next || mood !== pose || skin !== face) {
      kind = next; mood = pose; skin = face; measured = false;
      renderBounce(node,kind,pose,word,caption,button,face);
      if (kind === 'face') follow(); else { live = watch = null; pairs = []; }
    } else if (kind === 'face') mirror();
    const base = word ? Math.max(20,Math.min(46,ctx.bounds().w * .05))*scale : 1;
    const k = word ? 1 : copyScale(prefs);
    if (k !== k0) { k0 = k; measured = false; }
    let w, h, fontSize = word ? base : 16 * k;
    if (kind === 'face') {
      if (measured) return size;
      const art = node.firstElementChild;
      const fit = faceFit(art?.offsetWidth, art?.offsetHeight, k);
      if (art && fit.z) { art.style.transform = 'scale('+fit.z+')'; measured = true; }
      w = fit.w; h = fit.h;
    }
    else if (kind === 'dvd') {
      const columns = textLayout.columns, extraLine = textLayout.rows - 1;
      w = word ? Math.ceil(base * Math.max(3,columns*.77)) : Math.ceil(k * Math.max(52,Math.ceil(columns * 12.5)));
      h = (word ? Math.ceil(base * 1.43) : Math.round(22 * k)) + Math.ceil(extraLine * fontSize * .92);
      // Keep the painted phrase and its collision box inside a narrow screen, even at Big size.
      const fit = Math.min(1, Math.max(1, ctx.bounds().w - 16) / w);
      w = Math.ceil(w * fit); h = Math.ceil(h * fit); fontSize *= fit;
    }
    else if (kind === 'bubble') { w = h = word ? Math.round(base * 1.62) : Math.round(26 * k); }
    else { w = h = word ? Math.max(40,Math.round(base * 1.8)) : Math.round(43 * k); }
    if (w !== width || h !== height) {
      width=w; height=h; size.w=w; size.h=h;
      node.style.width = w+'px'; node.style.height = h+'px';
      node.style.fontSize = fontSize+'px';
    }
    return size;
  }
  return {
    sync,
    setWord(value) { word=value; textLayout=bounceTextLayout(value); if (kind === 'dvd') setBounceLabel(node,value); },
    /** The small copies' own word (not the big DVD word): resizes on the next sync. */
    setCaption(value) { caption=value; textLayout=bounceTextLayout(value); width=0; if (kind === 'dvd') setBounceLabel(node,value); },
  };
}
