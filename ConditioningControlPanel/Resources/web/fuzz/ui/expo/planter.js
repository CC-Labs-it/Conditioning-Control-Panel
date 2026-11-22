const cues = [[10,'planter_10'],[45,'planter_45'],[90,'planter_90']];
// The last line reaches "You may climb down now" about nine seconds in; the visitor is held until then.
const freeAt = 99;
export const planterLines = cues.map(([,id]) => id);

// Earlier baked rooms carry just the authored collider names.
const oldBeds = new Set(['gallery rain trough','rain garden trough','portal rain bed',
  'east conservatory grove','east workshop grove','west garden reading bay',
  'observation lounge rain bed','enclosure bed']);
export function isPlanterCollider(node) {
  return node.userData.expoPlanter === true || oldBeds.has(node.name.toLowerCase().replaceAll('_',' '));
}

export function createPlanterVisit(cue, claim = () => true) {
  let used=false, planter=null, origin=null, elapsed=0, next=0, pending=false;
  const free=()=>elapsed+.000001>=freeAt;
  function land(support,takeoff) {
    if (used || !support?.planter || !takeoff || takeoff.support?.planter) return;
    used=true;
    planter=support; origin=takeoff;
    const result=claim();
    if(result?.then){
      pending=true;
      result.then(allowed=>{pending=false;if(!allowed)leave();},()=>{pending=false;leave();});
    }else if(!result)leave();
  }
  function tick(seconds) {
    if (!planter || pending || free()) return;
    elapsed+=seconds;
    while (next<cues.length && elapsed+.000001>=cues[next][0]) {
      const id=cues[next++][1];
      cue?.(id);
    }
  }
  function leave() { planter=null; origin=null; }
  return {land,tick,leave,
    get support(){return planter;}, get origin(){return origin;},
    get held(){return !!planter && !free();},
    get released(){return !!planter && free();},
    state:()=>({used,held:!!planter && !free(),seconds:Math.min(elapsed,freeAt),cues:next})};
}
