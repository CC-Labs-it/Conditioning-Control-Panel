// Repaint the cyan enamel. Neutral highlights and warm metal contain no cyan excess.
export function amethystEnamel(image, width = image.naturalWidth) {
  const canvas = image.ownerDocument.createElement('canvas');
  canvas.width = width;
  canvas.height = Math.round(width * image.naturalHeight / image.naturalWidth);
  const pen = canvas.getContext('2d');
  pen.imageSmoothingQuality = 'high';
  pen.drawImage(image,0,0,canvas.width,canvas.height);
  const surface = pen.getImageData(0,0,canvas.width,canvas.height);
  const rgba = surface.data;
  for (let i = 0; i < rgba.length; i += 4) {
    const cyan = Math.max(0, Math.min(rgba[i+1],rgba[i+2]) - rgba[i] - 4);
    rgba[i]   += .76 * cyan;
    rgba[i+1] -= .63 * cyan;
    rgba[i+2] += .15 * cyan;
  }
  pen.putImageData(surface,0,0);
  return canvas;
}
