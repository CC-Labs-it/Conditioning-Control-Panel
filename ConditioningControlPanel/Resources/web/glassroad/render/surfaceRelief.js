/* Static finish relief follows the actual surface, including banking and nonuniform instances. */
import { float, normalize, dot, cross, abs, max, sign, dFdx, dFdy, fwidth, smoothstep, positionWorld } from 'three/tsl';

/** Bounded screen-space height gradient. Height must not contain derivatives or moving clocks. */
export function reliefNormal(normal, height, footprint, { from = .18, to = .65, slope = .42 } = {}) {
  const N = normalize(normal), dx = dFdx(positionWorld), dy = dFdy(positionWorld);
  const x = cross(dy, N), y = cross(N, dx), determinant = dot(dx, x);
  const gradient = x.mul(dFdx(height)).add(y.mul(dFdy(height)))
    .mul(sign(determinant)).div(max(abs(determinant), 1e-7));
  // Degenerate triangles and very steep borders cannot turn the normal through the surface.
  const bounded = gradient.div(max(float(1), gradient.length().div(slope)));
  const visible = float(1).sub(smoothstep(from, to, footprint));
  return normalize(N.sub(bounded.mul(visible)));
}

/** One fade for a two-dimensional detail domain; callers keep it outside the height derivative. */
export function reliefFootprint(p) { const px = fwidth(p); return max(px.x, px.y); }
