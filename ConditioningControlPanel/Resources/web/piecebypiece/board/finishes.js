// Animated finishes on the sculpted silicone. The game and showroom share this material.
import * as THREE from 'three';

export const FINISHES = Object.freeze([
  { id: 'polka', name: 'Candy lacquer', description: 'Cream dots and tangerine rings circle the caps and climb in rows.', teamNames: ['Raspberry', 'Cobalt'], teamColors: ['#bd085c', '#123fb4'] },
  { id: 'marble', name: 'Mineral strata', description: 'Mineral layers fold through jade and amethyst, carrying pale veins with them.', teamNames: ['Jade', 'Amethyst'], teamColors: ['#047f66', '#7923b3'] },
  { id: 'contour', name: 'Liquid ribbons', description: 'Bright currents run around collars and up the sculpted rails.', teamNames: ['Cyan', 'Ember'], teamColors: ['#008ba8', '#c9430b'] },
  { id: 'opal', name: 'Oil slick', description: 'Pools of turquoise and molten gold split, stretch and meet again.', teamNames: ['Violet', 'Copper'], teamColors: ['#6520b4', '#b94019'] },
  { id: 'kintsugi', name: 'Gold faultlines', description: 'Fine threads of gold bend and branch along the sculpt.', teamNames: ['Obsidian', 'Turquoise'], teamColors: ['#111522', '#008f88'] },
]);

const PALETTES = {
  polka: { w: ['#bd085c', '#fff1ce', '#ffd984'], b: ['#123fb4', '#ffad26', '#fff0a8'] },
  marble: { w: ['#047f66', '#90e8b3', '#e8ffd5'], b: ['#7923b3', '#e893f6', '#ffe8ff'] },
  contour: { w: ['#008ba8', '#a8fff4', '#00334e'], b: ['#c9430b', '#ffd38c', '#5c1209'] },
  opal: { w: ['#6520b4', '#25efce', '#ff6ce1'], b: ['#b94019', '#ffd75a', '#ff576d'] },
  kintsugi: { w: ['#111522', '#ffb72e', '#ffe2a0'], b: ['#008f88', '#ffb72e', '#ffe2a0'] },
};

const CLOCK = { value: 0 };
const PATTERN_SPEED = 0.5;
const materials = new WeakMap();
const shaped = new WeakSet();
const PIECE_TYPE = { p: 0, n: 1, b: 2, r: 3, q: 4, k: 5 };
let lastSeconds = null;
let previousRate = 0;

