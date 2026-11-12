// The spiral: patterns, the morph, the colours and how the bus hears the palette. Pure logic; dev/spiral-check.mjs looks at it in a browser.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loomBandAt } from '../../arcademy/engine/loom/loomField.js';
import {
  PATTERNS, createMorphClock, driftPair, groundOf, hueShift, isHex, mixHex, morphFrame, DRIFT_PERIOD_S, GENTLE_MORPH_S,
} from '../fx/spiral-patterns.js';
import { createBus, FADE_S } from '../fx/spiral-bus.js';

const PAIR = ['#ff4fa8', '#6a3cff'];
const G = groundOf(PAIR[0]);
const sign = (p) => JSON.stringify([p.l1, p.l2 || 0, p.glow || 0, p.wob || 0, p.pulse || 0]);

test('at least eight distinct patterns, every Loom style in use, the first is the plain spiral', () => {
  assert.ok(PATTERNS.length >= 8);
  assert.equal(new Set(PATTERNS.map(sign)).size, PATTERNS.length);
  assert.equal(new Set(PATTERNS.map((p) => p.id)).size, PATTERNS.length);
  const styles = new Set(PATTERNS.flatMap((p) => [p.l1.style, p.l2?.style]).filter(Boolean));
  for (const s of ['log', 'arch', 'ribbon', 'golden', 'tunnel', 'petal']) assert.ok(styles.has(s), s);
  assert.deepEqual([PATTERNS[0].l1.style, PATTERNS[0].l1.arms, PATTERNS[0].l1.turns, PATTERNS[0].l1.duty], ['log', 4, 2, 0.5]);
});

test('a frame is a whole set of field params with whole arms, and colours the shader can read', () => {
  for (let f = 0; f < PATTERNS.length; f++) {
    for (const t of [0, 0.2, 0.5, 0.8, 0.999]) {
      const fr = morphFrame(f, (f + 3) % PATTERNS.length, t, PAIR, G);
      for (const q of [fr.a, fr.b].filter(Boolean)) {
        for (const l of [q.layer, q.layer2]) {
          assert.ok(Number.isInteger(l.arms), 'a fractional arm count shows a seam');
          assert.ok(l.duty >= 0 && l.duty <= 0.8);
          assert.ok(l.colors.length && l.colors.every(isHex));
        }
        assert.ok(isHex(q.bg.color));
      }
    }
  }
});

test('the morph starts exactly on the old shape, ends exactly on the new, and has no jump in between', () => {
  const widths = (fr) => ({ a: fr.a.layer.duty, turns: fr.a.layer.turns, b: fr.b?.layer.duty ?? null, wob: fr.a.wobble.amp });
  for (let f = 0; f < PATTERNS.length; f++) {
    const to = (f + 1) % PATTERNS.length;
    assert.deepEqual(morphFrame(f, to, 0, PAIR, G), morphFrame(f, null, 0, PAIR, G));
    const last = morphFrame(f, to, 0.99999, PAIR, G), rest = morphFrame(to, null, 0, PAIR, G);
    assert.equal(last.b, null);
    assert.ok(Math.abs(last.a.layer.duty - rest.a.layer.duty) < 1e-3);
    assert.ok(Math.abs(last.a.layer.turns - rest.a.layer.turns) < 1e-3);
    let prev = widths(morphFrame(f, to, 0.001, PAIR, G));
    for (let t = 0.002; t < 0.37; t += 0.001) {
      const w = widths(morphFrame(f, to, t, PAIR, G));
      assert.ok(Math.abs(w.a - prev.a) < 0.01, `duty jumps at ${t}`);
      assert.ok(Math.abs(w.turns - prev.turns) < 0.02, `turns jump at ${t}`);
      assert.ok(Math.abs(w.wob - prev.wob) < 0.01, `wobble jumps at ${t}`);
      prev = w;
    }
  }
});

test('in the middle of a morph both shapes are there, as thin threads, on the same ground', () => {
  const fr = morphFrame(0, 4, 0.5, PAIR, G);
  assert.ok(fr.a && fr.b);
  assert.ok(fr.a.layer.duty < PATTERNS[0].l1.duty && fr.b.layer.duty < PATTERNS[4].l1.duty);
  assert.equal(fr.a.bg.color, fr.b.bg.color);
});

test('Gentle: no point on the field changes more than 3 times a second through any morph, and nothing warps', () => {
  const STEP = 1 / 30, RADII = [0.08, 0.2, 0.35, 0.5, 0.7, 0.95], ANGLES = [0.3, 1.1, 2.4, 3.9, 5.2];
  for (let f = 0; f < PATTERNS.length; f++) {
    for (const to of [(f + 1) % PATTERNS.length, (f + 5) % PATTERNS.length]) {
      for (const r of RADII) for (const th of ANGLES) {
        const flips = [];
        let was = null;
        for (let s = 0; s < GENTLE_MORPH_S; s += STEP) {
          const fr = morphFrame(f, to, s / GENTLE_MORPH_S, PAIR, G, { gentle: true });
          assert.equal(fr.a.wobble.amp, 0);
          assert.equal(fr.a.pulse.amp, 0);
          const seen = [fr.a, fr.b].filter(Boolean).flatMap((q) => [q.layer, q.layer2.enabled ? q.layer2 : null].filter(Boolean).map((l) => {
            const bd = loomBandAt(l, r, th, 0, 0);
            return bd.cov ? `${l.style}${bd.idx}` : '-';
          })).join('|');
          if (seen !== was) { flips.push(s); was = seen; }
        }
        /* A full on-off-on takes two changes; 3 Hz is 6 changes in any second. */
        for (let i = 0; i + 6 < flips.length; i++) {
          assert.ok(flips[i + 6] - flips[i] >= 1, `${PATTERNS[f].id} to ${PATTERNS[to].id} at r${r} th${th}: ${flips[i + 6] - flips[i]} s`);
        }
      }
    }
  }
});

