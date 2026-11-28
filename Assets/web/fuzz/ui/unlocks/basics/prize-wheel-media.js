import { SECTORS } from '../../../sim/prize-wheel.js';
import { slotEffects } from '../arcade/slot-effects.js';

const imageSectors = SECTORS.filter(s => s.effect === 'picture');

export function createWheelMedia(ctx, dock, view) {
  const doc = dock.ownerDocument;
  const effects = slotEffects(ctx);
  const credit = ctx.el('span', 'display:block;text-align:center;font:11px system-ui;color:var(--ink);', 'via Scrolller', dock);
  credit.hidden = true;
  const photoUrls = new WeakMap();
  const pending = new Set();
  let photos = new Map(), version = 0, activeKind = '', refreshAfterSpin = false;
  function stopEffect() {
    activeKind = '';
    effects.clear();
  }
  function reset() {
    version++;
    refreshAfterSpin = false;
    for (const image of pending) image.removeAttribute('src');
    pending.clear();
    photos = new Map();
    view.setPictures(photos);
    credit.hidden = true;
    stopEffect();
  }
  async function fillMissing() {
    const source = ctx.pictures.source;
    if (source !== 'own' && source !== 'scrolller') return;
    const token = version;
    const rows = ctx.pictures.stillsForDisplay(imageSectors.length,{allowScrolller:true});
    if (ctx.store.get('spin') || effects.active) { refreshAfterSpin = true; return; }
    refreshAfterSpin = false;
    for (const sector of imageSectors.slice(rows.length)) {
      if (photos.delete(sector.id)) view.setPictures(photos);
    }
    if (!photos.size) credit.hidden = true;
    for (const [i,row] of rows.entries()) {
      if (token !== version) return;
      const sector = imageSectors[i];
      if (photoUrls.get(photos.get(sector.id)) === row.url || [...pending].some(image => image.sector === sector.id)) continue;
      const image = new doc.defaultView.Image();
      image.sector = sector.id;
      image.referrerPolicy='strict-origin';
      pending.add(image);
      const lease = ctx.pictures.acquire(row);
      try {
        if (row.kind !== 'picture') continue;
        image.src = lease.url;
        await image.decode();
        if (token !== version || !image.naturalWidth || !image.naturalHeight) return;
        if (ctx.store.get('spin') || effects.active) { refreshAfterSpin = true; return; }
        // Drawing is display-only, so credited photos do not need CORS headers.
        const still = doc.createElement('canvas');
        const scale = Math.min(1, 1024 / Math.max(image.naturalWidth, image.naturalHeight));
        still.width = Math.max(1, Math.round(image.naturalWidth * scale));
        still.height = Math.max(1, Math.round(image.naturalHeight * scale));
        still.getContext('2d').drawImage(image, 0, 0, still.width, still.height);
        photoUrls.set(still,row.url);
        photos.set(sector.id, still);
        view.setPictures(photos);
        if (row.credit && source === 'scrolller') credit.hidden = false;
      } catch { /* Unavailable media keeps this wedge's pattern. */ }
      finally { pending.delete(image); lease.release(); }
      if (token !== version) return;
    }
  }
  function refresh() { reset(); void fillMissing(); }
  function landed(sector) {
    stopEffect();
    if (dock.dock?.visible === false || ctx.paused || !dock.getClientRects().length) return;
    const calm = ctx.gentle || ctx.reduced;
    if (sector.effect === 'picture' && calm) return;
    const image = photos.get(sector.id);
    activeKind = sector.effect === 'words' ? 'words' : image ? 'picture' : 'spiral';
    const preferred = image && {url:photoUrls.get(image), image};
    effects.start(activeKind, {portrait:() => view.portrait(sector.id)}, {preferred}, sector.trigger || '');
  }
  ctx.frame(() => {
    if (refreshAfterSpin && !ctx.store.get('spin') && !effects.active) void fillMissing();
    if (!effects.active) return;
    if (dock.dock?.visible === false ||
        (activeKind !== 'words' && (ctx.gentle || ctx.reduced))) stopEffect();
    else effects.draw();
  });
  for (const event of ['pause', 'suspend', 'ending']) ctx.on(event, stopEffect);
  ctx.on('pictures', refresh);
  ctx.on('pictures-added', fillMissing);
  ctx.cleanup(reset);
  refresh();
  return { landed };
}
