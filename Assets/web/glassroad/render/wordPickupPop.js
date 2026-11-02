import { abs, clamp, cos, float, length, max, sin, smoothstep, step, vec2 } from 'three/tsl';

/** A contact-only pop, composed around the original lettering. No new draw, texture or clock. */
export function wordPickupPop(loc, progress, motion) {
  const p = clamp(progress, 0, 1), on = step(0, progress);
  const amount = clamp(motion, 0, 1).mul(on);
  const rise = smoothstep(0, .13, p);
  const end = float(1).sub(smoothstep(.5, 1, p));
  const squash = rise.mul(float(1).sub(smoothstep(.12, .34, p))).mul(amount);
  const release = smoothstep(.1, .32, p).mul(end).mul(amount);
  const uv = vec2(loc.x.sub(.5).div(float(1).add(squash.mul(.16)).sub(release.mul(.07))).add(.5),
    loc.y.sub(.5).div(float(1).sub(squash.mul(.2)).add(release.mul(.22))).add(.5));
  const lift = smoothstep(.06, .42, p).mul(float(1).sub(smoothstep(.72, 1, p))).mul(amount).mul(.55);
  const radius = p.mul(.31).add(.07), point = loc.sub(vec2(.5));
  const ring = float(1).sub(smoothstep(.009, .026, abs(length(point).sub(radius))));
  let sparks = float(0);
  for (let i = 0; i < 12; i++) {
    const angle = i * Math.PI * 2 / 12 + .18;
    const reach = p.mul(.25 + (i % 3) * .035).add(.08);
    const delta = point.sub(vec2(cos(float(angle)), sin(float(angle))).mul(reach));
    const star = max(float(1).sub(smoothstep(.003, .008, abs(delta.x))).mul(float(1).sub(smoothstep(.009, .025, abs(delta.y)))),
      float(1).sub(smoothstep(.003, .008, abs(delta.y))).mul(float(1).sub(smoothstep(.009, .025, abs(delta.x)))));
    sparks = max(sparks, star);
  }
  const glow = ring.mul(.55).add(sparks).mul(rise).mul(end).mul(amount);
  return { uv, lift, glow, shine: rise.mul(end).mul(amount) };
}
