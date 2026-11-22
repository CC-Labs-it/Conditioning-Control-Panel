import {trailLines} from './trail-lines.js';

const refusals=trailLines.filter(row=>row.id.startsWith('r5_hoof_refusal_'));
const controls=new Set(['press_main','press_stop','moved_button','loan_stop_switch','photo_release','alpaca_button',
  'exit_sale','till_display','framed_contract','feeding_slot','annex_desk_cap','annex_socket','heater_plug',
  'annex_stapler','annex_docket','Tea round docket','shop_printer','annex_hook','annex_corrections_desk']);

export function needsFinger(name,label=''){
  if(controls.has(name)||/^expo_(?:replay|review|control|display|press)_|^annex_photo_/.test(name))return true;
  // Some fittings share a model with an entirely different exhibit. Classify the operation there.
  return /\b(?:stapler|docket|hang face|hang the front|sign the form|file the form|turn a page)\b/i.test(label);
}

export function hoofRefusals(api,state,slip){
  let index=0,pending=null,lastVoice=-Infinity,clock=0,request=null;
  function refuse(action){
    const room=api.walker.zone;
    if(!state.hoofWorn||room==='hoof')return false;
    const label=typeof action.label==='function'?action.label():action.label;
    if(!needsFinger(action.owner.name,label))return false;
    const row=refusals[index++%refusals.length];
    slip();api.say(row.subtitle);
    if(!request&&!pending&&clock-lastVoice>=20)pending={room,id:row.id};
    return true;
  }
  function tick(dt){
    clock+=dt;
    if(!pending)return;
    if(!state.hoofWorn||pending.room!==api.walker.zone){pending=null;return;}
    if(!api.narrationAvailable?.())return;
    const {room,id}=pending;pending=null;
    const handle=api.cue(id,{zone:room,kind:'beat',roomWide:true,exhibit:'hoof refusal',replay:true,onStart:()=>{lastVoice=clock;}});
    request=handle;Promise.resolve(handle).finally(()=>{if(request===handle)request=null;});
  }
  return {refuse,tick};
}
