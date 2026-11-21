import test from 'node:test';
import assert from 'node:assert/strict';
import {createExpoGamepad} from '../ui/expo/gamepad.js';
import {createControls} from '../ui/expo/controls.js';

const makePad=index=>({index,id:'test-'+index,connected:true,mapping:'standard',axes:[0,0,0,0],buttons:Array(17).fill(0)});
const near=(a,b)=>assert.ok(Math.abs(a-b)<1e-7,`${a} versus ${b}`);

test('the radial walk retains its analog magnitude, kills drift and caps diagonal speed',()=>{
  const p=makePad(0),reader=createExpoGamepad(()=>[p]);
  p.axes=[.11,.1,NaN,Infinity];
  assert.equal(reader.poll().activity,false);
  p.axes=[.59,0,0,-1];
  near(reader.poll().moveX,.5);near(reader.poll().lookY,-1);
  p.axes=[1,1,0,0];const state=reader.poll();
  near(Math.hypot(state.moveX,state.moveY),1);near(state.moveX,state.moveY);
});

test('held controls stay with the old owner and a new press fires once',()=>{
  const p=makePad(1),reader=createExpoGamepad(()=>[null,p]);
  reader.poll();p.buttons[0]={pressed:true,value:1};p.buttons[2]=1;p.buttons[9]=1;p.buttons[1]=1;
  const first=reader.poll();assert.ok(first.jump&&first.interact&&first.start&&first.back);
  assert.equal(reader.poll().activity,false);
  p.axes[0]=1;reader.reset();
  assert.equal(reader.poll().activity,false);
  p.buttons.fill(0);p.axes.fill(0);reader.poll();
  p.buttons[12]=1;p.buttons[2]=1;p.axes[0]=.8;
  const next=reader.poll();assert.ok(next.up&&next.interact&&next.moveX>0);
  assert.equal(reader.poll().up,false);
});

test('another standard controller can take over and disconnect removes every held axis',()=>{
  const a=makePad(0),b=makePad(1),unknown={...makePad(2),mapping:''};
  let pads=[a,b,unknown];const reader=createExpoGamepad(()=>pads);
  reader.poll();a.axes[1]=-1;assert.equal(reader.poll().index,0);
  b.buttons[2]=1;const state=reader.poll();assert.equal(state.index,1);assert.ok(state.interact);
  assert.equal(reader.poll().index,1);
  pads=[null,null,unknown];assert.equal(reader.poll(),null);
  pads=[null,b];b.axes[1]=-1;assert.equal(reader.poll().activity,false);
  b.axes.fill(0);b.buttons.fill(0);reader.poll();b.axes[1]=-1;assert.equal(reader.poll().moveY,-1);
  assert.equal(createExpoGamepad(()=>{throw Error('policy');}).poll(),null);
});

test('loss of the selected controller re-arms a second player who was already holding controls',()=>{
  for (const notify of [false,true]) {
    const a=makePad(0),b=makePad(1);let pads=[a,b];
    const reader=createExpoGamepad(()=>pads);
    reader.poll();a.axes[1]=-1;a.buttons[0]=1;reader.poll();
    b.buttons[2]=1;assert.equal(reader.poll().index,1);
    if(notify)reader.disconnect(b.index);
    pads=[a,null];const handoff=reader.poll();
    assert.equal(handoff.index,0);assert.equal(handoff.activity,false);assert.equal(handoff.jump,false);
    a.axes.fill(0);a.buttons.fill(0);reader.poll();a.axes[1]=-.9;a.buttons[0]=1;
    assert.ok(reader.poll().jump);assert.ok(reader.poll().moveY<-.8);
    b.buttons[9]=1;pads=[a,b];assert.equal(reader.poll().index,0);
    b.buttons.fill(0);reader.poll();b.buttons[9]=1;
    const rejoined=reader.poll();assert.equal(rejoined.index,1);assert.ok(rejoined.start);
  }
});

