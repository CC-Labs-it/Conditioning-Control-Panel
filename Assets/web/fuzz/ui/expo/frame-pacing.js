// A frame may arrive a fraction early on a 60 Hz display. Carry that fraction
// into the next deadline so the 30 Hz profile stays even without gaining time.
export function createExpoPaintClock(lite) {
  const interval=1/30, tolerance=.001;
  let elapsed=0;
  return (dt, resized=false) => {
    if (!lite) return true;
    elapsed+=dt;
    if (!resized && elapsed<interval-tolerance) return false;
    elapsed=elapsed<interval-tolerance ? 0 : (elapsed-interval)%interval;
    return true;
  };
}
