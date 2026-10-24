/* Swept architecture follows the same route frames as the supported racing surface. */
import {getGoonworksLayout} from '../track/goonworks-layout.js';

/** Shared longitudinal rings make the rabbit shell continuous through every bend. */
export function goonworksTubeShell(track, outer=false){
  const range=getGoonworksLayout(track)?.tube;if(!range)return null;
  const P=[],N=[],U=[],I=[],rings=Math.ceil((range.s1-range.s0)/2),sectors=72,radius=outer?24.25:23.5;
  for(let i=0;i<=rings;i++){
    const s=range.s0+(range.s1-range.s0)*i/rings,f=track.frameAt(s);
    for(let j=0;j<=sectors;j++){
      const a=j/sectors*Math.PI*2,c=Math.cos(a),q=Math.sin(a),normal=f.b.map((v,k)=>v*c+f.n[k]*q);
      P.push(...f.p.map((v,k)=>v+f.n[k]*22+normal[k]*radius));
      N.push(...normal.map(v=>v*(outer?1:-1)));U.push(a,s-range.s0);
    }
  }
  for(let i=0;i<rings;i++)for(let j=0;j<sectors;j++){
    const a=i*(sectors+1)+j,b=a+1,c=a+sectors+1,d=c+1;
    if(outer)I.push(a,d,b,a,c,d);else I.push(a,b,d,a,d,c);
  }
  return{positions:new Float32Array(P),normals:new Float32Array(N),uvs:new Float32Array(U),indices:new Uint32Array(I),rings,sectors,range};
}

/** No overlapping top faces: each broad landing has one cap and a separate edge rim. */
export function goonworksPlatformGeometry(w,len){
  const P=[],N=[],U=[],I=[];
  const add=(x,z)=>{P.push(x,.045,z);N.push(0,1,0);U.push(x,z);return P.length/3-1;};
  const a=add(-w/2,-len/2),b=add(w/2,-len/2),c=add(-w/2,len/2),d=add(w/2,len/2);
  I.push(a,c,b,b,c,d);
  for(const side of[-1,1])for(const along of[false,true]){
    const corners=along?[[side*w/2,-len/2],[side*w/2,len/2]]:[[-w/2,side*len/2],[w/2,side*len/2]];
    const base=P.length/3,normal=along?[side,0,0]:[0,0,side];
    for(const height of[.045,-.6])for(const [x,z]of corners){P.push(x,height,z);N.push(...normal);U.push(x,z);}
    const reverse=along?side<0:side>0;
    if(reverse)I.push(base,base+3,base+1,base,base+2,base+3);else I.push(base,base+1,base+3,base,base+3,base+2);
  }
  return{positions:new Float32Array(P),normals:new Float32Array(N),uvs:new Float32Array(U),indices:new Uint32Array(I)};
}
