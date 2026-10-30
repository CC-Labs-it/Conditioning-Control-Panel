// Lane B3-W5: the preview box's catalog (every item names art that is on disk), the lazy loader (rests, drops stale
// loads, disposes on leave, frees a kind's shared art when the focus leaves it) and the framing law.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { previewSpec, specFiles, sharedFile, KINDS, STAGE_KINDS } from '../preview/catalog.js';
import { createPreviewLoader } from '../preview/loader.js';
import { spinRate, fadeMs, viewBox, fitDistance, HEAD_SPAN, BODY_SPAN } from '../preview/frame.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const ASSETS = path.join(here, '..', 'assets');
const read = (f) => JSON.parse(fs.readFileSync(path.join(ASSETS, f), 'utf8'));
const index = read('preview/index.json'), manifest = read('manifest.json');
const state = JSON.parse(fs.readFileSync(path.join(here, '..', 'mock', 'state.sample.json'), 'utf8'));
const rowIds = state.tabs.flatMap((t) => t.shelves.flatMap((s) => s.rows.map((r) => r.id)));

// glTF JSON chunk: the names of every node (scenes[i] picks one scene's subtree).
function glbNames(file, sceneName) {
  const b = fs.readFileSync(path.join(ASSETS, file)), n = b.readUInt32LE(12), j = JSON.parse(b.subarray(20, 20 + n).toString('utf8'));
  const out = new Set(), walk = (i) => { out.add(j.nodes[i].name); for (const c of j.nodes[i].children || []) walk(c); };
  for (const s of j.scenes.filter((x) => !sceneName || x.name === sceneName)) for (const i of s.nodes) walk(i);
  return { names: out, required: j.extensionsRequired || [] };
}

test('every shelf row and every manifest item has a preview whose files are on disk', () => {
  const ids = new Set([...rowIds, ...manifest.items.map((i) => i.id)]);
  for (const id of ids) {
    const spec = previewSpec(id, index, manifest);
    assert.ok(spec, `${id} has no preview`);
    assert.ok(KINDS.includes(spec.kind), `${id}: unknown kind ${spec.kind}`);
    for (const f of specFiles(spec)) assert.ok(fs.existsSync(path.join(ASSETS, f)), `${id}: ${f} missing`);
  }
});

test('every family previews its own art, Back Room prizes their shelf model, a retired id with no art none', () => {
  const kind = (id) => previewSpec(id, index, manifest)?.kind;
  assert.equal(kind('rain.dress.rose-flare'), 'rain');
  assert.equal(kind('rain.acc.back-katana'), 'rain');
  assert.equal(kind('kart.body.petal'), 'model');
  assert.equal(previewSpec('kart.body.petal', index, manifest).file, 'preview/kart/emi-vehicle-petal.glb');
  assert.equal(kind('kart.paint.pattern-bundle'), 'swatch');
  assert.equal(previewSpec('kart.paint.pattern-bundle', index, manifest).srcs.length, 5);
  assert.equal(kind('kart.paint.hearts'), 'swatch', 'an owned retired paint shows its pattern');
  assert.equal(kind('chess.set.royal-capes'), 'chess');
  assert.equal(kind('prime.banner.arena'), 'chess');
  assert.equal(kind('profile.frame.lace'), 'card');
  assert.equal(kind('prime.frame.room-host'), 'card');
  assert.equal(kind('profile.avatar.emi-pixel'), 'avatar');
  assert.equal(kind('desk.toy.snow-globe'), 'desk');
  assert.equal(kind('prime.paint.host-gold'), 'swatch');
  assert.equal(kind('prime.trail.podium'), 'model');
  assert.deepEqual(previewSpec('jackpot_remix', index, manifest), { kind: 'model', file: 'items/jackpot_remix.glb' });
  assert.equal(previewSpec('kart.body.glass-slipper', index, manifest), null);
  assert.equal(previewSpec('nope', index, manifest), null);
  assert.equal(previewSpec(null, index, manifest), null);
});

