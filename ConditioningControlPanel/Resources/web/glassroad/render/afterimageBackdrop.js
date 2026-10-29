/* Ten dense, continuously deforming fields adapted from the retained Afterimage atlas.
 * One selected material per level. No recordings, typography, canvas uploads or particle buffers.
 * Geometry stays behind the world; the horizon is quiet so the road keeps the strongest contrast. */
import { Mesh, SphereGeometry, MeshBasicNodeMaterial, BackSide, Color } from 'three/webgpu';
import { uniform, positionLocal, normalize, vec2, vec3, float, sin, cos, abs, fract, floor,
  mix, smoothstep, max, min, dot, length, fwidth, pow } from 'three/tsl';
import { AFTERIMAGE_STYLES, pickAfterimageStyle, createAfterimageState } from './afterimageBackdropState.js';
export { AFTERIMAGE_STYLES } from './afterimageBackdropState.js';

const wave = x => sin(x).mul(.5).add(.5);
const rgb = hex => { const c = new Color(hex); return vec3(c.r, c.g, c.b); };
const hash = p => fract(sin(dot(p, vec2(127.1, 311.7))).mul(43758.5453));
const gradient = p => fract(sin(vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)))).mul(43758.5453)).mul(2).sub(1);
function noise(p) {
  const i = floor(p), f = fract(p), u = f.mul(f).mul(f).mul(f.mul(f.mul(6).sub(15)).add(10));
  return mix(mix(dot(gradient(i), f), dot(gradient(i.add(vec2(1, 0))), f.sub(vec2(1, 0))), u.x),
    mix(dot(gradient(i.add(vec2(0, 1))), f.sub(vec2(0, 1))), dot(gradient(i.add(1)), f.sub(1)), u.x), u.y);
}
function fbm(point, phone) {
  let p = point, value = float(0), amplitude = .58;
  // The atlas uses four octaves. Three retain its broad fluid identity; phones use two.
  for (let i = 0; i < (phone ? 2 : 3); i++) {
    value = value.add(noise(p).mul(amplitude));
    p = vec2(p.x.mul(1.62).sub(p.y.mul(1.17)), p.x.mul(1.17).add(p.y.mul(1.62))).add(vec2(13.6, 8.1));
    amplitude *= .49;
  }
  return value;
}
const spectral = x => cos(vec3(.02, .32, .61).add(x).mul(Math.PI * 2)).mul(.5).add(.5);
const filament = (value, power) => pow(max(float(1).sub(abs(value)), 0), power);

// Derivative-aware fine filaments stay visible without sparkling into aliasing at the horizon.
function thread(coordinate, width = .06) {
  const distance = abs(fract(coordinate).sub(.5));
  const aa = max(fwidth(coordinate), .004);
  return float(1).sub(smoothstep(width, aa.add(width), distance));
}

function granules(p, phone, gate) {
  const q = p.mul(phone ? 105 : 190), cell = floor(q), seed = hash(cell);
  const centre = vec2(seed.mul(.66).add(.17), fract(seed.mul(7.73)).mul(.66).add(.17));
  const radius = seed.mul(.065).add(.025), distance = length(fract(q).sub(centre));
  const aa = max(length(fwidth(q)), .025);
  return float(1).sub(smoothstep(radius, radius.add(aa), distance)).mul(smoothstep(.18, .8, seed)).mul(gate);
}

