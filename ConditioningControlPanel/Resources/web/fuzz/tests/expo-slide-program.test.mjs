import assert from 'node:assert/strict';
import test from 'node:test';
import {restingSlide,slideIndex} from '../ui/expo/slide-program.js';

test('timeline draws detail, wide and close at exactly 30 and 70 percent of the actual recording',()=>{
  const row={set:'event',slides:['detail','wide','close']};
  for(const [fraction,index] of [[0,0],[.299999,0],[.30,1],[.699999,1],[.70,2],[1,2]])
    assert.equal(slideIndex(row,fraction),index);
  assert.equal(restingSlide(row),'wide');
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
