import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { recordingRange, sendRecording, verifiedRecording } from '../dev/privateRecording.mjs';
import { GOON_CHART, GOON_AUDIO } from '../dev/goonChart.mjs';

test('single byte ranges support exact, open-ended and suffix reads, rejecting bad ranges', () => {
  assert.equal(recordingRange(undefined, 10), null);
  for (const [range, expected] of [['bytes=2-4', [2, 4]], ['bytes=7-', [7, 9]], ['bytes=-3', [7, 9]],
    ['bytes=2-999', [2, 9]], ['bytes=-999', [0, 9]]]) {
    const r = recordingRange(range, 10); assert.deepEqual([r.start, r.end], expected);
  }
  for (const range of ['bytes=-0', 'bytes=10-', 'bytes=4-2', 'bytes=-', 'bytes=0-1,4-6',
    'bytes=9007199254740992-', 'items=0-1']) assert.equal(recordingRange(range, 10), false, range);
});
test('recording responses preserve bytes and Content-Range, including HEAD and 416', () => {
  const bytes = Buffer.from('0123456789');
  const response = () => ({ writeHead(status, headers) { Object.assign(this, { status, headers }); }, end(body) { this.body = body; } });
  const partial = response(); sendRecording({ method: 'GET', headers: { range: 'bytes=2-4' } }, partial, bytes, {});
  assert.equal(partial.status, 206); assert.equal(partial.body.toString(), '234');
  assert.equal(partial.headers['Content-Range'], 'bytes 2-4/10'); assert.equal(partial.headers['Content-Length'], 3);
  const head = response(); sendRecording({ method: 'HEAD', headers: {} }, head, bytes, {});
  assert.equal(head.status, 200); assert.equal(head.body, undefined); assert.equal(head.headers['Content-Length'], 10);
  const bad = response(); sendRecording({ method: 'GET', headers: { range: 'bytes=99-' } }, bad, bytes, {});
  assert.equal(bad.status, 416); assert.equal(bad.headers['Content-Range'], 'bytes */10');
});
test('only a byte-identical explicitly supplied original can stand in for the owner file', async () => {
  const dir = await mkdtemp(join(tmpdir(), 'glassroad-recording-'));
  try {
    const correct = Buffer.from('fixture recording'), wrong = join(dir, 'wrong.mp3'), good = join(dir, 'good.mp3');
    await writeFile(wrong, 'wrong version'); await writeFile(good, correct);
    const hash = createHash('sha256').update(correct).digest('hex');
    const result = await verifiedRecording([join(dir, 'missing.mp3'), wrong, good], hash);
    assert.equal(result.file, good); assert.deepEqual(result.bytes, correct);
    await assert.rejects(verifiedRecording([wrong], hash), /unavailable/);
  } finally { await rm(dir, { recursive: true, force: true }); }
});
test('missing private song and timings return 503 without exposing any private path', async () => {
  const dir = await mkdtemp(join(tmpdir(), 'glassroad-no-recording-'));
  const child = spawn(process.execPath, [fileURLToPath(new URL('../dev/serve.mjs', import.meta.url)), '0'],
    { env: { ...process.env, GLASSROAD_AUDIO_DIR: dir, GLASSROAD_GOON_RECORDING: join(dir, 'missing.mp3'), GLASSROAD_PREVIEW_PROVIDER: '' }, windowsHide: true });
  try {
    const url = await new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('Dev server did not start')), 10000);
      child.on('error', e => { clearTimeout(timer); reject(e); });
      child.stdout.on('data', data => { const match = data.toString().match(/http:\/\/127\.0\.0\.1:\d+/); if (match) { clearTimeout(timer); resolve(match[0]); } });
      child.once('exit', code => { clearTimeout(timer); reject(new Error(`Dev server exited ${code}`)); });
    });
    for (const path of [GOON_CHART, GOON_AUDIO]) {
      const res = await fetch(url + path); assert.equal(res.status, 503); assert.equal((await res.text()).includes(dir), false);
      assert.equal((await fetch(url + path, { method: 'POST' })).status, 405);
    }
    assert.equal((await fetch(url + GOON_AUDIO + '/secret')).status, 404);
  } finally {
    child.kill(); await new Promise(resolve => child.exitCode !== null ? resolve() : child.once('exit', resolve));
    await rm(dir, { recursive: true, force: true });
  }
});
