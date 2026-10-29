import test from 'node:test';
import assert from 'node:assert/strict';
import {createMusicVisualData,stepMusicVisualData,INSTRUMENT_FIELDS} from '../render/musicVisualData.js';
import {normalizeChart} from '../audio/chart.js';
const quiet=()=>({active:true,songS:1,rawEnergy:.4,rawBass:0,rawMid:0,rawTreble:0});
test('bass, mid attacks and high attacks own independent clocks even inside the shared cooldown',()=>{
 const v=createMusicVisualData(),s=quiet();stepMusicVisualData(v,s,1/60);
 s.rawBass=.8;s.songS+=.02;stepMusicVisualData(v,s,.02);
 assert.equal(v.kickCount,1);assert.equal(v.kickAge,0);assert.equal(v.snareCount,0);assert.equal(v.hatsCount,0);
 s.rawMid=.7;s.songS+=.02;stepMusicVisualData(v,s,.02);
 assert.equal(v.snareCount,1);assert.equal(v.snareAge,0);assert.ok(v.kickAge>.019);
 s.rawTreble=.9;s.songS+=.02;stepMusicVisualData(v,s,.02);
 assert.equal(v.hatsCount,1);assert.equal(v.hatsAge,0);assert.ok(v.snareAge>.019);assert.ok(v.kickAge>.039);
 const counts=INSTRUMENT_FIELDS.map(f=>v[f.count]);
 for(let i=0;i<120;i++){s.songS+=1/60;stepMusicVisualData(v,s,1/60);}
 assert.deepEqual(INSTRUMENT_FIELDS.map(f=>v[f.count]),counts,'steady sound never invents repeating percussion');
 assert.ok(v.kickAge>2&&v.snareAge>2&&v.hatsAge>1.9);
});
test('simultaneous chart instruments each launch once, including look-ahead then audible onset',()=>{
 const song=normalizeChart({bpm:120,hits:{kick:[[2,.8]],snare:[[2,.9]],hats:[[2,1]]}}),v=createMusicVisualData(),s=quiet();
 s.songS=1.989;stepMusicVisualData(v,s,1/60,{song});
 assert.deepEqual(INSTRUMENT_FIELDS.map(f=>v[f.count]),[0,0,0]);
 s.songS=2;stepMusicVisualData(v,s,1/60,{song});
 assert.deepEqual(INSTRUMENT_FIELDS.map(f=>v[f.count]),[1,1,1]);
 assert.deepEqual(INSTRUMENT_FIELDS.map(f=>v[f.age]),[0,0,0]);
 s.songS=2.03;stepMusicVisualData(v,s,.03,{song});
 assert.deepEqual(INSTRUMENT_FIELDS.map(f=>v[f.count]),[1,1,1]);
});
test('pause freezes all instrument states; silence, seeks and new songs discard every front',()=>{
 const v=createMusicVisualData(),s=quiet();s.rawBass=s.rawMid=s.rawTreble=.8;stepMusicVisualData(v,s,.02);
 const snapshot=INSTRUMENT_FIELDS.flatMap(f=>[v[f.age],v[f.strength],v[f.count]]);
 s.songS=3;stepMusicVisualData(v,s,.1,{paused:true});
 assert.deepEqual(INSTRUMENT_FIELDS.flatMap(f=>[v[f.age],v[f.strength],v[f.count]]),snapshot);
 stepMusicVisualData(v,null,.1);
 for(const f of INSTRUMENT_FIELDS){assert.equal(v[f.age],9);assert.equal(v[f.strength],0);assert.equal(v[f.count],0);}
 s.rawBass=s.rawMid=s.rawTreble=0;s.songS=.2;stepMusicVisualData(v,s,.1);
 assert.deepEqual(INSTRUMENT_FIELDS.map(f=>v[f.count]),[0,0,0]);
 s.rawBass=s.rawMid=s.rawTreble=1;s.songS=.3;stepMusicVisualData(v,s,.1,{gentle:true});
 assert.deepEqual(INSTRUMENT_FIELDS.map(f=>v[f.count]),[0,0,0],'Gentle cannot launch fast instrument effects');
});
