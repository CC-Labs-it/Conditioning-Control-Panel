import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { evaluateColour } from './floor-colour-eval.mjs';
const hooks = registerHooks({ resolve(s,c,n) {
  const files = { 'three': 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[s] ? { url: new URL('../vendor/three/build/'+files[s],import.meta.url).href, shortCircuit: true } : n(s,c);
} });
const { Vector2 } = await import('three');
const { uniform, vec3, float } = await import('three/tsl');
const { createAudioReactiveUniforms, mainframeTileLight } = await import('../render/audioReactive.js');
const { keepMusicFloor } = await import('../render/materials/keepMusicFloor.js');
const { boudoirMusicFloor } = await import('../render/materials/boudoirMusicFloor.js');
const { goonworksMusicFloor } = await import('../render/materials/goonworksMusicFloor.js');
hooks.deregister();

for (const kind of ['mainframe','keep','boudoir','goonworks']) test(kind+' actual floor graph preserves silence, measured notes and Gentle onset', () => {
  const p=uniform(new Vector2()), u=createAudioReactiveUniforms();
  const music={tileBeat:uniform(0),tileBar:uniform(0),tileOn:uniform(1),tileGentle:uniform(0),big:uniform(.4),calm:uniform(0)};
  const base=vec3(.08,.10,.12), seam=float(0);
  const roots={
    mainframe:()=>mainframeTileLight(p,u,music,float(8),base),
    keep:()=>keepMusicFloor({p,base,seam,reactive:u,music}),
    boudoir:()=>boudoirMusicFloor({p,base,trueEdge:float(8),distance:float(24),wet:float(0),reactive:u,music,lapM:4380}),
    goonworks:()=>goonworksMusicFloor({p,base,top:float(1),edge:float(8),half:float(18),pink:vec3(.8,.1,.4),accent:vec3(.2,.7,.6),reactive:u,music}),
  };
  const root=roots[kind](), notes=Array.from({length:8},(_,i)=>'note'+i);
  for(const name of ['bass','mid','treble','energy',...notes])u[name].value=.6;
  for(let i=0;i<64;i++) {
    const data=u.vizTexture.image.data; data[i*4]=Math.round(128+100*Math.sin(i*.35));
    data[i*4+1]=Math.round(80+140*(i%11)/10);
    for(let row=1;row<18;row++)data[(row*64+i)*4]=Math.round(40+160*((i+row)%13)/12);
  }
  const samples=[];for(const x of [.8,2.4,4.1,5.9,7.5,9.3,11.2,12.4,13.1,14.2])for(const y of [.8,3.5,6.2,8.9,11.6,14.3,17,19.7,22.4])samples.push([x,y]);
  const capture=()=>samples.flatMap(([x,y])=>{p.value.set(x,y);return evaluateColour(root)});
  const authored=samples.flatMap(()=>[.08,.10,.12]);
  const capturePhrases=()=>[0,8,16].flatMap(bar=>{music.tileBar.value=bar;return capture()});
  music.tileOn.value=0;
  assert.deepEqual(capture(),authored,'no song preserves the authored finish exactly');
  music.tileOn.value=1;u.energy.value=0;
  assert.deepEqual(capture(),authored,'real silence restores the exact authored finish');u.energy.value=.6;
  if (kind !== 'mainframe') {
    // Hold time and sustained bands steady: actual percussive events must change broad floor paint.
    // Compare spatial paint across the playable road, not a decorative edge or one bright pixel.
    for(const kind of ['kick','snare','hats']){u[kind].value=u[kind+'Strength'].value=.9;u[kind+'Age'].value=.1;}
    music.tileBeat.value=0; const onBeat=capture();
    for(const kind of ['kick','snare','hats']){u[kind].value=u[kind+'Strength'].value=0;u[kind+'Age'].value=9;}
    const between=capture();
    const vivid=onBeat.filter((v,j)=>Math.abs(v-between[j])>.04).length/onBeat.length;
    assert.ok(vivid>.1, kind+' distinct attacks light visible groups while retaining quiet space: '+vivid);
    music.tileBeat.value=0;
  }
  const phrases=[0,8,16].map(bar=>{music.tileBar.value=bar;return capture()});
  for(const colour of phrases.flat())assert.ok(Number.isFinite(colour)&&colour>=0&&colour<=1,'bounded finite linear colour');
  for(let i=1;i<phrases.length;i++)assert.ok(phrases[i].some((v,j)=>Math.abs(v-phrases[0][j])>.04),'phrase changes the visible spatial composition');
  music.tileBar.value=0;u.gentle.value=music.tileGentle.value=1;music.tileBeat.value=.0;const still=capture();music.tileBeat.value=.5;
  assert.deepEqual(capture(),still,'Gentle removes beat-rate shape changes while slow phrase motion remains');
  u.energy.value=1;const full=capture();u.energy.value=.1;const onset=capture();
  assert.ok(full.some((v,j)=>Math.abs(v-authored[j])>.005),'the Gentle onset check exercises visible paint');
  for(let j=0;j<onset.length;j++)assert.ok(Math.abs((onset[j]-authored[j])-(full[j]-authored[j])*.1)<1e-10,
    'Gentle retains the envelope rise without multiplying or clipping it');
  u.gentle.value=music.tileGentle.value=0;u.energy.value=.6;
  if(kind==='goonworks')music.tileBar.value=16;
  const low=capture();u.bass.value=.02;u.mid.value=.03;u.treble.value=.01;
  assert.ok(capture().some((v,j)=>Math.abs(v-low[j])>.01),'measured bands visibly reshape the graph');
  for(const key of notes)u[key].value=0;const noNotes=capturePhrases();
  for(const key of notes){
    u[key].value=1;
    assert.ok(capturePhrases().some((v,j)=>Math.abs(v-noNotes[j])>.002),key+' independently changes visible paint');
    u[key].value=0;
  }
});
