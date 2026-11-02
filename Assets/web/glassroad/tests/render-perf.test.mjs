// tests/render-perf.test.mjs - polish 2 PERF lane: the frame cap gate, dynamic resolution, the display
// options and the shadow modes per tier. Pure modules only (no three.js).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createFrameGate } from '../core/clock.js';
import { createDynRes, dynLines, DYNRES, TIERS, shadowMode, setTier, Q, shortGpu } from '../render/quality.js';
import { cleanOptions, cycleOption, choiceLabel, stepOption, OPTION_DEFAULTS, OPTION_CHOICES } from '../hud/options.js';

/** Drive a gate at a display rate for `seconds`; returns rendered frame times. */
function drive(gate, hz, seconds, t0 = 0, jitter = 0) {
  const out = [], dt = 1000 / hz;
  let seed = 3;
  const rnd = () => { seed = (seed * 16807) % 2147483647; return seed / 2147483647 - 0.5; };
  for (let i = 0, t = t0; t < t0 + seconds * 1000; i++, t = t0 + i * dt) {
    const now = t + rnd() * jitter;
    if (gate.due(now)) out.push(now);
  }
  return out;
}
const rate = (times) => (times.length - 1) * 1000 / (times[times.length - 1] - times[0]);

test('frame gate: display cap renders every frame and learns the display interval', () => {
  const g = createFrameGate({ cap: 'display' });
  const r = drive(g, 144, 3);
  assert.ok(Math.abs(rate(r) - 144) < 1);
  assert.ok(Math.abs(g.displayMs - 1000 / 144) < 0.2);
  assert.ok(Math.abs(g.targetMs - 1000 / 144) < 0.2);
});

test('frame gate: 60 on a 144 Hz display averages 60 with 2 and 3 frame steps', () => {
  const g = createFrameGate({ cap: 60 });
  drive(g, 144, 1);                       // learn the display
  const r = drive(g, 144, 10, 1000, 0.6);
  assert.ok(Math.abs(rate(r) - 60) < 0.5, 'rate ' + rate(r));
  for (let i = 1; i < r.length; i++) { const d = r[i] - r[i - 1]; assert.ok(d > 12 && d < 22.5, 'gap ' + d); }
  assert.ok(Math.abs(g.targetMs - 1000 / 60) < 0.01);
});

test('frame gate: whole ratios render every nth frame even with a drifting display clock', () => {
  const g = createFrameGate({ cap: 60 });
  drive(g, 119.88, 1);
  const r = drive(g, 119.88, 20, 1000, 1);
  for (let i = 1; i < r.length; i++) { const d = r[i] - r[i - 1]; assert.ok(d > 15 && d < 18.5, 'gap ' + d); }
  const g30 = createFrameGate({ cap: 30 });
  drive(g30, 59.94, 1);
  const r30 = drive(g30, 59.94, 20, 1000, 1);
  for (let i = 1; i < r30.length; i++) { const d = r30[i] - r30[i - 1]; assert.ok(d > 31 && d < 35.5, 'gap ' + d); }
});

test('frame gate: a cap at or above the display rate never skips; setCap switches live', () => {
  const g = createFrameGate({ cap: 60 });
  drive(g, 60, 1);
  const r = drive(g, 60, 5, 1000, 1.5);
  assert.ok(Math.abs(rate(r) - 60) < 0.5);
  g.setCap(30);
  const r30 = drive(g, 60, 5, 7000);
  assert.ok(Math.abs(rate(r30) - 30) < 0.5);
  g.setCap('display');
  assert.ok(Math.abs(rate(drive(g, 60, 2, 13000)) - 60) < 0.5);
});

test('frame gate: a stall resyncs instead of rendering a burst', () => {
  const g = createFrameGate({ cap: 60 });
  drive(g, 144, 1);
  let t = 1000;
  for (; t < 2000; t += 1000 / 144) g.due(t);
  t += 400;                                // a 400 ms hitch
  const after = [];
  for (let i = 0; i < 20; i++, t += 1000 / 144) if (g.due(t)) after.push(t);
  for (let i = 1; i < after.length; i++) assert.ok(after[i] - after[i - 1] > 12);
});

