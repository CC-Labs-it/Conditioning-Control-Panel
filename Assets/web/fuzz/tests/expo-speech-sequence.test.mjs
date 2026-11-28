import test from 'node:test';
import assert from 'node:assert/strict';
import { createSpeechSequence, createExhibitBeat } from '../ui/expo/speech-sequence.js';
import {contactMask,markHoof} from '../ui/expo/hoof-board.js';

function studio(){
  let clock=0, zone='hoof', voice=null;
  const started=[], dismissed=[];
  const s=createSpeechSequence({
    now:()=>clock, nearby:e=>e.zone===zone,
    start(e){assert.equal(voice,null);voice=e.id;started.push(e.id);},
    suspend(){voice=null;},
    resume(e){assert.equal(voice,null);voice=e.id;},
    release(e,ok){if(voice===e.id)voice=null;if(!ok)dismissed.push(e.id);e.resolve(ok);}
  });
  const cue=(id,options={})=>new Promise(resolve=>s.submit({id,zone,exhibit:'hoof',...options,resolve}));
  return {s,cue,started,dismissed,advance:ms=>clock+=ms,leave:()=>zone='hall',voice:()=>voice};
}

test('a beat takes the waiting slot and holds ambient speech until it finishes',()=>{
  const {s,cue,started,dismissed}=studio();
  cue('ident');cue('screen',{kind:'ambient'});cue('hoof instruction',{kind:'beat'});
  assert.equal(s.queue.length,1);s.finish(s.current);cue('passing gallery',{kind:'ambient'});
  assert.equal(s.queue.length,0);s.finish(s.current);
  assert.deepEqual(started,['ident','hoof instruction']);
  assert.deepEqual(dismissed,['screen','passing gallery']);
});

test('fresh reactions replace the same exhibit, expire after 1.2 seconds and leave no backlog',()=>{
  const {s,cue,started,dismissed,advance}=studio();
  cue('first');cue('old',{kind:'react'});cue('new',{kind:'react'});
  advance(1201);s.finish(s.current);assert.equal(s.available,true);
  assert.deepEqual(started,['first']);assert.deepEqual(dismissed,['old','new']);
});

test('leaving cancels a waiting beat while the playing line finishes, including between frame updates',async()=>{
  const {s,cue,started,leave}=studio();
  const one=cue('playing',{kind:'beat'}),two=cue('waiting',{kind:'beat'});
  leave();s.finish(s.current);
  assert.equal(await one,true);assert.equal(await two,false);assert.deepEqual(started,['playing']);
  cue('new room');const pending=cue('planter',{kind:'beat'});s.cancel(e=>e.id==='planter');
  assert.equal(await pending,false);assert.equal(s.current.id,'new room');
});

test('pause, resume, Panic and duplicate native end events keep a single speaker',async()=>{
  const {s,cue,voice}=studio();
  const first=cue('screen',{kind:'beat'}),one=s.current,next=cue('planter',{kind:'beat'});
  s.pause(true);assert.equal(voice(),null);s.pause(false);assert.equal(voice(),'screen');
  s.finish(one);s.finish(one);assert.equal(voice(),'planter');s.pause(true);s.dispose();s.pause(false);
  assert.equal(voice(),null);assert.equal(s.available,false);
  assert.equal(await first,true);assert.equal(await next,false);
});

test('the picture board marks and unmarks immediately while the single speaker is explaining it',async()=>{
  const {s,cue,started}=studio(),visit={hoofMask:0,hoofWorn:true};
  const instruction=cue('r5_hoof_verify',{kind:'beat'});
  const centre=contactMask(1.5,1.5,true),corner=contactMask(.5,.5,true);
  assert.equal(markHoof(visit,centre),5);assert.equal(visit.hoofMask,centre);
  assert.equal(markHoof(visit,corner),3);assert.equal(visit.hoofMask,centre^corner);
  markHoof(visit,centre);assert.equal(visit.hoofMask,corner);
  assert.deepEqual(started,['r5_hoof_verify']);assert.equal(s.current.id,'r5_hoof_verify');
  s.finish(s.current);assert.equal(await instruction,true);assert.equal(visit.hoofMask,corner);
});

test('a visitor can return after abandoning a multi-part instruction without a stale continuation',async()=>{
  const {s,cue,started}=studio(),beat=createExhibitBeat(cue);
  let page=0;
  const run=()=>beat.run(['introduction','test'],{zone:'hoof'},()=>page++);
  const abandoned=run();beat.leave();s.finish(s.current);assert.equal(await abandoned,false);
  assert.deepEqual(started,['introduction']);assert.equal(page,0);
  const resumed=run();s.finish(s.current);await Promise.resolve();s.finish(s.current);
  assert.equal(await resumed,true);assert.equal(page,1);
});
