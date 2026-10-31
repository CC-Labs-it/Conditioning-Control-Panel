// RACE YOUR OWN SONG (desktop host, Services/Glassroad/SongWords): the page side of CONTRACT section 7 `song-words`.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { userSongRows, USER_SONG_ROWS } from '../host/bridge.js';
import { normalizeChart } from '../audio/chart.js';
import { createMusic, analyzeAudioBuffer } from '../audio/music.js';
import { createFeel } from '../core/feel.js';
import { pickWords, PICK, BUBBLE, ROAD } from '../sim/wordPick.js';
import { createCourseMedia } from '../hud/courseMedia.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const url = (id) => `https://ccp.songs/${id}/chart.json`;

test('user song rows: plain ids on the ccp.songs host only, labels plain text, at most 12, no duplicates', () => {
  const good = { id: 'u0123456789abcdef', label: 'My song', url: url('u0123456789abcdef') };
  assert.deepEqual(userSongRows([good]), [good]);
  assert.deepEqual(userSongRows(null), []);
  assert.deepEqual(userSongRows([{ ...good, url: 'https://ccp.game/u0123456789abcdef/chart.json' }]), [], 'another host');
  assert.deepEqual(userSongRows([{ ...good, url: url('u0123456789abcdee') }]), [], 'url names another id');
  assert.deepEqual(userSongRows([{ ...good, id: 'U0123' }]), [], 'ids are lowercase');
  assert.deepEqual(userSongRows([{ ...good, url: 'https://ccp.songs/../x/chart.json' }]), []);
  assert.equal(userSongRows([{ ...good, label: 'a\u0000b<c>' + 'x'.repeat(60) }])[0].label.length, 40);
  assert.equal(userSongRows([{ ...good, label: '' }])[0].label, good.id);
  assert.equal(userSongRows([good, good]).length, 1);
  const many = Array.from({ length: 15 }, (_, i) => ({ id: 'u' + String(i).padStart(3, '0'), label: 's' + i, url: url('u' + String(i).padStart(3, '0')) }));
  assert.equal(userSongRows(many).length, USER_SONG_ROWS);
});

test('a heard chart keeps its userSong flag and bubble marks through normalizeChart', () => {
  const c = normalizeChart({ id: 'u1', title: 'x', audio: 'audio.mp3', version: 1, userSong: true, durationS: 60, bpm: 96,
    sections: [{ id: 's1', startS: 0, endS: 60 }], lines: ['hold me closer'],
    words: [{ w: 'hold', startS: 1, endS: 1.3, line: 0, bubble: true }, { w: 'me', startS: 1.3, endS: 1.4, line: 0, bubble: false }] });
  assert.equal(c.userSong, true);
  assert.equal(normalizeChart({ bpm: 96, sections: [] }).userSong, false);
  assert.deepEqual(c.words.map((w) => w.bubble), [true, false]);
});

test('a heard song gives bubbles to confident plain words (impactOnly off), one per line, spaced by minGapS', () => {
  const words = [
    { w: 'hold', startS: 1, endS: 1.3, line: 0, bubble: true, gate: false },
    { w: 'closer', startS: 1.5, endS: 1.9, line: 0, bubble: true, gate: false },
    { w: 'river', startS: 3, endS: 3.4, line: 1, bubble: true, gate: false },
    { w: 'sunlight', startS: 9, endS: 9.5, line: 2, bubble: true, gate: false },
    { w: 'mm', startS: 12, endS: 12.2, line: 3, bubble: false, gate: false },
  ];
  const song = normalizeChart({ bpm: 96, sections: [], userSong: true, words });
  const strict = pickWords(song);
  assert.ok(strict.every((k) => k === ROAD), 'default pick: none of these is an IMPACT word');
  const open = [...pickWords(song, { ...PICK, impactOnly: false })];
  assert.equal(open[1], BUBBLE, 'the longer word of the first line');
  assert.equal(open[0], ROAD, 'one bubble per line');
  assert.equal(open[2], ROAD, 'river is inside minGapS of closer');
  assert.equal(open[3], BUBBLE);
  assert.equal(open[4], ROAD, 'not a bubble candidate');
});

function fakeCtx(buffer) {
  const param = (v = 0) => {
    const p = { value: v };
    for (const k of ['setValueAtTime', 'linearRampToValueAtTime', 'exponentialRampToValueAtTime', 'setTargetAtTime', 'cancelScheduledValues']) p[k] = () => p;
    return p;
  };
  const node = (extra = {}) => ({ connect() {}, disconnect() {}, start() {}, stop() {}, ...extra });
  const ctx = {
    state: 'running', currentTime: 0, sampleRate: 8000, destination: node(),
    createGain: () => node({ gain: param(1) }),
    createBiquadFilter: () => node({ type: 'lowpass', frequency: param(350), Q: param(1) }),
    createOscillator: () => node({ type: 'sine', frequency: param(440), detune: param(0) }),
    createBufferSource: () => node({ buffer: null, loop: false }),
    createStereoPanner: () => node({ pan: param(0) }),
    createBuffer: (_c, len) => ({ getChannelData: () => new Float32Array(len) }),
    decodeAudioData: () => Promise.resolve(buffer),
    resume: async () => { ctx.state = 'running'; }, suspend: async () => { ctx.state = 'suspended'; }, close() {},
  };
  return ctx;
}