/** Feed a dynres controller frames of `cost(scale)` ms on a vsync display of `dispMs` for `seconds`. */
function run(d, cost, dispMs, seconds, trace = []) {
  let t = 0;
  while (t < seconds * 1000) {
    const work = cost(d.scale), gap = Math.ceil(work / dispMs - 1e-9) * dispMs;   // missed vsyncs
    const r = d.sample(gap, dispMs, dispMs * 1.75);
    if (r !== null) trace.push([Math.round(t), r]);
    t += gap;
  }
  return trace;
}

/** The renderer's wiring: the frame gate learns from the same gaps, dynLines turns it into the lines. */
function runGated(cost, dispMs, seconds, gate = createFrameGate({ cap: 'display' }), d = createDynRes(), t0 = 1000) {
  let t = t0;
  while (t < t0 + seconds * 1000) {
    const gap = Math.ceil(cost(d.scale) / dispMs - 1e-9) * dispMs;
    t += gap; gate.due(t);
    const { target, slow } = dynLines(gate.displayMs, gate.capMs);
    d.sample(gap, target, slow);
  }
  return { gate, d, t };
}

test('dynres: a GPU that never holds 60 still drops pixels (the gate learns its slow frame as the display)', () => {
  const slow = (s) => 22 * s * s;                           // 22 ms of work = 33 ms frames on a 59 to 60 Hz display
  const a = runGated(slow, 1000 / 60, 30);
  assert.ok(a.d.scale < 1 && a.d.scale >= DYNRES.min && slow(a.d.scale) <= 1000 / 60, 'scale ' + a.d.scale);
  const flat = createFrameGate({ cap: 'display' });         // the trap itself: 33 ms gaps teach a 33 ms display
  for (let t = 0; t < 30000; t += 33.3) flat.due(t);
  assert.ok(flat.displayMs > 30);
  assert.ok(dynLines(flat.displayMs, 0).target <= DYNRES.displayMaxMs);
  // fine in the menu, slow in the race: the learnt interval drifts up, the scale still comes down
  const menu = runGated(() => 8, 1000 / 60, 5);
  assert.equal(menu.d.scale, 1);
  const race = runGated(slow, 1000 / 60, 60, menu.gate, menu.d, menu.t);
  assert.ok(race.d.scale < 1 && slow(race.d.scale) <= 1000 / 60, 'scale ' + race.d.scale);
  // a healthy frame keeps full scale; a 30 cap still aims at 33 ms
  assert.equal(runGated(() => 12, 1000 / 60, 20).d.scale, 1);
  assert.ok(Math.abs(dynLines(16.7, 1000 / 30).target - 1000 / 30) < 1e-9);
});

test('dynres: a fast frame holds full scale; a fill-bound slow one steps down within about a second', () => {
  const d = createDynRes();
  assert.deepEqual(run(d, () => 4, 1000 / 60, 10), []);
  assert.equal(d.scale, 1);
  const slow = createDynRes();
  const tr = run(slow, (s) => 22 * s * s, 1000 / 60, 3);   // 22 ms at full scale: 30 fps on a 60 Hz display
  assert.ok(tr.length >= 1 && tr[0][0] <= 1100, JSON.stringify(tr));
  assert.ok(slow.scale <= 0.85 && slow.scale >= DYNRES.min);
});

test('dynres: holds the display rate without oscillating and never leaves min..max', () => {
  const d = createDynRes();
  const tr = run(d, (s) => 20 * s * s, 1000 / 60, 120);    // fits 60 fps below about 0.91
  const tail = tr.filter(([t]) => t > 30000);
  assert.ok(tail.length <= 6, 'steps after 30 s: ' + JSON.stringify(tail));
  for (const [, s] of tr) assert.ok(s >= DYNRES.min && s <= DYNRES.max);
  assert.ok(d.scale >= 0.75 && d.scale <= 0.9, 'scale ' + d.scale);
});

test('dynres: a CPU-bound slow frame gets its pixels back and stops stepping down', () => {
  const d = createDynRes();
  const tr = run(d, () => 25, 1000 / 60, 15);              // scale changes nothing
  assert.equal(d.scale, 1, JSON.stringify(tr));
  assert.ok(tr.length <= 2, JSON.stringify(tr));
});

