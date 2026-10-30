import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import * as loomField from '../../arcademy/engine/loom/loomField.js';
import {
  SPIRAL_LOOK as L, loadLoom, gateParams, loopS, spanRad, layerRot, tOfR, rOfT, loomCover, buildParticles, loomState,
  particleLocal, toWorld, gateFrames, axialOf, lateralOf, partOffset, pickSlots, crossed, budgetFor, burstCount, burstEnvelope,
  twinkle, hueRows, checkLayout, toHex, hexRgb,
} from '../render/spiralMath.js';

const layout = JSON.parse(readFileSync(new URL('../tracks/threshold.spirals.json', import.meta.url), 'utf8'));
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
const loom = await loadLoom();
const TAU = Math.PI * 2;
const thin = { radiusM: 20, depthM: 0 };
const hexOf = (rgb) => toHex(rgb);

test('spirals: the layout sits in the brief corridors, one Loom source per gate, every gate different', () => {
  assert.deepEqual(checkLayout(layout), []);
  assert.equal(layout.track, 'threshold');
  assert.deepEqual(layout.gates.map((g) => g.f), [0.09, 0.17, 0.315, 0.785, 0.965]);   // polish 6: the stacked course (GEO)
  const keys = layout.gates.map((g) => JSON.stringify(gateParams(g, loom)));
  assert.equal(new Set(keys).size, keys.length);
  assert.ok(layout.gates.some((g) => g.depthM > 0) && layout.gates.some((g) => !g.depthM), 'portals and helix tunnels');
});

test('spirals: the real Loom is imported (not forked) and gives the params and the loop timing', () => {
  assert.ok(loom && loom.field && loom.seeded, 'loomField + seeded load by relative path');
  assert.equal(loom.field.normalizeParams2, loomField.normalizeParams2);
  for (const g of layout.gates.filter((x) => x.loom)) {
    const q = gateParams(g, loom), n = loomField.normalizeParams2(g.loom);
    assert.deepEqual({ ...q.layer, direction: 0 }, { ...n.layer, direction: 0 });
    assert.equal(q.layer.direction, 1, 'handedness: layer 1 clockwise');
    assert.equal(loopS(q, loom), loomField.loopMs2(q) / 1000);
  }
  const seeded = layout.gates.find((g) => g.seed != null);
  assert.deepEqual(gateParams(seeded, loom), gateParams(seeded, loom), 'a fixed seed weaves the same gate');
  assert.notDeepEqual(gateParams({ seed: 1 }, loom), gateParams({ seed: 2 }, loom));
  const fb = gateParams(layout.gates[0], null);
  assert.equal(fb.layer.arms, 4, 'without the Loom on the page the stored params still draw');
});

test('spirals: span rule and style radius maps match the Loom', () => {
  assert.ok(Math.abs(spanRad({ arms: 4, colors: ['#ffffff'] }) - TAU / 4) < 1e-9);
  assert.ok(Math.abs(spanRad({ arms: 4, colors: ['#ffffff', '#000000'] }) - TAU / 2) < 1e-9);
  assert.ok(Math.abs(spanRad({ arms: 3, colors: ['#ffffff', '#000000'] }) - TAU) < 1e-9);
  assert.equal(layerRot({ arms: 4, colors: ['#ffffff'], direction: 1, speedMul: 2 }, 0.5), TAU / 4);
  for (const s of ['log', 'arch', 'ribbon', 'golden', 'tunnel', 'petal']) {
    for (const r of [0.1, 0.4, 0.9]) assert.ok(Math.abs(rOfT(tOfR(r, s), s) - r) < 1e-6, s + ' ' + r);
  }
});

