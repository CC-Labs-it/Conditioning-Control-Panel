// Each strike reaches the motifs in sequence. The attack, recoil and fade
// are sampled from the original strike time, including on a slower display.
export function passingBeat(scene, x) {
  let turn = 0, swell = 0;
  for (const hit of scene.events) {
    const age = scene.time - hit.time;
    if (age < 0 || age >= 3.8) continue;
    const distance = Math.abs(x - hit.u * scene.w);
    const arrival = distance / ((scene.w * .70 + 75) * hit.speed);
    const local = age - arrival;
    if (local < -.11 || local > 2.5) continue;
    const attack = Math.min(1, Math.max(0, (local + .11) / .17));
    const fade = Math.exp(-Math.max(0, local) * 2.1) * Math.min(1, (3.8 - age) * 3);
    const weight = hit.power * attack * attack * (3 - 2 * attack) * fade;
    swell += weight;
    turn += weight * hit.polarity * Math.cos(Math.max(0, local) * (6.1 + 2.2 * hit.seed));
  }
  return { swell: Math.tanh(1.7 * swell), turn: Math.tanh(2.3 * turn) };
}

export function pearl(g, x, y, radius, color) {
  g.fillStyle = color;
  g.beginPath();
  g.arc(x, y, radius, 0, 2 * Math.PI);
  g.fill();
}

export function sparkle(g, x, y, radius, color, tilt = 0) {
  g.save();
  g.translate(x, y);
  g.rotate(tilt);
  g.beginPath();
  g.moveTo(0, -radius);
  const waist = .15 * radius;
  const side = .73 * radius;
  g.quadraticCurveTo(waist, -waist, side, 0);
  g.quadraticCurveTo(waist, waist, 0, radius);
  g.quadraticCurveTo(-waist, waist, -side, 0);
  g.quadraticCurveTo(-waist, -waist, 0, -radius);
  g.fillStyle = color;
  g.fill();
  g.restore();
}
