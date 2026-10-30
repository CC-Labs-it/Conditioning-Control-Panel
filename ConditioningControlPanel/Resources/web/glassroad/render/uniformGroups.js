/* Uniform groups: the same per-draw value check as three r186, without its per-uniform dispatch (perf, 2026-10-27).
 *
 * Every draw of every pass runs UniformsGroup.update() on its object group and its shared groups: ~6000 uniforms a
 * frame on GOONWORKS high. Stock three asks each uniform up to seven `isXUniform` questions, then calls getValue(),
 * getType() and the `values` getter again per uniform; on Firefox that dispatch (updateByType) cost about 1 ms a
 * frame. fastUniformGroupUpdates() swaps UniformsGroup.prototype.update for a loop that learns each group's uniform
 * kinds and offsets once (re-learned whenever the group's uniform list changes) and reads node uniform values
 * directly. It compares and writes exactly what stock does (the `values` shadow, the buffer or its int/uint view,
 * addUniformUpdateRange for each changed uniform), so the same bytes reach the GPU. Uniforms that are not node
 * uniforms, and kinds stock does not handle here, still go through three's own updateByType.
 * Keep aligned with the vendored UniformsGroup (update, updateByType, update<Kind>, _getBufferForType) when upgrading. */
const KINDS = Symbol('glassroad.uniformKinds');
const NUM = 1, V2 = 2, V3 = 3, V4 = 4, COLOR = 5, M3 = 6, M4 = 7;
let installed = null;

function kindOf(u) {
  if (!u || !u.nodeUniform || u.nodeUniform.isNodeUniform !== true) return 0;
  if (u.isNumberUniform) return NUM;
  if (u.isVector2Uniform) return V2;
  if (u.isVector3Uniform) return V3;
  if (u.isVector4Uniform) return V4;
  if (u.isColorUniform) return COLOR;
  if (u.isMatrix3Uniform) return M3;
  if (u.isMatrix4Uniform) return M4;
  return 0;
}

function learn(group, uniforms) {
  const n = uniforms.length, info = { list: uniforms.slice(), kinds: new Uint8Array(n), offs: new Int32Array(n), nus: new Array(n) };
  for (let i = 0; i < n; i++) {
    const u = uniforms[i];
    info.kinds[i] = kindOf(u); info.offs[i] = u.offset; info.nus[i] = u.nodeUniform;
  }
  group[KINDS] = info;
  return info;
}

export function fastGroupUpdate() {
  const uniforms = this.uniforms, n = uniforms.length;
  const a = this.values;                     // also lays out the buffer (offsets) the first time
  let info = this[KINDS];
  if (info === undefined || info.list.length !== n) info = learn(this, uniforms);
  let updated = false;
  for (let i = 0; i < n; i++) {
    const u = uniforms[i];
    if (info.list[i] !== u) { info = learn(this, uniforms); i = -1; continue; }
    const kind = info.kinds[i];
    if (kind === 0) { if (this.updateByType(u) === true) updated = true; continue; }
    const o = info.offs[i], v = info.nus[i].value;
    switch (kind) {
      case NUM:
        if (a[o] !== v) { const b = this._getBufferForType(info.nus[i].type); b[o] = a[o] = v; updated = true; this.addUniformUpdateRange(u); }
        break;
      case V2:
        if (a[o] !== v.x || a[o + 1] !== v.y) {
          const b = this._getBufferForType(info.nus[i].type);
          b[o] = a[o] = v.x; b[o + 1] = a[o + 1] = v.y; updated = true; this.addUniformUpdateRange(u);
        }
        break;
      case V3:
        if (a[o] !== v.x || a[o + 1] !== v.y || a[o + 2] !== v.z) {
          const b = this._getBufferForType(info.nus[i].type);
          b[o] = a[o] = v.x; b[o + 1] = a[o + 1] = v.y; b[o + 2] = a[o + 2] = v.z; updated = true; this.addUniformUpdateRange(u);
        }
        break;
      case V4:
        if (a[o] !== v.x || a[o + 1] !== v.y || a[o + 2] !== v.z || a[o + 3] !== v.w) {
          const b = this._getBufferForType(info.nus[i].type);
          b[o] = a[o] = v.x; b[o + 1] = a[o + 1] = v.y; b[o + 2] = a[o + 2] = v.z; b[o + 3] = a[o + 3] = v.w;
          updated = true; this.addUniformUpdateRange(u);
        }
        break;
      case COLOR:
        if (a[o] !== v.r || a[o + 1] !== v.g || a[o + 2] !== v.b) {
          const b = this.buffer;
          b[o] = a[o] = v.r; b[o + 1] = a[o + 1] = v.g; b[o + 2] = a[o + 2] = v.b; updated = true; this.addUniformUpdateRange(u);
        }
        break;
      case M3: {
        const e = v.elements;
        if (a[o] !== e[0] || a[o + 1] !== e[1] || a[o + 2] !== e[2] || a[o + 4] !== e[3] || a[o + 5] !== e[4] || a[o + 6] !== e[5]
          || a[o + 8] !== e[6] || a[o + 9] !== e[7] || a[o + 10] !== e[8]) {
          const b = this.buffer;
          b[o] = a[o] = e[0]; b[o + 1] = a[o + 1] = e[1]; b[o + 2] = a[o + 2] = e[2];
          b[o + 4] = a[o + 4] = e[3]; b[o + 5] = a[o + 5] = e[4]; b[o + 6] = a[o + 6] = e[5];
          b[o + 8] = a[o + 8] = e[6]; b[o + 9] = a[o + 9] = e[7]; b[o + 10] = a[o + 10] = e[8];
          updated = true; this.addUniformUpdateRange(u);
        }
        break;
      }
      case M4: {
        const e = v.elements, l = e.length;
        let same = true;
        for (let k = 0; k < l; k++) if (a[o + k] !== e[k]) { same = false; break; }
        if (!same) {
          this.buffer.set(e, o);
          for (let k = 0; k < l; k++) a[o + k] = e[k];
          updated = true; this.addUniformUpdateRange(u);
        }
        break;
      }
    }
  }
  return updated;
}