test('spirals: Loom params become 3D points on the covered bands, in the thread colours, at every phase', () => {
  for (const g of layout.gates) {
    const q = gateParams(g, loom), P = buildParticles(q, thin, 3000, 7);
    for (const phase of [0, 0.37, 0.81]) {
      const st = loomState(q, phase);
      let on = 0, total = 0, colour = 0, coloured = 0;
      for (let i = 0; i < P.n; i++) {
        const halo = P.a2[i * 4 + 2] < 0.5, glitter = P.a0[i * 4 + 3] >= 2, l2 = (P.a0[i * 4 + 3] % 2) >= 1;
        const [u, v, , rc] = particleLocal(P.a0, P.a2, i, q, thin, st);
        if (halo || rc < 0.08 || rc > 0.98) continue;
        const layer = l2 ? q.layer2 : q.layer, rot = l2 ? st.rot2 : st.rot1;
        const rf = Math.hypot(u, v) / (thin.radiusM * st.zoom), th = Math.atan2(-v, u);
        const wob = q.wobble.amp > 0.001 ? q.wobble.amp * Math.sin(TAU * q.wobble.freq * rf + st.wobPhase) : 0;
        const c = loomCover(layer, rf, th, rot, wob);
        total++; on += c.cov;
        if (!glitter && layer.bandMode === 'hard' && layer.style !== 'petal') {
          coloured++;
          if (hexOf([P.a1[i * 4], P.a1[i * 4 + 1], P.a1[i * 4 + 2]]) === hexOf(hexRgb(layer.colors[c.idx]).map((v) => v + (1 - v) * L.coreWhite))) colour++;
        }
      }
      assert.ok(total > 1000, g.id + ' sampled');
      assert.ok(on / total > 0.97, g.id + ' phase ' + phase + ' on bands ' + (on / total).toFixed(3));
      if (coloured) assert.ok(colour / coloured > 0.95, g.id + ' thread colours ' + (colour / coloured).toFixed(3));
    }
  }
});

test('spirals: direction 1 turns clockwise as the driver sees it (screen angle falls as the phase grows)', () => {
  const q = gateParams(layout.gates[0], loom), P = buildParticles(q, thin, 50, 3);
  const a = particleLocal(P.a0, P.a2, 0, q, thin, loomState(q, 0)), b = particleLocal(P.a0, P.a2, 0, q, thin, loomState(q, 0.01));
  let d = Math.atan2(b[1], b[0]) - Math.atan2(a[1], a[0]);
  d = Math.atan2(Math.sin(d), Math.cos(d));
  assert.ok(d < 0, 'clockwise ' + d);
});

test('spirals: gates stand across the road, centred over it, perpendicular to it, wide enough for the road', () => {
  const frames = gateFrames(track, layout.gates);
  layout.gates.forEach((g, k) => {
    const gf = frames[k], road = track.frameAt(gf.s);
    assert.ok(Math.abs(axialOf(gf, road.p)) < 1e-6);
    assert.ok(lateralOf(gf, road.p) <= L.upM + 1e-6, 'road through the eye');
    assert.ok(g.radiusM > road.w / 2 + L.upM, 'disc spans the road');
    assert.ok(g.radiusM * 2 >= 30 && g.radiusM * 2 <= 60, '30 to 60 m across');
    const q = gateParams(g, loom), P = buildParticles(q, g, 500, 5), st = loomState(q, 0.2);
    for (let i = 0; i < P.n; i++) {
      const w = toWorld(gf, particleLocal(P.a0, P.a2, i, q, g, st));
      const half = (g.depthM || L.jitterM) / 2 + 1e-6;
      assert.ok(Math.abs(axialOf(gf, w)) <= half, 'inside the gate depth');
      assert.ok(lateralOf(gf, w) <= g.radiusM * st.zoom * (1 + (g.taper || 0) * 0.5) + 1e-6);
    }
  });
});

test('spirals: both widened road edges fit through the narrowest pulsing and tapered gate envelope', () => {
  const frames = gateFrames(track, layout.gates);
  for (const [i, gate] of layout.gates.entries()) {
    const gf = frames[i], params = gateParams(gate, loom), depth = gate.depthM || 0;
    const minZoom = Math.min(...Array.from({ length: 200 }, (_, phase) => loomState(params, phase / 200).zoom));
    for (const fraction of [-.5, -.25, 0, .25, .5]) {
      const road = track.frameAt(gf.s + depth * fraction);
      for (const side of [-1, 1]) {
        const edge = road.p.map((v, k) => v + road.b[k] * side * road.w / 2);
        const taper = depth ? 1 + (gate.taper || 0) * axialOf(gf, edge) / depth : 1;
        assert.ok(gate.radiusM * minZoom * taper - lateralOf(gf, edge) >= .5, gate.id + ' retains half a metre beyond the road edge');
      }
    }
  }
});

