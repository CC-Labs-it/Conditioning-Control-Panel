// sim/wordPick.js: which words ride a bubble and which are painted on the road (pass 4).
import test from 'node:test';
import assert from 'node:assert/strict';
import { pickWords, pickStats, ROAD, BUBBLE, GATE, TRIGGER, PICK } from '../sim/wordPick.js';

const w = (word, startS, line, extra = {}) => ({ w: word, startS, endS: startS + 0.3, line, bubble: false, gate: false, ...extra });

test('trigger phrase words and gate words always ride bubbles', () => {
  const song = {
    words: [w('you', 9.9, 0), w('good', 10, 0, { bubble: true }), w('girl', 10.4, 0, { bubble: true }), w('pink', 11, 1, { gate: true })],
    triggers: [{ kind: 'word', w: 'good girl', startS: 10 }, { kind: 'drop', startS: 10 }],
  };
  const k = pickWords(song);
  assert.deepEqual(Array.from(k), [ROAD, TRIGGER, TRIGGER, GATE]);
});

test('impact bubbles keep one per line and the minimum gap', () => {
  const words = [];
  for (let i = 0; i < 40; i++) words.push(w(i % 2 ? 'pink' : 'the', i, Math.floor(i / 4), { bubble: true }));
  const k = pickWords({ words, triggers: [] });
  const times = words.filter((_, i) => k[i] === BUBBLE).map((x) => x.startS);
  assert.ok(times.length > 3);
  for (let i = 1; i < times.length; i++) assert.ok(times[i] - times[i - 1] >= PICK.minGapS - 1e-9);
  const lines = words.filter((_, i) => k[i] === BUBBLE).map((x) => x.line);
  assert.equal(new Set(lines).size, lines.length);
  assert.ok(words.every((x, i) => k[i] !== BUBBLE || x.w === 'pink'), 'only impact words');
});

test('no song, no words; stats count every kind', () => {
  assert.equal(pickWords(null).length, 0);
  assert.deepEqual(pickStats(Uint8Array.from([0, 0, 1, 2, 3])), { road: 2, bubble: 1, gate: 1, trigger: 1 });
});