// Surface coordinates follow the sculpt's bending centreline and radius profile.
// Only extra GPU attributes are added; original positions, normals and GLBs stay intact.
function shapeCoordinates(geometry) {
  if (shaped.has(geometry)) return;
  const position = geometry.getAttribute('position'), normal = geometry.getAttribute('normal');
  if (!position) return;
  geometry.computeBoundingBox();
  const box = geometry.boundingBox, height = Math.max(.001, box.max.y - box.min.y), bins = 80;
  const rows = Array.from({ length: bins }, (_, i) => ({ y: box.min.y + height * i / (bins - 1),
    minX: Infinity, maxX: -Infinity, minZ: Infinity, maxZ: -Infinity, x: 0, z: 0, r: 0, count: 0 }));
  const rowOf = y => Math.max(0, Math.min(bins - 1, Math.round((y - box.min.y) / height * (bins - 1))));
  for (let i = 0; i < position.count; i++) {
    const row = rows[rowOf(position.getY(i))], x = position.getX(i), z = position.getZ(i);
    row.minX = Math.min(row.minX, x); row.maxX = Math.max(row.maxX, x);
    row.minZ = Math.min(row.minZ, z); row.maxZ = Math.max(row.maxZ, z); row.count++;
  }
  for (let i = 0; i < bins; i++) {
    let row = rows[i];
    if (!row.count) {
      let nearest = 0, distance = bins;
      rows.forEach((candidate, n) => { if (candidate.count && Math.abs(n - i) < distance) { nearest = n; distance = Math.abs(n - i); } });
      row = rows[nearest];
    }
    rows[i].x = (row.minX + row.maxX) * .5; rows[i].z = (row.minZ + row.maxZ) * .5;
    rows[i].r = Math.max(.001, (row.maxX - row.minX + row.maxZ - row.minZ) * .25);
  }
  const smooth = (values, radius) => values.map((_, i) => {
    let total = 0, count = 0;
    for (let j = Math.max(0, i - radius); j <= Math.min(bins - 1, i + radius); j++) { total += values[j]; count++; }
    return total / count;
  });
  const xs = smooth(rows.map(row => row.x), 3), zs = smooth(rows.map(row => row.z), 3);
  const radii = smooth(rows.map(row => row.r), 1), broad = smooth(radii, 6), maxRadius = Math.max(...radii);
  const distance = [0];
  for (let i = 1; i < bins; i++) distance[i] = distance[i - 1] + Math.hypot(xs[i] - xs[i - 1], height / (bins - 1), zs[i] - zs[i - 1], radii[i] - radii[i - 1]);
  const length = Math.max(.001, distance.at(-1));
  const surface = new Float32Array(position.count * 4), form = new Float32Array(position.count * 4);
  const tangent = new THREE.Vector3(), right = new THREE.Vector3(), forward = new THREE.Vector3();
  const offset = new THREE.Vector3(), vertexNormal = new THREE.Vector3();
  for (let i = 0; i < position.count; i++) {
    const h = Math.max(0, Math.min(1, (position.getY(i) - box.min.y) / height));
    const n = rowOf(position.getY(i)), lo = Math.max(0, n - 1), hi = Math.min(bins - 1, n + 1);
    tangent.set(xs[hi] - xs[lo], (hi - lo) * height / (bins - 1), zs[hi] - zs[lo]).normalize();
    right.set(tangent.y, -tangent.x, 0).normalize();
    forward.crossVectors(right, tangent).normalize();
    offset.set(position.getX(i) - xs[n], 0, position.getZ(i) - zs[n]);
    const x = offset.dot(right), z = offset.dot(forward), radius = Math.max(.0001, Math.hypot(x, z));
    const slope = normal ? Math.abs(vertexNormal.fromBufferAttribute(normal, i).dot(tangent)) : 0;
    surface.set([x / radius, z / radius, distance[n] / length, h], i * 4);
    form.set([radius / maxRadius, Math.max(0, Math.min(1, .5 + (radii[n] - broad[n]) / maxRadius * 7)), slope, 0], i * 4);
  }
  geometry.setAttribute('finishSurface', new THREE.BufferAttribute(surface, 4));
  geometry.setAttribute('finishForm', new THREE.BufferAttribute(form, 4));
  shaped.add(geometry);
}

const VERTEX = `
attribute vec4 finishSurface;
attribute vec4 finishForm;
varying vec3 vFinishPosition;
varying vec4 vFinishSurface;
varying vec4 vFinishForm;
`;