/** Each family retains its own path law, spatial density and colour register from dense-atlas. */
function denseField(id, x, y, t, bass, mid, high, phone) {
  let a = y, b = x, lines = float(0), mass = float(0), tint = float(.5), grains = float(.5), colour = null;
  if (id === 'velvet-aurora') {
    // Four silk folds, 64 engraved fibers per fold, with broad particle currents between them.
    for (let k = -1; k < 3; k++) {
      const centre = sin(x.mul(4.3).add(t.mul(.522)).add(k * 1.7)).mul(.19)
        .add(sin(x.mul(9.7).sub(t.mul(.725)).add(k * .7)).mul(bass.mul(.045).add(.055))).add(.46 + k * .135);
      const spread = sin(x.mul(8).add(t.mul(.87)).add(k)).sub(cos(x.mul(7).add(t.mul(.667)).add(k)));
      const thick = bass.mul(.035).add(.09), off = y.sub(centre);
      const width = max(abs(spread).mul(thick), .007);
      const f = off.div(width).add(.5), envelope = smoothstep(0, .09, f).mul(float(1).sub(smoothstep(.88, 1, f)));
      const body = pow(max(sin(f.mul(Math.PI)), 0), 3).mul(envelope);
      lines = lines.add(thread(f.mul(phone ? 35 : 64)).mul(body).mul(.65));
      mass = mass.add(body.mul(.11));
      grains = grains.add(float(1).sub(smoothstep(.025, .16, abs(off))).mul(.45));
    }
    a = y.sub(sin(x.mul(4.3).add(t.mul(.522))).mul(.19));
    b = x.sub(t.mul(.012)); tint = wave(a.mul(9).add(x));
  } else if (id === 'mercury-tides') {
    // Liquid contours use the retained 5.5 / 12 / 24 harmonics and moving compression ridges.
    a = y.sub(sin(x.mul(5.5).add(y.mul(7)).add(t.mul(.52))).mul(.13))
      .sub(sin(x.mul(12).sub(y.mul(4)).sub(t.mul(.832))).mul(bass.mul(.033).add(.035)))
      .sub(sin(x.mul(24).add(y.mul(14)).add(t.mul(.338))).mul(.022));
    const near = wave(a.mul(17).add(t.mul(.312)));
    lines = thread(a.mul(phone ? 62 : 108)).mul(near.mul(.55).add(.3));
    mass = pow(near, 5).mul(.12); grains = near.mul(.9).add(.12);
    b = x.sub(t.mul(.008)); tint = wave(a.mul(5).add(t.mul(.143)));
  } else if (id === 'neon-mycelium') {
    // The network's actual filaments weave. Sparse oblique connections join unequal currents.
    const phase = y.mul(33);
    a = y.sub(sin(x.mul(5.9).sub(t.mul(.61)).add(phase)).mul(.15))
      .sub(sin(x.mul(13.5).add(t.mul(.79)).add(phase.mul(1.3))).mul(bass.mul(.035).add(.055)))
      .sub(sin(x.mul(29).sub(t.mul(1.1)).add(phase.mul(.7))).mul(.019));
    b = x.add(sin(x.mul(9).sub(t.mul(.43)).add(phase)).mul(.032));
    lines = thread(a.mul(phone ? 36 : 53)).mul(.74)
      .add(thread(a.add(b.mul(.62)).mul(19), .035).mul(wave(b.mul(29).sub(t)).mul(.18)));
    mass = thread(a.mul(53), .22).mul(.045); grains = thread(a.mul(53), .18).add(.13);
    tint = wave(y.mul(3).add(a)); b = b.sub(t.mul(.06));
  } else if (id === 'moire-drift' || id === 'spectral-shear' || id === 'phase-textile') {
    // Three directional populations are intrinsic to each retained interference/weave design.
    for (let layer = 0; layer < 3; layer++) {
      const dir = layer === 1 ? -1 : 1;
      let u = x, v = y, coordinate;
      if (id === 'moire-drift') {
        const q = x.mul(6.2).add(y.mul(4.8)).add(t.mul(dir * .7)).add(layer * 1.2);
        coordinate = y.sub(sin(q).mul(.115)).sub(sin(x.mul(12.1).sub(y.mul(5.8)).sub(t.mul(1.05)).add(layer)).mul(bass.mul(.025).add(.043)))
          .sub(sin(x.mul(20).add(y.mul(8)).add(t.mul(1.3))).mul(.022)).add(x.mul(dir * .16)).add(t.mul(dir * .012));
      } else if (id === 'spectral-shear') {
        const q = x.mul(5.3).add(y.mul(4 + layer * .6)).sub(t.mul(dir * .85)).add(layer * .48);
        coordinate = y.sub(x.mul(.48 - layer * .065)).sub(sin(q).mul(sin(q.mul(.5).add(t.mul(.43)))).mul(.17))
          .sub(sin(x.mul(46).add(y.mul(23)).add(t.mul(dir * 1.8))).mul(sin(q.mul(.8))).mul(.009));
      } else {
        if (layer === 1) { u = y; v = x; }
        if (layer === 2) v = y.add(x.sub(.5).mul(.53));
        const q = sin(u.mul(5.2).add(v.mul(3.4)).sub(t.mul(dir * .64)).add(layer * .24));
        const r = sin(v.mul(6.6).sub(u.mul(4.3)).add(t.mul(dir * .87)));
        coordinate = v.sub(q.mul(.105)).sub(r.mul(bass.mul(.03).add(.045))).add(t.mul(dir * .008));
      }
      const count = phone ? 70 : id === 'moire-drift' ? 152 : 134;
      const shine = wave(coordinate.mul(18).sub(t.mul(.8)).add(layer));
      lines = lines.add(thread(coordinate.mul(count), .035).mul(shine.mul(.4).add(.12)));
      mass = mass.add(pow(shine, 8).mul(.028));
      if (layer === 0) { a = coordinate; b = u.sub(t.mul(.012)); tint = shine; }
    }
    grains = wave(a.mul(22)).mul(.6).add(.13);
  } else if (id === 'chromatic-turbulence') {
    let p = vec2(x.sub(.5), y.sub(.5)).mul(2.2).add(vec2(t.mul(.14), t.mul(-.085)));
    p = p.add(vec2(sin(p.y.mul(1.4).add(t.mul(.34))), cos(p.x.mul(1.15).sub(t.mul(.27)))).mul(.38));
    const q = vec2(fbm(p.add(vec2(t.mul(.1), 0)), phone), fbm(p.add(vec2(4.7, t.mul(-.115))), phone));
    const r = vec2(fbm(p.add(q.mul(2.8)).add(vec2(8.3, t.mul(.12))), phone), fbm(p.add(q.mul(2.8)).add(vec2(t.mul(-.1), 2.8)), phone));
    const flow = p.add(r.mul(bass.mul(.6).add(2.8))).add(q.mul(.65)), field = fbm(flow, phone);
    const ridge = sin(flow.x.mul(.9).add(flow.y.mul(.7)).add(field.mul(7)).add(t.mul(.18)));
    const pigment = field.mul(1.8).add(r.x.mul(.56)).sub(q.y.mul(.31));
    const thin = filament(sin(pigment.mul(mid.mul(5).add(20)).add(flow.y.mul(1.2))), 8);
    const hair = filament(sin(pigment.mul(66).add(flow.x.mul(1.9))), 14);
    const body = smoothstep(-.3, .25, field.add(r.y.mul(.35))), shadow = smoothstep(-.27, .27, field.add(q.x.mul(.12)));
    const ink = mix(spectral(pigment.mul(.72).add(.22).add(mid.mul(.025))), vec3(.08, .65, .77), smoothstep(.25, .65, ridge).mul(.38));
    const pearl = mix(vec3(.14, .72, .9), vec3(1, .5, .76), wave(pigment.mul(6)));
    colour = ink.mul(body.mul(.65).add(.11)).mul(shadow.mul(.8).add(.2))
      .add(pearl.mul(thin).mul(shadow.mul(.42).add(high.mul(.18)).add(.14)))
      .add(spectral(pigment.mul(.4).add(.7)).mul(hair).mul(shadow).mul(.15));
    a = flow.y; b = flow.x; grains = shadow.mul(.79).add(.07); tint = wave(pigment.mul(6));
  } else if (id === 'caustic-drift') {
    const angle = sin(t.mul(.1)).mul(.14), px = x.sub(.5).mul(2.45), py = y.sub(.5).mul(2.45);
    const p = vec2(cos(angle).mul(px).sub(sin(angle).mul(py)), sin(angle).mul(px).add(cos(angle).mul(py))).add(vec2(t.mul(.105), t.mul(.065)));
    const slow = vec2(fbm(p.mul(.75).add(vec2(t.mul(.12), 5.2)), phone), fbm(p.mul(.75).add(vec2(3.1, t.mul(-.13))), phone));
    let warp = p.add(slow.mul(2.4));
    warp = warp.add(vec2(sin(warp.y.mul(1.3).add(t.mul(.45))), sin(warp.x.mul(1.1).sub(t.mul(.31)))).mul(.52));
    const n = fbm(warp.mul(1.3).add(vec2(t.mul(.05), t.mul(-.09))), phone);
    const ripples = sin(warp.x.mul(3.1).add(warp.y.mul(2)).add(n.mul(7.5)).add(t.mul(.65)))
      .add(sin(warp.x.mul(-2.4).add(warp.y.mul(3.8)).add(n.mul(5)).sub(t.mul(.53))).mul(.55))
      .add(sin(warp.x.mul(5.6).add(warp.y.mul(.7)).add(t.mul(.37)).add(slow.x.mul(5))).mul(.24));
    const crest = filament(ripples.mul(.75), 8), secondary = filament(ripples.mul(.75).sub(.17), 16);
    const silk = sin(warp.x.mul(1.15).sub(warp.y.mul(.8)).add(n.mul(9)).add(t.mul(.26)));
    const shade = smoothstep(-.7, .8, silk.mul(.55).add(n.mul(1.1))), iridescence = n.mul(.85).add(slow.y.mul(.5)).add(ripples.mul(.04));
    let opal = mix(vec3(.03, .65, .61), vec3(.48, .14, .79), wave(iridescence.mul(8).add(1.6)));
    opal = mix(opal, vec3(.87, .48, .22), smoothstep(.12, .44, iridescence).mul(.7));
    colour = mix(vec3(.006, .016, .035), vec3(.018, .14, .17), shade).add(opal.mul(shade.mul(.42).add(.12)))
      .add(mix(vec3(.35, .91, .84), vec3(.88, .73, .99), wave(iridescence.mul(11))).mul(crest).mul(shade.mul(.66).add(.18)))
      .add(spectral(iridescence.mul(.65).add(.17)).mul(secondary).mul(.13));
    a = warp.y; b = warp.x; grains = shade.mul(.59).add(crest.mul(.32)).add(.09); tint = wave(iridescence.mul(11));
  } else if (id === 'curl-diffusion') {
    // Backtrace the retained curl velocity rather than moving grain over stationary lines.
    let px = x, py = y;
    for (let k = 0; k < (phone ? 3 : 6); k++) {
      const clock = t.mul(.57).sub(k * 1.1);
      const xx = px.add(sin(py.mul(3.1).add(clock.mul(.051))).mul(.28));
      const yy = py.add(sin(px.mul(2.7).sub(clock.mul(.043))).mul(.31));
      const vx = sin(yy.mul(5.1).add(clock.mul(.047))).mul(.063).add(cos(xx.mul(6.7).sub(yy.mul(3.1)).sub(clock.mul(.039))).mul(.025)).add(.052);
      const vy = cos(xx.mul(4.8).sub(clock.mul(.055))).mul(-.052).add(sin(xx.mul(3.4).add(yy.mul(5.3)).add(clock.mul(.031))).mul(.022)).sub(.018);
      px = px.sub(vx.mul(1.1)); py = py.sub(vy.mul(1.1));
    }
    const grain = wave(px.mul(3.7).add(py.mul(6.3)).add(sin(px.mul(4.1).sub(py.mul(2.2))).mul(1.8)));
    lines = thread(py.mul(phone ? 90 : 160), .035).mul(grain).mul(.6);
    mass = grain.mul(.035); grains = grain.mul(.7); a = py; b = px; tint = wave(px.add(py));
  } else {
    // Granular current carries independent counterflowing populations through its own velocity law.
    for (let layer = 0; layer < (phone ? 2 : 3); layer++) {
      let px = x, py = y;
      for (let k = 0; k < (phone ? 3 : 5); k++) {
        const clock = t.sub(k), warp = sin(px.mul(3.4).add(py.mul(2.9)).add(clock.mul(.037))).mul(.65);
        const q = px.mul(4.2).sub(py.mul(3.3)).add(warp).add(layer * 1.27);
        const r = py.mul(4.6).add(px.mul(1.9)).add(cos(px.mul(2.1).sub(clock.mul(.043))).mul(.7));
        const vx = sin(r.add(clock.mul(.031))).mul(.052).add(.043).mul(layer % 2 ? -1 : 1);
        const vy = sin(q.sub(clock.mul(.042))).mul(.044).add(cos(r.mul(.6).add(q.mul(.7))).mul(.018));
        px = px.sub(vx); py = py.sub(vy);
      }
      const cloud = pow(wave(px.mul(5.7).add(py.mul(4.1)).add(sin(py.mul(3.7)).mul(2.1))), 3);
      mass = mass.add(cloud.mul(.055));
      lines = lines.add(granules(vec2(px, py), phone, cloud).mul(.5));
      if (layer === 0) { a = py; b = px; tint = wave(py.mul(2).add(px)); grains = cloud.mul(1.2); }
    }
  }
  return { lines, mass, tint, grains, colour, p: vec2(b, a) };
}

