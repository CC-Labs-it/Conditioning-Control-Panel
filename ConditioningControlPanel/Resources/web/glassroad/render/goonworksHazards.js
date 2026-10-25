// Chunky comic-book toys. Every moving transform comes from the collision pose authority.
import { Group, Mesh, BoxGeometry, CylinderGeometry, SphereGeometry, Shape, Path, ShapeGeometry, ShapeUtils, ExtrudeGeometry,
  MeshBasicNodeMaterial, Matrix4, Vector2, Vector3, BackSide } from 'three/webgpu';
import { vec3, normalWorld, normalize, dot, clamp, floor } from 'three/tsl';
import { goonworksSetpieces, goonworksPose } from '../track/goonworks-setpieces.js';
import { GOONWORKS_PALETTE, GOONWORKS_DISTRICT_PALETTES } from './goonworksMaterials.js';
import { machineryBeatAt } from '../track/machineryBeat.js';
import { createGearSpiralPaint } from './gearSpirals.js';

export function createGoonworksHazardView(scene, track, layout) {
  const group = new Group(); group.name = 'goonworks-toys'; scene.add(group);
  const rows = goonworksSetpieces(track, layout), geometries = [], materials = [], entries = [], spiralPaint = [];
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
  const ink = material(GOONWORKS_PALETTE.ink, true);
  const outline = material(GOONWORKS_PALETTE.ink, true); outline.side = BackSide;
  const paintCache = new Map();
  function paint(key) {
    if (!paintCache.has(key)) paintCache.set(key, material(GOONWORKS_PALETTE[key]));
    return paintCache.get(key);
  }
  const paints = Object.fromEntries([
    ['wheel', 'aperture-mill'], ['shutter', 'screen-grotto'], ['block', 'pop-quiz'], ['flipper', 'pinball-launch'],
  ].map(([kind, district]) => {
    const theme = GOONWORKS_DISTRICT_PALETTES[district];
    return [kind, { white: paint(theme.road), pink: paint(theme.pink), candy: paint(theme.accent) }];
  }));
  const basis = new Matrix4(), axes = [new Vector3(), new Vector3(), new Vector3()];
  function mesh(parent, geo, mat, p = [0, 0, 0], size = [1, 1, 1]) {
    const m = new Mesh(geo, mat); m.position.set(...p); m.scale.set(...size); m.castShadow = true; parent.add(m); return m;
  }
  function cube(parent, mat, p, size, edge = .12) {
    const root = new Group(); root.position.set(...p); parent.add(root);
    mesh(root, box, outline, [0, 0, 0], size.map(v => v + edge * 2)); mesh(root, box, mat, [0, 0, .015], size);
    return root;
  }
  function sectorShape(radius, a, b, inner = 0) {
    const s = new Shape();
    s.moveTo(Math.cos(a) * inner, Math.sin(a) * inner);
    s.lineTo(Math.cos(a) * radius, Math.sin(a) * radius); s.absarc(0, 0, radius, a, b, false);
    if (inner > 0) { s.lineTo(Math.cos(b) * inner, Math.sin(b) * inner); s.absarc(0, 0, inner, b, a, true); }
    else s.lineTo(0, 0);
    s.closePath();
    return s;
  }
  function curvedArrow(radius, start, end, width) {
    const s = new Shape(), steps = 16, angle = end - .13;
    s.moveTo(Math.cos(start) * (radius - width), Math.sin(start) * (radius - width));
    for (let i = 1; i <= steps; i++) { const a = start + (angle - start) * i / steps; s.lineTo(Math.cos(a) * (radius - width), Math.sin(a) * (radius - width)); }
    s.lineTo(Math.cos(angle) * (radius - width * 2.8), Math.sin(angle) * (radius - width * 2.8));
    s.lineTo(Math.cos(end) * radius, Math.sin(end) * radius);
    s.lineTo(Math.cos(angle) * (radius + width * 2.8), Math.sin(angle) * (radius + width * 2.8));
    for (let i = steps; i >= 0; i--) { const a = start + (angle - start) * i / steps; s.lineTo(Math.cos(a) * (radius + width), Math.sin(a) * (radius + width)); }
    s.closePath();
    return s;
  }
  function insetShape(shape, thickness) {
    const points = shape.getPoints(); if (points[0].equals(points.at(-1))) points.pop();
    const winding = ShapeUtils.isClockWise(points) ? -1 : 1;
    return new Shape(points.map((p, i) => {
      const incoming = p.clone().sub(points[(i + points.length - 1) % points.length]).normalize();
      const outgoing = points[(i + 1) % points.length].clone().sub(p).normalize();
      const a = new Vector2(-incoming.y, incoming.x).multiplyScalar(winding);
      const b = new Vector2(-outgoing.y, outgoing.x).multiplyScalar(winding);
      return p.clone().addScaledVector(a.clone().add(b), thickness / (1 + a.dot(b)));
    }));
  }
  function wheelSurface(rotor, row, { white, pink }) {
    const painted=white.clone(); materials.push(painted);
    spiralPaint.push(createGearSpiralPaint(painted,row.radius,Number(row.id.at(-1))));
    const hole = shape => new Path(shape.getPoints(64));
    const bodies = [], inkShapes = [], whiteShapes = [], pinkShapes = [];
    // Four separate solid spokes. Every empty interval stays cut through both caps and walls.
    const spacing = Math.PI * 2 / row.openings;
    for (let i = 0; i < row.openings; i++) {
      const a = i * spacing + row.gap / 2, b = (i + 1) * spacing - row.gap / 2;
      const body = sectorShape(row.radius, a, b);
      const face = sectorShape(row.radius - .2, a + .0125, b - .0125, .5);
      const band = sectorShape(row.radius - .75, a + .04, b - .04, row.radius - 2.15);
      const dark = body.clone(); dark.holes.push(hole(face));
      const light = face.clone(); light.holes.push(hole(band));
      const arrow = curvedArrow(row.radius * .64, a + .08, b - .08, .16);
      const inset = insetShape(arrow, .055);
      light.holes.push(hole(arrow)); arrow.holes.push(hole(inset));
      bodies.push(body); whiteShapes.push(light); inkShapes.push(dark, arrow); pinkShapes.push(band, inset);
    }
    // One surface at each depth: the ink, enamel and arrow triangles tile the face without
    // overlapping. Millimetre-separated closed discs z-fight at the rear wheel and at overlooks.
    for (const [name, shapes, mat] of [['ink', inkShapes, ink], ['white', whiteShapes, painted], ['pink', pinkShapes, pink]]) {
      const front = own(new ShapeGeometry(shapes, 64)), back = own(front.clone());
      const index = back.index.array, normals = back.getAttribute('normal');
      for (let i = 0; i < index.length; i += 3) [index[i + 1], index[i + 2]] = [index[i + 2], index[i + 1]];
      for (let i = 0; i < normals.count; i++) normals.setZ(i, -1);
      for (const [side, geometry] of [[1, front], [-1, back]]) {
        const cap = mesh(rotor, geometry, mat, [0, 0, side * row.depth / 2]);
        cap.name = `wheel-face-${name}-${side}`;
      }
    }
    // Retain the complete physical thickness and radial cut walls, but never submit its hidden
    // filled caps beneath the tiled faces. Ordinary depth testing and front-face culling stay on.
    for (const body of bodies) {
      const walls = own(new ExtrudeGeometry(body, { depth: row.depth, bevelEnabled: false, curveSegments: 64, steps: 1 }).translate(0, 0, -row.depth / 2));
      const sides = walls.groups.find(g => g.materialIndex === 1);
      walls.setDrawRange(sides.start, sides.count); walls.clearGroups();
      mesh(rotor, walls, ink).name = 'wheel-cut-walls';
    }
  }
  const riseMark = new Shape();
  riseMark.moveTo(-1, -.08); riseMark.lineTo(0, .17); riseMark.lineTo(1, -.08);
  riseMark.lineTo(1, .07); riseMark.lineTo(0, .32); riseMark.lineTo(-1, .07); riseMark.closePath();
  const riseGeometry = own(new ExtrudeGeometry(riseMark, { depth: .035, bevelEnabled: false, steps: 1 }));
  for (const row of rows) {
    const { white, pink, candy } = paints[row.kind];
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
      rotor.name = 'wheel-rotor'; wheelSurface(rotor, row, paints.wheel);
      for (let i = 0; i < row.openings; i++) {
        const a = (i + .5) * Math.PI * 2 / row.openings;
        for (const side of [-1, 1]) {
          const bolt = mesh(rotor, cylinder, candy, [Math.cos(a) * (row.radius - 1.43), Math.sin(a) * (row.radius - 1.43), side * (row.depth / 2 + .08)], [.25, .18, .25]);
          bolt.name = 'wheel-bolt'; bolt.rotation.x = Math.PI / 2;
        }
      }
      // The hub stays above traffic and does not bridge the road-level missing sector.
      const hub = mesh(root, cylinder, ink, [row.x, row.centerY, 0], [1.65, row.depth + .45, 1.65]); hub.rotation.x = Math.PI / 2;
      const cap = mesh(root, cylinder, candy, [row.x, row.centerY, row.depth / 2 + .35], [1.35, .25, 1.35]); cap.name = 'wheel-hub-cap'; cap.rotation.x = Math.PI / 2;
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
      const p = goonworksPose(e.row, timeS,machineryBeatAt(track,timeS));
      if (e.rotor) e.rotor.rotation.z = p.angle;
      if (e.screen) e.screen.position.x = p.x;
      if (e.block) { e.block.scale.y = Math.max(.035, p.height); e.crown.position.y = p.height + .16; }
      if (e.flipper) e.flipper.rotation.x = p.angle;
      e.lampMaterial.color.set(p.warning ? '#FFD45C' : p.active && p.phase !== 'open' ? '#FF5CB7' : '#97FFE0');
    }
  }
  update(0); let disposed = false;
  return { group, update, reset: () => update(0), inspect: () => ({ count: entries.length, wheels: entries.filter(e => e.rotor).length }),
    dispose() { if (disposed) return; disposed = true; group.removeFromParent(); geometries.forEach(g => g.dispose()); materials.forEach(m => m.dispose()); spiralPaint.forEach(p=>p.dispose()); } };
}
