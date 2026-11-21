export function restingSlide(row) {
  if (!row || !row.slides.length) return undefined;
  return row.set === 'event' && row.slides.length === 3 ? row.slides[1] : row.slides[0];
}

export function slideIndex(row, progress) {
  const count = row.slides.length;
  if (row.set === 'event' && count === 3) {
    if (progress >= .70) return 2;
    if (progress >= .30) return 1;
    return 0;
  }
  return Math.min(count - 1, Math.floor(Math.max(0,progress) * count));
}
