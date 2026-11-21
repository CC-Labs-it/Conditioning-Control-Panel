import test from 'node:test';
import assert from 'node:assert/strict';
import { createSpeechSequence } from '../ui/expo/speech-sequence.js';

function studio() {
  const log=[], voices=new Set();
  const open=e=>{assert.equal(voices.size,0);voices.add(e.id);};
  const s=createSpeechSequence({
    start(e){open(e);log.push('start '+e.id);},
    suspend(e){voices.delete(e.id);log.push('pause '+e.id);},
    resume(e){open(e);log.push('resume '+e.id);},
    release(e,ok){voices.delete(e.id);log.push((ok?'finish ':'stop ')+e.id);}
  });
  return {s,log,voices};
}

test('announcements, pictures and exhibits keep their order across spoken planter interruptions',()=>{
  const {s,log}=studio(),event={id:'picture'},pa={id:'announcement'},exhibit={id:'exhibit'};
  const ten={id:'10',priority:'planter'},sixty={id:'60',priority:'planter'};
  s.submit(event);s.submit(pa);s.submit(exhibit);s.submit(ten);s.submit(sixty);
  assert.equal(s.interrupted,event);assert.equal(s.available,false);
  s.finish(ten);s.finish(sixty);s.finish(event);s.finish(pa);s.finish(exhibit);
  assert.equal(s.available,true);
  assert.deepEqual(log,['start picture','pause picture','start 10','finish 10','start 60','finish 60',
    'resume picture','finish picture','start announcement','finish announcement','start exhibit','finish exhibit']);
});

test('a paused visit holds a planter and a queued exhibit; Panic disposes all three',()=>{
  const {s,voices}=studio(),event={id:'picture'},p={id:'10',priority:'planter'},e={id:'exhibit'};
  s.submit(event);s.pause(true);s.submit(p);s.submit(e);assert.equal(voices.size,0);
  s.pause(false);assert.deepEqual([...voices],['10']);assert.equal(s.interrupted,event);
  s.pause(true);assert.equal(voices.size,0);s.dispose();s.pause(false);s.finish(p);
  assert.equal(s.current,null);assert.equal(s.interrupted,null);assert.equal(s.queue.length,0);
  assert.equal(voices.size,0);assert.equal(s.available,false);
});

test('a recording already at its end is retired while the planter is speaking',()=>{
  const {s,log}=studio(),event={id:'picture'},planter={id:'taunt',priority:'planter'},next={id:'next'};
  s.submit(event);s.submit(planter);s.submit(next);
  s.finish(event);assert.equal(s.current,planter);assert.equal(s.interrupted,null);
  s.finish(planter);s.finish(next);
  assert.deepEqual(log,['start picture','pause picture','start taunt','finish picture','finish taunt','start next','finish next']);
});
