import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createUnderwater } from '../audio/underwater.js';
import { createAudio } from '../audio/bus.js';
import { createFeel } from '../core/feel.js';
import { createBus } from '../core/events.js';

function context() {
  const nodes = [], ctx = { currentTime: 0, sampleRate: 48000, state: 'running' };
  function param(initial) {
    let base = initial, target = initial, since = 0, tc = 0;
    const calls = [], p = {
      get value() { return tc ? target + (base - target) * Math.exp(-Math.max(0,ctx.currentTime - since)/tc) : base; },
      set value(v) { base = target = v; tc = 0; }, calls,
      cancelAndHoldAtTime(t) { base = p.value; target = base; tc = 0; calls.push(['hold',t]); },
      cancelScheduledValues() { p.cancelAndHoldAtTime(ctx.currentTime); },
      setValueAtTime(v,t) { p.value = v; calls.push(['set',v,t]); return p; },
      setTargetAtTime(v,t,c) { base = p.value; target = v; since = t; tc = c; calls.push(['target',v,t,c]); return p; },
      linearRampToValueAtTime(v,t) { return p.setValueAtTime(v,t); },
      exponentialRampToValueAtTime(v,t) { return p.setValueAtTime(v,t); },
    }; return p;
  }
  function node(type, extra={}) {
    const n = { type, connections:[], disconnected:false, connect(to){this.connections.push(to);return to;},
      disconnect(){this.connections=[];this.disconnected=true;},start(){},stop(){},...extra };nodes.push(n);return n;
  }
  Object.assign(ctx, {
    destination:node('destination'), createGain:()=>node('gain',{gain:param(1)}),
    createBiquadFilter:()=>node('filter',{frequency:param(350),Q:param(1)}),
    createOscillator:()=>node('oscillator',{frequency:param(440),detune:param(0)}),
    createBufferSource:()=>node('source'), createBuffer:(_n,len)=>({getChannelData:()=>new Float32Array(len)}),
    resume:async()=>{ctx.state='running';}, suspend:async()=>{ctx.state='suspended';},close(){ctx.state='closed';},
    advance(dt){if(ctx.state==='running')ctx.currentTime+=dt;},
  });
  return {ctx,nodes};
}

test('underwater filters the shared output and glides in and out without a gain jump',()=>{
  const {ctx,nodes}=context(), wet=createUnderwater(ctx,ctx.destination),filter=wet.input, gain=filter.connections[0];
  assert.equal(filter.type,'lowpass');assert.equal(gain.connections[0],ctx.destination);
  wet.update(1);assert.equal(wet.inspect.cutoffHz,18000);assert.equal(wet.inspect.gain,1);
  assert.ok(Math.abs(wet.inspect.targetCutoffHz-650)<1e-9);assert.ok(Math.abs(wet.inspect.targetGain-.82)<1e-9);
  ctx.advance(.6);assert.ok(wet.inspect.cutoffHz<1400);assert.ok(wet.inspect.gain<.83);
  const previous=wet.inspect.cutoffHz;wet.update(0);assert.equal(wet.inspect.cutoffHz,previous);
  ctx.advance(2);assert.ok(wet.inspect.cutoffHz>17900);assert.ok(wet.inspect.gain>.999);
  assert.equal(nodes.length,3);assert.ok(Object.isFrozen(wet.inspect));
});

test('partial depth is bounded, unchanged updates do not reschedule and reset cancels wet automation',()=>{
  const {ctx}=context(), wet=createUnderwater(ctx,ctx.destination),p=wet.input.frequency;
  wet.update(.5);assert.ok(wet.inspect.targetCutoffHz>650&&wet.inspect.targetCutoffHz<18000);
  const count=p.calls.length;for(let i=0;i<20;i++)wet.update(.5);assert.equal(p.calls.length,count);
  wet.update(99);assert.equal(wet.inspect.submerged,1);ctx.advance(.3);wet.reset();
  assert.equal(wet.inspect.cutoffHz,18000);assert.equal(wet.inspect.gain,1);ctx.advance(4);assert.equal(wet.inspect.gain,1);
  wet.update(-1);assert.equal(wet.inspect.submerged,0);wet.update(NaN);assert.equal(wet.inspect.submerged,0);
  wet.dispose();assert.equal(wet.inspect.available,false);assert.ok(wet.input.disconnected);wet.update(1);assert.equal(wet.inspect.submerged,0);
});

