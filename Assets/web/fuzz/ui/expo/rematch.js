// World positions in metres for the Rematch start, flag line and scoring radius.
export const rangeOrigin=[-66.28,0,94],flagDistances=[2.9,3.4,6.1];
export function atRematchStart(p){return Math.hypot(p.x-rangeOrigin[0],p.z-rangeOrigin[2])<2.5;}

export function rematchHint(room,p,mode,spitInActions=false){
  if(room!=='commons'||Math.abs(p.y-rangeOrigin[1])>1.8)return '';
  const lastFlag=rangeOrigin[2]-flagDistances[flagDistances.length-1];
  const closestZ=Math.max(lastFlag,Math.min(rangeOrigin[2],p.z));
  if(Math.hypot(p.x-rangeOrigin[0],p.z-closestZ)>3.2)return '';
  const start=atRematchStart(p);
  if(mode==='touch'){
    if(spitInActions)return start?'Open Actions, hold Spit, then release.':'From the judge, open Actions, hold Spit, then release.';
    return start?'Hold Spit, then release.':'From the judge, hold Spit, then release.';
  }
  const key=mode==='gamepad'?'RT / R2':'Q';
  return (start?'Hold ':'From the judge, hold ')+key+' and release to spit.';
}

