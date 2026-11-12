import test from 'node:test';
import assert from 'node:assert/strict';
import { createPictures } from '../ui/pictures.js';
import { loadRewardPictures } from '../ui/unlocks/arcade/reward-pictures.js';

const origin='http://127.0.0.1:8817';
const feed=names=>({v:1,rows:names.map(name=>({kind:'image',url:origin+'/photo/'+name}))});
const urls=rows=>rows.map(row=>row.url);

test('all prize machines reserve globally, walk the available set, and release on last fade',()=>{
  const p=createPictures({scrolller:{consent:true,origin:'device'},pageOrigin:origin});
  p.receiveScrolller(feed(['a','a','b','c','d','e']));
  const spin=new AbortController(), capsule=new AbortController();
  const wheel=p.rewardBatch(3,origin+'/photo/b',spin.signal);
  assert.equal(wheel[0].url,origin+'/photo/b');
  assert.equal(new Set(urls(wheel)).size,3);
  const first=p.rewardBatch(3,null,capsule.signal);
  assert.equal(first.length,2);
  assert.equal(new Set([...urls(wheel),...urls(first)]).size,5);
  assert(wheel.every(row=>row.credit==='via Scrolller'));
  assert.deepEqual(p.rewardBatch(),[]);
  spin.abort();
  const next=p.rewardBatch();
  assert.equal(next.length,3);
  assert(next.every(row=>!urls(first).includes(row.url)));
  capsule.abort();p.setSource('none');
});

test('three-picture feed stays distinct after an identical refresh and avoids repeating the last landing',()=>{
  const p=createPictures({scrolller:{consent:true,origin:'device'},pageOrigin:origin});
  const batch=feed(['1','2','3']);
  p.receiveScrolller(batch);
  const first=urls(p.rewardBatch());
  p.receiveScrolller(batch);
  const second=urls(p.rewardBatch(3,first.at(-1)));
  assert.notEqual(first.at(-1),second[0]);
  assert.notEqual(first[0],second[0]);
  assert.deepEqual([...first].sort(),[...second].sort());
  p.setRemoteConsent(false); assert.deepEqual(p.rewardBatch(),[]);
});

test('signed URLs for a single CDN still, duplicate local files and exhausted pools produce fewer cards',async()=>{
  const p=createPictures({scrolller:{consent:true}});
  const prefix='https://cdn.scrolller.com/image/example.jpg?token=';
  p.receiveScrolller({v:1,rows:['a','b'].map(key=>({kind:'image',url:prefix+key}))});
  const lease=new AbortController();
  assert.equal(p.rewardBatch(3,null,lease.signal).length,1);
  assert.deepEqual(p.rewardBatch(),[]);lease.abort();
  p.setSource('own');
  const file=()=>new File(['test-photo'],'repeat.png',{type:'image/png',lastModified:1234});
  p.addOwn([file(),file(),new Blob(['other'],{type:'image/webp'}),new Blob(['gif'],{type:'image/gif'})]);
  assert.equal(p.ownCount,4);
  await p.next({still:true});
  const local=p.rewardBatch();
  assert.equal(local.length,2);
  assert(local.every(row => row.kind === 'picture'));
  assert(local.every(row=>row.credit===null));
  p.clearOwn();assert.deepEqual(p.rewardBatch(),[]);
});

test('aborted reveals and source changes cannot show a previously decoded preferred image',async()=>{
  const pictures=createPictures({source:'own'});
  pictures.addOwn([new Blob(['frame'],{type:'image/jpeg'})]);
  await pictures.next({still:true});
  const [row]=pictures.rewardBatch(), image={width:100,height:100};
  const pen={clearRect(){},drawImage(){},getImageData(){return {data:Uint8Array.of(11,22,33,255)};}};
  const ctx={pictures,button:{el:{ownerDocument:{createElement:()=>({getContext:()=>pen})}}}};
  const first=new AbortController();
  const oldCards=loadRewardPictures(ctx,{url:row.url,image},first.signal);
  first.abort();
  assert.deepEqual(await oldCards,[]);
  const second=new AbortController();
  const after=await loadRewardPictures(ctx,{url:row.url,image},second.signal);
  assert.equal(after.length,1);assert.equal(after[0].image,image);
  assert.deepEqual(pictures.rewardBatch(),[]);
  second.abort();
  const third=new AbortController();
  const oldSource=loadRewardPictures(ctx,{url:row.url,image},third.signal);
  pictures.setSource('none');
  assert.deepEqual(await oldSource,[]);
  third.abort();pictures.clearOwn();
});
