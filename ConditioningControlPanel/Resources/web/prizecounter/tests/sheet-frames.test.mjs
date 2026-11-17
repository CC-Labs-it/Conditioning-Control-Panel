// The profile frames on the page are the desktop's frames: sheet/frames.js is a port of CCP.Core/Frames/ProfileFrames.cs,
// and both sides are pinned to the same probe fingerprints (tests/fixtures/frame-probes.json; the C# side is
// ProfileFrameTests.Page_and_desktop_draw_the_same_frames).
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { buildFrame, frameMotion, detailFor, FRAME_IDS, WINDOW, KEEP, K, drawFrame } from '../sheet/frames.js';
import { probeAll } from './frame-probe.mjs';

const CORE = readFileSync(new URL('../../../../../CCP.Core/Frames/ProfileFrames.cs', import.meta.url), 'utf8');
const FIXTURE = JSON.parse(readFileSync(new URL('./fixtures/frame-probes.json', import.meta.url), 'utf8'));
const close = (a, b) => Math.abs(a - b) <= 2e-6 * Math.max(1, Math.abs(b));

test('the constants are the desktop constants', () => {
  assert.match(CORE, new RegExp(`C = 0.5, Window = ${WINDOW};`));
  assert.match(CORE, new RegExp(`Keep = ${KEEP};`));
  assert.match(CORE, new RegExp(`K = ${K};`));
  for (const id of FRAME_IDS) assert.ok(CORE.includes(`"${id}"`), id);
});

test('every probe matches the fingerprints the desktop is pinned to', () => {
  const now = probeAll();
  assert.equal(now.length, FIXTURE.length);
  now.forEach((p, i) => {
    const f = FIXTURE[i], at = `${p.id} t=${p.t} ${p.level}${p.gentle ? ' gentle' : ''} d=${p.d}`;
    assert.equal(p.id, f.id); assert.equal(p.n, f.n, at); assert.equal(p.sig, f.sig, at);
    assert.ok(close(p.a, f.a), `${at}: a ${p.a} vs ${f.a}`); assert.ok(close(p.b, f.b), `${at}: b ${p.b} vs ${f.b}`);
  });
});

/** Nearest any ink of a step comes to the centre, and whether it stays on the canvas. */
function reach(p) {
  const pts = [];
  if (p.kind === 'e') for (let i = 0; i < 48; i++) {
    const u = i / 48 * Math.PI * 2, r = p.rot * Math.PI / 180, x = p.rx * Math.cos(u), y = p.ry * Math.sin(u);
    pts.push([p.x + x * Math.cos(r) - y * Math.sin(r), p.y + x * Math.sin(r) + y * Math.cos(r)]);
  } else for (let k = 0; k < p.pts.length; k += 2) pts.push([p.pts[k], p.pts[k + 1]]);
  const half = p.stroke ? p.w / 2 : 0;
  let near = Infinity, lo = Infinity, hi = -Infinity;
  for (const [x, y] of pts) { near = Math.min(near, Math.hypot(x - 0.5, y - 0.5)); lo = Math.min(lo, x, y); hi = Math.max(hi, x, y); }
  return { near: (p.hole > 0 ? p.hole : near) - half, lo: lo - half, hi: hi + half };
}

test('the avatar stays clear and every frame stays on its canvas, at every size, level and moment', () => {
  for (const id of FRAME_IDS) for (const d of [0, 1, 2]) for (const level of ['full', 'less', 'off']) for (let t = 0; t < 12; t += 0.37) {
    for (const p of buildFrame(id, t, frameMotion(level, false), d)) {
      if (p.alpha <= 0.002 || (p.kind === 'e' && (p.rx <= 0 || p.ry <= 0))) continue;
      const r = reach(p);
      assert.ok(r.near >= KEEP - 1e-9, `${id} d=${d} ${level} t=${t.toFixed(2)}: a ${p.kind} reaches ${r.near.toFixed(4)} from the centre`);
      assert.ok(r.lo >= -0.003 && r.hi <= 1.003, `${id} d=${d} t=${t.toFixed(2)}: off the canvas (${r.lo.toFixed(3)}..${r.hi.toFixed(3)})`);
    }
  }
});

test('the steps keep their count and live flags at every moment (a head may cache the still ones)', () => {
  for (const id of FRAME_IDS) for (const d of [0, 1, 2]) {
    const a = buildFrame(id, 0, frameMotion('full'), d), b = buildFrame(id, 6.1, frameMotion('full'), d);
    assert.equal(a.length, b.length, id);
    a.forEach((p, i) => { assert.equal(p.live, b[i].live, `${id} step ${i}`); if (!p.live) assert.deepEqual(p, b[i], `${id} still step ${i} moved`); });
    assert.ok(a.some((p) => p.live), `${id} has nothing that moves`);
  }
});

test('motion off is one still pose; full moves; Gentle softens the marquee', () => {
  for (const id of FRAME_IDS) {
    assert.deepEqual(buildFrame(id, 0, frameMotion('off'), 1), buildFrame(id, 9.3, frameMotion('off'), 1), id);
    assert.notDeepEqual(buildFrame(id, 0.4, frameMotion('full'), 1), buildFrame(id, 1.3, frameMotion('full'), 1), id);
  }
  const spread = (gentle) => { let lo = 1, hi = 0; for (let t = 0; t < 5; t += 0.1) for (const p of buildFrame('prime.frame.room-host', t, frameMotion('full', gentle), 1)) if (p.fill === '#FFF6C2') { lo = Math.min(lo, p.alpha); hi = Math.max(hi, p.alpha); } return hi - lo; };
  assert.ok(spread(true) < spread(false) * 0.6);
  assert.deepEqual(frameMotion('less', false), { speed: 0.5, amp: 0.5, gentle: false });
  assert.deepEqual(frameMotion('full', true), { speed: 0.5, amp: 1, gentle: true });
});

test('small rows get fewer steps than the profile card, the stage gets the most', () => {
  assert.deepEqual([28, 104, 220].map(detailFor), [0, 1, 2]);
  for (const id of FRAME_IDS) {
    const n = [0, 1, 2].map((d) => buildFrame(id, 0, frameMotion('full'), d).length);
    assert.ok(n[0] < n[1] && n[1] <= n[2], `${id}: ${n}`);
  }
});

test('no two frames share a palette or a step plan', () => {
  const sig = (id) => buildFrame(id, 0, frameMotion('off'), 1).map((p) => p.fill).filter(Boolean);
  for (let i = 0; i < FRAME_IDS.length; i++) for (let j = i + 1; j < FRAME_IDS.length; j++) {
    const a = new Set(sig(FRAME_IDS[i])), b = new Set(sig(FRAME_IDS[j]));
    const shared = [...a].filter((x) => b.has(x) && x !== '#FFFFFF');
    assert.ok(shared.length <= 2, `${FRAME_IDS[i]} and ${FRAME_IDS[j]} share ${shared}`);
  }
});

test('drawFrame paints with a 2D context and keeps hairlines visible', () => {
  const calls = []; const g = new Proxy({}, { get: (o, k) => (k in o ? o[k] : (...a) => { calls.push([k, ...a]); return { addColorStop() {} }; }), set: (o, k, v) => { o[k] = v; if (k === 'lineWidth') calls.push(['lw', v]); return true; } });
  drawFrame(g, buildFrame('profile.frame.gilt', 1, frameMotion('full'), 0), 0, 0, 30);
  assert.ok(calls.some((c) => c[0] === 'fill') && calls.some((c) => c[0] === 'stroke'));
  assert.ok(calls.filter((c) => c[0] === 'lw').every((c) => c[1] >= 0.75));
});
