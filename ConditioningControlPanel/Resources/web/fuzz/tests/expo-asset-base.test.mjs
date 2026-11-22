import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {assetURL,setExpoAssetBase} from '../ui/expo/asset-base.js';
import {createSound} from '../ui/expo/audio.js';
import {createPortal} from '../ui/expo/portal.js';
import {clearEntrance} from '../ui/expo/entrance.js';
import {loadHoofPhotos} from '../ui/expo/hoof-screen.js';
import {herdRoster} from '../data/content/llamas.js';

test('a transmission directory can be mounted, replaced and reset before the next visit', () => {
  const bundled=new URL('../assets/expo/',import.meta.url).href;
  assert.equal(assetURL(),bundled);
  try {
    setExpoAssetBase('https://packs.example.test/transmissions/2037');
    assert.equal(assetURL('zones/a.glb'),'https://packs.example.test/transmissions/2037/zones/a.glb');
    assert.equal(assetURL('zones/'),'https://packs.example.test/transmissions/2037/zones/');
    assert.throws(()=>assetURL('../audio/old.mp3'),/outside its pack/);
    assert.throws(()=>assetURL('https://other.example.test/clips/a.mp3'),/outside its pack/);
    setExpoAssetBase(new URL('https://cdn.example.test/another/'));
    assert.equal(assetURL('audio/sounds.json'),'https://cdn.example.test/another/audio/sounds.json');
  } finally {setExpoAssetBase(null);}
  assert.equal(assetURL(),bundled);
});

test('portal creation is quiet and its first accepted crossing requests the selected pack', async t => {
  const files=[],preloads=[];
  t.mock.method(globalThis,'fetch',async url=>{
    files.push(String(url));
    if(String(url).endsWith('zones.json'))return Response.json({start:{id:'unused'},zones:[]});
    return Response.json({portal:{profiles:{standard:{rise:'rise.ogg',arrival:'arrive.ogg'}}}});
  });
  const element=()=>({setAttribute(){},append(){},remove(){}});
  const parent={...element(),ownerDocument:{createElement:element}};
  setExpoAssetBase('https://cdn.example.test/transmission/');
  const portal=createPortal({audio:{input:()=>null,preload:url=>{preloads.push(url);}}},parent);
  try {
    portal.arrive();
    assert.deepEqual(files,[]);
    portal.begin();portal.begin();
    await new Promise(resolve=>setTimeout(resolve,0));
    assert.equal(files.filter(s=>s.endsWith('/audio/sounds.json')).length,1);
    assert(files.every(s=>s.startsWith('https://cdn.example.test/transmission/')));
    assert.deepEqual(preloads,['rise','arrive'].map(id=>assetURL('audio/'+id+'.ogg')));
  } finally {portal.dispose();clearEntrance();setExpoAssetBase(null);}
});

test('the visitor footsteps and loan ticker play files from the selected pack', async t => {
  const requests=[],played=[];
  t.mock.method(globalThis,'fetch',async url=>{requests.push(String(url));return Response.json({steps:{wood:['step-wood-1.ogg']}});});
  setExpoAssetBase('https://cdn.example.test/transmission/');
  const sound=createSound({audio:{play:url=>played.push(url),preload(){}}});
  try {
    await new Promise(resolve=>setTimeout(resolve,0));
    sound.footstep('wood');sound.play('loan_tick');
    assert.deepEqual(requests,[assetURL('audio/sounds.json')]);
    assert.deepEqual(played,['sfx/thud.mp3','step-wood-1.ogg','sfx/tally_tick.mp3'].map(path=>assetURL('audio/'+path)));
  } finally {sound.dispose();setExpoAssetBase(null);}
});

test('all six Dome frames, the bubble and the two reused sounds are present in the pack', async () => {
  const names=['dome/bubble.png','audio/sfx/thud.mp3','audio/sfx/tally_tick.mp3',...herdRoster(6).map(llama=>'dome/'+llama.id+'.webp')];
  for(const name of names)assert((await readFile(new URL(assetURL(name)))).length>100,name);
});

test('cross-origin hoof photos request CORS before receiving their source URL', () => {
  const previous=globalThis.Image,issued=[];
  globalThis.Image=class {set src(value){issued.push([this.crossOrigin,value]);}};
  setExpoAssetBase('https://pictures.example.test/package');
  try {
    const photos=loadHoofPhotos(assetURL,()=>{});
    assert.equal(issued.length,16);
    assert(issued.every(([mode,url])=>mode==='anonymous' && url.startsWith(assetURL('hoof-photos/'))));
    photos.dispose();
  } finally {setExpoAssetBase(null);if(previous)globalThis.Image=previous;else delete globalThis.Image;}
});
