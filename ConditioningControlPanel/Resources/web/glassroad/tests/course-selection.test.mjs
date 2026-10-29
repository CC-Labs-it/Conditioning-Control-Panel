import {test} from 'node:test';
import assert from 'node:assert/strict';
import {COURSES,selectedCourse,courseUrl,courseRaceIntent} from '../track/catalog.js';

test('course selection admits only known local assets',()=>{
  assert.equal(selectedCourse('?track=keep').id,'keep');
  for(const query of ['', '?track=../../secret', '?track=KEEP'])assert.equal(selectedCourse(query).id,'threshold');
  assert.equal(selectedCourse('?track=keep&r=invite').online,true);
  assert.equal(selectedCourse('?track=threshold').online,true);
});

test('a deliberate chooser transition carries its selected solo race through navigation',()=>{
  for(const course of COURSES){
    const u=new URL(courseUrl('http://127.0.0.1:18952/?track=threshold&r=old-room&renderer=webgl2',course.id,{race:true}));
    assert.equal(selectedCourse(u.search).id,course.id);
    assert.equal(courseRaceIntent(u.search),course.id);
    assert.equal(u.searchParams.get('renderer'),'webgl2');
    assert.equal(u.searchParams.has('r'),false);
    // Once boot consumes the marker, refresh returns to the selected course's garage.
    u.searchParams.delete('start');
    assert.equal(courseRaceIntent(u.search),null);
    assert.equal(selectedCourse(u.search).id,course.id);
  }
  for(const query of ['','?track=keep','?start=race','?track=unknown&start=race','?track=keep&start=race&r=invite'])
    assert.equal(courseRaceIntent(query),null,query);
  assert.equal(new URL(courseUrl('http://localhost/?track=keep&start=race','threshold')).searchParams.has('start'),false);
});
test('switching course preserves preview settings but cannot carry an online room across layouts',()=>{
  const u=new URL(courseUrl('http://127.0.0.1:18898/?r=secret&lobby=http://localhost:9&room=http://localhost:8&uid=one&renderer=webgl2&chart=0','keep'));
  assert.equal(u.searchParams.get('track'),'keep');
  assert.equal(u.searchParams.get('renderer'),'webgl2');
  assert.equal(u.searchParams.get('chart'),'0');
  for(const key of ['r','lobby','room','uid'])assert.equal(u.searchParams.has(key),false);
  assert.equal(courseUrl(u.href,'missing'),null);
});
