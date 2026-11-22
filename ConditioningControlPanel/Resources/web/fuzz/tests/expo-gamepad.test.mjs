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

function controlsFixture(initialInputMode,cameraOptions={},trail){
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
  const camera={rotation:{x:0,y:0,z:0}},modes=[],hits=[];
  canvas.getBoundingClientRect=()=>({left:0,top:0,width:200,height:100});
  const controls=createControls({root,canvas,camera,initialInputMode,cameraOptions,trail,move:(dx,dz)=>{x+=dx;z+=dz;},
    onInputMode:m=>modes.push(m),onJump:()=>jumps++,onInteract:point=>{uses++;hits.push(point);},
    onPause:()=>pauses++,isPaused:()=>paused});
  let now = 0;
  win.performance = {now: () => now};
  const tick=(n=1,dt=1/60)=>{for(let i=0;i<n;i++){now+=dt*1000;controls.step(dt);}};
  tick();
  const key=(type,code,repeat=false,extra={})=>{const e=new Event(type,{cancelable:true});Object.assign(e,{code,repeat,...extra});win.dispatchEvent(e);return e.defaultPrevented;};
  const pointer=(type,x,y,device='mouse')=>{const ev=new Event(type,{cancelable:true});Object.assign(ev,{pointerType:device,pointerId:1,button:0,clientX:x,clientY:y});canvas.dispatchEvent(ev);};
  return {p,controls,camera,tick,modes,key,doc,canvas,pointer,hits,state:()=>({x,z,jumps,uses,pauses}),
    focus:state=>{focused=state;},pause:state=>{paused=state;},unplug:()=>{pads=[];}};
}

test('camera arrows turn and tilt without walking, while Shift runs and A still strafes',()=>{
  const f=controlsFixture('keyboard',{arrowMode:'turn'});
  f.key('keydown','ArrowLeft');f.key('keydown','ArrowDown');f.tick(30);
  near(f.camera.rotation.y,.9);near(f.camera.rotation.x,-.6);near(f.state().x,0);near(f.state().z,0);
  f.key('keyup','ArrowLeft');f.key('keyup','ArrowDown');f.key('keydown','ShiftRight');f.key('keydown','KeyA');
  f.tick(60);near(Math.hypot(f.state().x,f.state().z),6);
  f.controls.applyCameraOptions({arrowMode:'strafe'});const old=f.state();
  f.tick(60);assert.deepEqual(f.state(),old);
  f.key('keydown','ArrowUp');f.tick(60);near(Math.hypot(f.state().x-old.x,f.state().z-old.z),4);
  f.controls.dispose();
});

test('free-cursor E and taps use the hover, while a drag looks and consumes the tap',()=>{
  const f=controlsFixture('mouse',{dragLook:true,invertLook:true});let requests=0;
  f.canvas.requestPointerLock=()=>{requests++;};
  f.pointer('pointermove',150,25);f.key('keydown','KeyW');f.key('keyup','KeyW');
  assert.equal(f.controls.freePointer,true);assert.deepEqual(f.controls.aimPoint,{x:.5,y:.5});
  f.key('keydown','KeyE');f.key('keyup','KeyE');
  f.pointer('pointerdown',75,25);f.pointer('pointermove',79,29);f.pointer('pointerup',75,25);
  assert.deepEqual(f.hits,[{x:.5,y:.5},{x:-.25,y:.5}]);near(f.camera.rotation.y,0);
  f.pointer('pointerdown',20,20,'touch');f.pointer('pointermove',60,40,'touch');f.pointer('pointerup',60,40,'touch');
  near(f.camera.rotation.y,.12);near(f.camera.rotation.x,.06);assert.equal(f.hits.length,2);
  f.pointer('pointerdown',50,50,'pen');f.pointer('pointerup',50,50,'pen');assert.deepEqual(f.hits[2],{x:-.5,y:0});
  assert.equal(requests,0);f.controls.dispose();
});

