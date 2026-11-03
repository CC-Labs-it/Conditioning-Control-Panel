import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const root = new URL('../assets/audio/ambient/', import.meta.url);
const manifest = JSON.parse(readFileSync(new URL('manifest.json', root), 'utf8'));
for (const row of manifest.files) test(`${row.file} is a playable, bounded loop`, t => {
  const wav = readFileSync(new URL(row.file, root));
  if (wav.subarray(0, 42).toString().startsWith('version https://git-lfs.github.com/spec/v1')) {
    t.skip('Media checkout contains an LFS pointer'); return;
  }
  assert.equal(wav.toString('ascii', 0, 4), 'RIFF');
  assert.equal(wav.toString('ascii', 8, 12), 'WAVE');
  assert.equal(wav.readUInt16LE(20), 1);
  assert.equal(wav.readUInt16LE(22), row.channels);
  assert.equal(wav.readUInt32LE(24), manifest.sampleRate);
  assert.equal(wav.readUInt16LE(34), 16);
  const samples = (wav.length - 44) / 2;
  assert.equal(samples / row.channels / manifest.sampleRate, row.seconds);
  let peak = 0, squares = 0, crossings = 0, last = 0;
  for (let i = 0; i < samples; i++) {
    const value = wav.readInt16LE(44 + i * 2) / 32767;
    peak = Math.max(peak, Math.abs(value)); squares += value * value;
    if (i && last <= 0 && value > 0) crossings++;
    last = value;
  }
  assert.ok(Math.abs(peak - row.peak) < .001);
  assert.ok(Math.abs(Math.sqrt(squares / samples) - row.rms) < .001);
  if (row.file === 'hum-left.wav' || row.file === 'hum-right.wav') {
    const hz = row.file.includes('left') ? 220 : 226;
    assert.ok(Math.abs(crossings / row.seconds - hz) < .3, 'headphone tones retain the 6 Hz difference');
    assert.ok(Math.abs(wav.readInt16LE(44) - wav.readInt16LE(wav.length - 2)) / 32767 < .011, 'loop seam is continuous');
  }
});
