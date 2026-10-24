// Distinct industrial floor finishes, blended at room thresholds in track distance.
import { vec2, vec3, float, fract, abs, max, min, smoothstep, fwidth, mix, mod, texture, sin } from 'three/tsl';
import { machineSurface, plateDetails } from './mainframeMaterials.js';

export const ROOM_FLOORS = Object.freeze({
  'boot-hall': 'control', 'core-approach': 'engine', 'upper-bus': 'containment',
  'lower-bus': 'tar', 'return-weave': 'cables', 'skybridge': 'accelerator',
});
export function floorStyleAt(track, s) {
  const at = ((s % track.lengthM) + track.lengthM) % track.lengthM;
  return ROOM_FLOORS[track.sections.find(z => at >= z.fromS && at < z.toS)?.id] || 'circuit';
}
export function mainframeFloor(track, p, circuit, { halfWidth = float(14), wear = null, roughness = null } = {}) {
  const along = mod(p.y, track.lengthM), pixel = max(fwidth(p), vec2(.001));
  const line = (v, period, width, px) => {
    const d = abs(fract(v.div(period)).sub(.5)).mul(period);
    return float(1).sub(smoothstep(width, float(width).add(px), d));
  };
  const plates = plateDetails(p), plate = base => machineSurface(p, base, null, plates);
  const edge = halfWidth.sub(abs(p.x));
  const verge = smoothstep(0.8, 1.2, edge).mul(float(1).sub(smoothstep(2.6, 3.1, edge)));
  const stripe = smoothstep(.42, .58, fract(p.x.add(p.y).div(3.2)));
  const paint = mix(vec3(.027,.031,.029), vec3(.38,.24,.055), stripe).mul(verge);
  const slat = line(p.y, 0.9, .19, pixel.y);
  const grate = slat.mul(smoothstep(3.5, 4.1, abs(p.x))).mul(float(1).sub(smoothstep(5.9, 6.4, abs(p.x))));
  const diamond = max(line(p.y.add(p.x),1.2,.055,max(pixel.x,pixel.y)), line(p.y.sub(p.x),1.2,.055,max(pixel.x,pixel.y)));
  const tread = diamond.mul(float(1).sub(smoothstep(.11,.3,max(pixel.x,pixel.y))));
  const serviceBand = float(1).sub(smoothstep(.85,1.1,abs(abs(p.x).sub(halfWidth.mul(.5)))));
  const cableRibs = line(p.y,0.8,.14,pixel.y).mul(serviceBand);
  const darkChannel = vec3(.008,.017,.023).add(vec3(.25,.13,.04).mul(cableRibs));
  const launchArrow = line(p.y.sub(abs(p.x).mul(.8)),12,.7,pixel.y);
  const scuff = wear ? texture(wear, p.div(vec2(2.2,.55))).r : float(.5);
  const grain = roughness ? texture(roughness,p.div(vec2(.9,3.6))).r : float(.5);
  const tyreLane = float(1).sub(smoothstep(.34,.75,abs(abs(p.x).sub(1.15))));
  const tyreWear = tyreLane.mul(scuff.mul(.08).add(.04));
  const warning = (base, amount = 1) => mix(base, paint, verge.mul(amount));
  const finishes = {
    control: plate(vec3(.24,.255,.225)).add(vec3(.06,.085,.075).mul(line(p.x,12,.23,pixel.x))),
    engine: warning(mix(plate(vec3(.10,.105,.10)).add(vec3(.09,.075,.047).mul(tread)),vec3(.006,.012,.015),grate)),
    containment: mix(plate(vec3(.30,.35,.34)),vec3(.018,.16,.13),verge).add(vec3(.12,.15,.14).mul(plates.rim)),
    tar: warning(plate(vec3(.018,.022,.026)).add(vec3(.075,.09,.10).mul(tread.mul(.5))).mul(float(1).sub(serviceBand.mul(.32)))),
    cables: mix(plate(vec3(.105,.12,.135)),darkChannel,serviceBand).add(vec3(.08,.10,.12).mul(line(abs(p.x).sub(halfWidth.mul(.5)),2,.055,pixel.x).mul(serviceBand))),
    accelerator: mix(plate(vec3(.025,.04,.07)),vec3(.085,.32,.29),launchArrow).add(vec3(.025,.045,.06).mul(line(p.x,5,.12,pixel.x))),
  };
  let result = circuit;
  for (const section of track.sections) {
    const style = ROOM_FLOORS[section.id]; if (!style) continue;
    const mask = smoothstep(section.fromS, section.fromS + 7, along)
      .mul(float(1).sub(smoothstep(section.toS - 7, section.toS, along)));
    result = mix(result, finishes[style], mask);
  }
  // These masks stay attached to the road, with no time term or extra texture upload.
  return result.mul(scuff.mul(.15).add(.87)).mul(grain.mul(.09).add(.94)).mul(float(1).sub(tyreWear));
}
