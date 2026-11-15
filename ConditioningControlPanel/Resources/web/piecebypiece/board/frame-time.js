// Animation follows wall time; physics and decorative effects retain their safe step.
export function frameTime(now, last) {
  const elapsed = Math.max(0, (now - last) / 1000);
  return { elapsed, dt: Math.min(0.1, elapsed) };
}