test('spirals: the curtain parts round a kart, opens ahead, closes behind, never far away', () => {
  const T = [0, 0, 1], K = [0, 1, 0];
  const beside = partOffset([1, 1, 0], K, 1, T);
  assert.ok(Math.hypot(...beside) >= L.partM * 0.8, 'next to the kart moves far');
  assert.ok(beside[0] > 0, 'outward from the kart line');
  assert.equal(Math.hypot(...partOffset([L.partRadiusM + 1, 1, 0], K, 1, T)), 0);
  assert.equal(Math.hypot(...partOffset([1, 1, -L.partTrailM - 1], K, 1, T)), 0, 'closed behind');
  assert.ok(Math.hypot(...partOffset([1, 1, L.partLeadM * 0.5], K, 1, T)) > 0, 'opening ahead');
  assert.equal(Math.hypot(...partOffset([1, 1, 0], K, 0, T)), 0, 'weight 0 = still');
  const g = Math.hypot(...partOffset([1, 1, 0], K, 1, T, L, L.gentlePartMul));
  assert.ok(Math.abs(g - Math.hypot(...beside) * L.gentlePartMul) < 1e-9, 'Gentle scales it');
});

test('spirals: slots take the player first, then the nearest, at most L.slots, only near the gate', () => {
  const gf = { c: [0, 0, 0], t: [0, 0, 1], b: [1, 0, 0], n: [0, 1, 0] }, g = { radiusM: 20, depthM: 0 };
  const karts = [[0, 0, -15], [0, 0, -1], [2, 0, 2], [0, 0, -5], [1, 0, 3], [0, 0, -300], [100, 0, 0], [0, 0, 10]];
  const s = pickSlots(gf, g, karts);
  assert.equal(s.length, L.slots);
  assert.equal(s[0], 0);
  assert.ok(!s.includes(5) && !s.includes(6));
  assert.ok(crossed(-0.5, 0.3, 4, 20) && !crossed(0.3, 0.6, 4, 20) && !crossed(-0.5, 0.3, 30, 20));
});

test('spirals: denser tier budgets, far gates thin, past drawM none', () => {
  assert.equal(budgetFor('high'), 18000);
  assert.equal(budgetFor('mid'), 9000);
  assert.equal(budgetFor('phone'), 4200);
  assert.equal(budgetFor('high', L.activeM + 1), Math.round(18000 * L.farShare));
  assert.equal(budgetFor('high', L.drawM + 1), 0);
  const q = gateParams(layout.gates[0], loom), P = buildParticles(q, thin, 12000, 9);
  const meanRc = (n) => { let s = 0; for (let i = 0; i < n; i++) s += P.a0[i * 4]; return s / n; };
  assert.ok(Math.abs(meanRc(3000) - meanRc(12000)) < 0.03, 'a prefix is a fair subset');
});

test('spirals: twinkle never flickers the whole gate, each particle under 3 Hz; Gentle halves it', () => {
  const q = gateParams(layout.gates[1], loom), P = buildParticles(q, thin, 4000, 11);
  const mean = (t, amp) => { let s = 0; for (let i = 0; i < P.n; i++) s += twinkle(t, P.a2[i * 4], P.a2[i * 4 + 1], amp); return s / P.n; };
  const m = [0, 0.13, 0.5, 1.7, 3.3].map((t) => mean(t, L.twinkleAmp));
  assert.ok(Math.max(...m) - Math.min(...m) < 0.03, 'gate mean flat ' + m.map((x) => x.toFixed(3)).join(' '));
  for (let i = 0; i < P.n; i++) assert.ok(P.a2[i * 4 + 1] <= 3);
  assert.equal(L.gentleTwinkleMul, 0.5);
  assert.equal(L.gentleTurnMul, 0.5);
});

test('spirals: the camera burst asks photosafe, Gentle softens its rise and halves it', () => {
  assert.equal(burstCount('high'), L.burst.count.high);
  assert.ok(burstCount('high', { granted: false }) < burstCount('high'));
  assert.equal(burstCount('high', { gentle: true }), Math.round(L.burst.count.high * 0.5));
  assert.ok(burstCount('phone') < burstCount('mid'));
  assert.ok(burstEnvelope(0.02) > 0 && burstEnvelope(L.burst.lifeS + 0.01) === 0);
  assert.ok(burstEnvelope(0.1, { gentle: true, riseMs: 300 }) < 0.5, 'Gentle rise at least riseMs');
});

test('spirals: hue rows are the Loom rotation (identity at 0), colours parse every way', () => {
  const r = hueRows(0);
  for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) assert.ok(Math.abs(r[i][j] - (i === j ? 1 : 0)) < 1e-9);
  assert.equal(toHex(0xff8000), '#ff8000');
  assert.equal(toHex([1, 0, 0]), '#ff0000');
  assert.equal(toHex({ r: 0, g: 1, b: 0 }), '#00ff00');
  assert.deepEqual(hexRgb('#ff0000'), [1, 0, 0]);
});

