/* Mounted CRT faces use the existing shared video owners, Loom programs and source-off lifetime. */
import { goonworksWorldPlan } from './goonworksWorld.js';
export function goonworksMediaLayout(track){
  const plan=goonworksWorldPlan(track);if(!plan)return null;
  return{track:'goonworks',slots:plan.mounts.map((m,i)=>({...m,id:'goonworks-media-'+m.id,mountId:m.id,kind:'landscape',program:Math.floor(i/2)%2?3:0}))};
}
export function goonworksSpiralLayout(track){
  if(track?.id!=='goonworks')return null;
  // The physical screen caverns carry Loom. No giant sky helix competes with the real ribbon road.
  return{track:'goonworks',gates:[]};
}
