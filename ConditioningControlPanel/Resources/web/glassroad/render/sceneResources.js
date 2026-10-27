/** r186 retains render variants for each light/context until their object or material is disposed.
 * Pooled racers survive a course, so explicitly retire only their old scene variants. Node-only
 * attributes also need an owner: Sprite's global geometry cannot release each course's arrays.
 * Keep this adapter aligned with the vendored renderer when upgrading three.js.
 */
const histories = new WeakMap();
// Per draw fast path (Firefox, 2026-10-27): a render variant remembers the attribute list it was folded with and the
// release epoch; any course release (records deleted) bumps the epoch, so the next draw folds the variant again.
const FOLDED = Symbol('glassroad.folded'), FOLDED_EPOCH = Symbol('glassroad.foldedEpoch');
const epochs = new WeakMap();                  // three -> { n }

/** A warm-up can replace a render variant before course teardown. Preserve ownership of
 * every node-only array it uploaded, including arrays absent from the final render variant. */
export function trackSceneResources(three) {
  if (!three?._geometries || histories.has(three)) return;
  const records = new Map(), geometries = three._geometries, update = geometries.updateAttributes;
  histories.set(three, records);
  const epoch = epochs.get(three) || { n: 0 };
  epoch.n++; epochs.set(three, epoch);         // a fresh history never trusts an older fold
  // This runs for every draw of every pass. A render variant keeps its scene and attribute list until three
  // rebuilds the list (RenderObject.setGeometry and the geometry dispose listener null it), so a variant
  // already folded into its object's record skips the Set work (~0.3 ms a frame on GOONWORKS, 2026-10-26).
  geometries.updateAttributes = function(row) {
    const result = update.call(this, row);
    const list = row.getAttributes();
    if (row[FOLDED] === list && row[FOLDED_EPOCH] === epoch.n) return result;
    let record = records.get(row.object);
    if (!record) records.set(row.object, record = { scenes: new Set(), attributes: new Set(), seen: new WeakMap() });
    // The mark is the list itself (never the scene or geometry), so it can never pin a departed course.
    const attributes = list;
    if (record.seen.get(row) === attributes && record.scenes.has(row.scene)) { row[FOLDED] = attributes; row[FOLDED_EPOCH] = epoch.n; return result; }
    record.scenes.add(row.scene);
    const own = new Set(Object.values(row.geometry.attributes));
    for (const attribute of attributes) if (!own.has(attribute)) record.attributes.add(attribute);
    record.seen.set(row, attributes);
    row[FOLDED] = attributes; row[FOLDED_EPOCH] = epoch.n;
    return result;
  };
}

export function disposeSceneResources(three) {
  histories.get(three)?.clear(); histories.delete(three);
  const epoch = epochs.get(three); if (epoch) epoch.n++;
}

/** r186 Geometries.initGeometry closes its per-geometry dispose listener over the FIRST render variant
 * that uploaded the geometry. A geometry shared across courses (pooled racers, library models, Sprite's
 * quad) therefore pins that variant until the geometry itself is disposed, and the variant's scene pins
 * the whole departed course (about 110 MB of heap per switch, measured on the phone tier). Cut every
 * course reference from a retired variant; keep the geometry and attribute fields the listener reads. */
export function severRow(three, row) {
  try { three.backend?.delete?.(row); } catch (e) { /* no backend data for this variant */ }
  // getAttributes() rebuilds from the node builder state when attributes are null; keep that path whole
  if (row.attributes || row._nodeBuilderState) { row.object = null; row.material = null; row._sourceMaterial = null; }
  // the builder state's update nodes hold uniform callbacks bound into the course; once the attribute list is cached it is unused
  if (row.attributes) row._nodeBuilderState = null;
  row.scene = null; row.camera = null; row.lightsNode = null; row.context = null; row.clippingContext = null;
  row.pipeline = null; row.bundle = null; row._bindings = null; row._monitor = null;
}

/** Take a departed course's scene tree apart: every node loses its parent and its children, so a stray
 * reference to any one object (a three cache keyed by string, a dispose listener, a cached Error's stack)
 * keeps that object alone and never the whole course. Pooled racers leave the scene before this runs. */
export function dismantleScene(root) {
  const all = [];
  root?.traverse?.(o => all.push(o));
  for (const o of all) { o.children.length = 0; o.parent = null; }
  return all.length;
}

export function releaseSceneResources(three, scene, beforeRetire = () => {}) {
  const objects = three?._objects?._renderObjects;
  if (!objects) { beforeRetire(); return; }
  const retired = [], candidates = new Set(), retained = new Set();
  const members = new Set();
  scene.traverse?.(object => members.add(object));
  const identity = attribute => attribute.isInterleavedBufferAttribute ? attribute.data : attribute;
  const records = histories.get(three);
  const epoch = epochs.get(three); if (epoch) epoch.n++;      // every folded variant re-checks its record
  if (records) for (const [object, record] of records) {
    if (members.has(object) || record.scenes.has(scene)) {
      for (const attribute of record.attributes) candidates.add(attribute);
      records.delete(object);
    } else for (const attribute of record.attributes) retained.add(identity(attribute));
  }
  for (const row of objects) {
    // Shadow and reflection passes may use their own scene while drawing the course's objects.
    const retiring = row.scene === scene || members.has(row.object);
    if (retiring) retired.push(row);
    if (!row.attributes && !row._nodeBuilderState) continue;
    const attributes = row.getAttributes();
    if (!retiring) {
      for (const attribute of attributes) retained.add(identity(attribute));
    } else {
      const geometryAttributes = new Set(Object.values(row.geometry.attributes));
      for (const attribute of attributes) if (!geometryAttributes.has(attribute)) candidates.add(attribute);
    }
  }
  // Light disposal removes shadow render variants. Capture their node arrays first,
  // then release targets while light listeners still exist, and retire remaining bindings.
  beforeRetire();
  for (const row of retired) if (objects.has(row)) { row.dispose(); severRow(three, row); }
  for (const attribute of candidates) {
    const buffer = identity(attribute);
    // Each interleaved view has its own r186 attribute/version/memory entry even when
    // views share one GPU buffer. Retire every view; buffer destruction is idempotent.
    if (!retained.has(buffer)) three._attributes.delete(attribute);
  }
}
