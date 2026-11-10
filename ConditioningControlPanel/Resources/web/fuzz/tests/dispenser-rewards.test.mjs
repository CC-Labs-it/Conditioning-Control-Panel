import test from 'node:test';
import assert from 'node:assert/strict';
import {PRIZES,rollPrize,prizePay} from '../sim/dispenser-rewards.js';
import {dispenserState,dispense,dispenserStep,catchPrize,DROP_MS,CATCH_MS} from '../sim/arcade.js';
import {createRun,earn,sideLump} from '../sim/economy.js';
test('four prizes have exact rarity weights and increasing rare payouts',()=>{
 const counts={red:0,cyan:0,purple:0,gold:0};for(let i=0;i<10000;i++)counts[rollPrize(()=>(i+.5)/10000)]++;
 assert.deepEqual(counts,{red:4500,cyan:4500,purple:800,gold:200});
 for(const [color,mult] of [['red',1],['cyan',1],['purple',4],['gold',10]])assert.equal(prizePay(color,{fps:10,click:2}),120*mult);
});
test('a crank commits one outcome through restore, catch and missed prizes',()=>{
 for(const [roll,color]of [[.2,'red'],[.6,'cyan'],[.95,'purple'],[.99,'gold']]){
  let s=dispenserState(),calls=0;assert(dispense(s,30000,()=>{calls++;return roll;}));
  assert.equal(dispense(s,30000,()=>{calls++;return 0;}),false);assert.equal(calls,1);
  dispenserStep(s,3000);s=dispenserState(JSON.parse(JSON.stringify(s)));assert.equal(s.prize,color);
  dispenserStep(s,DROP_MS-3000);assert(catchPrize(s));assert.equal(catchPrize(s),false);assert.equal(s.prize,color);
  dispenserStep(s,30000);dispense(s,30000,()=>roll);dispenserStep(s,DROP_MS+CATCH_MS);assert.equal(catchPrize(s),false);assert.equal(s.missed,1);
 }
 assert.equal(dispenserState({prize:'invalid'}).prize,'red');
});
test('dispenser rarity multipliers survive an exhausted shared side budget',()=>{
 const run=createRun();run.owned.dispenser=1;run.order.push('dispenser');
 for(let t=0;t<600;t++){run.clock+=1000;earn(run,1000);}  // its own toy budget (CONFIG.TOY_SHARE), filled by play
 sideLump(run,1e6,'other');const before={...run.side};
 for(const color of Object.keys(PRIZES)){const value=prizePay(color,{fps:10,click:2});assert.equal(sideLump(run,value,'dispenser'),value);}
 assert.deepEqual(run.side,before);
});
