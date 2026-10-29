import {test} from 'node:test';
import assert from 'node:assert/strict';
import {KARTS,LOCKED,DEFAULT,cleanGarage,loadGarage,stepKart,routeTo,raceLook,skinFromWire} from '../hud/garageState.js';
import {normLook,TYPES,decodeMsg,MSG_MAX_BYTES} from '../net/protocol.js';

test('garage: invalid saved state cannot select locked karts, unknown outfits or unbounded colors',()=>{
  const s=cleanGarage({kart:'undertow',outfit:'labcoat',primary:'red',secondary:'#123456',pattern:99,skin:'x'.repeat(200)});
  assert.equal(s.kart,DEFAULT.kart);assert.equal(s.outfit,'builder');assert.equal(s.primary,DEFAULT.primary);assert.equal(s.secondary,'#123456');assert.equal(s.pattern,0);assert.equal(s.skin.length,32);
  assert.equal(loadGarage({getItem(){throw Error('denied');}}).kart,DEFAULT.kart);
});
test('garage: arrows skip both locks in either direction and a direct click uses the shorter arc',()=>{
  const dark=new Set(['velvet-lock','undertow']),feedback=KARTS.findIndex(k=>k[0]==='feedback'),petal=KARTS.findIndex(k=>k[0]==='petal');
  assert.deepEqual(stepKart(feedback,1,dark),{index:petal,steps:3});
  assert.deepEqual(stepKart(petal,-1,dark),{index:feedback,steps:-3});
  assert.equal(routeTo(0,6,dark),null);assert.equal(routeTo(0,0,dark),null);assert.deepEqual(routeTo(5,1,dark),{index:1,steps:-4});
  let index=0;const seen=new Set();for(let i=0;i<8;i++){index=stepKart(index,1,dark).index;seen.add(KARTS[index][0]);}
  assert.equal(seen.size,8);assert.ok([...seen].every(id=>!dark.has(id)));
  // nothing owned: only the free kart, so every arrow and click is refused
  const free=KARTS.findIndex(k=>!LOCKED.has(k[0]));
  assert.equal(stepKart(free,1),null);assert.equal(stepKart(free,-1),null);assert.equal(routeTo(free,0),null);
});
test('garage: six colors and patterns round-trip in a fixed 37-character appearance field',()=>{
  const s={...DEFAULT,original:false,pattern:3,outfit:'builder'},look=raceLook(s);
  assert.equal(look.skin.length,37);assert.equal(look.outfit,'emi-outfit-builder');assert.ok(TYPES.look(look));
  const restored=skinFromWire(normLook(look).skin);for(const k of ['primary','secondary','antenna','hands','shoes','face','pattern','original'])assert.equal(restored[k],s[k]);
  assert.ok(!('skin'in normLook({...look,skin:'a'.repeat(10000)})));assert.equal(TYPES.look({...look,skin:'<script>'}),false);
  assert.equal(skinFromWire('no').original,true);assert.ok(!('skin'in raceLook(DEFAULT)));
});
test('garage: look messages remain bounded and reject injected skin fields',()=>{
  const look=raceLook({...DEFAULT,original:false,pattern:2});
  assert.ok(decodeMsg(JSON.stringify({t:'look',...look}),'c2r'));
  assert.ok(!decodeMsg(JSON.stringify({t:'look',...look,skin:look.skin+'x'}),'c2r'));
  const seats=Array.from({length:8},(_,seat)=>({seat,kind:'human',name:'x'.repeat(32),look}));
  assert.ok(new TextEncoder().encode(JSON.stringify({t:'welcome',seats})).length<MSG_MAX_BYTES,'full grid cosmetic payload fits the frame budget');
});
