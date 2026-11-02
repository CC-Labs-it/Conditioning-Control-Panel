// Reservoir membership and physical water depth are shared by camera, audio and karts.
// A road crossing above/below the reservoir elsewhere in the lap is not underwater.
export function createImmersion(track) {
  const start = track.sections?.find(s => s.id === 'submerged');
  const end = track.sections?.find(s => s.id === 'emerge');
  const waterY = track.reservoir?.waterY;
  if (!start || !Number.isFinite(waterY)) return () => 0;
  const from = start.fromS, to = end?.toS ?? start.toS, length = track.lengthM;
  const smooth = v => { const t = Math.max(0, Math.min(1, v)); return t * t * (3 - 2 * t); };
  return (s, worldY) => {
    if (!Number.isFinite(s) || !Number.isFinite(worldY)) return 0;
    const at = length > 0 ? ((s % length) + length) % length : s;
    if (at < from || at > to) return 0;
    const edge = smooth(Math.min(at - from, to - at) / 5);
    return edge * smooth((waterY - worldY + 0.1) / 1.8);
  };
}
