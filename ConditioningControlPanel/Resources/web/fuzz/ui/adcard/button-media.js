import { mediaReady } from '../media-cache.js';

// A picture on the board is a plane in the tunnel. Borrow each displayed player, existing held GIF frame, or
// CSS picture layer itself. No canvas readback, image decoding, or request for another picture happens here.
const PHOTO_HOST = '.scr-slot,.scr-body,.trance-photo-wall .ta-side,.tb-tile,[data-pop-picture],.mini-slot-reel-pic';
const CREDIT = '.scr-credit,.tb-credit,.et-credit,.mde-credit,.mini-slot-credit';
const CSS_PICTURE = '.vw-k,[data-fz-owner="tunnel"][style*="url("]';
const ENDED = '[data-button-ending],#l-ending,#pause,#game-controls,#dock-strip';

function publicGameArt(url,base) {
  try {
    const u = new URL(url,base);
    return /^https?:$/.test(u.protocol) && u.origin === new URL(base).origin && /\/(?:fuzz|arcademy|glassroad|backroom)\/(?:.*\/)?assets\//.test(u.pathname);
  } catch { return false; }
}

function isPlayerPicture(el,doc) {
  if (el.tagName === 'VIDEO') return true;
  if (el.matches('img[data-kind],.mde-face,.et-p') || el.closest(PHOTO_HOST)) return true;
  // EMI's body and baked face are game art cached as blob URLs. They print with their owner.
  if (el.matches('.et-b,.et-f') || el.closest('.xe-sprite,.dk-chip,.ico')) return false;
  // Theme frames are often just an img and a credit in a div on the back layer. Local game art used by the
  // online demo is still a photo when assigned to one of these frames. The button/toy skins stay with the toy.
  if (el.matches('#l-back img[data-fz-owner],#l-wash img[data-fz-owner],#l-screens img[data-fz-owner]') &&
      el.parentElement?.querySelector(':scope > span,:scope > small')) return true;
  return !publicGameArt(el.currentSrc || el.src,doc.baseURI);
}

function visibleBox(el,env) {
  const raw = el.getBoundingClientRect();
  if (raw.width < 1 || raw.height < 1) return null;
  let left = Math.max(0,raw.left), right = Math.min(env.width,raw.right);
  let top = Math.max(0,raw.top), bottom = Math.min(env.height,raw.bottom);
  let alpha = 1, radius = '', roundBox = null;
  for (let n = el; n && n !== env.doc.documentElement; n = n.parentElement) {
    const s = env.style(n);
    if (s.display === 'none' || s.visibility === 'hidden' || s.contentVisibility === 'hidden') return null;
    alpha *= +s.opacity;
    if (alpha < .004) return null;
    const clipX = n !== el && /^(?:hidden|clip|scroll|auto)$/.test(s.overflowX);
    const clipY = n !== el && /^(?:hidden|clip|scroll|auto)$/.test(s.overflowY);
    if (clipX || clipY) {
      const box = n.getBoundingClientRect();
      if (clipX) {left = Math.max(left,box.left);right = Math.min(right,box.right);}
      if (clipY) {top = Math.max(top,box.top);bottom = Math.min(bottom,box.bottom);}
      if (!roundBox && s.borderRadius && s.borderRadius !== '0px') { radius=s.borderRadius;roundBox=box; }
    }
    if (right-left < 1 || bottom-top < 1) return null;
  }
  // A rounded screen several times larger than this picture is not the picture's own mask.
  if (roundBox && (roundBox.width > raw.width*1.35 || roundBox.height > raw.height*1.35)) radius = '';
  return {x:left,y:top,w:right-left,h:bottom-top,raw,alpha,radius};
}

function nearbyCredit(el,source) {
  // The wall and the Swipe Feed use a plain positioned sibling rather than a class on the attribution.
  const isCredit = n => n && !n.hidden && n.childElementCount===0 && /^via\s|scrolller/i.test(n.textContent.trim());
  for (let n=el.parentElement, i=0;n && i<5;n=n.parentElement,i++) {
    const label = [...n.children].find(c => c.matches(CREDIT) && !c.hidden && c.textContent.trim()) || [...n.children].find(isCredit);
    if (label) return {node:label,text:label.textContent.trim()};
    if (n.matches('.dk,#world,.layer,.vw-sway')) break;
  }
  const scope = el.closest('.et-box,.mini-slot,.mde');
  const shared = scope?.querySelector(CREDIT);
  if (shared && !shared.hidden && shared.textContent.trim()) return {node:shared,text:shared.textContent.trim()};
  return {node:null,text:source==='scrolller' ? 'via Scrolller' : ''};
}

