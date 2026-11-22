import assert from 'node:assert/strict';
import test from 'node:test';
import {createBuildingPlant} from '../ui/expo/building-plant.js';

const idle={zone:'hall',projectors:0,voices:0};
test('the greenhouse transfers its fixed water inventory over a two-hour visit',()=>{
  const plant=createBuildingPlant(),p=plant.state;
  for(let i=0;i<7200;i++){
    if(i%43===0)plant.pulse();
    plant.advance(1,idle);
    assert(Math.abs(p.tank+p.bed+p.returnTank-96)<1e-8);
    assert(p.tank>=0&&p.bed>=0&&p.returnTank>=0);
    assert.equal(p.roof+p.batteryWatts,p.watts);
  }
  assert(p.history.length===31);
});
test('manual irrigation immediately increases supply, lasts ten active seconds and ignores repeat presses',()=>{
  const plant=createBuildingPlant(),p=plant.state;
  assert(plant.pulse());assert.equal(p.delivery,6);assert.equal(plant.pulse(),false);
  plant.advance(9.75,idle);assert.equal(p.delivery,6);
  plant.advance(0,idle);assert.equal(p.time,9.75);
  plant.advance(.25,idle);assert(p.delivery<6);assert(plant.pulse());assert.equal(p.pulseCount,2);
});
test('actual live lamps, voices and visitor location affect their own electrical circuits',()=>{
  const plant=createBuildingPlant(),p=plant.state;
  plant.advance(.25,idle);const hall=p.circuits.lighting,services=p.circuits.services;
  plant.advance(.25,{zone:'service',projectors:2,voices:1});
  assert.equal(p.circuits.projectors,224);assert.equal(p.circuits.services,services+14+46);
  assert(p.circuits.lighting<hall);
  assert.equal(p.watts,Object.values(p.circuits).reduce((a,b)=>a+b,0));
});
test('model timing is independent of rendering cadence, and the roof test physically opens the intake',()=>{
  const slow=createBuildingPlant(),fast=createBuildingPlant();
  for(let i=0;i<48;i++)slow.advance(.25,idle);
  for(let i=0;i<120;i++)fast.advance(.1,idle);
  assert.deepEqual(slow.state,fast.state);
  slow.ventTest();const before=slow.state.air;slow.advance(5,idle);assert(slow.state.air>before+.25);
});