test('music: a heard chart takes its beat grid from the recording, a catalog chart keeps its own bpm', async () => {
  // a click every half second: a 120 bpm recording
  const sr = 8000, data = new Float32Array(sr * 30);
  for (let t = 0; t < 30; t += 0.5) for (let i = 0; i < 200; i++) data[Math.round(t * sr) + i] = (i % 2 ? -1 : 1) * Math.exp(-i / 40);
  const buffer = { duration: 30, sampleRate: sr, numberOfChannels: 1, getChannelData: () => data };
  const expected = analyzeAudioBuffer(buffer);
  const charts = {
    'https://ccp.songs/u1/chart.json': { id: 'u1', userSong: true, bpm: 96, durationS: 30, audio: 'audio.mp3', sections: [{ id: 's1', startS: 0, endS: 30 }],
      words: [{ w: 'hold', startS: 1, endS: 1.3, line: 0, bubble: true }] },
    'https://x.test/c/preview.json': { bpm: 120, downbeatS: 0.5, sections: [{ id: 'induction', startS: 0 }] },
  };
  const fetched = [];
  const fetchFn = async (u) => { fetched.push(u); return { ok: u in charts || u.endsWith('.mp3'), json: async () => charts[u], arrayBuffer: async () => new ArrayBuffer(8) }; };
  const ctx = fakeCtx(buffer);
  const music = createMusic(ctx, ctx.createGain(), createFeel(feelJson), { fetchFn });
  assert.equal(await music.playChart('https://ccp.songs/u1/chart.json'), true);
  assert.ok(fetched.includes('https://ccp.songs/u1/audio.mp3'), 'the audio copy sits next to the chart');
  assert.equal(music.song.userSong, true);
  assert.equal(music.song.words.length, 1, 'the heard words stay');
  if (expected && expected.bpm > 0) {
    assert.equal(music.song.bpm, expected.bpm);
    assert.deepEqual(music.song.beats, expected.beats);
    assert.equal(music.chart.bpm, expected.bpm);
    assert.equal(music.beat.bpm, expected.bpm);
  } else assert.equal(music.song.bpm, 96, 'no beat found: the placeholder grid');
  assert.equal(music.analysis, null, 'a chart, not a dropped file');
  assert.equal(await music.playChart('https://x.test/c/preview.json'), true);
  assert.equal(music.song.userSong, false);
  assert.equal(music.chart.bpm, 120, 'a catalog chart is never re-analysed');
});

function element(tag = '') {
  const selectors = new Map();
  return { tag, dataset: {}, children: [], attrs: {}, open: false, style: {},
    append(...nodes) { this.children.push(...nodes); }, setAttribute(k, v) { this.attrs[k] = v; },
    querySelector(s) { if (!selectors.has(s)) selectors.set(s, element(s)); return selectors.get(s); },
    querySelectorAll() { return []; }, contains() { return false; }, addEventListener() {},
    showModal() { this.open = true; }, close() { this.open = false; }, remove() {}, focus() { this.focused = true; } };
}

test('garage dialog on a desktop host: Choose music file asks the host; a job in flight shows its line and takes no click', (t) => {
  const previousDocument = globalThis.document, previousEvent = globalThis.CustomEvent;
  globalThis.document = { createElement: element, body: element() };
  globalThis.CustomEvent = class { constructor(type, options) { this.type = type; Object.assign(this, options); } };
  t.after(() => { globalThis.document = previousDocument; globalThis.CustomEvent = previousEvent; });
  const root = element(), events = [];
  root.dispatchEvent = (e) => events.push(e.detail);
  let view = { source: 'off', niches: [], catalog: [], songs: [{ id: 'u1', label: 'My song' }], songWords: true, songProgress: null };
  const picker = createCourseMedia({ root, mediaFor: () => view });
  picker.show({ id: 'threshold', name: 'Threshold' });
  // the fake element never clears on repaint: every paint appends, so the newest nodes sit last
  let songs = root.children[0].querySelector('.g-media-songs').children;
  const chooser = (list) => list.filter((n) => n.dataset?.focus === 'song-file').at(-1);
  const choose = chooser(songs);
  assert.equal(choose.textContent, 'Choose music file');
  const note = (list) => list[list.length - 2].textContent;   // the last line is "Selected: ..."
  assert.match(note(songs), /stay on this device/);
  choose.onclick();
  assert.deepEqual(events, [{ trackId: 'threshold', kind: 'song-pick' }]);
  view = { ...view, songProgress: { trackId: 'threshold', status: 'progress', phase: 'listen', pct: 0.42, note: 'Listening for the words' } };
  picker.refresh();
  songs = root.children[0].querySelector('.g-media-songs').children;
  const busy = chooser(songs);
  assert.equal(busy.textContent, 'Listening');
  busy.onclick();
  assert.equal(events.length, 1, 'no second pick while listening');
  assert.equal(note(songs), 'Listening for the words 42%');
  view = { ...view, songProgress: { trackId: 'threshold', status: 'error', note: 'Could not read this file.' } };
  picker.refresh();
  songs = root.children[0].querySelector('.g-media-songs').children;
  assert.equal(note(songs), 'Could not read this file.');
  assert.equal(chooser(songs).textContent, 'Choose music file');
});