const FRAGMENT = `
varying vec3 vFinishPosition;
varying vec4 vFinishSurface;
varying vec4 vFinishForm;
uniform float uFinishKind;
uniform float uFinishPiece;
uniform float uFinishSide;
uniform float uFinishClock;
uniform float uFinishOffset;
uniform vec3 uFinishBase;
uniform vec3 uFinishAccent;
uniform vec3 uFinishDetail;

float finishHash(vec3 p) {
  p = fract(p * 0.1031);
  p += dot(p, p.yzx + 33.33);
  return fract((p.x + p.y) * p.z);
}
float finishNoise(vec3 p) {
  vec3 i = floor(p), f = fract(p);
  f = f * f * (3.0 - 2.0 * f);
  return mix(mix(mix(finishHash(i), finishHash(i + vec3(1,0,0)), f.x),
                 mix(finishHash(i + vec3(0,1,0)), finishHash(i + vec3(1,1,0)), f.x), f.y),
             mix(mix(finishHash(i + vec3(0,0,1)), finishHash(i + vec3(1,0,1)), f.x),
                 mix(finishHash(i + vec3(0,1,1)), finishHash(i + vec3(1,1,1)), f.x), f.y), f.z);
}
float finishStone(vec3 p) {
  return finishNoise(p) * .55 + finishNoise(p * 2.03 + 9.1) * .28
    + finishNoise(p * 4.11 + 3.7) * .17;
}
vec2 finishCell(vec2 p) {
  return vec2(finishHash(vec3(p, 1.7)), finishHash(vec3(p, 9.2)));
}
float finishSeams(vec2 p) {
  vec2 cell = floor(p), local = fract(p);
  float columns = uFinishPiece < .5 ? 5.0 : uFinishPiece < 1.5 ? 3.0 : uFinishPiece < 2.5 ? 6.0 : uFinishPiece < 3.5 ? 8.0 : 5.0;
  float nearD = 8.0, nextD = 8.0;
  vec2 nearest = vec2(0.0), runnerUp = vec2(0.0);
  for (int y = -1; y <= 1; y++) {
    for (int x = -1; x <= 1; x++) {
      vec2 offset = vec2(float(x), float(y));
      vec2 wrapped = vec2(mod(cell.x + offset.x, columns), cell.y + offset.y);
      vec2 delta = offset + .14 + .72 * finishCell(wrapped) - local;
      float distance2 = dot(delta, delta);
      if (distance2 < nearD) {
        nextD = nearD; runnerUp = nearest;
        nearD = distance2; nearest = delta;
      } else if (distance2 < nextD) {
        nextD = distance2; runnerUp = delta;
      }
    }
  }
  // Distance to the bisector keeps the seam narrow even at a fork.
  return (nextD - nearD) / max(.001, 2.0 * length(runnerUp - nearest));
}
struct FinishSurface {
  vec3 ink;
  vec3 glow;
  float roughness;
  float metalness;
};
struct FinishLayout {
  vec2 uv;
  float zone;
  float trim;
  float path;
  float trace;
};
float finishBand(float x, float centre, float width) {
  float aa = max(fwidth(x), .003);
  return 1.0 - smoothstep(width - aa, width + aa, abs(x - centre));
}
float goldThread(float distanceToLine, float width) {
  float aa = clamp(fwidth(distanceToLine) * .5, .001, width);
  return 1.0 - smoothstep(width - aa, width + aa, distanceToLine);
}
// Candy lacquer and mineral strata coat the entire king, including its foot.
bool fullKingCoat() {
  return uFinishPiece > 4.5 && uFinishKind < 1.5;
}
FinishLayout finishLayout(float angle, float s, float h) {
  FinishLayout motif;
  motif.zone = smoothstep(.18, .25, h);
  motif.trim = 0.0;
  float turn = angle * 6.28318530718;
  float trimWidth = uFinishKind > 3.5 ? .42 : 1.0;
  if (uFinishPiece < .5) {
    // Pawn: the bulb wears the pattern; its narrow collar stays a deliberate band.
    motif.uv = vec2(angle * 5.0, s * 3.5);
    motif.zone = smoothstep(.32, .44, h);
    motif.trim = finishBand(h, .32, .021 * trimWidth);
    motif.path = s * 10.0 + vFinishForm.x * 2.0;
    motif.trace = goldThread(abs(sin(turn * 3.0)), .095) * smoothstep(.42, .65, h);
  } else if (uFinishPiece < 1.5) {
    // Knight: long rails follow the curved shaft, with an accent below the head.
    motif.uv = vec2(angle * 3.0 + sin(s * 4.0) * .18, s * 5.0);
    motif.path = turn * 3.0 + sin(s * 5.0) * .55;
    motif.trim = finishBand(h, .82, .018 * trimWidth);
    motif.trace = goldThread(abs(sin(turn * 2.0 + sin(s * 5.0) * .45)), .11);
  } else if (uFinishPiece < 2.5) {
    // Bishop: a helical wrap and highlights following the sculpted rib crests.
    motif.uv = vec2(angle * 6.0 + s * 3.0, s * 7.0);
    motif.path = s * 25.0 + turn * 2.0 + vFinishForm.y * .8;
    motif.trim = smoothstep(.72, .94, vFinishForm.y) * smoothstep(.22, .35, h) * .75;
    if (uFinishKind > 3.5) motif.trim = 0.0;
    motif.trace = goldThread(abs(sin(turn + s * 11.0)), .09);
  } else if (uFinishPiece < 3.5) {
    // Rook: vertical flutes terminate in a broad polished rim.
    motif.uv = vec2(angle * 8.0, s * 2.4);
    motif.zone *= 1.0 - smoothstep(.86, .93, h);
    motif.path = turn * 5.0 + vFinishForm.x * .35;
    motif.trim = finishBand(h, .91, .027 * trimWidth);
    motif.trace = goldThread(abs(sin(turn * 4.0)), .11);
  } else if (uFinishPiece < 4.5) {
    // Queen: rising petal panels follow the changing profile into the upper collar.
    motif.uv = vec2(angle * 5.0 + s * 2.3, s * 3.0 + cos(turn) * .32);
    motif.path = s * 13.0 + sin(turn) * 2.2 + vFinishForm.x * 2.5;
    motif.trim = finishBand(h, .82, .019 * trimWidth) + finishBand(h, .88, .012 * trimWidth);
    motif.trace = goldThread(abs(sin(turn * 2.0 - s * 7.5 + vFinishForm.x)), .095);
  } else {
    // The inlaid finishes frame the front and back. Coatings wrap the entire body.
    motif.uv = vec2(angle * 5.0 + sin(s * 4.0) * .12, s * 4.5);
    if (fullKingCoat()) motif.zone = 1.0;
    else motif.zone *= 1.0 - smoothstep(.35, .72, abs(cos(turn)));
    motif.path = turn * 2.0 + s * 4.0 + vFinishForm.x;
    motif.trim = uFinishKind > .5 && uFinishKind < 1.5 ? 0.0 : finishBand(h, .88, .021 * trimWidth);
    motif.trace = goldThread(abs(cos(turn + sin(s * 5.0) * .11)), .065);
  }
  motif.trace *= motif.zone;
  return motif;
}
FinishSurface finishPaint(vec3 p) {
  float h = vFinishSurface.w, s = vFinishSurface.z;
  float angle = atan(vFinishSurface.y, vFinishSurface.x) / 6.28318530718 + .5;
  float time = uFinishClock + uFinishOffset;
  float turn = angle * 6.28318530718;
  vec2 chart = vec2(angle, s);
  if (uFinishKind > 3.5) {
    // Bend the chart before drawing both the main vein and its branches.
    // The ends stay seated at the foot and crown while the intervening line travels.
    float bend = smoothstep(.08, .27, h) * (1.0 - smoothstep(.83, .98, h));
    chart.x += bend * (.073 * sin(s * 7.0 - time * 1.36 + .55 * sin(turn))
                     + .019 * sin(s * 12.0 + time * .83 - turn));
    chart.y += bend * .047 * sin(turn * 2.0 + s * 4.0 - time * 1.08);
  }
  FinishLayout motif = finishLayout(chart.x, chart.y, h);
  // Travel around a pawn's cap and along the centreline of the taller sculpts.
  float travel = uFinishPiece < .5 ? turn : s * 8.5 + .45 * sin(turn);
  // On ribbed sculpts the local surface tips away from the shaft along each
  // raised edge. Pick up those edges within the designed panel or wrap.
  float relief = smoothstep(.28, .70, vFinishForm.z) * smoothstep(.31, .42, h)
    * (1.0 - smoothstep(.84, .94, h)) * step(1.5, uFinishPiece);
  FinishSurface surface;
  surface.ink = uFinishBase;
  surface.glow = uFinishBase * .018;
  surface.roughness = .29;
  surface.metalness = .035;
  if (uFinishKind < .5) {
    vec2 uv = motif.uv;
    if (uFinishPiece < .5) uv.x -= time * .85;
    else uv.y -= time * .92;
    uv.x += mod(floor(uv.y), 2.0) * .5;
    float d = length(fract(uv) - .5);
    float aa = max(fwidth(d), .008);
    float dots = 1.0 - smoothstep(.295 - aa, .295 + aa, d);
    float rings = (1.0 - smoothstep(.35 - aa, .35 + aa, d)) * smoothstep(.19 - aa, .19 + aa, d);
    float shape = mix(dots, rings, uFinishSide);
    surface.ink = mix(uFinishBase, uFinishAccent, shape);
    surface.roughness = .22;
  } else if (uFinishKind < 1.5) {
    vec3 mineral = vec3(vFinishSurface.xy * (1.0 + vFinishForm.x), s * 3.0);
    // The stone grain stays put as the mineral layers shear past it.
    float fold = sin(travel - time * 1.65);
    float cloud = finishStone(mineral * 1.8 + vec3(.46 * fold, 0.0, .19 * cos(travel + time)));
    float strata = sin(motif.path + cloud * 5.0 + vFinishForm.y * .6 + fold * 1.9);
    float broad = smoothstep(.32, .8, strata);
    float seam = abs(strata + .12);
    float aa = max(fwidth(seam), .012);
    float vein = 1.0 - smoothstep(.045 - aa, .10 + aa, seam);
    surface.ink = mix(uFinishBase * (.77 + .23 * cloud), uFinishAccent, broad * .8);
    surface.ink = mix(surface.ink, uFinishDetail, vein * .95);
    surface.glow += uFinishDetail * vein * .055;
    surface.roughness = mix(.27, .19, vein);
  } else if (uFinishKind < 2.5) {
    // The dark rails are fixed. A broad two-tone slug runs inside each rail.
    float route = uFinishPiece < .5 ? turn * 2.0 : travel * 1.3 + .5 * sin(motif.path);
    float crest = .5 + .5 * cos(route - time * 2.65);
    float head = smoothstep(.46, .94, crest);
    float bands = abs(sin(motif.path));
    float aa = max(fwidth(bands), .012);
    float edge = 1.0 - smoothstep(.48 - aa, .55 + aa, bands);
    float width = mix(.12, .44, crest);
    float ribbon = 1.0 - smoothstep(width - aa, width + aa, bands);
    vec3 ribbonInk = mix(uFinishBase, uFinishAccent, .16 + .84 * head);
    surface.ink = mix(uFinishBase, uFinishDetail, edge * .80);
    surface.ink = mix(surface.ink, ribbonInk, ribbon);
    surface.glow += uFinishAccent * ribbon * head * .30;
    surface.roughness = .25;
  } else if (uFinishKind < 3.5) {
    float swirl = sin(motif.uv.x * 6.28318530718 + time * 1.10 + .85 * sin(travel - time * 1.30));
    float tide = sin(motif.uv.y * 2.2 + swirl * 1.8 + vFinishForm.x * 2.1 - time * 1.48);
    float islands = smoothstep(.48, .72, tide);
    float fringe = smoothstep(.20, .36, tide) - smoothstep(.43, .55, tide);
    surface.ink = mix(uFinishBase, uFinishDetail, fringe * .95);
    surface.ink = mix(surface.ink, uFinishAccent, islands);
    surface.glow += (uFinishAccent * islands + uFinishDetail * fringe) * .055;
    surface.roughness = .19;
    surface.metalness = .30;
  } else {
    vec2 uv = motif.uv * vec2(1.0, .70);
    uv += vec2(sin(chart.y * 5.0) * .11, vFinishForm.x * .15);
    float seam = finishSeams(uv);
    float branches = goldThread(seam, .027) * (uFinishPiece > 4.5 ? .76 : .88);
    float gold = max(branches, motif.trace);
    vec3 alloy = mix(uFinishAccent, uFinishDetail, .23);
    surface.ink = mix(uFinishBase * (.90 + .10 * finishNoise(p * 12.0)), alloy, gold);
    // Real metallic seams with a bounded warm lift, readable on the shaded side.
    surface.roughness = mix(.34, .21, gold);
    surface.metalness = mix(.025, .88, gold);
    surface.glow += alloy * gold * .48;
  }
  surface.ink = mix(uFinishBase, surface.ink, motif.zone);
  surface.glow = mix(uFinishBase * .018, surface.glow, motif.zone);
  surface.metalness = mix(.035, surface.metalness, motif.zone);
  float trim = clamp(uFinishKind > 3.5 ? motif.trim : max(motif.trim, relief * motif.zone * .28), 0.0, 1.0);
  surface.ink = mix(surface.ink, uFinishAccent, trim);
  surface.glow += uFinishAccent * trim * (uFinishKind > 3.5 ? .30 : .055);
  surface.metalness = mix(surface.metalness, uFinishKind > 3.5 ? .88 : .12, trim);
  surface.roughness = mix(surface.roughness, .21, trim);
  // Stable team-colored feet: one pale rim on the first side, two on the second.
  float foot = smoothstep(.13, .19, h);
  if (fullKingCoat()) foot = 1.0;
  float rim = 1.0 - smoothstep(.006, .012, abs(h - .045));
  rim = max(rim, (1.0 - smoothstep(.006, .012, abs(h - .09))) * uFinishSide);
  vec3 rimColor = vec3(1.0, .87, .62);
  surface.ink = mix(uFinishBase, surface.ink, foot);
  surface.ink = mix(surface.ink, rimColor, rim);
  surface.glow = mix(uFinishBase * .018, surface.glow, foot) + rimColor * rim * .055;
  surface.metalness = mix(.035, surface.metalness, foot);
  return surface;
}
`;

