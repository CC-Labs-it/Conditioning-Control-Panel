/* Seconds of active play. Give the first three objects time to register, even on the busiest page. */
const OPENING = [1.36, 1.12, 0.92];
const GAPS = [0.14, 0.10, 0.07];
const BOARD_BY = 11.2;

export function scanPlan(count) {
  const shots = [];
  let time = 0.28;
  for (let i = 0; i < Math.min(count, OPENING.length); i++) {
    shots.push({ start: time, duration: OPENING[i] });
    time += OPENING[i];
    if (i + 1 < count) time += GAPS[i];
  }

  const remaining = count - shots.length;
  if (remaining > 0) {
    // A quick roll-off after the opener, then a steady acceleration through the controls.
    // The linear part averages 0.75 over the remaining targets. Smaller pages use their natural pace.
    const falloff = 0.76;
    const rolloffTime = 0.54 * (1 - falloff ** remaining) / (1 - falloff);
    const weight = remaining === 1 ? 1 : remaining * 0.75;
    const cruise = Math.min(0.28, (BOARD_BY - time - rolloffTime) / weight);
    for (let i = 0; i < remaining; i++) {
      const progress = i / Math.max(1, remaining - 1);
      const duration = 0.54 * falloff ** i + cruise * (1 - 0.5 * progress);
      shots.push({ start: time, duration });
      time += duration;
    }
  }
  const nameHold = time, firstCut = nameHold + .46, secondCut = firstCut + 1.03 + .14;
  const tracking = secondCut + 1.27, black = tracking + .20, terminal = tracking + 1.6;
  return { shots, nameHold, firstCut, secondCut, tracking, black, terminal, end: terminal + 6.8 };
}
