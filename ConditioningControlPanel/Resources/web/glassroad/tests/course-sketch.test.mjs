import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {COURSES} from '../track/catalog.js';
import {courseSketch} from '../hud/courseSketch.js';

test('each selectable course has a finite map and lap count from its real authored route',()=>{
  for(const course of COURSES){
    const json=JSON.parse(readFileSync(new URL('../tracks/'+course.id+'.json',import.meta.url),'utf8')),sketch=courseSketch(json);
    assert.ok(sketch.path.startsWith('M'));assert.ok(sketch.path.endsWith('Z'));assert.equal(sketch.laps,json.laps);assert.ok(Number(sketch.km)>1);
    const values=sketch.path.match(/-?[0-9]+(?:[.][0-9]+)?/g).map(Number);
    assert.ok(values.length<=444);assert.ok(values.every((n,i)=>Number.isFinite(n)&&n>=(i%2?6:8)&&n<=(i%2?82:172)));
  }
});
test('unusable route data produces no invented map',()=>{
  for(const bad of [null,{}, {nodes:[]}, {nodes:[{p:[NaN,0,0]}]}])assert.equal(courseSketch(bad),null);
});
