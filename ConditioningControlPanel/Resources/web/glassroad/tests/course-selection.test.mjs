import {test} from 'node:test';
import assert from 'node:assert/strict';
import {selectedCourse,courseUrl} from '../track/catalog.js';

test('course selection admits only known local assets',()=>{
  assert.equal(selectedCourse('?track=keep').id,'keep');
  for(const query of ['', '?track=../../secret', '?track=KEEP'])assert.equal(selectedCourse(query).id,'threshold');
  assert.equal(selectedCourse('?track=keep&r=invite').online,false);
  assert.equal(selectedCourse('?track=threshold').online,true);
});
test('switching course preserves preview settings but cannot carry an online room across layouts',()=>{
  const u=new URL(courseUrl('http://127.0.0.1:18898/?r=secret&lobby=http://localhost:9&room=http://localhost:8&uid=one&renderer=webgl2&chart=0','keep'));
  assert.equal(u.searchParams.get('track'),'keep');
  assert.equal(u.searchParams.get('renderer'),'webgl2');
  assert.equal(u.searchParams.get('chart'),'0');
  for(const key of ['r','lobby','room','uid'])assert.equal(u.searchParams.has(key),false);
  assert.equal(courseUrl(u.href,'missing'),null);
});
