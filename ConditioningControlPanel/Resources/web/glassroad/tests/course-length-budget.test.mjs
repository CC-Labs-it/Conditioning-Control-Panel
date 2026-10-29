import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadTrack } from '../track/load.js';
import { createFeel } from '../core/feel.js';
import { trackTableWidth } from '../render/spectrum.js';

// Course length budget (2026-10-28). #3052 made every course 25-70% longer and the GOONWORKS road table (one texel per
// metre) silently passed WebGPU's default maxTextureDimension2D (8192): no load on every WebGPU browser. This file
// fails the suite the moment a reshape outgrows a per-metre table, and prints lap-time estimates so a reshape is
// never merged blind again.
const TEXTURE_MAX = 8192;           // WebGPU default maxTextureDimension2D; trackTableWidth caps here
const METRES_PER_TEXEL_MAX = 2;     // coarser than this smears the district bands (GOONWORKS sits at 1.18)
const COURSE_CEILING_M = 16000;     // a reshape past this is an owner call
const SPEED_MEAN_FACTOR = 1.07;     // measured solo clean-pace mean on the bench, relative to speed.topMps (estimate)

const root = fileURLToPath(new URL('..', import.meta.url));
const feel = createFeel(JSON.parse(readFileSync(join(root, 'feel.json'), 'utf8')));

/** Every real course in tracks/ (side files such as threshold.media.json carry no id/laps and are skipped). */
function loadCourses() {
  const out = [];
  for (const name of readdirSync(join(root, 'tracks')).sort()) {
    if (!name.endsWith('.json')) continue;
    const json = JSON.parse(readFileSync(join(root, 'tracks', name), 'utf8'));
    if (!json.id || !json.laps || !Array.isArray(json.nodes)) continue;
    out.push({ file: name, track: loadTrack(json) });
  }
  return out;
}

function listJs(dir, out = []) {
  for (const name of readdirSync(dir)) {
    const p = join(dir, name);
    if (statSync(p).isDirectory()) listJs(p, out); else if (name.endsWith('.js')) out.push(p);
  }
  return out;
}

const courses = loadCourses();

test('course table: length, laps and lap-time estimates (printed for every reshape review)', () => {
  assert.ok(courses.length >= 5, 'five live courses expected, found ' + courses.length);
  const mps = feel.get('speed.topMps') * SPEED_MEAN_FACTOR;
  const rows = courses.map(({ track }) => {
    const lapS = track.lengthM / mps;
    return { course: track.id, lengthM: Math.round(track.lengthM), laps: track.laps, lapS: Math.round(lapS), raceS: Math.round(lapS * track.laps), texels: trackTableWidth(track.lengthM), mPerTexel: +(track.lengthM / trackTableWidth(track.lengthM)).toFixed(2) };
  });
  console.log('course length budget (lap time estimated at speed.topMps x ' + SPEED_MEAN_FACTOR + ' = ' + mps.toFixed(1) + ' m/s, the bench solo mean):');
  console.table(rows);
  for (const r of rows) assert.ok(r.lapS > 0 && r.raceS >= r.lapS, r.course);
});

test('every per-metre table builder caps its width through trackTableWidth or an explicit Math.min(8192', () => {
  const alloc = /new (DataTexture|CanvasTexture|Uint8Array)\(/;
  const texture = /DataTexture|CanvasTexture/;
  const capped = /trackTableWidth|Math\.min\(\s*8192/;
  const WINDOW = 10;
  /** The width expression of an allocation: the first argument of a typed array, the second of a DataTexture. */
  function widthExpr(line) {
    const m = alloc.exec(line);
    if (!m) return '';
    let depth = 0;
    const args = [''];
    for (let i = m.index + m[0].length; i < line.length; i++) {
      const c = line[i];
      if (c === '(' || c === '[' || c === '{') depth++;
      else if (c === ')' || c === ']' || c === '}') { if (depth === 0) break; depth--; }
      else if (c === ',' && depth === 0) { args.push(''); continue; }
      args[args.length - 1] += c;
    }
    return m[1] === 'DataTexture' ? (args[1] || '') : args[0];
  }
  /** The right-hand side of an identifier's nearest definition inside the window, or null. */
  function definition(ident, window) {
    const re = new RegExp('(?:^|[\\s,;({])' + ident.replace(/\$/g, '\\$') + '\\s*=(?!=)\\s*([^;]*)');
    for (let k = window.length - 1; k >= 0; k--) { const m = re.exec(window[k]); if (m) return m[1]; }
    return null;
  }
  const offenders = [], sites = [];
  for (const file of listJs(join(root, 'render'))) {
    const lines = readFileSync(file, 'utf8').split(/\r?\n/);
    const isTextureFile = texture.test(lines.join('\n'));
    for (let i = 0; i < lines.length; i++) {
      if (!alloc.test(lines[i])) continue;
      const window = lines.slice(Math.max(0, i - WINDOW), i + 1);
      if (!/lengthM/.test(window.join('\n'))) continue;
      const expr = widthExpr(lines[i]);
      // the width derives from lengthM when the expression reads it, or an identifier in it is defined from it
      let derived = /lengthM/.test(expr), isCapped = capped.test(expr);
      for (const ident of expr.match(/[A-Za-z_$][\w$]*/g) || []) {
        if (ident === 'Math' || ident === 'lengthM') continue;
        const def = definition(ident, window);
        if (def && /lengthM/.test(def)) { derived = true; if (capped.test(def)) isCapped = true; }
      }
      if (!derived) continue;
      // A CPU lookup array in a file that never builds a texture (waterMath.js waterTable) is not a GPU table.
      if (!isTextureFile) continue;
      const rel = file.slice(root.length).replace(/\\/g, '/') + ':' + (i + 1);
      sites.push(rel);
      if (!isCapped) offenders.push(rel);
    }
  }
  assert.ok(sites.some((s) => s.startsWith('render/materials/road.js')), 'road.js table site found');
  assert.ok(sites.some((s) => s.startsWith('render/goonworksMaterials.js')), 'goonworksMaterials.js table site found');
  assert.deepEqual(offenders, [], 'per-metre tables sized from lengthM without a width cap (route the width through trackTableWidth in render/spectrum.js): ' + offenders.join(', '));
});

test('every course fits the 8192 texel table and stays under 2 m per texel', () => {
  for (const { track } of courses) {
    const n = trackTableWidth(track.lengthM);
    assert.ok(n <= TEXTURE_MAX, track.id + ' table width ' + n + ' over ' + TEXTURE_MAX);
    const mPerTexel = track.lengthM / n;
    assert.ok(mPerTexel < METRES_PER_TEXEL_MAX, track.id + ' is ' + mPerTexel.toFixed(2) + ' m per texel (' + Math.round(track.lengthM) + ' m over ' + n + ' texels): the district bands smear past ' + METRES_PER_TEXEL_MAX);
  }
});

test('no course is longer than the 16000 m ceiling', () => {
  for (const { track } of courses) {
    assert.ok(track.lengthM <= COURSE_CEILING_M, track.id + ' is ' + Math.round(track.lengthM) + ' m, over the ' + COURSE_CEILING_M + ' m ceiling: a reshape past it is an owner call, not a merge');
  }
});
