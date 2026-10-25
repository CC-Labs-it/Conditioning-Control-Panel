/** r186 retains render variants for each light/context until their object or material is disposed.
 * Pooled racers survive a course, so explicitly retire only their old scene variants. Node-only
 * attributes also need an owner: Sprite's global geometry cannot release each course's arrays.
 * Keep this adapter aligned with the vendored renderer when upgrading three.js.
 */
const histories = new WeakMap();

/** A warm-up can replace a render variant before course teardown. Preserve ownership of
 * every node-only array it uploaded, including arrays absent from the final render variant. */
export function trackSceneResources(three) {
  if (!three?._geometries || histories.has(three)) return;
  const records = new Map(), geometries = three._geometries, update = geometries.updateAttributes;
  histories.set(three, records);
  geometries.updateAttributes = function(row) {
    const result = update.call(this, row);
    let record = records.get(row.object);
    if (!record) records.set(row.object, record = { scenes: new Set(), attributes: new Set() });
    record.scenes.add(row.scene);
    const own = new Set(Object.values(row.geometry.attributes));
    for (const attribute of row.getAttributes()) if (!own.has(attribute)) record.attributes.add(attribute);
    return result;
  };
}

export function disposeSceneResources(three) {
  histories.get(three)?.clear(); histories.delete(three);
}

export function releaseSceneResources(three, scene, beforeRetire = () => {}) {
  const objects = three?._objects?._renderObjects;
  if (!objects) { beforeRetire(); return; }
  const retired = [], candidates = new Set(), retained = new Set();
  const members = new Set();
  scene.traverse?.(object => members.add(object));
  const identity = attribute => attribute.isInterleavedBufferAttribute ? attribute.data : attribute;
  const records = histories.get(three);
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
  for (const row of retired) if (objects.has(row)) row.dispose();
  for (const attribute of candidates) {
    const buffer = identity(attribute);
    // Each interleaved view has its own r186 attribute/version/memory entry even when
    // views share one GPU buffer. Retire every view; buffer destruction is idempotent.
    if (!retained.has(buffer)) three._attributes.delete(attribute);
  }
}
