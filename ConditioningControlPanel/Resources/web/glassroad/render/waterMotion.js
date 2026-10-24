// Small wind waves share their height between geometry, foam and contact spray.
import {sin,cos,vec2} from 'three/tsl';
export function ambientWaterHeight(p,time) {
  return Math.sin(p[0]*.32+p[2]*.19-time*.8)*.045+Math.sin(p[0]*.57-p[2]*.27+time*.62)*.025;
}
export function ambientWaterNodes(p,time) {
  const a=p.x.mul(.32).add(p.z.mul(.19)).sub(time.mul(.8)),b=p.x.mul(.57).sub(p.z.mul(.27)).add(time.mul(.62));
  return {height:sin(a).mul(.045).add(sin(b).mul(.025)),
    gradient:vec2(cos(a).mul(.32*.045).add(cos(b).mul(.57*.025)),cos(a).mul(.19*.045).sub(cos(b).mul(.27*.025)))};
}