test('Rain previews name groups the avatar really has; the counter three (r169) reads every 3D file', () => {
  const lib = glbNames(index.rain.file, index.rain.library).names, base = glbNames(index.rain.file, 'rain_default').names;
  for (const [id, s] of Object.entries(index.items).filter(([, s]) => s.kind === 'rain')) {
    for (const n of s.show) assert.ok(lib.has(n), `${id}: ${n} not in ${index.rain.library}`);
    for (const n of s.hide) assert.ok(base.has(n), `${id}: ${n} not in the dressed default`);
    if (s.wear) assert.ok(glbNames(s.wear).names.size > 0);
  }
  const files = new Set(Object.values(index.items).flatMap((s) => specFiles(previewSpec('x', { ...index, items: { x: s } }, manifest))));
  for (const f of [...files].filter((x) => x.endsWith('.glb'))) {
    const extra = glbNames(f).required.filter((e) => !['EXT_meshopt_compression', 'KHR_mesh_quantization'].includes(e));
    assert.deepEqual(extra, [], `${f} needs ${extra}`);
  }
});

test('size rule: no preview file over 8 MB, the preview tree under 10 MB', () => {
  let total = 0;
  const walk = (d) => { for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    const p = path.join(d, e.name);
    if (e.isDirectory()) walk(p); else { const n = fs.statSync(p).size; total += n; assert.ok(n < 8 * 2 ** 20, `${p} is ${n} bytes`); }
  } };
  walk(path.join(ASSETS, 'preview'));
  assert.ok(total < 10 * 2 ** 20, `preview tree is ${total} bytes`);
  assert.ok(fs.existsSync(path.join(ASSETS, 'preview', 'ATTRIBUTION.txt')), 'Rain is CC BY: the licence travels with her');
});

test('only Rain and chess keep art between items; the 3D kinds go to the stage', () => {
  assert.equal(sharedFile(previewSpec('rain.hair.ink-sleek', index, manifest)), index.rain.file);
  assert.equal(sharedFile(previewSpec('chess.insignia.star', index, manifest)), 'preview/chess/knight.glb');
  assert.equal(sharedFile(previewSpec('kart.body.petal', index, manifest)), null);
  assert.deepEqual([...STAGE_KINDS].sort(), ['chess', 'model', 'rain']);
});

// A loader over fake drawing: every handle records what happened to it; wait() resolves when the test says so.
function rig(specs) {
  const log = [], timers = [], handles = [];
  const wait = (ms) => new Promise((r) => timers.push({ ms, r }));
  const flush = async () => { for (let i = 0; i < 20; i++) { while (timers.length) timers.shift().r(); await new Promise((r) => setImmediate(r)); } };
  const pending = new Map();
  const pv = createPreviewLoader({
    resolve: (id) => specs[id] || null,
    open: (spec) => new Promise((res, rej) => pending.set(spec.name, { res, rej })).then(() => {
      const h = { name: spec.name, events: [], show() { this.events.push('show'); }, hide() { this.events.push('hide'); }, dispose() { this.events.push('dispose'); } };
      handles.push(h); return h;
    }),
    release: (k) => log.push(`release:${k}`),
    empty: (on) => log.push(`empty:${on}`),
    wait, settleMs: 140, outMs: 120,
  });
  const land = async (name) => { await flush(); pending.get(name).res(); await flush(); };
  return { pv, log, handles, flush, land, pending };
}
const SPECS = { a: { kind: 'rain', name: 'a' }, b: { kind: 'rain', name: 'b' }, c: { kind: 'model', name: 'c' }, d: { kind: 'chess', name: 'd' } };

test('loader: nothing loads until the focus rests; a stale load is disposed unseen', async () => {
  const { pv, handles, flush, pending, land } = rig(SPECS);
  pv.focus('a'); pv.focus('b');
  await flush();
  assert.deepEqual([...pending.keys()], ['b'], 'a walk past a only loads where it stops');
  pv.focus('c');
  await land('b');
  assert.deepEqual(handles.find((h) => h.name === 'b').events, ['hide', 'dispose'], 'b landed after the focus left: never shown');
  await land('c');
  assert.deepEqual(handles.find((h) => h.name === 'c').events, ['show']);
});

