import { frameRect } from '../../perf.js';
import { any } from './emi-kit.js';

const PET_LINES = ['oh. do that again.', 'saving that one.', 'my turn next. still me.', 'head pats count as income.', 'yes, right there.', 'i like your management style.'];
const PET_CSS = `
html.et-check-pet [data-et-pet]{pointer-events:auto!important}
html.et-check-pet :is(.et-emi,.et-ui-emi)[data-et-pet]{clip-path:inset(22% 18% 21% 17%)}
[data-et-pet-heart]{position:fixed;z-index:2147482999;color:#ff72af;-webkit-text-stroke:.7px #61213b;font:900 19px/1 system-ui,sans-serif;text-shadow:0 1px 0 #fff3f8;pointer-events:none}
[data-et-pet-bark]{position:fixed;z-index:2147482999;width:max-content;max-width:min(210px,72vw);box-sizing:border-box;padding:4px 8px;border:1px solid #482c40;border-radius:8px;background:#fff2f8;color:#301c33;font:600 14px/1.2 var(--font);text-shadow:none;pointer-events:none}
`;

/** The browser delivers the real click to the shop, HUD or button underneath. A synchronous hit test finds which visible EMI felt it. */
export function bindCopyPets(ctx, enabled) {
  const doc = ctx.button.el.ownerDocument;
  ctx.css(PET_CSS);
  let bark = null, last = -Infinity, oldLine = '';
  const cooled = new WeakMap(), pulses = new WeakMap(), hearts = new Set();
  const clear = () => {
    bark?.remove(); bark = null;
    for (const heart of hearts) heart.remove();
    hearts.clear();
  };
  function showHeart(el, here, time) {
    if (time-(pulses.get(el) ?? -Infinity) < 300) return;
    pulses.set(el, time);
    const heart = doc.createElement('span');
    heart.dataset.etPetHeart = ''; heart.setAttribute('aria-hidden','true'); heart.textContent = '♥';
    heart.style.left = Math.max(0,Math.min(doc.defaultView.innerWidth-20, here.right-12))+'px';
    heart.style.top = Math.max(5,here.top+here.height*.25)+'px';
    if (hearts.size >= 6) {
      const oldest = hearts.values().next().value;
      oldest.remove(); hearts.delete(oldest);
    }
    doc.body.appendChild(heart); hearts.add(heart);
    const still = ctx.gentle || ctx.reduced;
    ctx.anim(heart, [{opacity:1}, {opacity:1,offset:.45}, {opacity:0,translate:still?'0 0':'0 -12px'}], {duration:650,fill:'both'});
    ctx.after(660,()=>{heart.remove();hearts.delete(heart);});
  }
  function pet(el) {
    if (!el || ctx.paused || !enabled()) return;
    const time = doc.defaultView.performance.now();
    const here = frameRect(el);
    showHeart(el, here, time);
    if (time-last < 950 || time-(cooled.get(el) ?? -Infinity) < 2100) return;
    last=time; cooled.set(el,time); bark?.remove();
    oldLine = any(PET_LINES.filter(line => line !== oldLine));
    const line = doc.createElement('span'); line.dataset.etPetBark = ''; line.textContent = oldLine;
    doc.body.appendChild(line); bark = line;
    const {width,height}=line.getBoundingClientRect(), view=doc.defaultView;
    line.style.left = Math.max(3,Math.min(view.innerWidth-width-3,here.left+(here.width-width)/2))+'px';
    line.style.top = Math.max(3,Math.min(view.innerHeight-height-3,here.top>=height+6 ? here.top-height-5 : here.bottom+4))+'px';
    if (!(ctx.gentle || ctx.reduced)) ctx.anim(line,[{opacity:0,translate:'0 3px'},{opacity:1,translate:'0 0'}],{duration:130});
    ctx.after(1900,()=>{if(bark===line){line.remove();bark=null;}});
  }
  function click(event) {
    if (!event.isTrusted || !event.detail || ctx.paused || !enabled() || event.target.closest?.('#wake,#pause,#cards,.et-dlg')) return;
    let target;
    doc.documentElement.classList.add('et-check-pet');
    try { target=doc.elementFromPoint(event.clientX,event.clientY)?.closest('[data-et-pet]'); }
    finally { doc.documentElement.classList.remove('et-check-pet'); }
    pet(target);
  }
  doc.addEventListener('click',click,true);
  for (const type of ['pause','suspend','ending']) ctx.on(type, clear);
  ctx.cleanup(()=>{doc.removeEventListener('click',click,true);clear();});
  return { pet,clear };
}
