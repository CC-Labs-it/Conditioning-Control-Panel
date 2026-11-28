// PNG, WebP and AVIF can be pictures or animations under the same MIME type.
// Inspect their container headers on the device, without decoding their pixels.
const text = new TextDecoder('latin1');
const tag = (bytes, offset) => text.decode(bytes.subarray(offset, offset+4));

export async function localImageInfo(file) {
  try {
    const head = new Uint8Array(await file.slice(0,4096).arrayBuffer());
    if (tag(head,0).startsWith('GIF8')) return {kind:'gif',mime:'image/gif'};
    if (head[0]===0x1a && head[1]===0x45 && head[2]===0xdf && head[3]===0xa3) return {kind:'clip',mime:'video/webm'};
    if (tag(head,0) === 'RIFF' && tag(head,8) === 'WEBP') {
      return {kind:tag(head,12) === 'VP8X' && (head[20] & 2) ? 'gif' : 'picture',mime:'image/webp'};
    }
    if (head[0] === 137 && tag(head,1).startsWith('PNG')) {
      // APNG requires acTL before the first IDAT. Jump over any intervening
      // metadata rather than reading a large profile or the compressed pixels.
      let offset = 8;
      for (let chunks=0; chunks<256 && offset+8<=file.size; chunks++) {
        const h = offset+8 <= head.length ? head.subarray(offset,offset+8) :
          new Uint8Array(await file.slice(offset,offset+8).arrayBuffer());
        const name = tag(h,4);
        if (name === 'acTL') return {kind:'gif',mime:'image/png'};
        if (name === 'IDAT' || name === 'IEND') return {kind:'picture',mime:'image/png'};
        offset += 12 + new DataView(h.buffer,h.byteOffset,h.byteLength).getUint32(0);
      }
      return {kind:'gif',mime:'image/png'};
    }
    if (tag(head,4) === 'ftyp') {
      const size = new DataView(head.buffer).getUint32(0);
      // A still AVIF carries avif; an image sequence carries the avis brand.
      if (size > head.length || size < 16) return {kind:'gif'};
      const brands=[];
      for (let i=8; i+4<=size; i+=4) if (i !== 12) brands.push(tag(head,i));
      if (brands.includes('avis')) return {kind:'gif',mime:'image/avif'};
      if (brands.includes('avif')) return {kind:'picture',mime:'image/avif'};
      if (brands.includes('qt  ')) return {kind:'clip',mime:'video/quicktime'};
      if (brands.some(b=>/^(isom|iso[2-9]|mp4[12]|M4V |avc1)$/.test(b))) return {kind:'clip',mime:'video/mp4'};
    }
    return {kind:'picture'};
  } catch {
    // A file we could not inspect stays out of the standalone photo pool.
    return {kind:'gif'};
  }
}
