const TAU = Math.PI * 2;

// Local +X runs from the tail to the nose. The right fin stays level with the surface through each turn.
export function swimPose(motion, seconds, position, tangent) {
  const [length, rise, width, period, offset] = motion;
  const phase = seconds * TAU / period + offset;
  const s = Math.sin(phase), c = Math.cos(phase);
  position.set(length * s, rise * Math.sin(phase + .28), width * c);
  tangent.set(length * c, rise * Math.cos(phase + .28), -width * s).normalize();
}

export function mountAquariumLife(THREE, room, gentle) {
  room.updateWorldMatrix(true,true);
  const waterUp = new THREE.Vector3(0,1,0), across = new THREE.Vector3(), up = new THREE.Vector3();
  const forward = new THREE.Vector3(), offset = new THREE.Vector3(), position = new THREE.Vector3(), scale = new THREE.Vector3();
  const rotation = new THREE.Quaternion(), basis = new THREE.Matrix4(), placement = new THREE.Matrix4();
  const local = room.matrixWorld.clone().invert(), matrix = new THREE.Matrix4(), families = new Map(), bodies = [], old = [];
  const heading = angle => new THREE.Quaternion().setFromAxisAngle(waterUp,angle);
  room.traverse(node => {
    const info = node.userData;
    if (!Array.isArray(info.aquariumSwim) || info.aquariumSwim.length !== 5) return;
    // Earlier workshop files stored entire schools in one object.
    if (!info.aquariumOrigin) {
      const school = Number(node.name.split('_').at(-1));
      const authored = node.name.includes('_ray_') ? (school === 1 ? .28 : 2.89) : ((school-1)%3 ? 0 : Math.PI);
      old.push({node, rest:node.position.clone(), rotation:node.quaternion.clone(), inverseHeading:heading(authored).invert(), motion:info.aquariumSwim});
      return;
    }
    const body = {node, origin:new THREE.Vector3().fromArray(info.aquariumOrigin), heading:info.aquariumHeading,
      size:info.aquariumSize, motion:info.aquariumSwim, species:info.aquariumSpecies};
    bodies.push(body);
    if (!families.has(body.species)) families.set(body.species, {bodies:[],parts:[]});
    families.get(body.species).bodies.push(body);
  });
  for (const family of families.values()) {
    const first = family.bodies[0];
    const inverse = new THREE.Matrix4().compose(first.origin,heading(first.heading),scale.setScalar(first.size)).invert();
    first.node.traverse(part => {
      if (!part.isMesh) return;
      const mesh = new THREE.InstancedMesh(part.geometry,part.material,family.bodies.length);
      mesh.name = 'Swimming_'+first.species+'_'+part.name;
      mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
      // Schools span the whole tank and change position every frame.
      mesh.frustumCulled = false;
      room.add(mesh);
      family.parts.push({mesh,rest:inverse.clone().multiply(part.matrixWorld)});
    });
    for (const body of family.bodies) body.node.visible = false;
  }
  let seconds = 0;
  function orient(motion) {
    swimPose(motion,seconds,offset,forward);
    across.crossVectors(forward,waterUp).normalize();
    up.crossVectors(across,forward).normalize();
    rotation.setFromRotationMatrix(basis.makeBasis(forward,up,across));
  }
  function pose() {
    for (const family of families.values()) {
      family.bodies.forEach((body,index) => {
        orient(body.motion);
        position.copy(body.origin).add(offset);
        placement.compose(position,rotation,scale.setScalar(body.size)).premultiply(local);
        for (const {mesh,rest} of family.parts) mesh.setMatrixAt(index,matrix.copy(placement).multiply(rest));
      });
      for (const {mesh} of family.parts) mesh.instanceMatrix.needsUpdate = true;
    }
    for (const body of old) {
      orient(body.motion);
      body.node.position.copy(body.rest).add(offset);
      body.node.quaternion.copy(rotation).multiply(body.inverseHeading).multiply(body.rotation);
    }
  }
  if (bodies.length || !gentle) pose();
  return {
    tick(dt,calm) {
      if (calm || !dt) return;
      seconds += dt;
      pose();
    },
    dispose() {
      for (const {parts} of families.values()) for (const {mesh} of parts) { mesh.removeFromParent(); mesh.dispose(); }
      for (const body of bodies) body.node.visible = true;
      for (const body of old) { body.node.position.copy(body.rest); body.node.quaternion.copy(body.rotation); }
    },
  };
}
