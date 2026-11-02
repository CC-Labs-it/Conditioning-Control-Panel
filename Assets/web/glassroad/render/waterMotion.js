// Crossing swells share one spectrum between geometry, foam and contact spray.
import {sin,cos,vec2,float} from 'three/tsl';
export const WATER_WAVES=Object.freeze([
  [.26,.18,-1.15,.095],
  [.49,-.29,.92,.065],
  [.76,.53,-1.48,.04],
  [-1.12,.77,1.72,.025],
].map(Object.freeze));
const SHARPEN=.22;
export function ambientWaterHeight(p,time) {
  let height=0;
  for(const [x,z,speed,amplitude] of WATER_WAVES){
    const phase=p[0]*x+p[2]*z+time*speed;
    height+=(Math.sin(phase)+SHARPEN*Math.sin(2*phase))*amplitude;
  }
  return height;
}
export function ambientWaterGradient(p,time) {
  const gradient=[0,0];
  for(const [x,z,speed,amplitude] of WATER_WAVES){
    const phase=p[0]*x+p[2]*z+time*speed,g=(Math.cos(phase)+2*SHARPEN*Math.cos(2*phase))*amplitude;
    gradient[0]+=g*x;gradient[1]+=g*z;
  }
  return gradient;
}
export function ambientWaterNodes(p,time) {
  let height=float(0),gradient=vec2(0);
  for(const [x,z,speed,amplitude] of WATER_WAVES){
    const phase=p.x.mul(x).add(p.z.mul(z)).add(time.mul(speed));
    height=height.add(sin(phase).add(sin(phase.mul(2)).mul(SHARPEN)).mul(amplitude));
    gradient=gradient.add(vec2(x,z).mul(cos(phase).add(cos(phase.mul(2)).mul(2*SHARPEN))).mul(amplitude));
  }
  return {height,gradient};
}
