// render/instancePrograms.js - one vertex program per instanced material.
// r186 keeps a small InstancedMesh's matrices in a uniform buffer NAMED BY NODE ID when they fit the backend's
// uniform-buffer limit, so every instanced mesh of the same material still got its own vertex program (chain
// links, cups, glints, props: about 280 of the 577 vertex programs at Threshold, each one a pipeline build at
// warm). With the limit reported as 0 every instanced mesh takes the interleaved instance-attribute path
// (identical source, one program per material); InstanceNode syncs instanceMatrix versions into that buffer
// on both backends. Skinned meshes take the bone texture path by the same rule.
export function shareInstancePrograms(three) {
  const caps = three && three.backend && three.backend.capabilities;
  if (caps) caps.getUniformBufferLimit = () => 0;
}
