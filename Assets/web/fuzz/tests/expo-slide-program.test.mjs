import assert from 'node:assert/strict';
import test from 'node:test';
import {restingSlide,slideIndex} from '../ui/expo/slide-program.js';

test('timeline draws detail, wide and close at exactly 30 and 70 percent of the actual recording',()=>{
  const row={set:'event',slides:['detail','wide','close']};
  for(const [fraction,index] of [[0,0],[.299999,0],[.30,1],[.699999,1],[.70,2],[1,2]])
    assert.equal(slideIndex(row,fraction),index);
  assert.equal(restingSlide(row),'wide');
});

test('a record with cues changes picture at those seconds of its recording and rests on its last picture',()=>{
  const row={set:'event',seconds:73.85,cues:[22.7,40.8,48.4],slides:['a','b','c','d']};
  for(const [at,index] of [[0,0],[22.69,0],[22.7,1],[40.79,1],[40.8,2],[48.39,2],[48.4,3],[73.85,3],[90,3]])
    assert.equal(slideIndex(row,at/73.85,at),index);
  // Without the clock it reads the progress against the row's own length.
  assert.equal(slideIndex(row,41/73.85),2);
  assert.equal(restingSlide(row),'d');
});

test('individual logs retain their program, and an entry with no illustration has no slide',()=>{
  const log={set:'log',slides:['portrait','arrival']};
  assert.equal(slideIndex(log,.499999),0);
  assert.equal(slideIndex(log,.5),1);
  assert.equal(restingSlide(log),'portrait');
  const voice={set:'pa',slides:[]};
  assert.equal(slideIndex(voice,.5),-1);
  assert.equal(restingSlide(voice),undefined);
});
