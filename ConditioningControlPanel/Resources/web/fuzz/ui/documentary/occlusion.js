/** Exact double-sided triangle queries, accelerated by a static bounding tree. */
export function createOcclusionQuery(THREE, geometry) {
  const positions = geometry.attributes.position, index = geometry.index;
  const vertex = offset => index ? index.getX(offset) : offset;
  const a = new THREE.Vector3(), b = new THREE.Vector3(), c = new THREE.Vector3();
  const hit = new THREE.Vector3();
  const triangles = [];
  for (let offset = 0; offset < (index?.count ?? positions.count); offset += 3) {
    a.fromBufferAttribute(positions, vertex(offset));
    b.fromBufferAttribute(positions, vertex(offset + 1));
    c.fromBufferAttribute(positions, vertex(offset + 2));
    const box = new THREE.Box3().setFromPoints([a, b, c]);
    triangles.push({ offset, box });
  }
  function build(items) {
    const box = new THREE.Box3();
    for (const item of items) box.union(item.box);
    if (items.length <= 16) return { box, items };
    const size = box.getSize(new THREE.Vector3());
    const axis = size.x >= size.y && size.x >= size.z ? 'x' : size.y >= size.z ? 'y' : 'z';
    items.sort((x, y) => (x.box.min[axis] + x.box.max[axis]) - (y.box.min[axis] + y.box.max[axis]));
    const middle = items.length >> 1;
    return { box, left: build(items.slice(0, middle)), right: build(items.slice(middle)) };
  }
  const root = build(triangles);
  function query(node, ray, near, far) {
    if (!ray.intersectBox(node.box, hit)) return false;
    // When the origin is inside the box its exit can be beyond the target.
    if (!node.box.containsPoint(ray.origin) && hit.distanceToSquared(ray.origin) > far * far) return false;
    if (!node.items) return query(node.left, ray, near, far) || query(node.right, ray, near, far);
    for (const { offset } of node.items) {
      a.fromBufferAttribute(positions, vertex(offset));
      b.fromBufferAttribute(positions, vertex(offset + 1));
      c.fromBufferAttribute(positions, vertex(offset + 2));
      if (ray.intersectTriangle(a, b, c, false, hit)) {
        const distance = hit.distanceTo(ray.origin);
        if (distance >= near && distance <= far) return true;
      }
    }
    return false;
  }
  return (ray, near, far) => far >= near && query(root, ray, near, far);
}
