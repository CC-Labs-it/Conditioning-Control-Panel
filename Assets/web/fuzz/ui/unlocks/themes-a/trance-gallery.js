import { thumbnailKeyReader } from '../../photo-identity.js';

export function mountTranceGallery(ctx) {
  const limit = 4;
  const wall = ctx.el('div', '', null, ctx.dock());
  wall.className = 'trance-photo-wall';
  ctx.css(`
.trance-photo-wall {display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:6px}
.trance-photo-wall:empty {display:none}
.trance-photo-wall > :only-child {grid-column:1/-1}
.trance-photo-wall .ta-side {position:relative;aspect-ratio:1;min-width:0;overflow:hidden;border-radius:12px;opacity:.85}
.trance-photo-wall img {display:block;width:100%;height:100%;object-fit:cover}
.trance-photo-wall small {position:absolute;left:4px;bottom:2px;font:10px system-ui;color:white;text-shadow:0 0 3px #000}
  `);
  const doc = wall.ownerDocument;
  const readKey = thumbnailKeyReader(doc);
  const shown = [];
  let pool = [], cursor = 0, nextTile = 0;
  let generation = 0, busy = false, pending = null;

  function remove(tile) {
    tile.el.remove();
    tile.release();
  }
  async function fill(rotate = false) {
    if (busy || !pool.length || (shown.length === limit && !rotate)) return;
    const version = generation;
    busy = true;
    try {
      for (let tries = pool.length; tries > 0; tries--) {
        const entry = pool[cursor++ % pool.length];
        if (shown.some(t => t.entry === entry || t.entry.key === entry.key)) continue;
        if (entry.retryAfter > Date.now()) continue;
        const image = doc.createElement('img');
        image.alt = '';
        image.decoding = 'async';
        image.referrerPolicy='strict-origin';
        const lease = ctx.pictures.acquire(entry.row);
        const release = () => { image.removeAttribute('src'); lease.release(); };
        pending = release;
        const timeout = setTimeout(release, 8000);
        try {
          image.src = lease.url;
          await image.decode();
          if (version !== generation) return;
          entry.key ??= readKey(image,entry.row.url);
          if (shown.some(t => t.entry.key === entry.key)) continue;
          const el = doc.createElement('div');
          el.className = 'ta-side';
          el.append(image);
          if (entry.row.credit) {
            const credit = doc.createElement('small');
            credit.textContent = entry.row.credit;
            el.append(credit);
          }
          const tile = {el,entry,release};
          if (shown.length < limit) {
            wall.append(el);
            shown.push(tile);
          } else {
            const index = nextTile++ % limit;
            const old = shown[index];
            old.el.replaceWith(el);
            old.release();
            shown[index] = tile;
          }
          pending = null;
          if (shown.length === limit) return;
        } catch {
          if (version !== generation) return;
          entry.retryAfter = Date.now() + 30000;
        } finally {
          clearTimeout(timeout);
          if (pending === release) { pending = null; release(); }
        }
      }
    } finally {
      if (version === generation) busy = false;
    }
  }
  function cancel() {
    generation++;
    pending?.();
    pending = null;
    busy = false;
  }
  function sync(reset) {
    const rows = ctx.pictures.stillsForDisplay(Infinity,{allowScrolller:true});
    if (!reset && rows.length === pool.length && rows.every((r,i) => pool[i].row.url === r.url)) return;
    cancel();
    const previous = new Map(pool.map(entry => [entry.row.url,entry]));
    const present = new Set(rows.map(r => r.url));
    for (let i = shown.length - 1; i >= 0; i--) {
      if (reset || !present.has(shown[i].entry.row.url)) remove(shown.splice(i,1)[0]);
    }
    pool = rows.map(row => (!reset && previous.get(row.url)) || {row});
    cursor = 0;
    if (reset) nextTile = 0;
    void fill();
  }
  ctx.on('pictures', () => sync(true));
  ctx.on('pictures-added', () => sync(false));
  ctx.every(9000, () => { void fill(true); });
  ctx.cleanup(() => {
    cancel();
    for (const tile of shown.splice(0)) remove(tile);
    pool = [];
  });
  sync(true);
}