test('spirals: seeded gates take their threads from spectrum.js when it is there, the lane palette when not', async () => {
  const { paletteFor, loadSpectrum } = await import('../render/spiralMath.js');
  const sp = await loadSpectrum();
  assert.deepEqual(paletteFor('dawn', null), [...L.palette]);
  assert.deepEqual(paletteFor('nowhere', { SPECTRUM: {} }), [...L.palette]);
  if (sp && sp.SPECTRUM && Array.isArray(sp.SPECTRUM.dawn)) assert.deepEqual(paletteFor('dawn', sp), sp.SPECTRUM.dawn.map((c) => c.toLowerCase()));
  assert.ok(L.palette.length >= 8, 'a full spectrum, not a pink wash');
});

test('spirals: particles hug the Loom arms with a dark gap between them, and the road window stays clear', async () => {
  const { corridorFade, sampleOnLayer, makeRng: rng } = await import('../render/spiralMath.js');
  assert.ok(L.haloShare <= 0.05 && L.haloBand <= 0.05, 'almost no halo between arms');
  assert.ok(L.armFill > 0 && L.armFill <= 0.7, 'arms thinner than their band');
  assert.ok(L.sizeRimGain <= 0.4, 'small rim sprites');
  // on a log layer, every non-halo particle sits inside the centred armFill share of its band
  const layer = { arms: 5, turns: 2.5, duty: 0.5, style: 'log', direction: 1, colors: ['#00e5ff', '#ff2fb3'], bandMode: 'hard', speedMul: 1 };
  const r = rng(9);
  for (let i = 0; i < 2000; i++) {
    const p = sampleOnLayer(layer, r, L, 0);
    const t = Math.pow(p.rc, 0.5714286), a = p.th - 2 * Math.PI * layer.turns * t;
    const pos = (((a / (2 * Math.PI)) % 1) + 1) % 1 * layer.arms, f = pos - Math.floor(pos);
    assert.ok(Math.abs(f - layer.duty / 2) <= layer.duty * L.armFill / 2 + 1e-6, 'on the arm ' + f);
  }
  // the window: faint over the road at eye height, full beside it and high above it
  const half = 6;
  assert.ok(Math.abs(corridorFade(0, 3, half) - L.corridorAlpha) < 1e-9);
  assert.equal(corridorFade(half + L.corridorSideM + L.corridorSoftM + 0.1, 3, half), 1);
  assert.equal(corridorFade(0, L.corridorUpM + L.corridorSoftM + 0.1, half), 1);
});

// ---- polish 4: Loom band maths imported, glow and aura, the gate screen ----

test("spirals p4: the band maths are the Loom's own exports (imported, never mirrored)", async () => {
  const sm = await import('../render/spiralMath.js');
  assert.equal(sm.spanRad, loomField.loomSpanRad);
  assert.equal(sm.layerRot, loomField.loomLayerRotationRad);
  assert.equal(sm.tOfR, loomField.loomStyleT);
  assert.equal(sm.rOfT, loomField.loomStyleR);
  assert.equal(sm.helixOf, loomField.loomHelix);
  assert.equal(sm.loomCover, loomField.loomBandAt);
  assert.equal(sm.hueRows, loomField.loomHueRows);
  assert.equal(sm.LOOM_GOLDEN_K, loomField.LOOM_GOLDEN_K);
  const src = readFileSync(new URL('../render/spiralMath.js', import.meta.url), 'utf8');
  for (const k of ['0.5714286', '0.787', '1.9459101']) assert.ok(!src.includes(k), 'no mirrored constant ' + k);
  // the new Loom exports follow the shader: pulse zoom, wobble, hue angle
  const q = loomField.normalizeParams2({ layer: { colors: ['#ff0000'] }, pulse: { amp: 0.1, cycles: 2 }, wobble: { amp: 0.2, freq: 3, cycles: 1 }, hueCycles: 1 });
  assert.ok(Math.abs(loomField.loomPulseZoom(q, 0.125) - 1.1) < 1e-9);
  assert.ok(Math.abs(loomField.loomWobble(q, 0.5, 0) - 0.2 * Math.sin(TAU * 1.5)) < 1e-9);
  assert.equal(loomField.loomHueRad(q, 0.5), Math.PI);
  // an existing export still behaves (v1 sidecars keep their look)
  const v1 = loomField.normalizeParams2({ schema: 1, arms: 6, turns: 1.5, style: 'arch', speed: 2, colors: ['#ff0000'], bg: '#102030' });
  assert.deepEqual([v1.layer.arms, v1.layer.turns, v1.layer.style, v1.speed, v1.bg.color], [6, 1.5, 'arch', 2, '#102030']);
});

