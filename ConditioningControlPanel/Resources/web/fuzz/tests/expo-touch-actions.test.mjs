import test from 'node:test';
import assert from 'node:assert/strict';
import {createTouchActions} from '../ui/expo/touch-actions.js';

function phone(){
  let paused=false,rapidUses=0,jumps=0,charges=0,releases=0;
  class Element {
    children=[];attributes={};hidden=false;className='';
    setAttribute(name,value){this.attributes[name]=value;}
    append(...nodes){this.children.push(...nodes);}
    remove(){} focus(){} setPointerCapture(){}
  }
  const doc={defaultView:{performance:{now:()=>0}},createElement:()=>Object.assign(new Element(),{ownerDocument:doc})};
  const root=doc.createElement('div');
  const actions=createTouchActions(root,{active:()=>!paused,canMove:()=>!paused,interact:()=>rapidUses++,jump:()=>jumps++,
    spit:{charge:()=>charges++,release:()=>releases++,cancel(){}}});
  const group=root.children.find(n=>n.className.includes('expo-thumb-actions'));
  const [use,jump,spit,more]=group.children;
  function event(extra={}){return {pointerType:'touch',pointerId:1,clientX:0,clientY:0,detail:1,preventDefault(){},stopPropagation(){},...extra};}
  return {actions,more,use,jump,spit,event,stats:()=>({rapidUses,jumps,charges,releases}),pause:()=>{paused=true;}};
}

test('walking has one actions control, with jump and spit available both on demand and at their exhibits',()=>{
  const {actions,more,use,jump,spit,event,stats}=phone();
  assert.ok(use.hidden&&jump.hidden&&spit.hidden&&!more.hidden);
  more.onpointerdown(event());more.onpointerup(event());
  assert.ok(use.hidden&&!jump.hidden&&!spit.hidden);
  more.onclick(event());
  assert.ok(!jump.hidden&&!spit.hidden);
  assert.equal(more.attributes['aria-expanded'],'true');
  jump.onpointerdown(event());jump.onclick(event());assert.equal(stats().jumps,1);
  actions.close();actions.setAction('',{jump:true});assert.ok(!jump.hidden&&spit.hidden&&use.hidden);
  actions.setAction('',{spit:true});assert.ok(jump.hidden&&!spit.hidden&&use.hidden);
  spit.onpointerdown(event());actions.setAction('Read the board',{spit:true});
  assert.ok(!spit.hidden&&use.hidden);
  spit.onpointerup(event());assert.ok(spit.hidden&&!use.hidden);
  assert.deepEqual(stats(),{rapidUses:0,jumps:1,charges:1,releases:1});
});

test('a named touch action fires once, a sliding finger cancels, and pause clears an unfinished tap',()=>{
  const {actions,use,event,stats,pause}=phone();actions.setAction('Enter the Grand Hall');
  assert.equal(use.textContent,'Enter the Grand Hall');
  use.onpointerdown(event());use.onpointerup(event());use.onclick(event());
  assert.equal(stats().rapidUses,1);
  use.onpointerdown(event());use.onpointerup(event({clientX:20}));use.onclick(event());
  assert.equal(stats().rapidUses,1);
  use.onpointerdown(event());use.onpointermove(event({clientY:24}));use.onpointerup(event());
  assert.equal(stats().rapidUses,1);
  use.onpointerdown(event());actions.close();pause();use.onpointerup(event());
  assert.equal(stats().rapidUses,1);
});
