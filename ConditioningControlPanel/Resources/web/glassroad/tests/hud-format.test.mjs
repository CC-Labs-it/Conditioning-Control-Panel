import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { formatTime, ordinal, racerLabels, BOT_NAMES, ITEM_NAMES, iconOf, itemName, rollFrame, resultRows, pauseLine } from '../hud/format.js';
import { cleanOptions, loadOptions, saveOptions, stepVolume, OPTION_DEFAULTS, OPTIONS_KEY } from '../hud/options.js';
import { formatTime as phaseAFormat } from '../hud/hud.js';

const G = fileURLToPath(new URL('..', import.meta.url));

test('time format: minutes, two decimals, no 60.00 seconds', () => {
  assert.equal(formatTime(0), '0:00.00');
  assert.equal(formatTime(75.256), '1:15.26');
  assert.equal(formatTime(59.999), '1:00.00');
  assert.equal(formatTime(-3), '0:00.00');
  assert.equal(formatTime(NaN), '0:00.00');
  assert.equal(formatTime(605.5), '10:05.50');
  assert.equal(phaseAFormat, formatTime);
});

test('ordinals', () => {
  const s = (n) => { const o = ordinal(n); return o.n + o.suf; };
  assert.deepEqual([1, 2, 3, 4, 8, 11, 12, 13, 21, 22, 23, 101].map(s),
    ['1st', '2nd', '3rd', '4th', '8th', '11th', '12th', '13th', '21st', '22nd', '23rd', '101st']);
});

test('racer labels: You, own names kept, shared names get our short names by tint', () => {
  const metas = [{ i: 0, name: 'You', isBot: false, tint: 0 }];
  for (let i = 1; i < 8; i++) metas.push({ i, name: 'EMI bot', isBot: true, tint: i });
  const l = racerLabels(metas);
  assert.equal(l[0], 'You');
  assert.deepEqual(l.slice(1), BOT_NAMES);
  assert.equal(new Set(l).size, 8);
  const own = racerLabels([{ i: 0, name: 'You', isBot: false }, { i: 1, name: 'Rook', isBot: true, tint: 1 }]);
  assert.deepEqual(own, ['You', 'Rook']);
  for (const n of BOT_NAMES) assert.ok(n.length <= 8, n);
});

test('items: every id has a name and an icon file on disk', () => {
  assert.equal(itemName('glass'), 'Bubble Shield');
  assert.equal(itemName('pink'), 'Fog');
  for (const id of Object.keys(ITEM_NAMES)) {
    assert.ok(itemName(id).length > 0);
    assert.ok(existsSync(G + iconOf(id)), iconOf(id));
  }
  assert.equal(iconOf('nope'), 'art/fx/bubble.png');
});

test('roll: spins through icons, slows, then settles on the drawn id; gentle caps the rate', () => {
  const seen = [];
  let lastTick = -2, changes = 0;
  for (let t = 0; t <= 1.0001; t += 1 / 120) {
    const f = rollFrame(t, 1, 'melt');
    if (f.tick !== lastTick) { changes++; lastTick = f.tick; }
    seen.push(f);
  }
  assert.equal(seen.at(-1).id, 'melt');
  assert.equal(seen.at(-1).tick, -1);
  assert.ok(new Set(seen.map((f) => f.id)).size > 5);
  // ease out: more changes in the first half than in the second
  const ticks = seen.map((f) => f.tick).filter((x) => x >= 0);
  const half = Math.floor(ticks.length / 2);
  assert.ok(ticks[half] - ticks[0] > ticks.at(-1) - ticks[half]);
  let g = 0, gl = -2;
  for (let t = 0; t <= 1; t += 1 / 120) { const f = rollFrame(t, 1, 'melt', { gentle: true }); if (f.tick !== gl) { g++; gl = f.tick; } }
  assert.ok(g <= 4, 'gentle changes ' + g);
  assert.equal(rollFrame(0, 0, 'surge').id, 'surge');
});

test('results rows and pause line', () => {
  const result = { track: 'Threshold', laps: 3, rows: [
    { place: 1, i: 3, name: 'EMI bot', isBot: true, me: false, timeS: 131.04, bestLapS: 42.1, finished: true },
    { place: 2, i: 0, name: 'You', isBot: false, me: true, timeS: 132.3, bestLapS: 42.88, finished: true },
    { place: 3, i: 5, name: 'EMI bot', isBot: true, me: false, timeS: 140, bestLapS: 0, finished: false },
  ], me: { place: 2, timeS: 132.3, bestLapS: 42.88 } };
  const metas = [{ i: 0, name: 'You', isBot: false, tint: 0 }];
  for (let i = 1; i < 8; i++) metas.push({ i, name: 'EMI bot', isBot: true, tint: i });
  const rows = resultRows(result, metas);
  assert.deepEqual(rows.map((r) => r.place + r.suf + ' ' + r.name + ' ' + r.time + (r.est ? ' est' : '')),
    ['1st Lull 2:11.04', '2nd You 2:12.30', '3rd Static 2:20.00 est']);
  assert.ok(rows[1].me);
  assert.equal(rows[1].best, '0:42.88');
  assert.deepEqual(resultRows(null), []);
  assert.equal(pauseLine({ track: 'Threshold', lap: 1, laps: 3, place: 3, count: 8 }), 'Threshold, lap 2 of 3, 3rd of 8');
  assert.equal(pauseLine({ track: 'Threshold', lap: 5, laps: 3 }), 'Threshold, lap 3 of 3');
  assert.equal(pauseLine(), '');
});

