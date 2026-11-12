import test from 'node:test';
import assert from 'node:assert/strict';
import {createMediaCache, releasePlayer} from '../ui/media-cache.js';
import {createPictures} from '../ui/pictures.js';

const origin = 'http://127.0.0.1:8817';
const nextTurn = () => new Promise(resolve => setTimeout(resolve,0));

test('background preparation starts with play, hands off a ready player and pins retired bytes through playback', async () => {
  const built = [], fetched = [];
  const doc = {createElement(tag) {
    const node = Object.assign(new EventTarget(), {
      tagName:tag.toUpperCase(), dataset:{}, setAttribute() {}, removeAttribute() {}, remove() {},
      pause() {}, load() {}, complete:true, naturalWidth:32, readyState:2, videoWidth:32,
    });
    built.push(node);
    return node;
  }};
  const cache = createMediaCache(()=>{},doc,async url => {
    fetched.push(url);
    return new Response(new Blob(['payload'],{type:'image/jpeg'}));
  });
  const rows = Array.from({length:12},(_,i)=>({url:origin+'/'+i,kind:i<4?'picture':'clip'}));
  try {
    cache.update(rows,true);
    await nextTurn();
    assert.equal(fetched.length,0);
    cache.setActive(true);
    const deadline = Date.now()+5000;
    while ((built.length < 6 || rows.some(row=>!cache.available(row.url))) && Date.now() < deadline) await nextTurn();
    assert.equal(fetched.length,12);
    assert.equal(built.length,6);
    const node = cache.take(rows[0],doc);
    assert(built.includes(node));
    assert.equal(built.length,6,'take reuses a prepared element');
    const url=node.src;
    assert.match(url,/^blob:/);
    cache.update(rows.slice(1),true);
    assert.equal(await (await fetch(url)).text(),'payload','a visible player survives catalog retirement');
    releasePlayer(node);
    await assert.rejects(fetch(url));
    cache.setActive(false);
    cache.update([...rows.slice(1),{url:origin+'/later',kind:'picture'}],true);
    await nextTurn();
    assert.equal(fetched.length,12);
    assert.equal(new Set(fetched).size,12);
  } finally { cache.setActive(false); cache.clear(); }
});

test('a catalog rotation preserves current prizes and a new account catalog invalidates them', () => {
  const sent=[];
  const p=createPictures({source:'none',scrolller:{origin:'device',consent:true,playWarmup:true},pageOrigin:origin,send:(...args)=>sent.push(args)});
  p.setGameplayActive(true);
  assert.equal(sent.length,0,'local gameplay is never a remote opt-in');
  p.setSource('scrolller');
  assert(sent.some(([type,data])=>type==='scrolller-play' && data.active));
  const feed=(poolId,id)=>({v:1,poolId,rows:[{kind:'image',url:origin+'/'+id,mime:'image/jpeg'}]});
  p.receiveScrolller(feed('pool-one',1));
  const rev=p.rewardRevision;
  const lease=new AbortController();
  assert.equal(p.rewardBatch(1,null,lease.signal)[0].url,origin+'/1');
  p.receiveScrolller(feed('pool-one',2));
  assert.equal(p.rewardRevision,rev);
  assert.equal(p.rewardBatch(1)[0].url,origin+'/2');
  p.receiveScrolller(feed('pool-two',2));
  assert(p.rewardRevision>rev);
  p.setGameplayActive(false);
  assert.deepEqual(sent.at(-1),['scrolller-play',{active:false}]);
  lease.abort(); p.setSource('none');
});