// The last scene has no viewport rim. Free its ordinary wall photos and its screen-edge crops only.
// A feed window, puzzle piece, circular frame or decorated photo still has the owner's aperture there.
function freePhotoBox(el,view,env) {
  const {raw,w,h}=view;
  if (el.closest('.dk,.scr,.tb-tile,.ta-side,.mde-tile,.et-emi,[data-pop-picture],.mini-slot-reel-pic')) return null;
  const plainWall = el.matches('.vw-k') && el.closest('.vw-sway > *');
  let ownerCrop = false;
  for (let a=el;a && a!==env.doc.body;a=a.parentElement) {
    const s=env.style(a);
    if ((s.maskImage && s.maskImage!=='none') || (s.clipPath && s.clipPath!=='none')) return null;
    if (a.matches('.tb-bar-on') || a.querySelector(':scope > .tb-bar-on')) return null;
    if (a===el || a.matches('#world,.layer,.vw-sway')) continue;
    if (!/hidden|clip|auto|scroll/.test(s.overflowX+' '+s.overflowY)) continue;
    const r=a.getBoundingClientRect();
    const clipsSide=raw.left<r.left-.5||raw.right>r.right+.5;
    const clipsHeight=raw.top<r.top-.5||raw.bottom>r.bottom+.5;
    if (clipsSide||clipsHeight) ownerCrop=true;
  }
  if (plainWall) {
    const cell=plainWall.getBoundingClientRect(), width=plainWall.clientWidth, height=plainWall.clientHeight;
    if (width<2 || height<2 || (w>=width-.75 && h>=height-.75)) return null;
    // The wall's incoming flip can project a 165px square to a thin strip. Use the actual cell's layout size,
    // with its original background-size/position, rather than baking that transient projection into the paper.
    return {x:cell.left,y:cell.top,w:width,h:height};
  }
  const edge=raw.left<0||raw.top<0||raw.right>env.width||raw.bottom>env.height;
  if (ownerCrop || !edge || (w>=raw.width-.75 && h>=raw.height-.75)) return null;
  return {x:raw.left,y:raw.top,w:raw.width,h:raw.height};
}