test('the capturing click cannot operate an exhibit, subsequent captured clicks use the crosshair, and Escape pauses',()=>{
  const f=controlsFixture('mouse');let requests=0;
  f.canvas.requestPointerLock=()=>{requests++;};
  f.pointer('pointerdown',150,25);f.pointer('pointerup',150,25);
  assert.equal(requests,1);assert.equal(f.hits.length,0);
  f.doc.pointerLockElement=f.canvas;f.doc.dispatchEvent(new Event('pointerlockchange'));
  assert.ok(f.controls.captured);assert.equal(f.controls.aimPoint,null);
  const movement=new Event('mousemove');Object.assign(movement,{movementX:30,movementY:-20});f.doc.dispatchEvent(movement);
  near(f.camera.rotation.y,-.066);near(f.camera.rotation.x,.044);
  f.pointer('pointerdown',150,25);f.pointer('pointerup',150,25);
  assert.deepEqual(f.hits,[undefined]);
  f.doc.pointerLockElement=null;f.doc.dispatchEvent(new Event('pointerlockchange'));assert.equal(f.state().pauses,1);
  f.controls.dispose();
});

test('unavailable mouse capture falls back to coordinate taps and late grants release after a settings change or disposal',()=>{
  const f=controlsFixture('mouse');let requests=0,exits=0;
  f.canvas.requestPointerLock=()=>{requests++;};
  f.doc.exitPointerLock=()=>{exits++;f.doc.pointerLockElement=null;f.doc.dispatchEvent(new Event('pointerlockchange'));};
  for(let i=0;i<4;i++){
    f.pointer('pointerdown',60,60);f.doc.dispatchEvent(new Event('pointerlockerror'));f.pointer('pointerup',60,60);
  }
  assert.equal(requests,4);assert.equal(f.hits.length,0);assert.ok(f.controls.freePointer);
  f.pointer('pointerdown',150,25);f.pointer('pointerup',150,25);assert.deepEqual(f.hits,[{x:.5,y:.5}]);
  f.controls.applyCameraOptions({dragLook:false});f.pointer('pointerdown',50,50);
  f.controls.applyCameraOptions({dragLook:true});
  f.doc.pointerLockElement=f.canvas;f.doc.dispatchEvent(new Event('pointerlockchange'));
  assert.equal(exits,1);assert.equal(f.state().pauses,0);assert.ok(!f.controls.captured);
  f.controls.applyCameraOptions({dragLook:false});f.pointer('pointerdown',50,50);f.controls.dispose();
  f.doc.pointerLockElement=f.canvas;f.doc.dispatchEvent(new Event('pointerlockchange'));
  assert.equal(exits,2);assert.equal(f.doc.pointerLockElement,null);assert.equal(f.state().pauses,0);
});

function captureStub(f,delayExit=false){
  let requested=0,exited=0;
  const change=owner=>{f.doc.pointerLockElement=owner;f.doc.dispatchEvent(new Event('pointerlockchange'));};
  f.canvas.requestPointerLock=()=>{requested++;};
  f.doc.exitPointerLock=()=>{exited++;if(!delayExit)change(null);};
  return {gain:()=>change(f.canvas),lose:()=>change(null),requests:()=>requested,exits:()=>exited};
}

test('Escape opens one pause in either order of keyboard and pointer-unlock events',()=>{
  for(const keyFirst of [false,true]){
    const f=controlsFixture('mouse'),lock=captureStub(f,true);
    f.pointer('pointerdown',80,30);lock.gain();
    if(keyFirst){
      assert.ok(f.key('keydown','Escape'));
      f.pause(true);lock.lose();
    }else{
      lock.lose();f.pause(true);
      assert.ok(f.key('keydown','Escape'),'the late keydown cannot reach the document pause toggle');
    }
    assert.equal(f.state().pauses,1);
    assert.ok(f.key('keydown','Escape',true));f.key('keyup','Escape');
    assert.ok(!f.key('keydown','Escape'),'a second separate Escape can resume through the normal pause UI');
    f.controls.dispose();
  }
  const f=controlsFixture('mouse'),lock=captureStub(f);
  f.pointer('pointerdown',80,30);lock.gain();lock.lose();f.pause(true);
  f.key('keyup','Escape');f.tick(19);
  assert.ok(f.key('keydown','Escape',false,{keyCode:27,which:27}),'a delayed host replay is consumed once');
  assert.ok(!f.key('keydown','Escape',false,{keyCode:27,which:27}));
  f.controls.dispose();
});

