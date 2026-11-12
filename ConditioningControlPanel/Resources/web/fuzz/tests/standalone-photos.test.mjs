import test from 'node:test';
import assert from 'node:assert/strict';
import {createPictures} from '../ui/pictures.js';
import {loadRewardPictures} from '../ui/unlocks/arcade/reward-pictures.js';

const text = string => Array.from(Buffer.from(string,'latin1'));
const png = (name, animated) => new File([
  Uint8Array.from([137,80,78,71,13,10,26,10,0,0,0,0,...text('IHDR'),0,0,0,0,0,0,0,0,...text(animated?'acTL':'IDAT'),0,0,0,0])
], name, {type:'image/png'});
const webp = (name, animated) => new File([
  Uint8Array.from([...text('RIFF'),22,0,0,0,...text('WEBPVP8X'),10,0,0,0,animated ? 2 : 0,0,0,0,0,0,0,0,0,0])
], name, {type:'image/webp'});
const avif = (name, animated) => new File([
  Uint8Array.from([0,0,0,20, ...text('ftypavif'),0,0,0,0,...text(animated?'avis':'mif1')])
], name, {type:'image/avif'});

test('separate local images stay eligible for art and prizes while animated containers play as animations', async () => {
  const p=createPictures({source:'own'});
  const files=[
    png('photo.png',false),webp('photo.webp',false),avif('photo.avif',false),
    png('sequence.png',true),webp('sequence.webp',true),avif('sequence.avif',true),
    new File(['movie'],'movie.mp4',{type:'video/mp4'}), new File(['gif'],'loop.gif',{type:'image/gif'}),
  ];
  assert.equal(p.addOwn(files),8);
  // Unclassified image containers never slip into a synchronous prize draw.
  assert.equal(p.rewardBatch().length,0);
  assert.equal((await p.next({still:true})).kind,'picture');
  assert.deepEqual(p.ownCounts,{picture:3,gif:4,clip:1});
  const photos=p.stillsForDisplay(30), photoUrls=new Set(photos.map(row=>row.url));
  assert.equal(photoUrls.size,3);
  const motion=[];
  for(let i=0;i<5;i++) motion.push(await p.next({kind:'gif'}));
  assert.equal(new Set(motion.map(row=>row.url)).size,5);
  assert(motion.every(row=>!photoUrls.has(row.url)));
  assert(p.rewardBatch().every(row=>photoUrls.has(row.url)));
  p.clearOwn();
});

test('a motion-only library can play without a photo turn, paper prize, or Breakout image', async () => {
  const p=createPictures({source:'own'});
  p.replaceOwn([png('sequence.png',true),new File(['movie'],'clip.mp4',{type:'video/mp4'})]);
  assert.equal(await p.next({still:true}),null);
  assert.equal(await p.next({kind:'picture'}),null);
  assert.deepEqual(p.stillsForDisplay(2),[]);
  const row=await p.next({kind:'mixed'});
  assert(['gif','clip'].includes(row.kind));
  assert.equal(await p.asStill(row),null);
  assert.deepEqual(p.rewardBatch(3,row.url),[]);
  const ctx={pictures:p,button:{el:{ownerDocument:{}}}};
  assert.deepEqual(await loadRewardPictures(ctx,{url:row.url,image:{}},new AbortController().signal),[]);
  p.clearOwn();
});

test('the signed-in source rejects a movie poster also offered as an image', async () => {
  const origin='http://127.0.0.1:8817';
  const p=createPictures({scrolller:{consent:true,origin:'device',clips:true},pageOrigin:origin});
  const poster=origin+'/extracted',photo=origin+'/standalone';
  p.receiveScrolller({v:1,rows:[
    {kind:'image',url:poster,mime:'image/jpeg'},
    {kind:'video',url:origin+'/movie',mime:'video/webm',poster},
    {kind:'image',url:origin+'/also-poster',mime:'image/jpeg',isPoster:true},
    {kind:'image',url:photo,mime:'image/jpeg'},
  ]});
  assert.deepEqual(p.stillsForDisplay(8,{allowScrolller:true}).map(r=>r.url),[photo]);
  assert.deepEqual(p.rewardBatch().map(r=>r.url),[photo]);
  assert.equal((await p.next({kind:'gif',allowScrolller:true})).kind,'clip');
  p.setSource('none');
});

test('replacing a library during image classification cannot reintroduce the abandoned photos', async () => {
  const p=createPictures({source:'own'});
  p.replaceOwn([png('pending.png',false)]);
  const old=p.next({still:true});
  p.replaceOwn([new File(['movie'],'clip.mp4',{type:'video/mp4'})]);
  assert.equal(await old,null);
  assert.equal(await p.next({still:true}),null);
  assert.deepEqual(p.stillsForDisplay(10),[]);
  p.clearOwn();
});