test('spirals p4: cores sit on the arm centre lines sized to the band width, a glow layer under them, few strays', async () => {
  const { bandSpacing, KIND } = await import('../render/spiralMath.js');
  const tight = { arms: 5, turns: 2.5, duty: 0.45, style: 'log', colors: ['#ffffff'] };
  assert.ok(bandSpacing(tight, 0.5) < TAU * 0.5 / 5 * 0.5, 'a wound arm is closer across than along the arc');
  assert.ok(bandSpacing({ ...tight, turns: 0.5 }, 0.5) > bandSpacing(tight, 0.5));
  for (const g of layout.gates) {
    const q = gateParams(g, loom), P = buildParticles(q, g, 6000, 3);
    let glow = 0, glitter = 0;
    for (let i = 0; i < P.n; i++) {
      const k = P.a0[i * 4 + 3], size = P.a1[i * 4 + 3];
      if (k >= KIND.glow) {
        glow++;
        assert.ok(size >= L.glowMinM - 1e-6 && size <= L.glowMaxM + 1e-6);
        assert.ok(Math.abs(P.a2[i * 4 + 2] - L.glowAlpha * ((k % 2) ? L.layer2Alpha : 1)) < 1e-6);
      } else if ((k % 4) >= 2) glitter++;
      else assert.ok(size <= L.sizeM[1] * (1 + L.sizeRimGain) + 1e-6, 'thin cores');
    }
    assert.ok(Math.abs(glow / P.n - L.glowShare) < 0.03, g.id + ' glow share ' + (glow / P.n).toFixed(3));
    assert.ok(glitter / P.n < 0.05, g.id + ' few strays');
  }
});

test('spirals p4: the aura darkens behind the arms, keeps the road window clear, wears a rim; Gentle halves it', async () => {
  const { auraAt } = await import('../render/spiralMath.js');
  const o = { halfW: 6, upM: 3.5, radiusM: 20 };
  assert.ok(Math.abs(auraAt(0, 0.6, o).dim - L.aura.dim) < 1e-9, 'inside the disc, above the road');
  assert.equal(auraAt(0, -0.05, o).dim, 0, 'the road window stays clear');
  assert.equal(auraAt(1.15, 0, o).dim, 0, 'nothing past the rim');
  assert.ok(auraAt(1, 0, o).ring > auraAt(0.5, 0.3, o).ring * 5, 'a bright rim, a faint inside');
  assert.ok(Math.abs(auraAt(0, 0.6, { ...o, gentle: true }).dim - L.aura.dim * 0.5) < 1e-9);
  assert.ok(L.aura.dim <= 0.75 && L.aura.tint <= 0.1, 'dark gaps, not a coloured wash');
});

test('spirals p4: the gate screen rises over at least 300 ms, holds about a second, falls; photosafe rate cap; Gentle', async () => {
  const { screenTimes, screenEnvelope, screenRateMul, bandHz, screenMaskCss } = await import('../render/spiralMath.js');
  const T = screenTimes();
  assert.ok(T.rise >= 0.3 && T.hold >= 0.8 && T.hold <= 1.3 && T.fall >= 0.3);
  const G = screenTimes({ gentle: true, riseMs: 900, fadeMs: 1200 });
  assert.ok(G.rise >= 0.9 && G.fall >= 1.2, 'Gentle stretches the rise and fall');
  assert.equal(screenEnvelope(-0.01), 0);
  assert.equal(screenEnvelope(T.life + 0.01), 0);
  assert.equal(screenEnvelope(T.rise + 0.5), 1);
  // no step: at 60 fps the level never moves faster than a 300 ms smoothstep ramp allows (peak slope 1.5 / rise)
  let worst = 0;
  for (let t = 0; t < T.life; t += 1 / 60) worst = Math.max(worst, Math.abs(screenEnvelope(t + 1 / 60) - screenEnvelope(t)));
  assert.ok(worst <= 1.5 / (60 * 0.3) + 1e-9, 'smooth ' + worst.toFixed(3));
  for (const g of layout.gates) {
    const q = gateParams(g, loom), hz = bandHz(q) * screenRateMul(q);
    assert.ok(hz <= L.screen.maxBandHz + 1e-9 && L.screen.maxBandHz < 3, g.id + ' arm sweep ' + hz.toFixed(2) + ' Hz');
    assert.ok(Math.abs(screenRateMul(q, { gentle: true }) - screenRateMul(q) * 0.5) < 1e-9, 'Gentle halves the speed');
  }
  const fast = loomField.normalizeParams2({ speed: 5, layer: { arms: 12, colors: ['#ff0000', '#00ff00', '#0000ff', '#ffffff', '#000000'] } });
  assert.ok(bandHz(fast) > 3 && bandHz(fast) * screenRateMul(fast) <= L.screen.maxBandHz + 1e-9, 'a fast Loom is slowed under the cap');
  assert.ok(screenMaskCss().startsWith('radial-gradient('));
  const m = L.screen.mask;
  assert.ok(m[0][1] >= 0.3 && m[0][1] <= 0.5, 'centre 0.3 to 0.5');
  assert.ok(m[m.length - 1][1] > m[0][1] + 0.3, 'strong at the edges');
});

