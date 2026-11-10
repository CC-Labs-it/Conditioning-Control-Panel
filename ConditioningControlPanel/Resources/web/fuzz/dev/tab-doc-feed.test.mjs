import test from 'node:test';
import assert from 'node:assert/strict';
import { createPreviewFeed } from './tab-doc-feed.js';
import { previewPictureSource, rememberPreviewPictureSource } from './tab-doc-source.js';

const flush=async()=>{for(let i=0;i<6;i++) await Promise.resolve();};
function harness({bridge=true}={}) {
  let now=0,next=0,listener=null,awaiting=null,stills=[],removed=0;
  const timers=new Map(),statuses=[],messages=[];
  const transport={addEventListener(_,fn){listener=fn;},removeEventListener(){listener=null;},postMessage(value){messages.push(value);}};
  const feed=createPreviewFeed({location:{hostname:'127.0.0.1',origin:'http://127.0.0.1:8897'},window:bridge?{chrome:{webview:transport}}:{},
    document:{createElement:()=>({remove(){removed++;}}),head:{append(){}}},
    onStatus:(text,state)=>statuses.push({text,...state}),
    setTimer:(fn,delay)=>{const id=++next;timers.set(id,{fn,at:now+delay});return id;},clearTimer:id=>timers.delete(id)});
  const pictures={source:'none',requests:0,
    chooseScrolller(fn){awaiting=fn;return()=>{if(awaiting===fn) awaiting=null;};},
    setRemoteConsent(allowed,reason){this.source=allowed?'scrolller':'none';const fn=awaiting;awaiting=null;fn?.(allowed,reason);},
    receiveScrolller(frame){stills=this.source==='scrolller'?frame.rows.filter(row=>row.valid&&row.kind==='image'):[];},
    stillsForDisplay:()=>stills,requestFeed(){this.requests++;},setGameplayActive(){}};
  feed.attach(pictures);
  return {feed,pictures,timers,statuses,messages,get removed(){return removed;},
    receive(frame){listener?.({data:frame});},
    tick(ms){now+=ms;for(const [id,timer] of [...timers]) if(timer.at<=now){timers.delete(id);timer.fn();}}};
}

test('normal visits default to Scrolller; explicit own/none choices survive reload',()=>{
  const values=new Map(),storage={getItem:key=>values.get(key),setItem:(key,value)=>values.set(key,value)};
  assert.equal(previewPictureSource('',storage),'scrolller');
  rememberPreviewPictureSource('none',storage);
  assert.equal(previewPictureSource('',storage),'none');
  assert.equal(previewPictureSource('?pics=scrolller',storage),'scrolller');
  rememberPreviewPictureSource('own',storage);
  assert.equal(previewPictureSource('?at=lever',storage),'own');
  assert.equal(previewPictureSource('?pics=scrolller',storage),'scrolller');
  rememberPreviewPictureSource('scrolller',storage);
  assert.equal(previewPictureSource('',storage),'scrolller');
  assert.equal(previewPictureSource('?pics=none',storage),'none');
});

test('fixture previews remain offline unless Scrolller is explicitly requested',()=>{
  const storage={getItem:()=>'scrolller'};
  for(const query of ['?manual=1','?test=1','?manual=1&voice=1']) assert.equal(previewPictureSource(query,storage),'none');
  assert.equal(previewPictureSource('?test=1&pics=scrolller',storage),'scrolller');
});

test('host consent alone never reports ready and rejected rows never count as pictures',async()=>{
  const h=harness(),chosen=h.feed.choose();await flush();
  h.receive({type:'setting',key:'media.remoteConsent',value:true});assert.equal(await chosen,true);
  assert.equal(h.feed.debug().phase,'loading');assert.equal(h.pictures.requests,1);
  h.receive({type:'scrolller-feed',v:1,rows:[{kind:'image',url:'https://unapproved.example/photo.jpg'}],building:false});
  assert.equal(h.feed.debug().phase,'empty');assert.equal(h.feed.debug().imageCount,0);
  h.receive({type:'scrolller-feed',v:1,rows:[{kind:'image',url:'http://127.0.0.1:8897/photo.jpg',mime:'image/jpeg'}],building:false});
  assert.equal(h.feed.debug().phase,'ready');assert.equal(h.feed.debug().imageCount,1);
  h.feed.dispose();assert.equal(h.timers.size,0);
});

test('motion-only feeds count approved GIFs and clips without consuming a display pick',async()=>{
  const h=harness(),chosen=h.feed.choose();await flush();
  h.receive({type:'setting',key:'media.remoteConsent',value:true});await chosen;
  const video={kind:'video',url:'http://127.0.0.1:8897/movie.mp4',mime:'video/mp4'};
  h.receive({type:'scrolller-feed',v:1,rows:[video,video,
    {kind:'image',url:'http://127.0.0.1:8897/motion.gif',mime:'image/gif'},
    {kind:'video',url:'https://unapproved.example/movie.mp4',mime:'video/mp4'},
    {kind:'image',url:'http://127.0.0.1:8897/thumbnail.jpg',mime:'image/jpeg',isThumbnail:true}],building:false});
  assert.equal(h.feed.debug().phase,'ready');assert.equal(h.feed.debug().imageCount,2);
  assert.equal(h.timers.size,0);
  h.receive({type:'setting',key:'media.remoteConsent',value:false,reason:'disabled'});
  h.receive({type:'scrolller-feed',v:1,rows:[video],building:false});
  assert.equal(h.feed.debug().phase,'error');assert.equal(h.feed.debug().imageCount,0);
  h.feed.dispose();
});

test('empty feeds show a bounded failure, retry requests fresh host data, and denial stays authoritative',async()=>{
  const h=harness(),first=h.feed.choose();await flush();
  h.receive({type:'setting',key:'media.remoteConsent',value:true});await first;
  h.receive({type:'scrolller-feed',rows:[],building:true});h.tick(30000);
  assert.equal(h.feed.debug().phase,'empty');
  h.receive({type:'scrolller-feed',rows:[],building:true});assert.equal(h.feed.debug().phase,'empty');
  const retry=h.feed.choose();await flush();
  h.receive({type:'setting',key:'media.remoteConsent',value:false,reason:'allowance-used'});
  assert.equal(await retry,false);assert.match(h.statuses.at(-1).text,/allowance/);
  assert.equal(h.pictures.source,'none');h.feed.dispose();assert.equal(h.timers.size,0);
});

test('canceling an unanswered consent request and bridge load settles without retained timers',async()=>{
  const h=harness(),choice=h.feed.choose();await flush();h.feed.cancel();
  assert.equal(await choice,false);assert.equal(h.timers.size,0);h.feed.dispose();
  const opening=harness({bridge:false}),waiting=opening.feed.choose();await flush();
  opening.feed.dispose();assert.equal(await waiting,false);assert.equal(opening.timers.size,0);assert.equal(opening.removed,1);
});
