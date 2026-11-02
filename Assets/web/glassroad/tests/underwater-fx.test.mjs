import test from 'node:test';
import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
const hooks=registerHooks({resolve(s,c,next){const f={'three/webgpu':'three.webgpu.min.js','three/tsl':'three.tsl.min.js'};return f[s]?{url:new URL('../vendor/three/build/'+f[s],import.meta.url).href,shortCircuit:true}:next(s,c);}});
const {Group,Vector3}=await import('three/webgpu');
const {createUnderwaterFx}=await import('../render/underwaterFx.js');
hooks.deregister();
const track={lengthM:500,reservoir:{waterY:4},sections:[{id:'submerged',fromS:50,toS:300},{id:'emerge',fromS:300,toS:350}],
  frameAt:s=>({p:[0,-8,-s],b:[1,0,0],w:30})};
function kart(){const root=new Group(),object=new Group(),rotor=new Group();root.add(object);object.add(rotor);root.position.set(12,-5,-150);root.rotation.y=Math.PI/2;rotor.position.z=1;root.updateMatrixWorld(true);return {root,underwater:1,propeller:{object,rotor,motion:{rpm:320}}};}
test('exhaust starts at the transformed rotor, moves behind it then rises, and stops on emergence',t=>{
 const fx=createUnderwaterFx(track),k=kart(),origin=k.propeller.rotor.getWorldPosition(new Vector3());t.after(()=>fx.dispose());
 const args={karts:[k],playerS:150,playerPos:k.root.position};
 fx.update(.1,args);assert.ok(fx.stats.bubbles>0);assert.equal(fx.stats.emitters,1);
 const d=fx.bubbles.data;assert.ok(Math.abs(d[0]-origin.x)<.6);assert.ok(Math.abs(d[2]-origin.z)<.3);assert.ok(d[3]>2,'local rear axis rotates into world X');
 const y=d[1],x=d[0],emitted=fx.stats.emitted;k.underwater=0;
 for(let i=0;i<10;i++)fx.update(.1,args);
 assert.equal(fx.stats.emitted,emitted);assert.ok(d[1]>y);assert.ok(d[0]>x);
 for(let i=0;i<60;i++)fx.update(.1,{...args,playerS:400});
 assert.equal(fx.stats.bubbles,0);
});
test('suspended particles respect reservoir depth, pause, fixed pools and reset',t=>{
 const fx=createUnderwaterFx(track),k=kart(),args={karts:[k],playerS:150,playerPos:k.root.position};t.after(()=>fx.dispose());
 for(let i=0;i<80;i++)fx.update(.1,args);
 assert.ok(fx.stats.motes>150);assert.ok(fx.stats.bubbles>0);
 for(const pool of [fx.bubbles,fx.motes]){assert.ok(pool.active<=pool.capacity);for(let i=0;i<pool.capacity;i++)if(pool.data[i*12+10])assert.ok(pool.data[i*12+1]<4);}
 const before=fx.bubbles.data.slice(),stats={...fx.stats};fx.update(1,{...args,paused:true});assert.deepEqual(fx.bubbles.data,before);assert.deepEqual(fx.stats,stats);
 fx.reset();assert.equal(fx.stats.bubbles,0);assert.equal(fx.motes.mesh.count,0);assert.equal(fx.stats.emitted,0);
 k.underwater=0;for(let i=0;i<10;i++)fx.update(.1,{...args,playerS:400});assert.equal(fx.stats.motes,0);assert.equal(fx.stats.bubbles,0);
 const phone=createUnderwaterFx(track,{quality:'phone'});t.after(()=>phone.dispose());assert.ok(phone.bubbles.capacity<fx.bubbles.capacity);
});
test('Gentle slows emission and motion, dry tracks remain empty',t=>{
 const normal=createUnderwaterFx(track),gentle=createUnderwaterFx(track),dry=createUnderwaterFx({sections:[]});t.after(()=>{normal.dispose();gentle.dispose();dry.dispose();});
 const k=kart(),args={karts:[k],playerS:150,playerPos:k.root.position};
 for(let i=0;i<20;i++){normal.update(.1,args);gentle.update(.1,{...args,gentle:true});dry.update(.1,args);}
 assert.ok(gentle.stats.emitted<normal.stats.emitted);assert.equal(dry.stats.bubbles,0);assert.equal(dry.stats.motes,0);
});
