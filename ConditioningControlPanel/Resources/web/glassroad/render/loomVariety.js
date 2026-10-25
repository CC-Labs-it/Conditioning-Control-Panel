// Authored variations of the real Loom, shared by landmark discs and gate takeovers.
import { normalizeParams2, createFieldRenderer, composeFrame, drawFallbackFrame } from '../../arcademy/engine/loom/loomField.js';
import { loopS, screenRateMul } from './spiralMath.js';
import { createSpiralBeatClock } from './spiralBeat.js';

export function loomVariation(index) {
  const i = ((index % 6) + 6) % 6;
  const styles = ['ribbon', 'petal', 'golden', 'tunnel', 'arch', 'log'];
  const colors = [['#20ef9c','#baffda','#126a67'], ['#43dfea','#d9ffea','#2479ac'], ['#cfef6b','#34d8ac','#266a69']][i % 3];
  return normalizeParams2({ schema: 2, speed: 2, bg: { kind: 'solid', color: '#05191b' },
    layer: { style: styles[i], arms: [3,6,5,4,7,2][i], turns: [2,1.5,3,2.5,1,4][i], duty: 0.38,
      direction: 1, colors, bandMode: i % 2 ? 'hard' : 'gradient', speedMul: 1 },
    layer2: { enabled: true, style: styles[(i + 2) % 6], arms: [6,3,4,8,2,5][i], turns: 1.5,
      duty: 0.23, direction: 1, colors: [colors[1], colors[2]], bandMode: 'gradient', speedMul: 2 },
    glow: 0.2, pulse: { amp: 0.06, cycles: 1 }, wobble: { amp: 0.12 + i * 0.02, freq: 2 + i % 4, cycles: 1 },
    hueCycles: 1, centerpiece: { kind: 'none' } });
}

// One bounded Loom context, three small output canvases, no per-frame shader compilation.
export function createLoomDisplayFrames(make, tier = 'high') {
  const px = tier === 'phone' ? 256 : 512, source = make(px, px);
  let field = null, age = 0;
  const beat = createSpiralBeatClock();
  try { field = createFieldRenderer(source); } catch { /* Canvas fallback retains the real Loom. */ }
  const rows = [0,1,2].map(i => ({ canvas: make(px, px), params: loomVariation(i), phase: i / 3, refreshed: false, visible: true }));
  function draw(row) {
    const ctx = row.canvas.getContext('2d');
    if (field) composeFrame(ctx, field, row.params, row.phase, px, px);
    else drawFallbackFrame(ctx, row.params, row.phase, px, px);
  }
  rows.forEach(draw);
  return { rows, update(dt, gentle = false, view = {}) {
    for (const row of rows) row.refreshed = false;
    if (!(dt > 0) || view.paused) return false;
    age += dt;
    const speed = beat.update(dt, view, gentle);
    for (const row of rows) row.phase = (row.phase + dt * screenRateMul(row.params, { gentle, speed }) / loopS(row.params)) % 1;
    const due = age >= 1 / (tier === 'phone' ? 8 : 15);
    if (due) age = 0;
    let changed = false;
    for (let i = 0; i < rows.length; i++) {
      const row = rows[i], visible = view.loomVisible?.[i] !== false;
      if (visible && (due || !row.visible)) { draw(row); row.refreshed = true; changed = true; }
      row.visible = visible;
    }
    return changed;
  }, dispose() {
    field?.dispose();
    field = null;
  } };
}
