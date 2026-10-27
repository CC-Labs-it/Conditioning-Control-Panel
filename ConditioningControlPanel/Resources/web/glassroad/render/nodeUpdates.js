/* Node updates: three r186's per-draw node refresh without a WeakMap lookup per shared node (perf, 2026-10-27).
 *
 * Every draw runs NodeFrame.updateNode() over its material graph's update nodes (~7400 a frame on GOONWORKS high).
 * About 3300 of them are shared 'render'/'frame' nodes (camera, lights, time) that update once per render call and
 * are then answered from `updateMap`, a WeakMap keyed by the node's update reference: one WeakMap.get per node per
 * draw, which Firefox makes slow. installNodeUpdates() swaps NodeFrame.prototype.updateNode for the same logic that
 * also remembers each reference's record on the reference itself (a symbol property, owned by one NodeFrame; the
 * WeakMap stays the record of truth and is filled exactly as before). A reference that cannot take a property, or
 * whose remembered record belongs to another NodeFrame, takes three's own lookup. Same calls, same order, same ids.
 * Keep aligned with the vendored NodeFrame (updateNode, _getMaps) when upgrading. */
const REC = Symbol('glassroad.nodeUpdateRecord');
const FRAME = 'frame', RENDER = 'render', OBJECT = 'object';     // r186 NodeUpdateType values
let stock = null;                                                 // { proto, updateNode }

function recordOf(frame, reference) {
  if (reference !== null && (typeof reference === 'object' || typeof reference === 'function')) {
    const own = reference[REC];
    if (own !== undefined && own.frame === frame && own.ref === reference) return own.maps;
    const maps = frame._getMaps(frame.updateMap, reference);
    if (own !== undefined && own.ref === reference) { own.frame = frame; own.maps = maps; }
    // never enumerable: a spread or Object.assign copy of the reference (a userData record) must not share it
    else if (Object.isExtensible(reference) && !Object.prototype.hasOwnProperty.call(reference, REC)) Object.defineProperty(reference, REC, { value: { ref: reference, frame, maps }, writable: true, configurable: true, enumerable: false });
    return maps;
  }
  return frame._getMaps(frame.updateMap, reference);
}

export function fastUpdateNode(node) {
  const updateType = node.getUpdateType(this);
  const reference = node.updateReference(this);
  if (updateType === FRAME) {
    const maps = recordOf(this, reference);
    if (maps.frameId !== this.frameId && node.update(this) !== false) maps.frameId = this.frameId;
  } else if (updateType === RENDER) {
    const maps = recordOf(this, reference);
    if (maps.renderId !== this.renderId && node.update(this) !== false) maps.renderId = this.renderId;
  } else if (updateType === OBJECT) {
    node.update(this);
  }
}

/** Install once on NodeFrame.prototype, found from the renderer's own node frame (or any NodeFrame instance). */
export function installNodeUpdates(three) {
  const frame = three && (three.isNodeFrame === true || typeof three.updateNode === 'function' ? three : three._nodes && three._nodes.nodeFrame);
  let proto = frame ? Object.getPrototypeOf(frame) : null;
  while (proto && !Object.prototype.hasOwnProperty.call(proto, 'updateNode')) proto = Object.getPrototypeOf(proto);
  if (stock !== null || !proto || typeof proto.updateNode !== 'function' || typeof proto._getMaps !== 'function') return false;
  stock = { proto, updateNode: proto.updateNode };
  proto.updateNode = fastUpdateNode;
  return true;
}

/** Tests: put three's own updateNode back. */
export function restoreNodeUpdates() {
  if (stock !== null) { stock.proto.updateNode = stock.updateNode; stock = null; }
}
