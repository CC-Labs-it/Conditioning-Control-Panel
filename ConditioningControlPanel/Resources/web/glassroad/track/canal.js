// One curved support law for the coolant tube, its mesh and its props.
export const CANAL = Object.freeze({ radiusM: 20, blendM: 35 });
const smooth = t => t * t * (3 - 2 * t);

export function canalProfile(sections, s, x, out = {}) {
  out.height = out.slope = out.alongSlope = out.blend = 0;
  const section = sections.find(v => ['coolant-canal', 'rabbit-hole'].includes(v.id) && s >= v.fromS && s <= v.toS);
  if (!section) return out;
  const a = Math.min(1, (s - section.fromS) / CANAL.blendM), b = Math.min(1, (section.toS - s) / CANAL.blendM);
  const blend = smooth(a) * smooth(b);
  const derivative = (6 * a * (1 - a) * smooth(b) - smooth(a) * 6 * b * (1 - b)) / CANAL.blendM;
  const lateral = Math.max(-19, Math.min(19, x)), root = Math.sqrt(CANAL.radiusM ** 2 - lateral ** 2);
  out.height = (CANAL.radiusM - root) * blend;
  out.slope = lateral / root * blend;
  out.alongSlope = (CANAL.radiusM - root) * derivative;
  out.blend = blend;
  return out;
}

export function canalNormal(frame, profile, out) {
  let length = 0;
  for (let k = 0; k < 3; k++) { out[k] = frame.n[k] - frame.b[k] * profile.slope - frame.t[k] * profile.alongSlope; length += out[k] ** 2; }
  length = Math.sqrt(length);
  for (let k = 0; k < 3; k++) out[k] /= length;
  return out;
}

// Road and rail UV.y is distance along the lap. Preserve underside thickness and rail height.
export function bendCanalMesh(mesh, track) {
  if (!track.canalProfile) return mesh;
  const f = track.frameAt(0), profile = {};
  for (let i = 0; i < mesh.positions.length / 3; i++) {
    const j = i * 3, s = mesh.uvs[i * 2 + 1];
    track.frameAt(s, f);
    let x = 0, normalUp = 0;
    for (let k = 0; k < 3; k++) { x += (mesh.positions[j + k] - f.p[k]) * f.b[k]; normalUp += mesh.normals[j + k] * f.n[k]; }
    if (!track.canalProfile(s, x, profile).blend) continue;
    let norm = 0;
    for (let k = 0; k < 3; k++) {
      mesh.positions[j + k] += f.n[k] * profile.height;
      mesh.normals[j + k] -= normalUp * (f.b[k] * profile.slope + f.t[k] * profile.alongSlope);
      norm += mesh.normals[j + k] ** 2;
    }
    norm = Math.sqrt(norm);
    for (let k = 0; k < 3; k++) mesh.normals[j + k] /= norm;
  }
  return mesh;
}
