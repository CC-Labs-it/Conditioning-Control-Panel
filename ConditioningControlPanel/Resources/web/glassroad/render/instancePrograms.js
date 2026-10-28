// Share programs for instance matrices without changing skinning or unrelated buffers.
// A backend-wide zero also sends every skeleton through bone textures. Keep the
// device's real limit for those draws and change only InstancedMesh builders.
import { NodeBuilder } from 'three/webgpu';

const renderers = new WeakSet();
let installed = false;

export function shareInstancePrograms(three) {
  if (!three?.backend?.capabilities) return;
  renderers.add(three);
  if (installed) return;
  installed = true;
  const stock = NodeBuilder.prototype.getUniformBufferLimit;
  NodeBuilder.prototype.getUniformBufferLimit = function() {
    if (renderers.has(this.renderer) && this.object?.isInstancedMesh === true) return 0;
    return stock.call(this);
  };
}