/** The prototype that owns three's UniformsGroup.update (found from a live group), or null. */
function groupProto(group) {
  for (let p = group && Object.getPrototypeOf(group); p && p !== Object.prototype; p = Object.getPrototypeOf(p)) {
    if (Object.prototype.hasOwnProperty.call(p, 'updateByType') && Object.prototype.hasOwnProperty.call(p, 'update')
      && typeof p.updateMatrix4 === 'function' && typeof p.addUniformUpdateRange === 'function') return p;
  }
  return null;
}

/** Swap UniformsGroup.prototype.update once the first uniform group is seen. `three` is the WebGPURenderer. */
export function fastUniformGroupUpdates(three) {
  const bindings = three && three._bindings;
  if (!bindings || typeof bindings._update !== 'function' || bindings[KINDS]) return false;
  bindings[KINDS] = true;
  const update = bindings._update;
  bindings._update = function(bindGroup, all) {
    if (installed === null) {
      for (const b of bindGroup.bindings) {
        if (b.isUniformsGroup !== true) continue;
        const proto = groupProto(b);
        if (proto) { installed = { proto, stock: proto.update }; proto.update = fastGroupUpdate; }
        break;
      }
    }
    if (installed !== null) bindings._update = update;     // learned: three's own method from here on
    return update.call(this, bindGroup, all);
  };
  return true;
}

/** Tests: put three's own update back. */
export function restoreUniformGroupUpdates() {
  if (installed) { installed.proto.update = installed.stock; installed = null; }
}

/* Group versions (perf, 2026-10-27): NodeManager.updateGroup looks every shared uniform group up in a two-level
 * ChainMap (two WeakMap gets) on every draw, ~500 times a frame on GOONWORKS high, to compare the group node's
 * version. cacheGroupVersions() keeps the very record that ChainMap holds on the binding itself (non-enumerable, and
 * checked against its owner, its group node and the current ChainMap, so a Binding.clone() copy or a reset map falls
 * back to three's lookup). Same record, same version rule, same answers. Keep aligned with NodeManager.updateGroup. */
const GROUP_REC = Symbol('glassroad.groupVersion');
const keys = [null, null];

export function cacheGroupVersions(three) {
  const nodes = three && three._nodes;
  if (!nodes || typeof nodes.updateGroup !== 'function' || nodes[GROUP_REC]) return false;
  Object.defineProperty(nodes, GROUP_REC, { value: true });
  const stock = nodes.updateGroup;
  nodes.updateGroup = function(binding) {
    const groupNode = binding.groupNode;
    if (!groupNode || groupNode.updateType === 'object') return stock.call(this, binding);
    const map = this.groupsData;
    let rec = binding[GROUP_REC], data;
    if (rec !== undefined && rec.owner === binding && rec.map === map && rec.groupNode === groupNode) data = rec.data;
    else {
      keys[0] = groupNode; keys[1] = binding;
      data = map.get(keys);
      if (data === undefined) map.set(keys, data = {});
      keys[0] = null; keys[1] = null;
      if (rec !== undefined && rec.owner === binding) { rec.map = map; rec.groupNode = groupNode; rec.data = data; }
      else if (Object.isExtensible(binding) && !Object.prototype.hasOwnProperty.call(binding, GROUP_REC)) {
        Object.defineProperty(binding, GROUP_REC, { value: { owner: binding, map, groupNode, data }, writable: true, configurable: true, enumerable: false });
      }
    }
    if (data.version !== groupNode.version) { data.version = groupNode.version; return true; }
    return false;
  };
  return true;
}
