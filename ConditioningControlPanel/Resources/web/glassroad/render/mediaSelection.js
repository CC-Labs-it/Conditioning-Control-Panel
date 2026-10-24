/* Shared renderer/host allocation. Slot ids are unique within a built layout.
 * Keep all trap cards on media and spread the requested world share in spatial order.
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
const spread = (slots, count) => slots.filter((_, i) => Math.ceil((i + 1) * count / slots.length) > Math.ceil(i * count / slots.length));

/** Returns original slot objects, without mutation, in fixed frame/spatial/id order.
 * Pass the slots actually built for the tier. Billboard visibility cohorts each
 * retain a distributed mix, including when high-tier geometry is hidden on phone.
 * The default preserves the established half-media assignment. Other shares round
 * the complete world count once, then distribute remaining faces across cohorts.
 */
export function selectScrolllerSlots(slots = [], { share = .5 } = {}) {
  share = Number.isFinite(share) ? Math.max(0, Math.min(1, share)) : .5;
  const group = frame => slots.filter(slot => slot?.frame === frame).sort(spatial);
  const billboards = group('billboard');
  const phone = billboards.filter(slot => slot.phone !== false);
  const desktop = billboards.filter(slot => slot.phone === false);
  if (share !== .5) {
    const cohorts = [phone, desktop, group('halo'), group('tunnel')].map((slots, order) =>
      ({ slots, order, count: Math.floor(slots.length * share), fraction: slots.length * share % 1 }));
    const total = cohorts.reduce((n, cohort) => n + cohort.slots.length, 0);
    const extra = Math.round(total * share) - cohorts.reduce((n, cohort) => n + cohort.count, 0);
    [...cohorts].sort((a, b) => b.fraction - a.fraction || a.order - b.order)
      .slice(0, extra).forEach(cohort => cohort.count++);
    const chosen = cohorts.map(cohort => spread(cohort.slots, cohort.count));
    return [...group('card'), ...[...chosen[0], ...chosen[1]].sort(spatial), ...chosen[2], ...chosen[3], ...group('boostPad')];
  }
  // Balance two odd cohorts so the complete group still selects ceil(count / 2).
  const desktopStart = phone.length % 2 && desktop.length % 2 ? 1 : 0;
  const boards = [...alternate(phone), ...alternate(desktop, desktopStart)].sort(spatial);
  return [...group('card'), ...boards, ...alternate(group('halo')), ...alternate(group('tunnel')), ...group('boostPad')];
}
