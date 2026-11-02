/** A small map from the actual authored route, with elevation separated slightly for stacked courses. */
export function courseSketch(course) {
  const points=course?.nodes?.map(n=>n.p).filter(p=>p?.length===3&&p.every(Number.isFinite));
  if(!points?.length)return null;
  let metres=0;for(let i=1;i<points.length;i++)metres+=Math.hypot(...points[i].map((v,k)=>v-points[i-1][k]));
  if(course.loop&&points.length>1)metres+=Math.hypot(...points[0].map((v,k)=>v-points.at(-1)[k]));
  const projected=points.map(([x,y,z])=>[x+y*.22,z-y*.18]);
  const min=[0,1].map(k=>Math.min(...projected.map(p=>p[k]))),max=[0,1].map(k=>Math.max(...projected.map(p=>p[k])));
  const scale=Math.min(164/Math.max(1,max[0]-min[0]),76/Math.max(1,max[1]-min[1]));
  const fit=([x,y])=>[(90+(x-(min[0]+max[0])*.5)*scale).toFixed(1),(44+(y-(min[1]+max[1])*.5)*scale).toFixed(1)];
  const path=projected.filter((_,i)=>i%Math.max(1,Math.ceil(points.length/220))===0||i===points.length-1).map((v,i)=>(i?'L':'M')+fit(v).join(' ')).join(' ')+(course.loop?' Z':'');
  return {path,start:fit(projected[0]),km:(metres/1000).toFixed(1),laps:course.laps};
}
