/* preview/pictures.js - the flat half of the preview box (lane B3-W5): profile cards, avatars, EMI desk sheets and
 * paint swatches as <img>. No three, so these still draw when the counter falls back to its plain list.
 *
 *   const h = await openPicture(box, spec, { base, motion, gentle }); h.show(); h.hide(); h.dispose();
 *
 * A swatch with several patterns walks them (a cross-fade) unless motion is off; then it holds its still. */

const WALK_MS = 1600;

function picture(doc, src, cls) {
  const img = doc.createElement('img');
  img.className = cls; img.alt = ''; img.decoding = 'async'; img.draggable = false;
  img.src = src;
  const ready = typeof img.decode === 'function' ? img.decode()
    : new Promise((res, rej) => { img.onload = res; img.onerror = () => rej(new Error(`no picture ${src}`)); });
  return { img, ready };
}

export async function openPicture(box, spec, { base = './assets/', motion = 'full' } = {}) {
  const doc = box.ownerDocument, wrap = doc.createElement('div');
  wrap.className = `pc-pv pc-pv-${spec.kind}`;
  const parts = [];
  const add = (file, cls) => { const p = picture(doc, base + file, cls); parts.push(p); wrap.append(p.img); return p.img; };
  let walk = null;
  if (spec.kind === 'desk') { add(spec.base, 'pc-pv-img'); add(spec.over, 'pc-pv-img'); }
  else if (spec.kind === 'swatch') {
    const srcs = motion !== 'off' && spec.srcs && spec.srcs.length > 1 ? spec.srcs : [spec.still || spec.srcs[0]];
    const imgs = srcs.map((f) => add(f, 'pc-pv-img pc-pv-tile'));
    if (imgs.length > 1) {
      let i = 0;
      imgs[0].classList.add('pc-pv-lit');
      walk = () => { imgs[i].classList.remove('pc-pv-lit'); i = (i + 1) % imgs.length; imgs[i].classList.add('pc-pv-lit'); };
    } else imgs[0].classList.add('pc-pv-lit');
  } else add(spec.src, 'pc-pv-img');
  try { await Promise.all(parts.map((p) => p.ready)); } catch (e) { for (const p of parts) p.img.removeAttribute('src'); throw e; }

  let timer = 0, gone = false;
  return {
    show() {
      if (gone) return;
      box.append(wrap);
      void wrap.offsetWidth;   // let the IN start from the hidden state
      wrap.classList.add('pc-pv-on');
      if (walk && !timer) timer = setInterval(walk, WALK_MS);
    },
    hide() { wrap.classList.remove('pc-pv-on'); clearInterval(timer); timer = 0; },
    dispose() { gone = true; clearInterval(timer); timer = 0; wrap.remove(); for (const p of parts) p.img.removeAttribute('src'); },
  };
}
