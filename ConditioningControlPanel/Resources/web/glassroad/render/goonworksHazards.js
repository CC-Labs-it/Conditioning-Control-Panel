// Chunky comic-book toys. Every moving transform comes from the collision pose authority.
import { Group, Mesh, BoxGeometry, CylinderGeometry, SphereGeometry, Shape, ExtrudeGeometry,
  MeshBasicNodeMaterial, Matrix4, Vector3, BackSide } from 'three/webgpu';
import { vec3, normalWorld, normalize, dot, clamp, floor } from 'three/tsl';
import { goonworksSetpieces, goonworksPose } from '../track/goonworks-setpieces.js';

export function createGoonworksHazardView(scene, track, layout) {
  const group = new Group(); group.name = 'goonworks-toys'; scene.add(group);
  const rows = goonworksSetpieces(track, layout), geometries = [], materials = [], entries = [];
  const own = geometry => { geometries.push(geometry); return geometry; };
  const box = own(new BoxGeometry()), cylinder = own(new CylinderGeometry(1, 1, 1, 24));
  const sphere = own(new SphereGeometry(1, 12, 8));
  const shade = floor(clamp(dot(normalize(normalWorld), normalize(vec3(-.4, .85, .3))).mul(.5).add(.5), 0, 1).mul(3)).div(3).mul(.24).add(.76);
  function material(hex, flat = false) {
    const m = new MeshBasicNodeMaterial({ color: hex });
    m.toneMapped = false;
    if (!flat) m.colorNode = vec3(m.color.r, m.color.g, m.color.b).mul(shade);
    materials.push(m); return m;
  }
  const ink = material('#1A061C', true), white = material('#FFF9FF'), pink = material('#BD006E');
  const outline = material('#1A061C', true); outline.side = BackSide;
  const candy = material('#FF5CB7');
  const basis = new Matrix4(), axes = [new Vector3(), new Vector3(), new Vector3()];
  function mesh(parent, geo, mat, p = [0, 0, 0], size = [1, 1, 1]) {
    const m = new Mesh(geo, mat); m.position.set(...p); m.scale.set(...size); m.castShadow = true; parent.add(m); return m;
  }
  function cube(parent, mat, p, size, edge = .12) {
    const root = new Group(); root.position.set(...p); parent.add(root);
    mesh(root, box, outline, [0, 0, 0], size.map(v => v + edge * 2)); mesh(root, box, mat, [0, 0, .015], size);
    return root;
  }
  function discSector(radius, gap, depth, inner = 0) {
    const s = new Shape(), a = gap / 2, b = Math.PI * 2 - gap / 2;
    s.moveTo(Math.cos(a) * inner, Math.sin(a) * inner);
    s.lineTo(Math.cos(a) * radius, Math.sin(a) * radius); s.absarc(0, 0, radius, a, b, false);
    if (inner > 0) { s.lineTo(Math.cos(b) * inner, Math.sin(b) * inner); s.absarc(0, 0, inner, b, a, true); }
    else s.lineTo(0, 0);
    s.closePath();
    return own(new ExtrudeGeometry(s, { depth, bevelEnabled: false, curveSegments: 64, steps: 1 }).translate(0, 0, -depth / 2));
  }
  function curvedArrow(radius, start, end, width, depth) {
    const s = new Shape(), steps = 16, angle = end - .13;
    s.moveTo(Math.cos(start) * (radius - width), Math.sin(start) * (radius - width));
    for (let i = 1; i <= steps; i++) { const a = start + (angle - start) * i / steps; s.lineTo(Math.cos(a) * (radius - width), Math.sin(a) * (radius - width)); }
    s.lineTo(Math.cos(angle) * (radius - width * 2.8), Math.sin(angle) * (radius - width * 2.8));
    s.lineTo(Math.cos(end) * radius, Math.sin(end) * radius);
    s.lineTo(Math.cos(angle) * (radius + width * 2.8), Math.sin(angle) * (radius + width * 2.8));
    for (let i = steps; i >= 0; i--) { const a = start + (angle - start) * i / steps; s.lineTo(Math.cos(a) * (radius + width), Math.sin(a) * (radius + width)); }
    s.closePath();
    return own(new ExtrudeGeometry(s, { depth, bevelEnabled: false, steps: 1 }).translate(0, 0, -depth / 2));
  }
  const riseMark = new Shape();
  riseMark.moveTo(-1, -.08); riseMark.lineTo(0, .17); riseMark.lineTo(1, -.08);
  riseMark.lineTo(1, .07); riseMark.lineTo(0, .32); riseMark.lineTo(-1, .07); riseMark.closePath();
  const riseGeometry = own(new ExtrudeGeometry(riseMark, { depth: .035, bevelEnabled: false, steps: 1 }));
  for (const row of rows) {
    const root = new Group(); root.name = row.id; root.position.fromArray(row.p);
    basis.makeBasis(axes[0].fromArray(row.b), axes[1].fromArray(row.n), axes[2].fromArray(row.t).negate());
    root.quaternion.setFromRotationMatrix(basis); group.add(root);
    const dots = [], lampMaterial = material('#97FFE0', true), entry = { row, root, dots, lampMaterial };
    for (const x of [-1, 0, 1]) {
      const dot = mesh(root, cylinder, ink, [row.x + x * 1.25, .07, row.depth / 2 + 4], [.45, .1, .45]);
      const lamp = mesh(root, cylinder, lampMaterial, [row.x + x * 1.25, .13, row.depth / 2 + 4], [.31, .04, .31]);
      dots.push({ dot, lamp });
    }
    if (row.kind === 'wheel') {
      const rotor = new Group(); rotor.position.set(row.x, row.centerY, 0); root.add(rotor); entry.rotor = rotor;
      mesh(rotor, discSector(row.radius, row.gap, row.depth), ink);
      mesh(rotor, discSector(row.radius - .2, row.gap + .025, row.depth + .025), white);
      mesh(rotor, discSector(row.radius - .75, row.gap + .08, row.depth + .055, row.radius - 2.15), pink);
      // Broad curved arrows make the wheel's direction and open sector readable while approaching.
      for (let i = 0; i < 3; i++) {
        const a = row.gap / 2 + .28 + i * 1.15, b = a + .81;
        mesh(rotor, curvedArrow(row.radius * .64, a, b, .16, row.depth + .12), ink);
        mesh(rotor, curvedArrow(row.radius * .64, a + .025, b - .025, .105, row.depth + .15), pink);
      }
      for (let i = 0; i < 9; i++) {
        const a = row.gap / 2 + .16 + i * (Math.PI * 2 - row.gap - .32) / 8;
        for (const side of [-1, 1]) {
          const bolt = mesh(rotor, cylinder, ink, [Math.cos(a) * (row.radius - 1.43), Math.sin(a) * (row.radius - 1.43), side * (row.depth / 2 + .08)], [.25, .18, .25]);
          bolt.rotation.x = Math.PI / 2;
        }
      }
      // The hub stays above traffic and does not bridge the road-level missing sector.
      const hub = mesh(root, cylinder, ink, [row.x, row.centerY, 0], [1.65, row.depth + .45, 1.65]); hub.rotation.x = Math.PI / 2;
      const cap = mesh(root, cylinder, candy, [row.x, row.centerY, row.depth / 2 + .35], [1.35, .25, 1.35]); cap.rotation.x = Math.PI / 2;
      cube(root, pink, [row.x, row.centerY + row.radius + 2.2, 0], [row.roadWidth + 4, 1.3, 3]);
      for (const side of [-1, 1]) {
        cube(root, white, [side * (row.roadWidth / 2 + 3), 16, 0], [1.8, 31, 3]);
        mesh(root, sphere, candy, [side * row.bypass, .24, row.depth / 2 + 6], [.48, .16, .8]);
      }
    } else if (row.kind === 'shutter') {
      cube(root, ink, [0, row.h + 1.7, 0], [row.roadWidth + 1, .7, 2.4]);
      const screen = new Group(); root.add(screen); entry.screen = screen;
      cube(screen, white, [0, row.h / 2, 0], [row.w, row.h, row.depth]);
      cube(screen, pink, [0, row.h / 2, row.depth / 2 + .09], [row.w - .7, row.h - .7, .13], .08);
      // A bold concentric eye moves with the physical screen, readable from either approach.
      for (const side of [-1, 1]) for (let i = 0; i < 4; i++) {
        const eye = mesh(screen, cylinder, i % 2 ? white : ink, [0, row.h / 2, side * (row.depth / 2 + .25 + i * .025)], [2.8 - i * .62, .035, 2.8 - i * .62]);
        eye.rotation.x = Math.PI / 2;
      }
      cube(screen, white, [0, row.h + .5, 0], [.8, 1, 1]);
    } else if (row.kind === 'block') {
      cube(root, ink, [row.x, .015, 0], [row.w + .5, .1, row.depth + .5], .03);
      const block = new Group(); block.position.x = row.x; root.add(block); entry.block = block;
      mesh(block, box, outline, [0, .5, 0], [row.w, 1, row.depth]);
      mesh(block, box, (Number(row.id.at(-1)) % 2) ? white : pink, [0, .5, .015], [row.w - .25, .96, row.depth - .25]);
      // Two bold upward marks identify a rising obstacle even without its warning colour.
      for (const y of [.3, .58]) mesh(block, riseGeometry, ink, [0, y, row.depth / 2 + .025], [1.45, .65, 1]);
      for (const side of [-1, 1]) mesh(block, box, candy, [side * (row.w / 2 - .35), .5, 0], [.22, .92, row.depth - .45]);
      entry.crown = cube(root, white, [row.x, .16, 0], [row.w + .05, .18, row.depth + .05], .08);
      for (const side of [-1, 1]) mesh(entry.crown, box, lampMaterial, [0, .115, side * (row.depth / 2 - .45)], [row.w - .65, .035, .55]);
    } else {
      cube(root, white, [row.x, .12, 0], [row.w + 1, .2, row.depth + 1]);
      const flipper = new Group(); flipper.position.set(row.x, .28, row.depth / 2); root.add(flipper); entry.flipper = flipper;
      cube(flipper, pink, [0, 0, -row.depth / 2], [row.w, .45, row.depth], .14);
      for (let i = 0; i < 4; i++) cube(flipper, white, [0, .26, -2 - i * 2.5], [row.w - 1, .04, .55], .025);
      for (const side of [-1, 1]) {
        const hinge = mesh(root, cylinder, candy, [side * (row.w / 2 + .5), .5, row.depth / 2], [.8, 1.5, .8]); hinge.rotation.z = Math.PI / 2;
      }
    }
    entries.push(entry);
  }
  function update(timeS = 0) {
    for (const e of entries) {
      const p = goonworksPose(e.row, timeS);
      if (e.rotor) e.rotor.rotation.z = p.angle;
      if (e.screen) e.screen.position.x = p.x;
      if (e.block) { e.block.scale.y = Math.max(.035, p.height); e.crown.position.y = p.height + .16; }
      if (e.flipper) e.flipper.rotation.x = p.angle;
      e.lampMaterial.color.set(p.warning ? '#FFD45C' : p.active && p.phase !== 'open' ? '#FF5CB7' : '#97FFE0');
    }
  }
  update(0); let disposed = false;
  return { group, update, reset: () => update(0), inspect: () => ({ count: entries.length, wheels: entries.filter(e => e.rotor).length }),
    dispose() { if (disposed) return; disposed = true; group.removeFromParent(); geometries.forEach(g => g.dispose()); materials.forEach(m => m.dispose()); } };
}
