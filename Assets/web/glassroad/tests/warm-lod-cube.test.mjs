import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

// The water probe's cube warm holds every LOD level shown like warmAll: projection had hidden the far
// finish-portal levels, so their cube programs linked mid-race on Threshold (Firefox WebGL2, 2026-10-27).
test('the cube warm freezes and restores LOD.autoUpdate', () => {
  const src = readFileSync(new URL('../render/waterReflections.js', import.meta.url), 'utf8');
  assert.match(src, /if\(object\.isLOD\)\{lods\.push\(\[object,object\.autoUpdate\]\);object\.autoUpdate=false;\}/);
  assert.match(src, /for\(const \[object,autoUpdate\] of lods\)object\.autoUpdate=autoUpdate;/);
  const warm = readFileSync(new URL('../render/renderer.js', import.meta.url), 'utf8');
  assert.match(warm, /if \(o\.isLOD\) \{ lods\.push\(\[o, o\.autoUpdate\]\); o\.autoUpdate = false; \}/, 'warmAll keeps the same rule');
  assert.match(warm, /qs\.get\('logLinks'\) === '1'/, 'the link log is dev only');
});
