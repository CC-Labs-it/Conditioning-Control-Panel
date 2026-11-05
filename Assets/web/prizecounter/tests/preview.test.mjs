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
import { coveredGroups, cutBelow, BODY_BELOW_MAX, bindsTo, KART_JOINTS, shellMask, PATTERN_ID, paintWalk } from '../preview/fit.js';

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
  assert.equal(kind('kart.paint.pattern-bundle'), 'kart', 'a paint previews on the kart shell (F5)');
  assert.equal(previewSpec('kart.paint.pattern-bundle', index, manifest).layers.length, 5);
  assert.equal(previewSpec('kart.paint.pattern-bundle', index, manifest).fallback.srcs.length, 5, 'no 3D: the flat swatch walk');
  assert.equal(kind('kart.paint.hearts'), 'kart', 'an owned retired paint shows its pattern');
  assert.equal(kind('kart.outfit.ringmaster'), 'kart', 'an outfit previews worn on the kart (F5)');
  assert.equal(kind('kart.umbrella.lace'), 'kart');
  assert.equal(kind('chess.set.royal-capes'), 'chess');
  assert.equal(kind('prime.banner.arena'), 'chess');
  assert.equal(kind('profile.frame.lace'), 'card');
  assert.equal(kind('prime.frame.room-host'), 'card');
  assert.equal(kind('profile.avatar.emi-pixel'), 'avatar');
  assert.equal(kind('desk.toy.snow-globe'), 'desk');
  assert.equal(kind('prime.paint.host-gold'), 'kart');
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

test('only Rain, the kart base and chess keep art between items; the 3D kinds go to the stage', () => {
  assert.equal(sharedFile(previewSpec('rain.hair.ink-sleek', index, manifest)), index.rain.file);
  assert.equal(sharedFile(previewSpec('chess.insignia.star', index, manifest)), 'preview/chess/knight.glb');
  assert.equal(sharedFile(previewSpec('kart.outfit.pit-crew', index, manifest)), index.kart.base);
  assert.equal(sharedFile(previewSpec('kart.body.petal', index, manifest)), null);
  assert.deepEqual([...STAGE_KINDS].sort(), ['chess', 'kart', 'model', 'rain']);
});

// Lane F5: worn kart pieces, paints on the shell, Rain coverage.
const glbJson = (file) => { const b = fs.readFileSync(path.join(ASSETS, file)), n = b.readUInt32LE(12); return JSON.parse(b.subarray(20, 20 + n).toString('utf8')); };
const jointNames = (j, skin) => skin.joints.map((i) => j.nodes[i].name);

test('F5 kart: the base is rigged, uncompressed (no KTX2 for r169), and every worn piece binds to its bones', () => {
  const kart = index.kart, b = glbJson(kart.base);
  assert.deepEqual(b.extensionsRequired || [], [], 'the counter three has no KTX2Loader: the base must not need one');
  assert.ok(!(b.images || []).some((i) => /ktx2/.test(i.mimeType || '')));
  assert.equal(b.skins.length, 1);
  const bones = jointNames(b, b.skins[0]);
  assert.ok(bindsTo(KART_JOINTS, bones), 'the base carries the accepted 15 joints');
  assert.ok(b.animations.some((a) => a.name === 'idle_bob'), 'the preview holds the garage idle at time zero');
  for (const id of ['kart.outfit.ballerina', 'kart.outfit.pit-crew', 'kart.outfit.ringmaster', 'kart.umbrella.lace']) {
    const spec = previewSpec(id, index, manifest);
    assert.equal(spec.base, kart.base);
    assert.ok(spec.wear.length > 0, `${id} wears something`);
    for (const f of spec.wear) {
      const w = glbJson(f);
      assert.ok(w.skins && w.skins.length, `${id}: ${f} is rigged (not the shelf GLB)`);
      for (const s of w.skins) assert.ok(bindsTo(jointNames(w, s), bones), `${id}: ${f} names a bone the kart lacks`);
    }
  }
  assert.ok(!bindsTo(['kart_root', 'tail'], KART_JOINTS) && !bindsTo([], KART_JOINTS), 'an unknown bone or none refuses the bind');
});