test('options: defaults, clamping, storage that throws', () => {
  assert.deepEqual(cleanOptions(null), OPTION_DEFAULTS);
  const o = cleanOptions({ gentlePreview: true, assist: 'yes', volMusic: 4, volSfx: -1, volMaster: 0.33, junk: 1 });
  assert.equal(o.gentlePreview, true); assert.equal(o.assist, false);
  assert.equal(o.volMusic, 1); assert.equal(o.volSfx, 0); assert.equal(o.volMaster, 0.35);
  assert.equal('junk' in o, false);
  const mem = new Map();
  const store = { getItem: (k) => mem.get(k) ?? null, setItem: (k, v) => mem.set(k, v) };
  assert.equal(saveOptions({ ...OPTION_DEFAULTS, assist: true }, store), true);
  assert.ok(mem.has(OPTIONS_KEY));
  assert.equal(loadOptions(store).assist, true);
  const bad = { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); } };
  assert.deepEqual(loadOptions(bad), OPTION_DEFAULTS);
  assert.equal(saveOptions(OPTION_DEFAULTS, bad), false);
  mem.set(OPTIONS_KEY, '{not json');
  assert.deepEqual(loadOptions(store), OPTION_DEFAULTS);
  assert.equal(stepVolume(0.95, 1), 1); assert.equal(stepVolume(0.05, -1), 0); assert.equal(stepVolume(0.5, 1), 0.6);
});

test('hud copy: no exclamation marks, no em or en dashes, no speech', () => {
  for (const f of ['hud/hud.js', 'hud/cards.js', 'hud/format.js', 'hud/minimap.js', 'hud/options.js', 'styles.css']) {
    const src = readFileSync(G + f, 'utf8');
    assert.ok(!/[–—]/.test(src), f + ' has a dash');
    const strings = src.match(/'[^'\n]*'/g) || [];
    for (const s of strings) assert.ok(!s.includes('!') || /!==?|!\w|\(!/.test(s), f + ': ' + s);
    assert.ok(!/speechSynthesis|SpeechSynthesisUtterance/.test(src), f);
  }
});

test('options: a record saved before mixV 2 keeps its other options and takes the new volume defaults once', () => {
  const mem = new Map(), storage = { getItem: (k) => mem.get(k) ?? null, setItem: (k, v) => mem.set(k, v) };
  mem.set(OPTIONS_KEY, JSON.stringify({ volMaster: 0.8, volMusic: 0.7, volSfx: 0.9, assist: true, quality: 'mid' }));
  const o = loadOptions(storage);
  assert.equal(o.volMusic, OPTION_DEFAULTS.volMusic); assert.equal(o.volSfx, OPTION_DEFAULTS.volSfx);
  assert.equal(o.assist, true); assert.equal(o.quality, 'mid');
  assert.ok(OPTION_DEFAULTS.volSfx < OPTION_DEFAULTS.volMusic, 'effects sit under the song');
  saveOptions({ ...o, volSfx: 0.3 }, storage);
  assert.equal(loadOptions(storage).volSfx, 0.3, 'a choice made after the reset is kept');
});

test('speed readout: shownTopMps keeps the km/h a player sees while the real top moves', async () => {
  const { shownTopMps, shownKmh } = await import('../hud/format.js');
  const { createFeel } = await import('../core/feel.js');
  const feelJson = JSON.parse(readFileSync(G + 'feel.json', 'utf8'));
  const feel = createFeel(feelJson);
  assert.equal(feel.get('speed.topMps'), 37.5);
  assert.equal(shownTopMps(feel), 30);
  assert.equal(shownKmh(1, feel), 108);
  assert.equal(shownKmh(0.5, feel), 54);
  assert.equal(shownKmh(-0.2, feel), 0);
  const bare = createFeel({ speed: { topMps: 30 } });
  assert.equal(shownTopMps(bare), 30);
  assert.equal(shownTopMps(bare), 30);
  assert.equal(shownKmh(1, bare), 108);
});
