// Exercise the verbatim kit without opening an engine or downloading anything.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const kit = fileURLToPath(new URL('./rt-spike/', import.meta.url));
for (const [name, change, verdict] of [
  ['full look', {}, 'MEETS BAR'],
  ['mean below bar', { fpsMean: 56 }, 'BELOW BAR: mean 56'],
  ['worst second below bar', { worst1sFps: 49 }, 'BELOW BAR: worst 1 s 49'],
  ['shader stall', { hitches100: 1 }, 'BELOW BAR: 1 frames > 100 ms'],
  ['quality reduced', { tier: 'low' }, 'BELOW BAR: tier low'],
]) {
  test(`spike report: ${name}`, async () => {
    const out = await mkdtemp(join(tmpdir(), 'ccp-spike-test-'));
    try {
      await mkdir(join(out, 'runs'));
      await writeFile(join(out, 'runs', 'sample.json'), JSON.stringify({
        run: 'sample', hashes: { match: true },
        race: { fpsMean: 57, worst1sFps: 50, hitches100: 0, tier: 'high', ...change },
      }));
      const result = spawnSync(process.execPath, [join(kit, 'make-report.mjs'), out], { encoding: 'utf8' });
      assert.equal(result.status, 0, result.stderr);
      assert.ok((await readFile(join(out, 'report.md'), 'utf8')).includes(verdict));
    } finally {
      await rm(out, { recursive: true, force: true });
    }
  });
}