// Capture only properties the finish owns. Live opacity, dissolve, flex and check
// flashes can change while a finish is worn and must survive taking it off.
function stockSurface(material) {
  return {
    onBeforeCompile: material.onBeforeCompile,
    customProgramCacheKey: material.customProgramCacheKey,
    roughness: material.roughness, metalness: material.metalness,
    clearcoat: material.clearcoat, clearcoatRoughness: material.clearcoatRoughness,
    sheen: material.sheen, sheenColor: material.sheenColor.clone(),
    emissive: material.emissive.clone(), iridescence: material.iridescence,
    iridescenceIOR: material.iridescenceIOR, iridescenceThicknessRange: material.iridescenceThicknessRange,
  };
}

/** Patch the body's material after flex is attached. A null or unknown finish restores its stock surface. */
export function applyFinish(piece, finishId, side = piece?.userData?.side || 'w') {
  const kind = FINISHES.findIndex((finish) => finish.id === finishId);
  if (!piece?.traverse) return false;
  const palette = kind >= 0 ? PALETTES[finishId][side === 'b' ? 'b' : 'w'] : null;
  const seen = new Set();
  piece.traverse((mesh) => {
    if (!mesh.isMesh || !mesh.geometry) return;
    const slots = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
    for (const material of slots) {
      if (material?.userData?.pbpPatch !== 'skin') continue;
      if (palette) shapeCoordinates(mesh.geometry);
      if (seen.has(material)) continue;
      seen.add(material);
      const saved = materials.get(material);
      if (!palette) {
        if (saved) {
          // Releasing this material's GPU programs forces Three to rebuild its
          // per-material uniforms when returning to the stock compile hook.
          material.dispose();
          Object.assign(material, saved.stock);
          materials.delete(material);
          material.needsUpdate = true;
        }
        continue;
      }
      let state = saved?.uniforms;
      if (!state) {
        // A second equip can otherwise reuse a linked program with the previous
        // stock uniform table and draw with the other side's last uploaded ink.
        // The mesh, material object and its spring remain live.
        material.dispose();
        const stock = stockSurface(material);
        const baseKey = material.customProgramCacheKey();
        state = {
          uFinishKind: { value: kind }, uFinishClock: CLOCK, uFinishSide: { value: side === 'b' ? 1 : 0 },
          uFinishOffset: { value: piece.userData.phase ?? (PIECE_TYPE[piece.userData.type] ?? 0) * .57 + (side === 'b' ? .81 : 0) },
          uFinishPiece: { value: PIECE_TYPE[piece.userData.type] ?? 0 },
          uFinishBase: { value: new THREE.Color(palette[0]) },
          uFinishAccent: { value: new THREE.Color(palette[1]) },
          uFinishDetail: { value: new THREE.Color(palette[2]) },
        };
        materials.set(material, { stock, uniforms: state });
        material.onBeforeCompile = (shader, renderer) => {
          stock.onBeforeCompile.call(material, shader, renderer);
          Object.assign(shader.uniforms, state);
          if (shader.uniforms.uSkinTint) shader.uniforms.uSkinTint = state.uFinishBase;
          shader.vertexShader = VERTEX + shader.vertexShader.replace('#include <begin_vertex>',
            '#include <begin_vertex>\nvFinishPosition = position;\nvFinishSurface = finishSurface;\nvFinishForm = finishForm;');
          shader.fragmentShader = FRAGMENT + shader.fragmentShader
            .replace('#include <color_fragment>', '#include <color_fragment>\nFinishSurface finishSurface = finishPaint(vFinishPosition);\ndiffuseColor.rgb = finishSurface.ink;')
            .replace('#include <roughnessmap_fragment>', '#include <roughnessmap_fragment>\nroughnessFactor = finishSurface.roughness;')
            .replace('#include <metalnessmap_fragment>', '#include <metalnessmap_fragment>\nmetalnessFactor = finishSurface.metalness;')
            .replace('#include <emissivemap_fragment>', '#include <emissivemap_fragment>\ntotalEmissiveRadiance += finishSurface.glow;');
        };
        material.customProgramCacheKey = () => baseKey + '|pbp-finish-6';
      }
      state.uFinishKind.value = kind;
      state.uFinishSide.value = side === 'b' ? 1 : 0;
      state.uFinishPiece.value = PIECE_TYPE[piece.userData.type] ?? 0;
      state.uFinishBase.value.set(palette[0]);
      state.uFinishAccent.value.set(palette[1]);
      state.uFinishDetail.value.set(palette[2]);
      material.roughness = .29;
      material.metalness = .035;
      material.clearcoat = finishId === 'polka' ? .65 : .8;
      material.clearcoatRoughness = .19;
      material.sheen = .12;
      material.sheenColor.set(palette[0]);
      material.emissive.set(palette[0]);
      material.iridescence = finishId === 'opal' ? .52 : 0;
      material.iridescenceIOR = 1.3;
      material.iridescenceThicknessRange = [120, 350];
      material.needsUpdate = true;
    }
  });
  piece.userData.finish = palette && seen.size ? finishId : null;
  return seen.size > 0;
}

/** Motion off holds the current frame and resumes without a phase jump. */
export function setFinishTime(seconds, motion = true) {
  if (!Number.isFinite(seconds)) return;
  const delta = seconds - (lastSeconds ?? seconds);
  const rate = Math.max(0, Math.min(1, Number(motion) || 0));
  if (previousRate && rate && delta > 0 && delta < 1) CLOCK.value += delta * PATTERN_SPEED * rate;
  lastSeconds = seconds;
  previousRate = rate;
}
