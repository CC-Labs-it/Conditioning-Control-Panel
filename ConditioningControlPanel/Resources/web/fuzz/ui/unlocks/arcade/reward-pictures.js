import { thumbnailKeyReader } from '../../photo-identity.js';

const ownPrintLeases=new WeakMap();

// Local copies may have different filenames and object URLs for identical
// artwork. Compare their whole thumbnails in memory before showing them.
function distinctOwnPrints(doc,pictures,cards,signal,revision) {
  if (!cards.length) return cards;
  let leases=ownPrintLeases.get(pictures);
  if (!leases || leases.revision!==revision) {
    leases={revision,held:new Map()};
    ownPrintLeases.set(pictures,leases);
  }
  const readKey=thumbnailKeyReader(doc);
  const selected=[],keys=new Set();
  for(const card of cards) {
    const key=readKey(card.image,card.url);
    if(keys.has(key)||leases.held.has(key))continue;
    selected.push(card);keys.add(key);
    if(signal)leases.held.set(key,signal);
  }
  if(signal && keys.size)signal.addEventListener('abort',()=>{
    for(const key of keys)if(leases.held.get(key)===signal)leases.held.delete(key);
  },{once:true});
  return selected;
}

// Reward images are display-only stills. A caller holds its AbortSignal until
// the last print leaves the screen, including after decoding has completed.
export async function loadRewardPictures(ctx, preferred=null, signal) {
  const pictures=ctx.pictures, revision=pictures.rewardRevision;
  const rows=pictures.rewardBatch(3,preferred?.url,signal);
  const current=()=>!signal?.aborted && revision===pictures.rewardRevision;
  const doc=ctx.button.el.ownerDocument;
  async function decode(row) {
    if (!current() || row.kind !== 'picture') return null;
    if (row.url===preferred?.url && preferred.image) return {...row,image:preferred.image};
    const image=new doc.defaultView.Image();
    image.referrerPolicy='strict-origin';
    let cancel;
    const cancelled=new Promise(resolve=>{ cancel=()=>resolve(false); });
    const timer=doc.defaultView.setTimeout(cancel,2500);
    const lease=pictures.acquire?.(row);
    signal?.addEventListener('abort',cancel,{once:true});
    try {
      image.src=lease?.url || row.url;
      const ready=await Promise.race([image.decode().then(()=>true),cancelled]);
      if (!ready || !current() || !image.naturalWidth || !image.naturalHeight) return null;
      const still=doc.createElement('canvas');
      const scale=Math.min(1,1024/Math.max(image.naturalWidth,image.naturalHeight));
      still.width=Math.max(1,Math.round(image.naturalWidth*scale));
      still.height=Math.max(1,Math.round(image.naturalHeight*scale));
      still.getContext('2d').drawImage(image,0,0,still.width,still.height);
      return {...row,image:still};
    } catch { return null; }
    finally {
      doc.defaultView.clearTimeout(timer);
      signal?.removeEventListener('abort',cancel);
      image.removeAttribute('src');
      lease?.release();
    }
  }
  const cards=await Promise.all(rows.map(decode));
  if(!current())return [];
  const ready=cards.filter(Boolean);
  return pictures.source==='own' ? distinctOwnPrints(doc,pictures,ready,signal,revision) : ready;
}
