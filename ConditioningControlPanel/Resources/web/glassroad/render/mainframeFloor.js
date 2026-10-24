// Distinct industrial floor finishes, blended at room thresholds in track distance.
import { vec2, vec3, float, fract, abs, max, min, smoothstep, fwidth, mix, mod } from 'three/tsl';

export const ROOM_FLOORS = Object.freeze({
  'boot-hall': 'control', 'core-approach': 'engine', 'upper-bus': 'containment',
  'lower-bus': 'tar', 'return-weave': 'cables', 'skybridge': 'accelerator',
});
export function floorStyleAt(track, s) {
  const at = ((s % track.lengthM) + track.lengthM) % track.lengthM;
  return ROOM_FLOORS[track.sections.find(z => at >= z.fromS && at < z.toS)?.id] || 'circuit';
}
export function mainframeFloor(track, p, circuit) {
  const along = mod(p.y, track.lengthM), pixel = max(fwidth(p), vec2(.001));
  const line = (v, period, width, px) => {
    const d = abs(fract(v.div(period)).sub(.5)).mul(period);
    return float(1).sub(smoothstep(width, float(width).add(px), d));
  };
  const tile = max(line(p.x, 4, .055, pixel.x), line(p.y, 4, .055, pixel.y));
  const slat = line(p.y.add(p.x.mul(.22)), 1.6, .2, pixel.y);
  const rib = line(p.x, 1.5, .07, pixel.x);
  const stripe = smoothstep(.42, .58, fract(p.x.add(p.y).div(3)));
  const verge = smoothstep(7, 10, abs(p.x));
  const finishes = {
    control: mix(vec3(.115,.155,.165),vec3(.018,.035,.04),tile).add(vec3(.018,.07,.075).mul(line(p.x,8,.08,pixel.x))),
    engine: mix(vec3(.068,.058,.04),vec3(.015,.022,.026),slat).add(vec3(.18,.09,.015).mul(stripe).mul(verge)),
    containment: mix(vec3(.15,.175,.17),vec3(.03,.07,.065),tile).add(vec3(.01,.13,.075).mul(verge).mul(stripe)),
    tar: mix(vec3(.022,.014,.031),vec3(.05,.043,.065),rib).add(vec3(.15,.085,.018).mul(verge).mul(stripe)),
    cables: mix(vec3(.035,.05,.057),vec3(.105,.075,.034),line(p.x.add(p.y.mul(.12)),2.8,.16,pixel.x)),
    accelerator: mix(vec3(.035,.07,.085),vec3(.035,.18,.15),line(p.y.sub(abs(p.x).mul(.8)),8,.32,pixel.y)),
  };
  let result = circuit;
  for (const section of track.sections) {
    const style = ROOM_FLOORS[section.id]; if (!style) continue;
    const mask = smoothstep(section.fromS, section.fromS + 7, along)
      .mul(float(1).sub(smoothstep(section.toS - 7, section.toS, along)));
    result = mix(result, finishes[style], mask);
  }
  return result;
}
