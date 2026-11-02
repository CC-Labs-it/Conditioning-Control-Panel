/* Structural hulls join the unchanged accepted platform tops from below. */
import { BufferGeometry, Float32BufferAttribute, Color } from 'three/webgpu';
export const PLATFORM_DEPTH = Object.freeze({threshold:3.8,keep:4.8,boudoir:3.2,goonworks:3.8,mainframe:6.4});
export function desiredPlatformDepth(row,theme) {
  return Math.min(PLATFORM_DEPTH[theme]||3.8,Math.max(row.shape==='hex'?3.8:1.4,Math.min(row.w,row.len)*.19));
}
/** Shared 5m footprint, unit depth, no top cap and no widening beyond collision. */
export function platformBodyGeometry(theme) {
  const palette={threshold:['#f2e0c9','#b47a9f','#654d72'],keep:['#bda4c4','#665474','#282635'],
    boudoir:['#dfbd73','#b87796','#705044'],goonworks:['#f6ead5','#ca3a93','#513751'],mainframe:['#96c8b3','#59716e','#253b39']};
  const colours=(palette[theme]||palette.threshold).map(c=>new Color(c)),positions=[],colors=[];
  const corners=[[-2.25,-2.5],[2.25,-2.5],[2.5,-2.25],[2.5,2.25],[2.25,2.5],[-2.25,2.5],[-2.5,2.25],[-2.5,-2.25]];
  const ring=(scale,y)=>corners.map(([x,z])=>[x*scale,y,z*scale]);
  const rings=[ring(.985,0),ring(.985,-.12),ring(.96,-.21),ring(.94,-.76),ring(.79,-1)];
  const tri=(a,b,c,tint)=>{for(const p of [a,b,c]){positions.push(...p);colors.push(tint.r,tint.g,tint.b);}};
  for(let layer=0;layer<rings.length-1;layer++)for(let i=0;i<8;i++){
    const j=(i+1)%8,a=rings[layer],b=rings[layer+1],tint=colours[layer<2?0:layer===2?1:2];
    tri(a[i],a[j],b[j],tint);tri(a[i],b[j],b[i],tint);
  }
  const bottom=rings.at(-1);for(let i=0;i<8;i++)tri([0,-1,0],bottom[i],bottom[(i+1)%8],colours[2]);
  const g=new BufferGeometry();g.setAttribute('position',new Float32BufferAttribute(positions,3));
  g.setAttribute('color',new Float32BufferAttribute(colors,3));g.computeVertexNormals();g.computeBoundingBox();g.computeBoundingSphere();return g;
}