/** A stand-in DOM: elements with style objects; the field renderer is a stub that records its frames. */
function fakeDom() {
  const kids = [];
  const el = () => ({ style: {}, width: 0, height: 0, setAttribute() {}, addEventListener() {}, getContext: () => ({}), parentNode: null });
  const body = { clientWidth: 1600, clientHeight: 900, appendChild(c) { kids.push(c); c.parentNode = body; }, removeChild(c) { kids.splice(kids.indexOf(c), 1); c.parentNode = null; } };
  return { doc: { body, hidden: false, createElement: el, defaultView: null }, body, kids };
}

test('spirals p4: createGateScreen plays the Loom on a DOM canvas, pauses, honours photosafe and Gentle, disposes', async () => {
  const { createGateScreen } = await import('../render/gateScreen.js');
  const q = gateParams(layout.gates[0], loom);
  const run = ({ granted = true, gentle = false, pausedAt = -1 } = {}) => {
    const { doc, body, kids } = fakeDom(), frames = [];
    const sc = createGateScreen(body, {
      doc, tier: 'mid', gentle, photosafe: { request: (k) => { assert.equal(k, 'gate-screen'); return granted; } },
      makeField: (c) => ({ canvas: c, render: (p, ph) => frames.push([p, ph]) }),
    });
    assert.equal(sc.active, false);
    assert.equal(sc.play(q, 1), true);
    assert.equal(kids.length, 1);
    const c = kids[0];
    assert.equal(c.style.pointerEvents, 'none');
    assert.equal(c.style.mixBlendMode, 'screen');
    assert.equal(c.style.position, 'fixed');
    assert.ok(c.width === 512 && c.height === 288, 'backing store long side by tier ' + c.width + 'x' + c.height);
    let peak = 0, t = 0;
    for (let i = 0; i < 600 && sc.active; i++) {
      if (i === pausedAt) {
        const before = frames.length, lv = sc.level;
        sc.update(1 / 60, true);
        assert.equal(frames.length, before, 'paused: no frame');
        assert.equal(sc.level, lv, 'paused: nothing moves');
        continue;
      }
      sc.update(1 / 60, false);
      t += 1 / 60; peak = Math.max(peak, sc.level);
    }
    assert.equal(sc.active, false);
    assert.equal(c.style.display, 'none');
    assert.ok(frames.length > 60 && frames.every(([p]) => p.layer.arms === q.layer.arms), "the gate's own Loom");
    assert.ok(frames.some(([, ph]) => ph > 0), 'the field turns');
    sc.dispose();
    assert.equal(kids.length, 0);
    return { peak, t };
  };
  const base = run({ pausedAt: 20 });
  assert.ok(Math.abs(base.peak - 1) < 1e-9 && base.t > 2 && base.t < 2.6, 'peak ' + base.peak + ' life ' + base.t.toFixed(2));
  assert.ok(Math.abs(run({ granted: false }).peak - L.screen.refusedMul) < 1e-9, 'refused: softer');
  assert.ok(Math.abs(run({ gentle: true }).peak - L.screen.gentleAlphaMul) < 1e-9, 'Gentle: half');
  assert.equal(createGateScreen(null, { doc: null }).play(q), false, 'no DOM, no screen');
});
