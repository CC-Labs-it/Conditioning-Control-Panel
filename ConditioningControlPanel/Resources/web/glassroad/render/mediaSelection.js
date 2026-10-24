/* Shared renderer/host allocation. Slot ids are unique within a built layout.
 * Keep all trap cards on media and alternate world faces in spatial order.
 */
const compareText = (a, b) => a < b ? -1 : a > b ? 1 : 0;
function compareIds(a, b) {
  const left = String(a.id), right = String(b.id);
  const aa = left.match(/\d+|\D+/g) || [], bb = right.match(/\d+|\D+/g) || [];
  for (let i = 0; i < Math.min(aa.length, bb.length); i++) {
    let delta;
    if (/^\d+$/.test(aa[i]) && /^\d+$/.test(bb[i])) {
      const x = aa[i].replace(/^0+(?=\d)/, ''), y = bb[i].replace(/^0+(?=\d)/, '');
      delta = x.length - y.length || compareText(x, y);
    } else delta = compareText(aa[i], bb[i]);
    if (delta) return delta;
  }
  return aa.length - bb.length || compareText(left, right);
}
const position = slot => Number.isFinite(slot.s) ? slot.s : 0;
const spatial = (a, b) => position(a) - position(b) || compareIds(a, b);
const alternate = (slots, start = 0) => slots.filter((_, i) => i % 2 === start);

/** Returns original slot objects, without mutation, in fixed frame/spatial/id order.
 * Pass the slots actually built for the tier. Billboard visibility cohorts each
 * retain an alternating mix, including when high-tier geometry is hidden on phone.
 */
export function selectScrolllerSlots(slots = []) {
  const group = frame => slots.filter(slot => slot?.frame === frame).sort(spatial);
  const billboards = group('billboard');
  const phone = billboards.filter(slot => slot.phone !== false);
  const desktop = billboards.filter(slot => slot.phone === false);
  // Balance two odd cohorts so the complete group still selects ceil(count / 2).
  const desktopStart = phone.length % 2 && desktop.length % 2 ? 1 : 0;
  const boards = [...alternate(phone), ...alternate(desktop, desktopStart)].sort(spatial);
  return [...group('card'), ...boards, ...alternate(group('halo')), ...alternate(group('tunnel'))];
}