export function captureButtonMedia(ctx) {
  const doc = ctx.world.ownerDocument, win = doc.defaultView, loans = [], members = new WeakSet(), styles = new WeakMap();
  const env = {doc,width:win.innerWidth,height:win.innerHeight,style(el) {
    if (!styles.has(el)) styles.set(el,win.getComputedStyle(el));
    return styles.get(el);
  }};
  const world = ctx.world;
  function record(original, owner = original, key) {
    if (!original || members.has(original) || original.closest(ENDED)) return;
    const measured = visibleBox(original,env);
    if (!measured) return;
    const {x,y,w,h,raw,alpha,radius} = measured, computed = env.style(original);
    const node = doc.createElement('div'); node.inert = true;
    node.style.cssText=`position:relative;display:block;overflow:hidden;pointer-events:none;box-sizing:border-box;width:${w}px;height:${h}px`;
    node.style.borderRadius = radius || computed.borderRadius;
    // The Censored theme draws its bars as siblings of the player. Keep their actual painted coverage and Peek opacity
    // on the borrowed picture when the frame and its photograph separate. Its owner's window stays clipped below.
    const decorated=owner.closest('.tb-tile,.scr-body');
    for (const bar of decorated?.querySelectorAll(':scope > .tb-bar-on') || []) {
      const b=visibleBox(bar,env);
      if (!b || b.x+b.w<=x || b.y+b.h<=y || b.x>=x+w || b.y>=y+h) continue;
      const mask=doc.createElement('span'); mask.dataset.buttonCensor='';
      mask.style.cssText=`position:absolute;pointer-events:none;z-index:3;left:${b.x-x}px;top:${b.y-y}px;width:${b.w}px;height:${b.h}px;opacity:${b.alpha}`;
      mask.style.backgroundColor=env.style(bar).backgroundColor;node.append(mask);members.add(bar);
    }
    const credit = nearbyCredit(owner,ctx.pictures?.source);
    if (credit.text) {
      const text = doc.createElement('small'); text.textContent=credit.text;
      text.style.cssText='position:absolute;right:2px;bottom:1px;z-index:5;padding:0 2px;background:#19152485;color:white;font:10px/1.2 Arial,sans-serif;text-shadow:0 1px 2px #000;white-space:nowrap;pointer-events:none';
      node.append(text);
      if (credit.node) members.add(credit.node);
    }
    members.add(original); members.add(owner);
    const container = owner.closest('.dk,.tb-tile,.ta-side');
    const label = container?.getAttribute('aria-label') || owner.closest('.scr')?.querySelector('.scr-tag')?.textContent || 'Picture';
    const surface = {x,y,w,h,node,kind:'media',label,owner:owner.closest('[data-fz-owner]')?.dataset.fzOwner || 'pictures',originLayer:owner.closest('.layer')?.id || ''};
    const css = {
      position:'absolute',inset:'auto',left:`${raw.left-x}px`,top:`${raw.top-y}px`,width:`${raw.width}px`,height:`${raw.height}px`,
      'box-sizing':'border-box','min-width':'0','min-height':'0','max-width':'none','max-height':'none',margin:'0',
      transform:'none',translate:'none',rotate:'none',scale:'none',animation:'none',transition:'none',opacity:String(alpha),
      'object-fit':computed.objectFit,'object-position':computed.objectPosition,filter:computed.filter,'mix-blend-mode':computed.mixBlendMode,
      'background-size':computed.backgroundSize,'background-position':computed.backgroundPosition,'background-repeat':computed.backgroundRepeat,
      display:'block',visibility:'visible','pointer-events':'none','z-index':'0',
    };
    const full=freePhotoBox(original,measured,env);
    if (full) surface.unclip=()=>{
      if (surface.node.hidden) return;
      Object.assign(surface,full);
      node.style.width=`${full.w}px`;node.style.height=`${full.h}px`;
      Object.assign(css,{left:'0px',top:'0px',width:`${full.w}px`,height:`${full.h}px`});
      if (original.parentNode===node) for (const k of ['left','top','width','height']) original.style.setProperty(k,css[k],'important');
    };
    loans.push({original,owner,surface,key,css,signature:key(),anchor:null,inline:null});
  }

  for (const el of world.querySelectorAll('img[src],video[src]')) {
    if (!isPlayerPicture(el,doc) || !mediaReady(el)) continue;
    // holdMedia can already have put a real still on top of a GIF at the end of a run.
    // Carry that exact native canvas. The capture never asks for its pixels.
    const shown = el.__still?.isConnected ? el.__still : el;
    record(shown,el,()=>el.currentSrc || el.getAttribute('src'));
  }
  for (const el of world.querySelectorAll(CSS_PICTURE)) {
    if (!/url\(/i.test(env.style(el).backgroundImage)) continue;
    record(el,el,()=>el.style.backgroundImage);
  }
  // The slot has an existing display canvas for each visible video/held reel window, and a tiny hidden decoder.
  for (const el of world.querySelectorAll('.mini-slot-reel-pic[data-on="true"] > canvas')) record(el,el,()=>el.parentElement?.dataset.on || 'true');

  function activate() {
    for (const l of loans) {
      const {original,surface}=l;
      if (l.anchor || surface.node.hidden) continue;
      if (!original.isConnected || l.key()!==l.signature) {surface.node.hidden=true;continue;}
      l.inline=original.getAttribute('style');l.anchor=doc.createComment('picture in tunnel');original.before(l.anchor);
      for (const [k,v] of Object.entries(l.css)) original.style.setProperty(k,v,'important');
      const first=surface.node.firstChild;
      if (surface.node.isConnected && surface.node.moveBefore) surface.node.moveBefore(original,first);
      else surface.node.insertBefore(original,first);
    }
  }
  // Existing owners can retire their player while it is on loan. Restore only a currently lent node, never a
  // retired one. A new source invalidates every plane, including its printed credit, before the next paint.
  ctx.on('pictures',()=>{for (const {surface} of loans) surface.node.hidden=true;});
  function dispose() {
    for (const l of loans) {
      const {original,anchor,surface}=l;
      if (anchor?.isConnected && original.parentNode===surface.node) {
        if (l.inline===null) original.removeAttribute('style'); else original.setAttribute('style',l.inline);
        if (surface.node.hidden && l.key()===l.signature) { original.remove(); anchor.remove(); }
        else anchor.replaceWith(original);
      } else anchor?.remove();
      surface.node.remove();
    }
    loans.length=0;
  }
  return {surfaces:loans.map(l=>l.surface),members,activate,dispose};
}
