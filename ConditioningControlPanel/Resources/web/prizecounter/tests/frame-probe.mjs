// The profile frame probes both sides check against tests/fixtures/frame-probes.json: the page here
// (tests/sheet-frames.test.mjs) and the desktop in Tests/ConditioningControlPanel.Tests/ProfileFrameTests.cs, which
// computes the same summary from CCP.Core ProfileFrames.Build. Rewrite the fixture only on purpose:
//   node tests/frame-probe.mjs --write
import { writeFileSync } from 'node:fs';
import { buildFrame, frameMotion, FRAME_IDS } from '../sheet/frames.js';

export const PROBES = [
  { t: 0, level: 'full', gentle: false, d: 1 },
  { t: 2.37, level: 'full', gentle: false, d: 1 },
  { t: 7.9, level: 'full', gentle: false, d: 2 },
  { t: 1.1, level: 'full', gentle: false, d: 0 },
  { t: 3.3, level: 'less', gentle: false, d: 1 },
  { t: 5, level: 'off', gentle: false, d: 1 },
  { t: 4.4, level: 'full', gentle: true, d: 1 },
];

/** FNV-1a over the UTF-16 code units of s, as 8 hex digits. */
export function fnv(s) {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193) >>> 0; }
  return h.toString(16).padStart(8, '0');
}

/** One scene's fingerprint: the step count, a hash of every step's kind, colours and flags, and two weighted sums of
 * its numbers (positions; sizes, widths and alphas). */
export function summary(prims) {
  let sig = '', a = 0, b = 0;
  prims.forEach((p, i) => {
    const w = i + 1;
    if (p.kind === 'e') {
      sig += `e|${p.fill || ''}|${p.stroke || ''}|${p.live ? 1 : 0};`;
      a += w * (p.x + 1.7 * p.y);
      b += w * (p.rx + p.ry + p.rot / 360 + p.w + p.alpha);
    } else {
      sig += `p|${p.fill || ''}|${p.fill2 || ''}|${p.stroke || ''}|${p.closed ? 1 : 0}|${p.smooth ? 1 : 0}|${p.live ? 1 : 0}|${p.pts.length / 2};`;
      let sx = 0;
      for (let k = 0; k < p.pts.length; k += 2) sx += p.pts[k] + 1.7 * p.pts[k + 1];
      a += w * sx;
      b += w * (p.w + p.alpha + p.hole + p.g0 + p.g1);
    }
  });
  return { n: prims.length, sig: fnv(sig), a: Number(a.toFixed(6)), b: Number(b.toFixed(6)) };
}

export function probeAll() {
  const out = [];
  for (const id of FRAME_IDS) for (const p of PROBES) out.push({ id, ...p, ...summary(buildFrame(id, p.t, frameMotion(p.level, p.gentle), p.d)) });
  return out;
}

if (process.argv.includes('--write')) {
  writeFileSync(new URL('./fixtures/frame-probes.json', import.meta.url), JSON.stringify(probeAll(), null, 1) + '\n');
  console.log('wrote fixtures/frame-probes.json');
}
