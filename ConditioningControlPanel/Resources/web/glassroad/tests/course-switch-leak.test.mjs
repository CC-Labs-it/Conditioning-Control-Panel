/* tests/course-switch-leak.test.mjs - a course switch must not keep the departed course in memory.
 *
 * Measured on the phone tier before these rules: every switch kept the whole previous world (about
 * 110 MB of JS heap per switch after a forced GC), which on a phone ends in a tab kill ("the page
 * reloads on switch"). Four retainers, each pinning the old scene through one object's parent chain:
 *   1. feel.get's cached missing-key Error kept V8's unformatted stack (every frame's receiver);
 *   2. an art lease's progress callback closed over boot's scope (world) after release;
 *   3. r186 Geometries' per-geometry dispose listener closes over the first render variant that
 *      uploaded it, and the variant's scene / builder state reach the course;
 *   4. r186's shared bind group cache (keyed by string) keeps light uniform nodes, light.parent = scene.
 * The scene is dismantled on dispose so any stray reference keeps one object, never the course.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel, stacklessError } from '../core/feel.js';
import { severRow, dismantleScene } from '../render/sceneResources.js';

const src = (p) => readFileSync(new URL('../' + p, import.meta.url), 'utf8');

test('a stackless Error carries no frames and restores the stack limit', () => {
  const before = Error.stackTraceLimit;
  const e = stacklessError('x');
  assert.equal(Error.stackTraceLimit, before);
  assert.ok(e instanceof Error && e.message === 'x');
  assert.ok(!/\n\s+at /.test(String(e.stack || '')), 'no frames: ' + e.stack);
  assert.ok(/\n\s+at /.test(new Error('y').stack), 'ordinary errors still carry frames');
});

test('feel: a missing key throws one cached Error per path, with no stack frames', () => {
  const feel = createFeel({ a: { b: 1 } });
  let first = null, second = null;
  try { feel.get('a.missing'); } catch (e) { first = e; }
  try { feel.get('a.missing'); } catch (e) { second = e; }
  assert.ok(first && first === second, 'cached per path');
  assert.ok(!/\n\s+at /.test(String(first.stack || '')), 'no frames kept: ' + first.stack);
  assert.equal(feel.get('a.b'), 1);
});

test('severRow cuts every course reference from a retired render variant and keeps what the geometry listener reads', () => {
  const geometry = { attributes: {} }, deleted = [];
  const row = { object: { name: 'mesh' }, material: {}, _sourceMaterial: {}, scene: {}, camera: {}, lightsNode: {}, context: {},
    clippingContext: {}, pipeline: {}, bundle: {}, _bindings: [], _monitor: {}, _nodeBuilderState: { updateNodes: [] },
    geometry, attributes: [{ name: 'position' }] };
  severRow({ backend: { delete: (r) => deleted.push(r) } }, row);
  assert.deepEqual(deleted, [row]);
  for (const k of ['object', 'material', '_sourceMaterial', 'scene', 'camera', 'lightsNode', 'context', 'clippingContext', 'pipeline', 'bundle', '_bindings', '_monitor', '_nodeBuilderState'])
    assert.equal(row[k], null, k + ' cut');
  assert.equal(row.geometry, geometry);
  assert.equal(row.attributes.length, 1);
  // a variant that never cached its attribute list keeps the builder state getAttributes() rebuilds from
  const fresh = { object: {}, material: {}, scene: {}, geometry, attributes: null, _nodeBuilderState: { nodeAttributes: [] } };
  severRow({}, fresh);
  assert.equal(fresh.scene, null);
  assert.ok(fresh._nodeBuilderState && fresh.object === null && fresh.material === null);
  // no backend, no attributes, no builder state: still safe
  severRow(null, { object: {}, material: {}, scene: {}, geometry, attributes: null, _nodeBuilderState: null });
});

test('dismantleScene leaves every node of a departed course without parent or children', () => {
  const node = (name, children = []) => { const o = { name, children, parent: null, traverse(fn) { fn(o); for (const c of o.children) c.traverse(fn); } }; for (const c of children) c.parent = o; return o; };
  const leaf = node('leaf'), group = node('group', [leaf, node('other')]), scene = node('scene', [group, node('light')]);
  assert.equal(dismantleScene(scene), 5);
  assert.equal(leaf.parent, null);
  assert.equal(group.children.length, 0);
  assert.equal(scene.children.length, 0);
  assert.equal(dismantleScene(null), 0);
});

test('boot dismantles the course scene after the world disposes, and the release path severs retired variants', () => {
  const boot = src('boot.js');
  assert.match(boot, /import \{ dismantleScene \} from '\.\/render\/sceneResources\.js'/);
  assert.match(boot, /world\.dispose\(\);\r?\n\s*dismantleScene\(world\.scene\);/);
  const res = src('render/sceneResources.js');
  assert.match(res, /row\.dispose\(\); severRow\(three, row\);/);
});

test('an art lease drops its progress callback when released', () => {
  const art = src('render/art.js');
  assert.match(art, /released = true;[\s\S]{0,400}onProgress = null;/);
});