test('dynres: never below min on a hopeless frame; reset goes back to full', () => {
  const d = createDynRes();
  run(d, (s) => 200 * s * s, 1000 / 60, 30);
  assert.ok(d.scale >= DYNRES.min);
  d.reset(1);
  assert.equal(d.scale, 1); assert.equal(d.ceiling, 1);
  assert.equal(d.sample(400, 16.7), null);                 // a hidden tab gap is not a slow frame
});

test('options: display rows clean, cycle and label; steering sensitivity steps and clamps', () => {
  const o = cleanOptions(null);
  assert.equal(o.frameCap, 'display'); assert.equal(o.renderScale, 'auto'); assert.equal(o.quality, 'auto');
  assert.equal(o.showFps, false); assert.equal(o.steerSens, 1); assert.equal(o.shadows, true);
  const c = cleanOptions({ frameCap: '30', renderScale: '0.75', quality: 'ultra', showFps: 'yes', steerSens: 9, shadows: false });
  assert.equal(c.frameCap, 30); assert.equal(c.renderScale, 0.75); assert.equal(c.quality, 'auto');
  assert.equal(c.showFps, false); assert.equal(c.steerSens, 1.4); assert.equal(c.shadows, false);
  assert.equal(cleanOptions({ steerSens: 0.73 }).steerSens, 0.7);
  assert.equal(cleanOptions({ steerSens: 'x' }).steerSens, 1);
  assert.equal(cleanOptions({ frameCap: null }).frameCap, 'display');
  assert.equal(cycleOption('frameCap', 'display'), 60);
  assert.equal(cycleOption('frameCap', 30), 'display');
  assert.equal(cycleOption('quality', 'auto', -1), 'phone');
  assert.equal(choiceLabel('renderScale', 0.5), '50%');
  assert.equal(choiceLabel('quality', 'phone'), 'Low');
  assert.equal(choiceLabel('frameCap', 'display'), 'Off');
  assert.equal(stepOption('steerSens', 1.4, 1), 1.4);
  assert.equal(stepOption('steerSens', 1, -1), 0.9);
  assert.equal(stepOption('volSfx', 0.5, 1), 0.6);
  for (const [k, list] of Object.entries(OPTION_CHOICES)) assert.equal(list[0], OPTION_DEFAULTS[k]);
});

test('quality: every tier names a shadow mode; the Shadows switch off wins', () => {
  assert.equal(TIERS.high.shadows, 'map'); assert.equal(TIERS.mid.shadows, 'map'); assert.equal(TIERS.phone.shadows, 'blob');
  assert.equal(TIERS.mid.shadowMapPx * 2, TIERS.high.shadowMapPx);
  setTier('mid');
  assert.equal(shadowMode({ shadows: true }), 'map');
  assert.equal(shadowMode({ shadows: false }), 'off');
  assert.equal(shadowMode(null, TIERS.phone), 'blob');
  setTier('high');
  assert.equal(Q.shadows, 'map');
});

test('quality: short adapter names for the fps readout', () => {
  assert.equal(shortGpu('ANGLE (NVIDIA, NVIDIA GeForce RTX 5080 (0x00002C02) Direct3D11 vs_5_0 ps_5_0, D3D11)'), 'NVIDIA GeForce RTX 5080');
  assert.equal(shortGpu('ANGLE (Intel, Intel(R) UHD Graphics 770 (0x00004680) Direct3D11 vs_5_0 ps_5_0, D3D11)'), 'Intel(R) UHD Graphics 770');
  assert.equal(shortGpu('nvidia blackwell'), 'nvidia blackwell');
  assert.equal(shortGpu(null), '');
});

test('clock: renderTime is smooth at 144 Hz while simTime steps at 60 Hz', async () => {
  const { createClock } = await import('../core/clock.js');
  const c = createClock({ hz: 60 });
  const dt = 1000 / 144, sim = [], ren = [];
  for (let i = 0; i <= 288; i++) { const o = c.frame(i * dt); sim.push(c.simTime); ren.push(o.renderTime); }
  const steps = (a) => a.slice(2).map((v, i) => v - a[i + 1]);
  const s = steps(sim), r = steps(ren);
  assert.ok(s.some((x) => x === 0), 'sim time holds on some frames');
  for (const x of r.slice(5)) assert.ok(Math.abs(x - dt / 1000) < 1e-6, 'render step ' + x);
  assert.ok(Math.abs(c.renderTime - c.simTime) <= c.dt);
});
