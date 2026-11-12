// Use the whole thumbnail so copies of the same image under different names
// share an identity. Display-only cross-origin images retain their source key.
export function thumbnailKeyReader(doc) {
  const sample = doc.createElement('canvas');
  sample.width = sample.height = 24;
  const pen = sample.getContext('2d', {willReadFrequently:true});
  return (image, fallback) => {
    if (!pen) return fallback;
    try {
      pen.clearRect(0,0,24,24);
      pen.drawImage(image,0,0,24,24);
      const pixels = pen.getImageData(0,0,24,24).data;
      const ratio = (image.naturalWidth || image.width) / (image.naturalHeight || image.height);
      return Math.round(ratio * 1000) + ':' + String.fromCharCode(...pixels);
    } catch {
      sample.width = 24;
      return fallback;
    }
  };
}