export function createAfterimageBackdrop({ theme = 'threshold', phone = false, radius = 3000,
  random = Math.random, style: requested } = {}) {
  const style = pickAfterimageStyle(random, requested), state = createAfterimageState();
  const uniforms = { time: uniform(7), bass: uniform(0), mid: uniform(0), high: uniform(0), gentle: uniform(0) };
  const d = normalize(positionLocal);
  // Cartesian direction charts have no longitude cut or polar pinch while taking a banked bend.
  const x = d.x.mul(.82).add(d.z.mul(.57)).add(.5), y = d.y.mul(.8).add(.48);
  const field = denseField(style.id, x, y, uniforms.time, uniforms.bass, uniforms.mid, uniforms.high, phone);
  const tint = mix(rgb(style.colours[0]), rgb(style.colours[1]), field.tint);
  const ink = mix(tint, rgb(style.colours[2]), wave(x.mul(3).add(y.mul(2))).mul(.35));
  const fine = granules(field.p, phone, min(field.grains, 1.5));
  const glow = field.lines.mul(.5).add(field.mass).add(fine.mul(uniforms.high.mul(.22).add(.7)));
  const horizon = smoothstep(.015, .35, abs(d.y)).mul(.62).add(.3);
  const quiet = uniforms.gentle.mul(-.25).add(1);
  const ground = rgb(theme === 'mainframe' ? '#071719' : theme === 'goonworks' ? '#160a18' : '#0c0b1b');
  const material = new MeshBasicNodeMaterial({ side: BackSide, depthWrite: false, fog: false });
  material.name = `afterimage-${style.id}`;
  const light = field.colour ? field.colour.add(ink.mul(fine).mul(.5)) : ink.mul(glow);
  material.colorNode = ground.add(light.mul(horizon).mul(quiet));
  const geometry = new SphereGeometry(radius, phone ? 24 : 40, phone ? 16 : 28);
  const object = new Mesh(geometry, material);
  object.name = `afterimage-backdrop-${style.id}`; object.frustumCulled = false; object.renderOrder = -2;
  let disposed = false;
  const keys = Object.keys(uniforms);
  const write = () => { for (const key of keys) uniforms[key].value = state.frame[key]; };
  return { object, style: style.id, uniforms,
    update(dt, view = {}) {
      if (disposed) return;
      if (view.playerPos) object.position.copy(view.playerPos);
      state.update(dt, view); write();
    },
    reset() { if (!disposed) { state.reset(); write(); } },
    dispose() {
      if (disposed) return; disposed = true; object.removeFromParent(); geometry.dispose(); material.dispose();
    },
  };
}