test('loader: leaving an item fades it OUT then frees it; same kind keeps the shared art, a new kind releases it', async () => {
  const { pv, handles, log, land } = rig(SPECS);
  pv.focus('a'); await land('a');
  pv.focus('b'); await land('b');
  assert.deepEqual(handles[0].events, ['show', 'hide', 'dispose']);
  assert.ok(!log.includes('release:rain'), 'a to b stays on the avatar');
  pv.focus('d'); await land('d');
  assert.ok(log.includes('release:rain'), 'leaving Rain frees the avatar');
  pv.focus(null);
  await land('d').catch(() => {});
  assert.deepEqual(handles[2].events, ['show', 'hide', 'dispose']);
  assert.equal(log.at(-1), 'empty:true', 'no focus: the box hides');
  pv.dispose();
  assert.ok(log.includes('release:chess'), 'closing the counter frees what chess kept');
});

test('loader: a failed load leaves the box empty; dispose drops the shown preview at once', async () => {
  const { pv, handles, log, flush, pending } = rig(SPECS);
  pv.focus('a'); await flush();
  pending.get('a').rej(new Error('offline')); await flush();
  assert.equal(log.at(-1), 'empty:true');
  pv.focus('c'); await flush(); pending.get('c').res(); await flush();
  assert.deepEqual(handles[0].events, ['show']);
  pv.dispose();
  assert.deepEqual(handles[0].events, ['show', 'hide', 'dispose']);
  pv.focus('a'); await flush();
  assert.equal(pending.get('a') && handles.length, 1, 'a disposed loader loads nothing');
});

test('frame: turntable speed and fades follow motion, reduced motion and Gentle', () => {
  assert.equal(spinRate({ motion: 'off' }), 0);
  assert.equal(spinRate({ motion: 'off', gentle: true }), 0);
  assert.ok(spinRate({ motion: 'full' }) > spinRate({ motion: 'less' }));
  assert.equal(spinRate({ motion: 'full', reducedMotion: true }), spinRate({ motion: 'less' }));
  assert.equal(spinRate({ motion: 'full', gentle: true }), spinRate({ motion: 'less' }));
  assert.deepEqual(fadeMs({ motion: 'off' }), { in: 0, out: 0 });
  assert.ok(fadeMs({ gentle: true }).in >= 300, 'Gentle fades rise over at least 300 ms');
});

test('frame: a head view keeps the top of the avatar; the camera backs off until a turning box fits', () => {
  const v = viewBox([-0.3, 0, -0.2], [0.3, 1.7, 0.2], 'head');
  assert.ok(Math.abs(v.size[1] - HEAD_SPAN) < 1e-9);
  assert.ok(Math.abs(v.center[1] - (1.7 - HEAD_SPAN / 2)) < 1e-9);
  const full = viewBox([-0.3, 0, -0.2], [0.3, 1.7, 0.2]);
  const tall = fitDistance(full.size, 28, 250 / 150), phone = fitDistance(full.size, 28, 360 / 96);
  assert.ok(tall > 0 && phone > 0);
  const wide = fitDistance([2, 0.2, 2], 28, 360 / 96), narrow = fitDistance([2, 0.2, 2], 28, 1);
  assert.ok(narrow > wide, 'a narrower box needs more distance for a wide model');
  // the widest side fits at its nearest face, with room to turn toward the diagonal
  const d = fitDistance([1, 1, 1], 28, 1, 0), halfW = Math.tan((28 * Math.PI) / 360) * (d - 0.5);
  assert.ok(halfW >= (1 + Math.SQRT2) / 4 - 1e-9);
  const b = viewBox([-0.3, 0, -0.2], [0.3, 1.7, 0.2], 'body');
  assert.ok(Math.abs(b.size[1] - BODY_SPAN) < 1e-9, 'a body view keeps head to knees');
});