function controlsFixture(initialInputMode){
  class Element extends EventTarget {
    style={};classList={add(){},remove(){}};
    setAttribute(){} append(){} remove(){} closest(){return null;}
    contains(el){return el===this;}
  }
  const win=new EventTarget(),doc=new EventTarget(),root=new Element(),canvas=new Element(),p=makePad(0);
  let focused=true,paused=false,pads=[p],x=0,z=0,jumps=0,uses=0,pauses=0;
  root.ownerDocument=doc;root.focus=()=>{doc.activeElement=root;};
  Object.assign(doc,{defaultView:win,activeElement:root,hasFocus:()=>focused,createElement:()=>new Element()});
  win.navigator={getGamepads:()=>pads};win.matchMedia=()=>({matches:false});
  const camera={rotation:{x:0,y:0,z:0}},modes=[];
  const controls=createControls({root,canvas,camera,initialInputMode,move:(dx,dz)=>{x+=dx;z+=dz;},
    onInputMode:m=>modes.push(m),onJump:()=>jumps++,onInteract:()=>uses++,
    onPause:()=>pauses++,isPaused:()=>paused});
  const tick=(n=1,dt=1/60)=>{for(let i=0;i<n;i++)controls.step(dt);};
  tick();
  const key=(type,code)=>{const e=new Event(type);Object.defineProperty(e,'code',{value:code});win.dispatchEvent(e);};
  return {p,controls,camera,tick,modes,key,state:()=>({x,z,jumps,uses,pauses}),
    focus:state=>{focused=state;},pause:state=>{paused=state;},unplug:()=>{pads=[];}};
}

test('normal controls use elapsed time for both sticks and share existing action callbacks',()=>{
  for(const hz of [30,60,120]){
    const f=controlsFixture();
    f.p.axes[1]=-1;f.tick(hz,1/hz);near(f.state().z,-4);
    f.p.axes[1]=0;f.p.axes[2]=1;f.tick(hz,1/hz);near(f.camera.rotation.y,-2.25);
    f.p.axes[2]=0;f.p.axes[3]=1;f.tick(hz,1/hz);near(f.camera.rotation.x,-1.45);
    f.p.axes[3]=0;f.p.buttons[2]=1;f.p.buttons[0]=1;f.tick(6);assert.equal(f.state().uses,1);assert.equal(f.state().jumps,1);
    f.p.buttons[9]=1;f.tick(6);assert.equal(f.state().pauses,1);
    f.p.buttons[9]=0;f.tick();f.p.buttons[1]=1;f.tick(6);assert.equal(f.state().pauses,2);
    assert.deepEqual(f.modes,['gamepad']);f.controls.dispose();
  }
});

test('loss of focus, pause, disconnection and teardown never carry movement or actions',()=>{
  const f=controlsFixture();
  f.p.axes[1]=-1;f.tick();const start=f.state().z;
  f.focus(false);f.p.buttons[0]=1;f.tick(5);f.focus(true);f.tick(5);
  near(f.state().z,start);assert.equal(f.state().jumps,0);
  f.p.axes.fill(0);f.p.buttons.fill(0);f.tick();f.p.axes[1]=-1;f.tick();
  assert.ok(f.state().z<start);const stop=f.state().z;
  f.pause(true);f.tick();f.pause(false);f.tick(4);near(f.state().z,stop);
  f.unplug();f.tick(5);near(f.state().z,stop);f.controls.dispose();f.tick();near(f.state().z,stop);
});

test('a held key keeps walking after pad disconnect and input hints follow the inherited mode',()=>{
  const f=controlsFixture('gamepad');
  assert.equal(f.controls.inputMode,'gamepad');f.key('keydown','KeyW');
  assert.equal(f.controls.inputMode,'keyboard');
  f.p.axes[0]=1;f.tick(60);near(Math.hypot(f.state().x,f.state().z),4);
  const {x,z}=f.state();f.unplug();f.tick(60);
  assert.equal(f.controls.inputMode,'keyboard');
  near(f.state().x,x);near(f.state().z,z-4);
  f.key('keyup','KeyW');const done=f.state();f.tick(60);
  assert.deepEqual(f.state(),done);f.controls.dispose();
});
