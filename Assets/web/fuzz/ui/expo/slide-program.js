// A record with `cues` changes picture at those seconds of its recording and rests on its last picture. Every other
// record keeps the old program: three-slide events change at 30% and 70%, the rest take even shares.
export function restingSlide(row) {
  if (!row || !row.slides.length) return undefined;
  if (row.cues?.length) return row.slides.at(-1);
  return row.set === 'event' && row.slides.length === 3 ? row.slides[1] : row.slides[0];
}

export function slideIndex(row, progress, seconds = progress * (row.seconds || 0)) {
  const count = row.slides.length;
  if (count && row.cues?.length)
    return Math.min(count - 1, row.cues.filter(at => seconds >= at).length);
  if (row.set === 'event' && count === 3) {
    if (progress >= .70) return 2;
    if (progress >= .30) return 1;
    return 0;
  }
  return Math.min(count - 1, Math.floor(Math.max(0,progress) * count));
}
