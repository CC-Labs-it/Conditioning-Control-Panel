import test from 'node:test';
import assert from 'node:assert/strict';
import { createMusicVisualData, stepMusicVisualData } from '../render/musicVisualData.js';
import { createMusicReactivity } from '../audio/reactivity.js';
import { normalizeChart } from '../audio/chart.js';

const signal = () => ({ active: true, songS: 1, rawEnergy: .6, rawBass: .1, rawMid: .1, rawTreble: .1,
  waveform: Float32Array.from({length:64},(_,i)=>Math.sin(i*.3)), spectrum: Float32Array.from({length:64},(_,i)=>i/63) });
test('one bounded atlas carries signed PCM, independent spectrum, decaying peaks and real song history', () => {
  const v=createMusicVisualData(), s=signal(), identity=v.data;
  stepMusicVisualData(v,s,1/60);
  assert.equal(v.data.byteLength,4608);
  assert.ok(v.data[5*4]>128 && v.data[15*4]<128,'actual signed waveform, not absolute loudness');
  assert.equal(v.data[1],0);assert.equal(v.data[63*4+1],255);
  const old=v.data[(64+40)*4];s.songS+=.1;s.spectrum.fill(0);stepMusicVisualData(v,s,.1);
  assert.equal(v.data[(128+40)*4],old,'previous frequency picture moves into history');
  assert.equal(v.data[40*4+1],0);assert.ok(v.data[40*4+2]>0,'peak falls after current bin');
  for(let i=0;i<300;i++){s.songS+=1/60;stepMusicVisualData(v,s,1/60);}
  assert.equal(v.data,identity);assert.equal(v.data.byteLength,4608);assert.equal(v.data[40*4+2],0);
});
test('fast musical drive reaches half response within 35ms while avoiding a binary loudness cap', () => {
  const v=createMusicVisualData(),s=signal();stepMusicVisualData(v,s,1/60);s.songS+=1/60;stepMusicVisualData(v,s,1/60);
  assert.ok(v.drive>.3&&v.drive<.6);
  const low=createMusicVisualData(),high=createMusicVisualData();
  for(let i=0;i<120;i++){stepMusicVisualData(low,{...s,rawEnergy:.25},1/60);stepMusicVisualData(high,{...s,rawEnergy:.5},1/60);}
  const gate=x=>x/(x+.08);assert.ok(gate(high.drive)>gate(low.drive)+.08,'ordinary-level changes remain visible');
});
test('onsets come from measured flux or chart hits and finite fronts expire without invented beats', () => {
  const v=createMusicVisualData(),s=signal();
  for(let i=0;i<90;i++){s.songS+=1/60;stepMusicVisualData(v,s,1/60);}
  assert.ok(v.onset<.001,'steady tone does not create repetitive false hits');
  s.rawBass=.9;s.songS+=1/60;stepMusicVisualData(v,s,1/60);
  assert.ok(v.kick>.5);assert.equal(v.hitAge,0);
  for(let i=0;i<120;i++){s.songS+=1/60;stepMusicVisualData(v,s,1/60);}
  assert.ok(v.hitAge>1.9&&v.onset<.001);
  const song=normalizeChart({bpm:120,sections:[{startS:0,endS:2},{startS:2,endS:9}],hits:{snare:[[3,1]]}});
  s.songS=3;stepMusicVisualData(v,s,1/60,{song});assert.ok(v.snare>.99);assert.equal(v.section,1);
  const ahead=createMusicVisualData();s.rawBass=s.rawMid=s.rawTreble=0;
  s.songS=2.989;stepMusicVisualData(ahead,s,1/60,{song});assert.equal(ahead.hitStrength,0);
  s.songS=3;stepMusicVisualData(ahead,s,1/60,{song});assert.ok(ahead.hitStrength>.99,'previewing a hit cannot consume its later attack');
});
test('pause and Gentle hold fast sample detail; silence, seeks and song changes discard old trails', () => {
  const v=createMusicVisualData(),s=signal();stepMusicVisualData(v,s,1/60);const image=v.data.slice();
  s.waveform.fill(-1);s.spectrum.fill(0);s.songS=1.1;
  assert.equal(stepMusicVisualData(v,s,.1,{paused:true}),false);assert.deepEqual(v.data,image);
  assert.equal(stepMusicVisualData(v,s,.1,{gentle:true}),false);assert.deepEqual(v.data,image);assert.equal(v.onset,0);
  s.songS=.1;stepMusicVisualData(v,s,.1);assert.ok(v.data.slice(64*8).every(x=>x===0),'seek clears old history');
  stepMusicVisualData(v,null,.1);assert.equal(v.drive,0);assert.equal(v.hitStrength,0);
  assert.equal(stepMusicVisualData(v,null,.1),false,'silence never repeats uploads');
});
test('the existing single analyser exposes reusable real waveform and frequency samples', () => {
  let count=0,frequency=1000;
  const analyser={frequencyBinCount:512,disconnect(){},getFloatTimeDomainData(a){for(let i=0;i<a.length;i++)a[i]=Math.sin(i*.1)*.2;},
    getFloatFrequencyData(a){a.fill(-Infinity);a[Math.round(frequency/(48000/1024))]=-12;}};
  const ctx={state:'running',sampleRate:48000,createAnalyser(){count++;return analyser;}};
  const source={connect(){},disconnect(){}},r=createMusicReactivity(ctx,source);
  const first=r.read(1/60,{playing:true,songS:1}),wave=first.waveform,bins=first.spectrum;
  assert.ok(wave.some(x=>x<0)&&wave.some(x=>x>0));const before=bins.slice();frequency=6000;
  r.read(1/60,{playing:true,songS:2});assert.notDeepEqual(bins,before);
  assert.equal(r.frame.waveform,wave);assert.equal(r.frame.spectrum,bins);assert.equal(count,1);
  r.read(1/60,{playing:true,muted:true});assert.ok(wave.every(x=>x===0)&&bins.every(x=>x===0));r.dispose();
});
