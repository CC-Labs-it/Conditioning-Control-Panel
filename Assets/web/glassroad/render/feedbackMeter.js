/* Source-space cassette meters. The nine bars keep their bases seated in the front bezel. */
export const METER_HEIGHTS = [.048,.079,.112,.086,.055,.091,.122,.085,.049];
export function advanceMeterPhase(phase,dt,signals) {
  if(signals.motionOff)return 0;
  if(signals.near===false)return phase;
  const step=Number.isFinite(dt)?Math.min(.1,Math.max(0,dt)):0;
  const boost=Number.isFinite(signals.boost)?Math.min(1,Math.max(0,signals.boost)):0;
  // Both meter frequencies are multiples of .01, so this wrap preserves their exact phase.
  return (phase+step*(signals.gentle?.38:1)*(1+boost*.85))%(Math.PI*200);
}
export function meterBar(x,y,z) {
  if (z < -.687 || z > -.679 || y < .2639) return -1;
  const i = Math.round(x / .088 + 4);
  return i >= 0 && i < 9 && Math.abs(x - (i-4)*.088) <= .021 && y <= .264 + METER_HEIGHTS[i] + .0001 ? i : -1;
}
export function meterScale(index, time, speed = 0, gentle = false) {
  const a = Math.sin(time * (gentle ? .8 : 2.4) + index * .83);
  const b = Math.sin(time * (gentle ? .47 : 1.37) + index * 1.71);
  return gentle ? .70 + .16*a + .08*b : .62 + .25*a + .13*b + Math.min(1,Math.max(0,speed))*.04;
}
export function createFeedbackMeter(meshes) {
  const entries = [], census = new Uint32Array(9);
  for (const mesh of meshes) {
    const source = mesh.geometry, position = source.getAttribute('position'), tagged = [];
    if (!position) continue;
    for (let v=0;v<position.count;v++) {
      const bar = meterBar(position.getX(v),position.getY(v),position.getZ(v));
      if (bar >= 0) { tagged.push(v,bar,position.getY(v)); census[bar]++; }
    }
    if (!tagged.length) continue;
    const geometry = source.clone(); mesh.geometry = geometry;
    entries.push({mesh,source,geometry,position:geometry.getAttribute('position'),tagged});
  }
  let disposed = false,phase=0;
  return {census,get phase(){return phase;}, update(dt, signals) {
    if(disposed || (signals.near === false && !signals.motionOff))return;
    phase=advanceMeterPhase(phase,dt,signals);
    for (let e=0;e<entries.length;e++) {
      const {position,tagged}=entries[e];
      for(let j=0;j<tagged.length;j+=3) {
        const scale=signals.motionOff?1:meterScale(tagged[j+1],phase,signals.speed,signals.gentle);
        position.setY(tagged[j],.264+(tagged[j+2]-.264)*scale);
      }
      position.needsUpdate=true;
    }
  },dispose() {
    if(disposed)return;disposed=true;
    for(const e of entries){if(e.mesh.geometry===e.geometry)e.mesh.geometry=e.source;e.geometry.dispose();}
  }};
}