test('F5 paints: each layer names a known pattern, its mask is on disk, host gold keeps its own colours', () => {
  for (const id of ['kart.paint.pattern-bundle', 'kart.paint.hearts', 'kart.paint.pink-checker', 'kart.paint.spiral', 'prime.paint.host-gold']) {
    const spec = previewSpec(id, index, manifest);
    assert.ok(spec.layers.length > 0, `${id} has a pattern`);
    for (const l of spec.layers) {
      assert.equal(l.pattern, PATTERN_ID[l.id]);
      assert.match(l.primary, /^#[0-9a-f]{6}$/i); assert.match(l.secondary, /^#[0-9a-f]{6}$/i);
      if (l.pattern >= 4) assert.ok(l.mask && fs.existsSync(path.join(ASSETS, l.mask)), `${id}: ${l.id} needs its mask`);
    }
    assert.equal(spec.fallback.kind, 'swatch');
    for (const f of specFiles(spec.fallback)) assert.ok(fs.existsSync(path.join(ASSETS, f)), `${id}: fallback ${f} missing`);
  }
  assert.deepEqual(previewSpec('kart.paint.pattern-bundle', index, manifest).layers.map((l) => l.id), ['stripes', 'dots', 'checks', 'hearts', 'spiral']);
  const gold = previewSpec('prime.paint.host-gold', index, manifest).layers[0];
  assert.deepEqual([gold.primary, gold.secondary], ['#d79b14', '#fff09a'], 'glassroad PAINT_COLORS host-gold');
  assert.equal(previewSpec('kart.paint.hearts', index, manifest).layers[0].primary, index.kart.primary, 'other paints wear the Blush preset');
});

test('F5 paint walk: motion off and reduced motion hold; Gentle blends slower, never cuts', () => {
  assert.equal(paintWalk(1), null);
  assert.equal(paintWalk(5, { motion: 'off' }), null);
  assert.equal(paintWalk(5, { reducedMotion: true }), null);
  const full = paintWalk(5), gentle = paintWalk(5, { gentle: true });
  assert.ok(full.blendMs > 0 && full.holdMs > full.blendMs);
  assert.ok(gentle.blendMs >= 300 && gentle.blendMs > full.blendMs);
});

test('F5 shell mask: only vertices EMI_root carries at half weight or more', () => {
  const names = ['kart_root', 'EMI_root', 'ant0'];
  const joints = new Float32Array([1, 0, 0, 0, 1, 2, 0, 0, 2, 0, 0, 0, 0, 1, 0, 0]);
  const weights = new Float32Array([1, 0, 0, 0, 0.4, 0.6, 0, 0, 1, 0, 0, 0, 0.5, 0.5, 0, 0]);
  assert.deepEqual([...shellMask(joints, weights, names, 4)], [1, 0, 0, 1]);
  assert.deepEqual([...shellMask(joints, weights, ['kart_root'], 4)], [0, 0, 0, 0]);
});

test('F5 Rain coverage: the coverall hides boots, hair and earrings; the action suit cuts the legs, keeps the hands', () => {
  const base = glbNames(index.rain.file, 'rain_default').names;
  for (const groups of Object.values(index.rain.slots)) for (const n of groups) assert.ok(base.has(n), `slot group ${n} not in the dressed default`);
  const hazmat = previewSpec('rain.suit.hazmat-yellow', index, manifest).hide;
  for (const n of ['accepted_boots', 'wear_original-blonde', 'wear_hoops-original', 'body_satin-original']) assert.ok(hazmat.includes(n), `coverall leaves ${n}`);
  const dress = previewSpec('rain.dress.rose-flare', index, manifest).hide;
  assert.ok(!dress.includes('accepted_boots'), 'a dress keeps the boots');
  assert.deepEqual(coveredGroups({ hide: ['a'], coverage: { hideSlots: ['hair', 'none'] } }, { hair: ['h', 'a'] }).sort(), ['a', 'h']);
  const action = previewSpec('rain.suit.action-yellow', index, manifest);
  assert.equal(action.coverage.hideBodyBelow, 0.92);
  assert.deepEqual(action.body, index.rain.slots.body);
  // Four triangles: a leg (all covered), a hand beside the hip, a shoulder above the cut, one straddling the hem.
  const pts = [[0.1, 0.3, 0], [0.12, 0.3, 0], [0.1, 0.35, 0], [0.25, 0.75, 0], [0.26, 0.75, 0], [0.25, 0.8, 0],
    [0.1, 1.2, 0], [0.12, 1.2, 0], [0.1, 1.25, 0], [0.1, 0.8, 0], [0.12, 0.8, 0], [0.1, 0.95, 0]];
  const kept = cutBelow(null, pts.length, (i) => pts[i], 0.92);
  assert.deepEqual([...kept], [3, 4, 5, 6, 7, 8, 9, 10, 11], 'only the leg goes; 0.92 clamps to the 0.86 neckline rule');
  assert.equal(BODY_BELOW_MAX, 0.86);
  const idx = new Uint16Array([0, 1, 2, 3, 4, 5]);
  assert.deepEqual([...cutBelow(idx, 6, (i) => pts[i], 0.5)], [3, 4, 5]);
});

// A loader over fake drawing: every handle records what happened to it; wait() resolves when the test says so.
function rig(specs) {
  const log = [], order = [], timers = [], handles = [];
  const wait = (ms) => new Promise((r) => timers.push({ ms, r }));
  const flush = async () => { for (let i = 0; i < 20; i++) { while (timers.length) timers.shift().r(); await new Promise((r) => setImmediate(r)); } };
  const pending = new Map();
  const pv = createPreviewLoader({
    resolve: (id) => specs[id] || null,
    open: (spec) => new Promise((res, rej) => pending.set(spec.name, { res, rej })).then(() => {
      const h = { name: spec.name, events: [], show() { this.events.push('show'); }, hide() { this.events.push('hide'); }, dispose() { this.events.push('dispose'); order.push(`dispose:${spec.name}`); } };
      handles.push(h); return h;
    }),
    release: (k) => { log.push(`release:${k}`); order.push(`release:${k}`); },
    empty: (on) => log.push(`empty:${on}`),
    wait, settleMs: 140, outMs: 120,
  });
  const land = async (name) => { await flush(); pending.get(name).res(); await flush(); };
  return { pv, log, order, handles, flush, land, pending };
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
  const { pv, handles, log, order, land } = rig(SPECS);
  pv.focus('a'); await land('a');
  pv.focus('b'); await land('b');
  assert.deepEqual(handles[0].events, ['show', 'hide', 'dispose']);
  assert.ok(!log.includes('release:rain'), 'a to b stays on the avatar');
  pv.focus('d'); await land('d');
  assert.ok(log.includes('release:rain'), 'leaving Rain frees the avatar');
  assert.ok(order.indexOf('release:rain') > order.indexOf('dispose:b'), 'the avatar is freed only after the last Rain item faded OUT');
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
