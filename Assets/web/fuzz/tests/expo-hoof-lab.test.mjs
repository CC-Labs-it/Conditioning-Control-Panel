import test from 'node:test';
import assert from 'node:assert/strict';
import {answerMask,board,boardHit,contactMask,dealHoofPhotos,hoofPictures,markCount,verifyHoof} from '../ui/expo/hoof-board.js';
import {hoofRefusals,needsFinger} from '../ui/expo/hoof-refusals.js';

test('each new deal has three llamas, one distinct alpaca, and five distinct ordinary pictures',()=>{
  assert.equal(hoofPictures.length,16);
  const used=new Set();let last=[],seed=2037;
  const sample=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed/4294967296;};
  for(let n=0;n<80;n++){
    const next=dealHoofPhotos(last,sample);assert.equal(new Set(next).size,9);
    assert.equal(markCount(answerMask(next)),3);
    assert.equal(next.filter(id=>id.startsWith('alpaca-')).length,1);
    assert.notDeepEqual(next,last);next.forEach(id=>used.add(id));last=next;
  }
  assert.equal(used.size,16);
  const fixed=()=>.1,first=dealHoofPhotos([],fixed);assert.notDeepEqual(dealHoofPhotos(first,fixed),first);
});
test('a finger hits one tile, while a hoof toggles three to five tile centres',()=>{
  assert.equal(contactMask(1.5,1.5,true),0b010111010);
  assert.equal(markCount(contactMask(.5,.5,true)),3);assert.equal(markCount(contactMask(.5,1.5,true)),4);
  assert.equal(contactMask(.5,2.5,false),1<<6);assert.equal(contactMask(-.1,1,false),0);
  const a=contactMask(.5,.5,true),b=contactMask(1.5,1.5,true);
  assert.equal((a^b)^a,b);
});
test('all 512 selection patterns are reachable on the visible board with real hoof presses',()=>{
  const masks=new Set();
  for(let px=0;px<=700;px+=12)for(let py=0;py<=board.height;py+=12){
    const at=boardHit(px/board.width,py/board.height,true);if(at?.mask)masks.add(at.mask);
  }
  assert.ok(masks.has(1));assert.ok(masks.has(1<<8));
  const visited=new Set([0]);
  for(const mask of masks)for(const previous of [...visited])visited.add(previous^mask);
  assert.equal(visited.size,512);
  assert.ok(boardHit(.75,.82,true).verify);
});
test('wrong verifies keep the board open, clear marks and escalate. Three right pictures earn either route',()=>{
  const state={hoof:'pictures',hoofWorn:true,hoofMask:0,hoofDeal:dealHoofPhotos(),misses:0};
  for(const cue of ['hoof_4','r5_hoof_circle','r5_hoof_off']){
    assert.deepEqual(verifyHoof(state),{correct:false,cue});assert.equal(state.hoofMask,0);
  }
  state.hoofMask=answerMask(state.hoofDeal);assert.deepEqual(verifyHoof(state),{correct:true,edge:true});
  state.hoof='pictures';state.hoofWorn=false;assert.deepEqual(verifyHoof(state),{correct:true,edge:false});
  assert.equal(verifyHoof(state),null);
});
test('precision fixtures refuse hooves while carrying, doors, turnstile and spitting remain available',()=>{
  for(const n of ['press_main','moved_button','loan_stop_switch','expo_replay_slot_01','expo_control_greenhouse','expo_display_grid','alpaca_button','shop_printer','till_display','annex_socket','annex_photo_loch','annex_stapler','feeding_slot','annex_docket'])assert.ok(needsFinger(n),n);
  assert.ok(needsFinger('annex_record','Hang face to the wall'));
  assert.ok(needsFinger('Tea round docket'));assert.equal(needsFinger('annex_tea','Carry the tea to the reading balcony'),false);
  assert.equal(needsFinger('annex_tea','Set down the tea'),false);
  for(const n of ['shop_door','annex_keeper_door','turnstile_arm','hoof_practice_button','hoof_display','habitat_glass','spit','annex_tea'])assert.equal(needsFinger(n),false,n);
});
test('each refused press slips, clops and captions, while one retained voice waits for a free slot and cooldown',async()=>{
  let slips=0,free=false;const captions=[],voice=[],api={walker:{zone:'hall'},say:t=>captions.push(t),narrationAvailable:()=>free,
    cue(id,options){voice.push(id);options.onStart();return Promise.resolve(true);}};
  const state={hoofWorn:true},f=hoofRefusals(api,state,()=>slips++);
  const press=()=>f.refuse({owner:{name:'press_main'}});for(let i=0;i<8;i++){assert.ok(press());f.tick(.02);}
  assert.equal(slips,8);assert.equal(captions.length,8);assert.equal(new Set(captions).size,5);assert.deepEqual(voice,[]);
  free=true;f.tick(1);await Promise.resolve();assert.deepEqual(voice,['r5_hoof_refusal_1']);
  f.tick(19.8);press();f.tick(.1);assert.equal(voice.length,1);
  f.tick(1);press();f.tick(.1);await Promise.resolve();assert.equal(voice.length,2);
  api.walker.zone='hoof';assert.equal(press(),false);
});
