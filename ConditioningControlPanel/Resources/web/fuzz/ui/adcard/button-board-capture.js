/* Freeze the mounted board into separate, inert pieces of paper. The clones live in local shadow roots:
 * printed controls cannot acquire game listeners, CSS animations or document IDs. */
const SVG = 'http://www.w3.org/2000/svg';
const CABINETS = new Set(['mini_slots','mini_breakout','mini_race','wheel','toy_box','dispenser','a_tick','quiz']);
const CABINET_ART = {mini_slots:'.mini-slot-cabinet',dispenser:'.prize-art',wheel:'.tc-wheel-stage',toy_box:'.tb-box'};
const OMIT = 'script,style,link,iframe,object,embed,template,source,track,audio,animate,animateTransform,animateMotion,set,foreignObject';
const CSS = `display position box-sizing top right bottom left width height min-width max-width min-height max-height
  margin padding border border-top border-right border-bottom border-left border-radius border-image outline outline-offset
  background-color background-image background-size background-position background-repeat background-clip background-origin
  box-shadow color opacity visibility filter backdrop-filter mix-blend-mode isolation overflow overflow-x overflow-y
  font-family font-style font-weight font-stretch font-size line-height letter-spacing word-spacing text-align white-space
  word-break overflow-wrap text-transform text-decoration text-shadow text-overflow vertical-align direction writing-mode
  float clear grid-template-columns grid-template-rows grid-auto-flow grid-auto-columns grid-auto-rows grid-column grid-row
  justify-content align-content align-items justify-items align-self justify-self flex-direction flex-wrap flex order gap
  transform transform-origin translate rotate scale zoom z-index aspect-ratio object-fit object-position clip-path
  mask-image mask-size mask-repeat mask-position image-rendering appearance list-style`.trim().split(/\s+/);
const VECTOR_STYLE = ['color','fill','fill-opacity','fill-rule','stroke','stroke-width','stroke-linecap','stroke-linejoin',
  'stroke-dasharray','stroke-dashoffset','stroke-opacity','opacity','stop-color','stop-opacity','paint-order','filter',
  'clip-path','mask','transform','transform-origin','transform-box','font-family','font-size','font-weight','text-anchor'];

