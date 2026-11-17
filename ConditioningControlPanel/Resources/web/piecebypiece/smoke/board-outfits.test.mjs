import test from 'node:test';
import assert from 'node:assert/strict';
import { createBus } from '../game/events.js';
import { createWear } from '../skins/wear.js';
import { clampLook, lookIds, normalLook, wireLook } from '../skins/look.js';

const board = 'chess.board.observatory', companion = 'chess.companion.emi';
const grants = [board,companion].map(id=>'cos.'+id);
const blank = {set:null,banner:null,insignia:null};

test('board and companion need owned exact ids and do not fetch attachment models',()=>{
  const look = {board,companion};
  assert.deepEqual(clampLook(look,[]),blank);
  assert.deepEqual(clampLook(look,grants),{...blank,...look});
  assert.deepEqual(lookIds(look),[]);
  assert.deepEqual(wireLook(look),{...blank,...look});
  assert.deepEqual(normalLook({board:'chess.board.unknown',companion:'chess.companion.unknown'}),blank);
});

test('board is local to the viewer and both Emis follow authoritative match seats',()=>{
  const bus=createBus(),posts=[],applied=[];
  let receive;
  const wear=createWear({bus,hosted:true,onHost:fn=>{receive=fn;},post:m=>posts.push(m),pieces:{setLooks:(looks,id)=>applied.push({looks,id})}});
  receive({type:'owned',grants});
  assert.ok(wear.choose('board',board));
  assert.ok(wear.choose('companion',companion));
  bus.emit('local',{mode:'online',me:'b'});
  bus.emit('looks',{white:{board:'chess.board.arcade',companion},black:{companion}});
  assert.equal(applied.at(-1).id,board);
  assert.equal(applied.at(-1).looks.w.companion,companion);
  assert.equal(applied.at(-1).looks.b.companion,companion);
  bus.emit('local',{mode:'watch'});
  bus.emit('looks',{white:{board:'chess.board.arcade',companion},black:{}});
  assert.equal(applied.at(-1).id,'chess.board.arcade');
  assert.equal(applied.at(-1).looks.w.companion,companion);
  assert.equal(applied.at(-1).looks.b.companion,undefined);
  wear.toMenu();
  assert.ok(wear.choose('companion',null));
  assert.equal(posts.at(-1).look.companion,null);
  assert.equal(posts.at(-1).look.board,board);
  assert.ok(wear.choose('board',null));
  assert.equal(posts.at(-1).look.board,null);
  receive({type:'owned',grants:[]});
  assert.equal(wear.boardId(),null);
  wear.dispose();
});