test('the trail releases a captured mouse and closes on Escape without closing the visit',()=>{
  const trail={opened:false,toggle(){this.opened=!this.opened;if(this.opened)f.controls.reset();},handlePad(){}};
  const f=controlsFixture('mouse',{},trail),lock=captureStub(f,true);
  f.pointer('pointerdown',80,30);lock.gain();assert.ok(f.key('keydown','KeyT'));
  assert.ok(trail.opened);assert.equal(lock.exits(),1);
  f.key('keydown','Escape');assert.ok(!trail.opened);lock.lose();
  assert.equal(f.state().pauses,0);assert.equal(f.hits.length,0);
  f.pointer('pointerdown',80,30);lock.gain();assert.ok(f.controls.captured);
  assert.equal(f.hits.length,0);f.controls.dispose();lock.lose();
});

test('a touch or controller takeover retires pending capture and preserves the first new touch',()=>{
  const f=controlsFixture('mouse'),lock=captureStub(f,true);
  f.pointer('pointerdown',80,30);f.pointer('pointerdown',150,25,'touch');
  lock.gain();lock.lose();f.pointer('pointerup',150,25,'touch');
  assert.deepEqual(f.hits,[{x:.5,y:.5}]);assert.equal(f.state().pauses,0);
  f.pointer('pointerdown',80,30);lock.gain();f.p.axes[0]=1;f.tick();
  assert.equal(f.controls.inputMode,'gamepad');assert.equal(lock.exits(),2);lock.lose();
  f.p.axes[0]=0;f.p.buttons[2]=1;f.tick();assert.deepEqual(f.hits,[{x:.5,y:.5},undefined]);
  f.controls.dispose();
});

test('a new press recovers when the previous free-cursor pointerup never arrived',()=>{
  const f=controlsFixture('mouse',{dragLook:true});
  f.pointer('pointerdown',10,30);f.pointer('pointermove',30,40);
  f.pointer('pointerdown',150,25);f.pointer('pointerup',150,25);
  assert.deepEqual(f.hits,[{x:.5,y:.5}]);f.controls.dispose();
});

test('inverted right stick reverses both camera axes',()=>{
  const f=controlsFixture('gamepad',{invertLook:true});
  f.p.axes[2]=1;f.tick(30);f.p.axes[2]=0;f.p.axes[3]=1;f.tick(30);
  near(f.camera.rotation.y,1.125);near(f.camera.rotation.x,.85);f.controls.dispose();
});

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

test('a quick double-tap sprints while held and diagonal steering keeps the same speed',()=>{
  const f = controlsFixture();
  f.key('keydown','ArrowUp'); f.tick(1,.1);
  f.key('keydown','ArrowUp',true); f.tick(1,.1);
  near(f.state().z,-.8);
  f.key('keyup','ArrowUp'); f.key('keydown','ArrowUp'); f.key('keydown','KeyD');
  const before = f.state();
  f.tick(60);
  near(Math.hypot(f.state().x-before.x,f.state().z-before.z),6);
  f.key('keyup','ArrowUp');
  const turn = f.state();
  f.tick(60); near(f.state().x-turn.x,4); near(f.state().z,turn.z);
  f.controls.dispose();
});

test('late taps, different directions and a key held through pause do not start a sprint',()=>{
  const f = controlsFixture();
  f.key('keydown','KeyW'); f.key('keyup','KeyW'); f.tick(4,.1);
  f.key('keydown','KeyW'); f.tick(1,.1); near(f.state().z,-.4);
  f.key('keyup','KeyW'); f.key('keydown','ArrowLeft'); f.tick(1,.1); near(f.state().x,-.4);
  f.pause(true); f.tick(); f.pause(false);
  f.key('keydown','ArrowLeft',true); f.tick(1,.1); near(f.state().x,-.4);
  f.key('keyup','ArrowLeft'); f.key('keydown','ArrowLeft'); f.tick(1,.1); near(f.state().x,-.8);
  f.controls.dispose();
});

test('RB scales an active analog stick, requires release after pause and never moves a resting player',()=>{
  const f = controlsFixture();
  f.p.buttons[5]=1; f.tick(60); near(f.state().x,0);
  f.p.axes[0]=.59; f.tick(60); near(f.state().x,3);
  f.p.buttons[5]=0; f.tick(60); near(f.state().x,5);
  f.p.buttons[5]=1; f.tick(60); near(f.state().x,8);
  f.pause(true); f.tick(); f.pause(false); f.tick(60); near(f.state().x,8);
  f.p.axes[0]=0; f.tick(); f.p.axes[0]=1; f.tick(60); near(f.state().x,12);
  f.p.buttons[5]=0; f.tick(); f.p.buttons[5]=1;
  const before = f.state(); f.tick(60); near(f.state().x-before.x,6);
  f.controls.dispose();
});