function rectOf(el) {
  const r = el.getBoundingClientRect();
  return { x: r.x, y: r.y, w: r.width, h: r.height };
}
function within(r, w, h) {
  return r.w >= 2 && r.h >= 2 && r.x < w && r.y < h && r.x + r.w > 0 && r.y + r.h > 0;
}
function gameArt(ref,base) {
  try {
    const url = new URL(ref,base);
    return /^https?:$/.test(url.protocol) && url.origin === new URL(base).origin &&
      /\/(?:fuzz|arcademy|glassroad|backroom)\/(?:assets|(?:[^/]+\/)*assets)\//i.test(url.pathname) &&
      !/\/(?:feed|pictures|photos|scrolller|uploads|user-media|licensed)\//i.test(url.pathname);
  } catch { return false; }
}
function looksLocal(value, base) {
  const urls = value.matchAll(/url\(\s*['"]?([^)'"\s]+)/gi);
  for (const [, ref] of urls) {
    if (ref.startsWith('#') || (ref.startsWith(base.split('#')[0]+'#'))) continue;
    if (!gameArt(ref,base)) return false;
  }
  return true;
}

function copyStyle(out, style, keys, base) {
  for (const key of keys) {
    const value = style.getPropertyValue(key);
    if (!value || (value.includes('url(') && !looksLocal(value, base))) continue;
    // SVG definitions live beside their own shapes in this shadow root, even on an iframe with a query string.
    out.style.setProperty(key, value.replace(/url\("[^"#]+#([^"#]+)"\)/g,'url("#$1")'));
  }
}

function freezePixels(src, doc) {
  const canvas = doc.createElement('canvas');
  const w = src.naturalWidth || src.videoWidth || src.width, h = src.naturalHeight || src.videoHeight || src.height;
  if (!w || !h || (src.tagName === 'IMG' && !src.complete)) { canvas.width = canvas.height = 1; return canvas; }
  const rect = src.getBoundingClientRect();
  const ratio = Math.min(1, 1024 / w, 1024 / h, Math.max(64, rect.width * 1.6) / w, Math.max(64, rect.height * 1.6) / h);
  canvas.width = Math.max(1, Math.round(w * ratio)); canvas.height = Math.max(1, Math.round(h * ratio));
  try { canvas.getContext('2d')?.drawImage(src,0,0,canvas.width,canvas.height); }
  catch { /* A media element can be waiting for its first frame. Its real frame and captions still print around it. */ }
  return canvas;
}

// Inline vector attributes carry the art's path data and local masks. Its paint can come from CSS variables on the board.
function vector(el, doc, win, budget, root = false) {
  if (budget.remaining-- <= 0 || el.matches(OMIT)) return null;
  const out = doc.createElementNS(SVG, el.localName), style = win.getComputedStyle(el);
  for (const a of el.attributes) {
    if (/^on/i.test(a.name) || a.name === 'style' || a.name === 'class') continue;
    if (/href$/i.test(a.name) && !looksLocal(`url("${a.value}")`,doc.baseURI)) continue;
    if (a.namespaceURI) out.setAttributeNS(a.namespaceURI,a.name,a.value); else out.setAttribute(a.name,a.value);
  }
  copyStyle(out,style,root ? [...CSS,...VECTOR_STYLE] : VECTOR_STYLE,doc.baseURI);
  for (const child of el.childNodes) {
    if (child.nodeType === 3) out.append(doc.createTextNode(child.data));
    else if (child.nodeType === 1) { const node = vector(child,doc,win,budget); if (node) out.append(node); }
  }
  return out;
}

function staticElement(el, env, root = false) {
  const {doc,win,budget} = env;
  // The attached quiz has its own dock plane and can extend several mascot widths beyond EMI.
  if (!root && el.matches('.xe .xq-dock')) return null;
  if (env.mediaMembers?.has(el)) {
    // Keep the cabinet's original grid and offsets while each real picture peels off on a separate plane.
    const space = doc.createElement('span'), style = win.getComputedStyle(el);
    for (const key of ['display','position','inset','width','height','margin','grid-area','order','flex','aspect-ratio']) {
      space.style.setProperty(key,style.getPropertyValue(key));
    }
    space.style.visibility = 'hidden';
    return space;
  }
  if (el.namespaceURI === SVG) return vector(el,doc,win,budget,true);
  if (budget.remaining-- <= 0 || el.matches(OMIT) || el.hidden) return null;
  const style = win.getComputedStyle(el);
  if (style.display === 'none' || style.visibility === 'hidden' || +style.opacity <= 0.001) return null;
  // Player-selected media and licensed feeds stay with their actual, credited players. Never freeze their pixels.
  // EMI's first-party renderer caches each local sprite as a blob URL. Only her own body element can be lent as-is.
  const cachedEmi = el.matches('.xe .xe-sprite > img') && (el.currentSrc || el.src).startsWith('blob:');
  if (el.localName === 'video' || (el.localName === 'img' && !cachedEmi && !gameArt(el.currentSrc || el.src,doc.baseURI))) return null;
  const pixels = el.matches('img,canvas');
  const tag = el.localName;
  // A never-opened slot intentionally has no first paint. This transparent still comes directly from the shipped
  // rose Mini Slots GLB at its real whole-cabinet camera, with the knight handle and the game's non-media reel deal.
  // Keep its sibling controls, wallet and overlays from this run. The already-visible cabinet lends its live canvas.
  const coldSlot = el.closest('.mini-slot') && (el.closest('.dk.min') || !el.closest('.mini-slot').hasAttribute('data-cabinet-ready'));
  if (coldSlot && el.classList.contains('mini-slot-result') && /^Loading cabinet|could not load/i.test(el.textContent)) return null;
  if (coldSlot && el.classList.contains('mini-slot-cabinet')) {
    const out = doc.createElement('img');out.alt = '';out.draggable = false;out.src = 'assets/arcade/button-slot-cabinet.webp';
    out.dataset.buttonSlotStill = '';
    copyStyle(out,style,CSS,doc.baseURI);out.style.pointerEvents = 'none';return out;
  }
  // Only use ordinary HTML elements: never instantiate custom components or native browsing contexts in a copy.
  // A WebGL default framebuffer can be cleared after every composite. Loan the original cabinet canvas, including
  // the knight and reel faces. Other toy canvases may hold a user-selected picture, so they use the same native path.
  const url = tag === 'img' ? el.currentSrc || el.src : '';
  const out = tag === 'img' && /\.gif(?:$|[?#])/i.test(url) && el.complete && el.naturalWidth ? freezePixels(el,doc)
    : doc.createElement(tag.includes('-') || tag === 'a' ? 'span' : tag);
  // A fresh EMI pose can still be decoding on the freeze frame. Reuse that exact local art URL, including transparency.
  if (out.localName === 'img') {
    out.alt = ''; out.draggable = false;
    if (cachedEmi) env.loans.push({source:el,stub:out}); else out.src = url;
  }
  if (tag === 'canvas') {
    out.width = el.width; out.height = el.height;
    out.dataset.buttonNativeCanvas = '';
    env.loans.push({source:el,stub:out});
  }
  copyStyle(out,style,CSS,doc.baseURI);
  out.style.pointerEvents = 'none';
  if (el.id === 'count') out.dataset.buttonPrintedCount = '';
  if (el.id === 'fz-roll') out.dataset.buttonPrintedRoll = '';
  if (el.matches('.xe .xq-dock')) out.dataset.buttonEmiExtra = 'quiz';
  if (el.matches('.xe .xe-s,.xe .xe-t')) out.dataset.buttonEmiExtra = 'bubble';
  if (el.matches('.xe .xe-c')) out.dataset.buttonEmiExtra = 'controls';
  if (el.getAttribute('dir')) out.dir = el.dir;
  if (tag === 'input') { out.type = el.type; out.value = el.value; out.checked = el.checked; }
  if (tag === 'button') out.type = 'button';
  if (pixels) return out;
  function decoration(pseudo) {
    const s = win.getComputedStyle(el,pseudo), content = s.content;
    if (!content || content === 'none' || content === 'normal' || s.display === 'none' || s.visibility === 'hidden') return;
    const part = doc.createElement('span');
    copyStyle(part,s,CSS,doc.baseURI);
    if (/^["']/.test(content)) part.textContent = content.slice(1,-1).replace(/\\(["'\\])/g,'$1');
    out.append(part);
  }
  decoration('::before');
  for (const child of el.childNodes) {
    if (child.nodeType === 3) out.append(doc.createTextNode(child.data));
    else if (child.nodeType === 1) { const nested = staticElement(child,env); if (nested) out.append(nested); }
  }
  decoration('::after');
  return out;
}

function printed(el, r, env) {
  const visual = staticElement(el,env,true);
  if (!visual) return null;
  const node = env.doc.createElement('div'); node.inert = true; node.setAttribute('aria-hidden','true');
  node.style.cssText = `position:relative;display:block;pointer-events:none;width:${r.w}px;height:${r.h}px;`;
  const shadow = node.attachShadow({mode:'open'});
  const layoutW = el.offsetWidth || r.w, layoutH = el.offsetHeight || r.h;
  Object.assign(visual.style,{ position:'absolute',left:'0',top:'0',right:'auto',bottom:'auto',margin:'0',
    width:`${layoutW}px`,height:`${layoutH}px`,minWidth:'0',minHeight:'0',maxWidth:'none',maxHeight:'none',
    boxSizing:'border-box',zoom:'1',translate:'none',rotate:'none',scale:'none',transformOrigin:'0 0',transform:`scale(${r.w/layoutW},${r.h/layoutH})` });
  shadow.append(visual);
  return node;
}

/** Capture just before the board is released. x/y/w/h use the same viewport space as getBoundingClientRect(). */
export function captureButtonBoard(ctx,mediaMembers) {
  const doc = ctx.world?.ownerDocument || document, win = doc.defaultView;
  const width = win.innerWidth, height = win.innerHeight, surfaces = [], sources = new Set();
  const env = {doc,win,budget:{remaining:8500},loans:[],mediaMembers};
  function add(el,kind,anchor = null) {
    if (!el || mediaMembers?.has(el) || sources.has(el) || surfaces.length >= 74 || env.budget.remaining <= 0) return;
    const r = rectOf(el);
    if ((!anchor && !within(r,width,height)) || r.w <= 1 || r.h <= 1) return;
    // Full-screen colour, rain and particle blankets belong to the wall rather than the cargo.
    if (kind === 'object' && r.w > width*.85 && r.h > height*.7) return;
    const node = printed(el,r,env);
    if (!node) return;
    const owner = el.dataset.fzOwner || el.dataset.id || '';
    const label = el.getAttribute('aria-label') || el.querySelector('.dk-label,.tag,.nm')?.textContent || el.textContent?.trim().slice(0,72) || owner || kind;
    const asset = CABINET_ART[owner] && el.querySelector(CABINET_ART[owner]);
    let artRect = null;
    if (asset) { const art = rectOf(asset); if (art.w > 0 && art.h > 0) artRect = {...art,x:art.x-r.x,y:art.y-r.y}; }
    if (anchor) { r.x = anchor.x + (anchor.w-r.w)/2; r.y = anchor.y + (anchor.h-r.h)/2; }
    sources.add(el);
    surfaces.push({...r,kind,label,owner,node,source:el,artRect,anchor,minimized:!!anchor});
  }
  function group(selector,kind) { for (const el of doc.querySelectorAll(selector)) add(el,kind); }
  group('#btn-wrap','button'); group('#counter','score'); group('#account-chip','name');
  if (!sources.has(doc.getElementById('account-chip'))) {
    const serial = doc.querySelector('#l-meters > [data-fz-owner="dr_serial"]');
    if (serial && within(rectOf(serial),width,height)) add(serial,'name');
    else {
      const counter = doc.getElementById('counter'), place = counter && rectOf(counter);
      if (place) {
        const counterStyle = win.getComputedStyle(counter), label = ctx.identity?.displayName?.trim() || `Signee #${1000 + (Number(ctx.state?.seed) || 0) % 9000}`;
        const node = doc.createElement('span'); node.inert = true; node.setAttribute('aria-hidden','true');
        const w = Math.min(230,Math.max(110,label.length*9+22)), h = 29;
        node.style.cssText = `position:relative;display:block;width:${w}px;height:${h}px;box-sizing:border-box;padding:4px 9px;overflow:hidden;white-space:nowrap;text-overflow:ellipsis;border-radius:4px;border:1px solid #ffffff8a;background:#21182b;color:${counterStyle.color};font:600 15px/19px system-ui;text-align:center`;
        node.textContent = label;
        surfaces.push({x:place.x+(place.w-w)/2,y:place.y+place.h,w,h,node,kind:'name',label,owner:'identity',source:null,emerging:true});
      }
    }
  }
  // The engine can hide the purchase shelf in the synchronous draw immediately before take(). Its rows still exist.
  const shop = doc.getElementById('shop'), hiddenShop = !!shop?.hidden;
  if (hiddenShop) shop.hidden = false;
  try { group('#shop .row','shop'); } finally { if (hiddenShop) shop.hidden = true; }
  for (const el of doc.querySelectorAll('.dk')) {
    const id = el.dataset.fzOwner;
    if (el.matches('.open')) add(el,'toy');
    else if (CABINETS.has(id)) {
      const chip = doc.querySelector(`.dk-chip[data-fz-owner="${id}"]`), anchor = chip && rectOf(chip);
      if (!anchor || !within(anchor,width,height)) continue;
      const style = el.style.getPropertyValue('display'), priority = el.style.getPropertyPriority('display');
      try {
        el.style.setProperty('display','block','important');
        add(el,'toy',anchor);
      } finally {
        if (style) el.style.setProperty('display',style,priority); else el.style.removeProperty('display');
      }
    }
  }
  group('#phrase,#l-ticker,#l-meters > *','hud');
  group('#l-banner > *','ad');
  for (const el of doc.querySelectorAll('#l-over > *,#l-floaters > *,#l-fx > *,#cards > *')) {
    const owner = el.dataset.fzOwner || '';
    if (el.matches('.xe')) add(el,'emi');
    else add(el,/^(?:o_popup|o_second|o_banner|banner|ads?|billboard|sponsor)$/.test(owner) || /^AD(?:\b|[A-Z])/.test(el.textContent.trim()) ? 'ad' : 'object');
  }
  group('#dock-strip .dk-chip','tab');
  let active = false, disposed = false;
  function activate() {
    if (active || disposed) return;
    active = true;
    for (const l of env.loans) {
      if (!l.source.parentNode || !l.stub.parentNode) continue;
      l.home = doc.createComment('tunnel canvas'); l.source.before(l.home);
      l.inline = l.source.getAttribute('style');
      l.source.style.cssText = l.stub.style.cssText;
      l.source.dataset.buttonNativeCanvas = '';
      // This is the same already-rendered canvas. Moving it preserves its backing WebGL surface.
      l.stub.replaceWith(l.source);
    }
  }
  function dispose() {
    if (disposed) return;
    disposed = true;
    for (const l of env.loans) if (l.home) {
      l.source.removeAttribute('data-button-native-canvas');
      if (l.inline !== null) l.source.setAttribute('style',l.inline); else l.source.removeAttribute('style');
      l.home.replaceWith(l.source);
    }
    for (const s of surfaces) s.node.remove();
  }
  return {width,height,surfaces,activate,dispose};
}