test('context suspension freezes the transition and resumes from the same point',async()=>{
  const {ctx}=context(), wet=createUnderwater(ctx,ctx.destination);wet.update(1);ctx.advance(.2);
  await ctx.suspend();const before=wet.inspect;ctx.advance(10);assert.deepEqual(wet.inspect,before);
  await ctx.resume();ctx.advance(.2);assert.ok(wet.inspect.cutoffHz<before.cutoffHz);
});

test('minimal fake contexts remain usable without optional filters or automation',()=>{
  const gain={gain:{value:1},connect(){},disconnect(){}},ctx={currentTime:0,createGain:()=>gain};
  const wet=createUnderwater(ctx,{});assert.equal(wet.input,gain);assert.equal(wet.inspect.available,false);
  wet.update(1);assert.ok(Math.abs(gain.gain.value-.82)<1e-9);wet.reset();assert.equal(gain.gain.value,1);wet.dispose();
});

test('audio bus routes music, engine and cues through one treatment, preserves volumes and pause',async()=>{
  const {ctx,nodes}=context(),events=createBus(),feel=createFeel(JSON.parse(readFileSync(new URL('../feel.json',import.meta.url))));
  let made=0;
  const audio=createAudio(feel,events,{win:null,makeCtx:()=>{made++;return ctx;},fetchFn:async()=>{throw Error('offline');}});
  audio.update(.5,{submerged:1});assert.equal(made,0);await audio.start();assert.equal(made,1);
  const filter=nodes.find(n=>n.type==='lowpass'),master=nodes.find(n=>n.connections.includes(filter));
  assert.ok(master);assert.equal(nodes.filter(n=>n.connections.includes(ctx.destination)).length,1,'one output, no dry bypass');
  assert.equal(nodes.filter(n=>n.connections.includes(master)).length,2,'sfx and music share this master');
  audio.setVolumes({master:.4,music:.3,sfx:.2});ctx.advance(3);const masterLevel=master.gain.value;
  audio.update(.6,{submerged:1,waterSurface:true,onRoad:true});ctx.advance(1);assert.ok(audio.underwater.cutoffHz<800);
  assert.ok(audio.waterTravel.wake>0);
  assert.ok(Math.abs(master.gain.value-masterLevel)<1e-6,'muffle does not overwrite user master level');
  events.emit('effect-start',{kind:'melt',durationS:2,by:3});
  assert.ok(Math.abs(audio.underwater.targetGain-.82)<1e-9,'effect duck retains its separate gain stage');
  audio.setPaused(true);const frozen=audio.underwater,waterFrozen=audio.waterTravel;audio.update(.7,{submerged:0});await audio.start();ctx.advance(10);
  assert.equal(ctx.state,'suspended');assert.deepEqual(audio.underwater,frozen);
  assert.deepEqual(audio.waterTravel,waterFrozen);
  audio.setPaused(false);audio.update(.6,{submerged:0});ctx.advance(2);assert.ok(audio.underwater.cutoffHz>17900);
  audio.update(.6,{submerged:1});audio.reset();assert.equal(audio.underwater.submerged,0);assert.equal(audio.underwater.gain,1);
  assert.equal(audio.waterTravel.wake,0);
  audio.setVolumes({master:0});ctx.advance(3);audio.update(.6,{submerged:1});ctx.advance(1);assert.ok(master.gain.value<1e-6,'mute remains muted underwater');
  audio.dispose();assert.equal(ctx.state,'closed');assert.ok(filter.disconnected);await audio.start();assert.equal(made,1,'disposed bus cannot recreate output');
});
