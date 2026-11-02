import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validate, FLAGS, SURFACES } from '../track/schema.js';

function tiny() {
  const nodes = [];
  for (let i = 0; i < 8; i++) {
    const a = (i / 8) * Math.PI * 2;
    nodes.push({ p: [Math.cos(a) * 16, 0, Math.sin(a) * 16], roll: 0, w: 12, flags: ['rail'], speed: 1, surface: 'glass' });
  }
  nodes[0].flags = ['start', 'rail'];
  return {
    format: 'glassroad-track', version: 1, id: 'tiny', name: { en: 'Tiny' }, author: 'ccl', created: '2026-10-22',
    loop: true, laps: 1, units: 'm', nodes,
    sections: [{ id: 'a', from: 0, to: 4, act: 'induction', palette: 'dawn', sky: 'spiral-slow', bandSpeed: 0.6 },
      { id: 'b', from: 4, to: 8, act: 'release', palette: 'afterglow', sky: 'spiral-wide', bandSpeed: 0.8 }],
    checkpoints: [0, 4], spawn: { node: 0, grid: [[-3, -4], [3, -6]] },
    items: [], boosts: [{ node: 2, x: 0, w: 4, len: 6 }], jumps: [], hazards: [], media: [],
    ai: { lines: [], pace: [1] }, beat: { bpm: null, offsetMs: 0, map: [] },
    limits: { fallY: -40, edgeGraceM: 1.2, edgeGraceS: 0.8 },
  };
}

test('a small valid track passes', () => {
  const r = validate(tiny());
  assert.equal(r.ok, true, r.errors.join('; '));
  assert.deepEqual(r.errors, []);
});

test('enums are the plan 3.1 sets', () => {
  assert.deepEqual([...SURFACES], ['glass', 'grip', 'haze', 'stream', 'pool']);
  for (const f of ['start', 'rail', 'railL', 'railR', 'open', 'magnet', 'boost', 'jump', 'glide', 'pit', 'flow']) assert.ok(FLAGS.includes(f));
});

const bad = (mut, re) => () => {
  const t = tiny(); mut(t);
  const r = validate(t);
  assert.equal(r.ok, false);
  assert.ok(r.errors.some((e) => re.test(e)), r.errors.join('; '));
};

test('rejects a wrong format', bad((t) => { t.format = 'x'; }, /format/));
test('rejects a wrong version', bad((t) => { t.version = 2; }, /version/));
test('rejects too few nodes', bad((t) => { t.nodes = t.nodes.slice(0, 3); }, /at least 4/));
test('rejects a bad node position', bad((t) => { t.nodes[3].p = [1, 2]; }, /nodes\[3\]\.p/));
test('rejects an unknown flag', bad((t) => { t.nodes[2].flags.push('lava'); }, /unknown flag lava/));
test('rejects an unknown surface', bad((t) => { t.nodes[2].surface = 'ice'; }, /surface/));
test('rejects open with rail', bad((t) => { t.nodes[2].flags = ['open', 'rail']; }, /open and rail/));
test('rejects roll out of range', bad((t) => { t.nodes[1].roll = 90; }, /roll/));
test('rejects width out of range', bad((t) => { t.nodes[1].w = 2; }, /\.w/));
test('rejects a section gap', bad((t) => { t.sections[1].from = 5; }, /continue at 4/));
test('rejects sections that stop short', bad((t) => { t.sections[1].to = 7; }, /end at node count/));
test('rejects checkpoints out of order', bad((t) => { t.checkpoints = [0, 5, 3]; }, /increase/));
test('rejects checkpoint out of range', bad((t) => { t.checkpoints = [0, 9]; }, /out of range/));
test('rejects a boost on a missing node', bad((t) => { t.boosts[0].node = 20; }, /boosts\[0\]\.node/));
test('rejects a jump whose node has no jump flag', bad((t) => { t.jumps = [{ node: 3, airLen: 10, height: 4 }]; }, /no jump flag/));
test('rejects a spawn slot off the road', bad((t) => { t.spawn.grid[1] = [9, -6]; }, /off the road/));
test('rejects fallY above the track', bad((t) => { t.limits.fallY = 5; }, /fallY/));
test('rejects a bad beat', bad((t) => { t.beat.bpm = -1; }, /beat\.bpm/));
test('rejects a bad media side', bad((t) => { t.media = [{ node: 1, side: 'X', w: 8, h: 4.5, kind: 'still', slot: 0 }]; }, /side/));

test('warns on spacing outside 8..15 m but stays ok', () => {
  const t = tiny();
  t.nodes[1].p = [t.nodes[0].p[0] - 1, 0, 3];
  const r = validate(t);
  assert.equal(r.ok, true, r.errors.join('; '));
  assert.ok(r.warnings.some((w) => /spacing/.test(w)));
});

test('a non-object fails cleanly', () => {
  assert.equal(validate(null).ok, false);
  assert.equal(validate([]).ok, false);
});

test('broad platforms remain bounded and contained by the road', () => {
  const t = tiny(); t.nodes.forEach(n => { n.w = 34; });
  t.fields = [{id: 'broad', from: 0, to: 4, lanes: [0], stones: [{id: 'deck', node: 1, x: 0, w: 28, len: 26}]}];
  assert.equal(validate(t).ok, true);
  const deck = t.fields[0].stones[0];
  deck.w = 33; assert.equal(validate(t).ok, false);
  deck.w = 28; deck.len = 33; assert.equal(validate(t).ok, false);
  deck.len = 26; deck.x = 4; assert.ok(validate(t).errors.some(e => e.includes('leaves the road footprint')));
});
