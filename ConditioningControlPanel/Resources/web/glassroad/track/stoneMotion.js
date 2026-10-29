// The simulation owns the angle. Drawing and ground probes read the same polygon.
export const STONE_HEX = Object.freeze([[-0.5,0],[-0.25,-0.5],[0.25,-0.5],[0.5,0],[0.25,0.5],[-0.25,0.5]].map(Object.freeze));
const polygons = new WeakMap();
export function stonePolygon(h) {
  const angle = h.angle || 0;
  let row = polygons.get(h);
  if (!row) { row={points:STONE_HEX.map(()=>[0,0])}; polygons.set(h,row); }
  if(row.angle===angle && row.w===h.w && row.len===h.len) return row.points;
  const c = Math.cos(angle), s = Math.sin(angle);
  STONE_HEX.forEach(([x,z],i)=>{row.points[i][0]=x*h.w*c-z*h.len*s; row.points[i][1]=x*h.w*s+z*h.len*c;});
  row.angle=angle; row.w=h.w; row.len=h.len;
  return row.points;
}
export function rotatingSpan(h, fixed, along = false, margin = 0) {
  const p = stonePolygon(h); let lo = -Infinity, hi = Infinity;
  for (let i=0;i<6;i++) {
    const a=p[i], b=p[(i+1)%6], dx=b[0]-a[0], dz=b[1]-a[1], len=Math.hypot(dx,dz);
    const nx=-dz/len, nz=dx/len, n=along?nz:nx;
    const d=nx*a[0]+nz*a[1]+margin-(along?nx:nz)*fixed;
    if (Math.abs(n)<1e-9) { if(d>1e-9) return null; }
    else if(n>0) lo=Math.max(lo,d/n); else hi=Math.min(hi,d/n);
  }
  return lo<=hi ? [lo,hi] : null;
}
export function updateStoneMotion(fields, dt, gentle = false, reset = false, timeS = null) {
  for (const f of fields || []) for (const h of f.stones) if (h.spin) {
    // Online shares the slower stone speed so Gentle never changes collision timing.
    if (h.swing) {
      h.motionPhase = reset ? 0 : Number.isFinite(timeS) ? Math.max(0, timeS) * h.spin * .5 / h.swing
        : (h.motionPhase || 0) + dt * h.spin * (gentle ? .5 : 1) / h.swing;
      h.angle = Math.sin(h.motionPhase) * h.swing;
    } else h.angle = reset ? 0 : (Number.isFinite(timeS) ? Math.max(0,timeS)*h.spin*0.5 : (h.angle || 0) + dt*h.spin*(gentle?0.5:1)) % (Math.PI*2);
  }
}