test('the clock deals every pattern before repeating one, never the same twice running', () => {
  let n = 12345;
  const rng = () => { n = (n * 1664525 + 1013904223) % 4294967296; return n / 4294967296; };
  const clock = createMorphClock({ rng, holdS: 1, morphS: 1 });
  clock.setOn(true);
  const seen = [];
  for (let i = 0; i < 400; i++) {
    const had = clock.from;
    clock.step(0.25);
    if (clock.from !== had) { assert.notEqual(clock.from, had); seen.push(clock.from); }
  }
  assert.ok(seen.length >= PATTERNS.length);
  assert.equal(new Set(seen.slice(0, PATTERNS.length - 1)).size, PATTERNS.length - 1, seen.join(','));
});

test('the clock: reduced motion holds, Gentle is slow, switched off it melts home and goes idle', () => {
  const c = createMorphClock({ rng: () => 0.4 });
  c.setOn(true);
  c.step(1, { reduced: true });
  assert.equal(c.morphing, false);
  c.step(0.1);
  assert.equal(c.morphing, true);
  const before = c.t;
  c.step(1, { reduced: true });
  assert.equal(c.t, before);
  c.step(1, { gentle: true });
  assert.ok(c.t - before < 0.07);
  for (let i = 0; i < 100; i++) c.step(0.25);
  c.setOn(false);
  for (let i = 0; i < 100; i++) c.step(0.25);
  assert.equal(c.from, 0);
  assert.equal(c.active, false);
});

test('colours: drift starts on the palette, walks without a jump, comes round, and stays near the palette', () => {
  assert.deepEqual(driftPair(PAIR, 0), PAIR);
  const big = (x, y) => { const v = (h) => [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16)); const a = v(x), b = v(y); return Math.max(...a.map((p, i) => Math.abs(p - b[i]))); };
  let prev = driftPair(PAIR, 0);
  for (let s = 0.1; s <= DRIFT_PERIOD_S; s += 0.1) {
    const next = driftPair(PAIR, s);
    assert.ok(big(prev[0], next[0]) <= 8 && big(prev[1], next[1]) <= 8, `jump at ${s}`);
    prev = next;
  }
  assert.ok(big(driftPair(PAIR, DRIFT_PERIOD_S)[0], PAIR[0]) <= 1);
  assert.notEqual(driftPair(['#7fd0ff', '#7fd0ff'], 6)[0], '#7fd0ff', 'one colour still drifts');
  assert.equal(mixHex('#000000', '#ffffff', 0.5), '#808080');
  assert.ok(isHex(hueShift('#ff4fa8', 30)) && hueShift('#808080', 30) === '#808080');
});

function fakeCtx(state) {
  const on = {};
  return {
    palette: () => ({ spiral: state.spiral }),
    on: (ev, fn) => { (on[ev] ||= []).push(fn); },
    emit: (ev, d) => { for (const fn of on[ev] || []) fn(d); },
  };
}

test('the palette event: the first read lands at once, a change fades slowly and never snaps', () => {
  const bus = createBus();
  const st = { spiral: ['#ff0000', '#00ff00'] };
  const ctx = fakeCtx(st);
  bus.use(ctx);
  assert.deepEqual(bus.colours(), st.spiral);
  assert.equal(bus.fading, false);
  st.spiral = ['#0000ff', '#ffff00'];
  ctx.emit('palette', { themes: ['trance'] });
  assert.equal(bus.fading, true);
  assert.deepEqual(bus.colours(), ['#ff0000', '#00ff00'], 'no snap on the event itself');
  bus.advance(FADE_S / 2, false, false);
  const mid = bus.colours();
  assert.notDeepEqual(mid, ['#ff0000', '#00ff00']);
  assert.notDeepEqual(mid, st.spiral);
  bus.advance(FADE_S, false, false);
  assert.deepEqual(bus.colours(), st.spiral);
  assert.equal(bus.fading, false);
  ctx.emit('palette', { name: 'pink' });   // the page colour's own event: the spiral's colours did not change
  assert.equal(bus.fading, false);
  bus.setDrift(true);
  bus.advance(5, false, false);
  assert.notDeepEqual(bus.colours(), st.spiral);
  bus.setDrift(false);
  bus.advance(3, false, false);
  assert.deepEqual(bus.colours(), st.spiral, 'switched off it settles back on the palette');
  ctx.emit('palette', {});
  st.spiral = ['nonsense'];
  ctx.emit('palette', {});
  assert.equal(bus.fading, false, 'a colour it cannot read is ignored');
});
