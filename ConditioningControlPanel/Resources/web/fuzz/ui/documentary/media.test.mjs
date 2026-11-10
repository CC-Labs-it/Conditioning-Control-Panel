import test from 'node:test';
import assert from 'node:assert/strict';
import { createMedia } from './media.js';
import { createMediaCache } from '../media-cache.js';

const still={kind:'picture',url:'https://museum.test/still.webp'};
const clip={kind:'clip',url:'https://museum.test/clip.mp4'};
const gif={kind:'gif',url:'https://museum.test/motion.gif'};
const flush=async()=>{for(let i=0;i<32;i++) await Promise.resolve();};
function deferred() {let resolve;const promise=new Promise(done=>{resolve=done;});return {promise,resolve};}

class Node extends EventTarget {
  constructor(tag,doc) {
    super();this.tagName=tag.toUpperCase();this.ownerDocument=doc;this.children=[];
    this.attributes=new Map();this.dataset={};this.style={};this.hidden=false;
    this.complete=false;this.naturalWidth=0;this.readyState=0;this.videoWidth=0;
    this.paused=true;this.playCalls=0;this.pauseCalls=0;this.loadCalls=0;this.assignments=[];
  }
  set src(value) {
    this.attributes.set('src',value);this.assignments.push({url:value,policy:this.referrerPolicy});
    this.complete=false;this.naturalWidth=0;this.readyState=0;this.videoWidth=0;
    if(this.ownerDocument.autoReady(value,this)) this.ready(false);
  }
  get src() {return this.attributes.get('src')||'';}
  setAttribute(name,value) {this.attributes.set(name,value);}
  getAttribute(name) {return this.attributes.get(name)??null;}
  removeAttribute(name) {
    this.attributes.delete(name);
    if(name==='src') {this.complete=false;this.naturalWidth=0;this.readyState=0;this.videoWidth=0;}
  }
  append(...nodes) {for(const node of nodes) {node.remove();node.parentNode=this;this.children.push(node);}}
  replaceChildren(...nodes) {for(const child of [...this.children]) child.remove();this.append(...nodes);}
  remove() {if(this.parentNode) {const list=this.parentNode.children;list.splice(list.indexOf(this),1);this.parentNode=null;}}
  ready(emit=true) {
    this.complete=true;this.naturalWidth=640;this.readyState=4;this.videoWidth=640;
    if(emit) {const type=this.tagName==='VIDEO'?'loadeddata':'load';this.dispatchEvent(new Event(type));this['on'+type]?.();}
  }
  fail() {this.dispatchEvent(new Event('error'));this.onerror?.();}
  decode() {return this.complete?Promise.resolve():new Promise(()=>{});}
  play() {this.playCalls++;this.paused=false;return this.playPromise||Promise.resolve();}
  pause() {this.pauseCalls++;this.paused=true;}
  load() {this.loadCalls++;}
}
function harness(options={}) {
  const doc={hidden:false,baseURI:'https://museum.test/dev/tab-doc.html',nodes:[],autoReady:options.autoReady||(()=>true)};
  doc.createElement=tag=>{const node=new Node(tag,doc);doc.nodes.push(node);return node;};
  const listeners=new Map(),timers=new Map(),leases=[],calls=[];
  let serial=0,views=0;
  const pictures={source:'own',next:async query=>{calls.push(query);return still;},dealStill:()=>still,
    acquire:async row=>{const lease={url:row.url,releases:0,release(){this.releases++;}};leases.push(lease);return lease;},
    noteView:()=>views++,...options.pictures};
  const ctx={pictures,paused:false,gentle:false,reduced:false,state:{ended:true},
    on(id,fn){if(!listeners.has(id))listeners.set(id,new Set());listeners.get(id).add(fn);return()=>listeners.get(id).delete(fn);},...options.ctx};
  const media=createMedia(ctx,{lite:options.lite,setTimer(fn){const id=++serial;timers.set(id,fn);return id;},clearTimer:id=>timers.delete(id)});
  const emit=id=>{for(const fn of [...(listeners.get(id)||[])]) fn();};
  const mount=()=>{const container=doc.createElement('div');const remove=media.mount(container,{kind:'run-picture'});return {container,layer:container.children[0],remove};};
  return {doc,pictures,ctx,media,emit,mount,timers,leases,calls,listeners,get views(){return views;}};
}
const player=slot=>slot.layer.children.find(node=>node.tagName==='IMG'||node.tagName==='VIDEO');

test('a cached same-origin clip reuses its prepared player and releases its blob lease',async()=>{
  const h=harness(),revoked=[];
  const revoke=URL.revokeObjectURL;
  URL.revokeObjectURL=url=>{revoked.push(url);revoke(url);};
  const cache=createMediaCache(()=>{},h.doc,async()=>({ok:true,headers:{get:()=>null},blob:async()=>new Blob(['fixture'],{type:'video/mp4'})}));
  try {
    cache.update([clip],true);cache.setActive(true);await flush();
    assert.equal(cache.prepared(clip.url),true);
    const prepared=h.doc.nodes.find(node=>node.tagName==='VIDEO');
    const cachedUrl=prepared.src;
    assert.match(cachedUrl,/^blob:/);
    h.pictures.next=async query=>{assert.equal(query.kind,'mixed');assert.equal(query.allowScrolller,true);return clip;};
    h.pictures.takePlayer=cache.take;h.pictures.acquire=()=>{throw Error('A borrowed player must not acquire twice');};
    const slot=h.mount();await flush();
    assert.equal(player(slot),prepared);assert.equal(prepared.assignments.length,1);
    assert.equal(prepared.dataset.kind,'clip');assert.equal(prepared.paused,false);
    assert.equal(h.ctx.state.ended,true);assert.equal(h.media.debug().clips,1);
    cache.update([],true);assert.equal(revoked.includes(cachedUrl),false,'visible borrower keeps the bytes leased');
    h.media.dispose();assert.equal(revoked.filter(url=>url===cachedUrl).length,1);
    assert.equal(prepared.src,'');assert.equal(prepared.paused,true);assert.equal(prepared.parentNode,null);
    assert.equal(h.timers.size,0);
  } finally {h.media.dispose();cache.clear();URL.revokeObjectURL=revoke;}
});

test('GIF pause removes its source, resumes the same URL, and cancels late motion under pause',async()=>{
  const h=harness({pictures:{next:async()=>gif}}),slot=h.mount();await flush();
  const image=player(slot);assert.equal(h.media.debug().gifs,1);assert.equal(h.views,1);
  h.media.pause(true);assert.equal(image.src,'');assert.equal(slot.layer.hidden,true);
  h.media.pause(false);await flush();assert.equal(image.src,gif.url);assert.equal(slot.layer.hidden,false);
  assert.equal(h.views,1);assert.equal(image.assignments.length,2);
  h.media.dispose();assert.equal(h.leases[0].releases,1);

  const selected=deferred(),late=harness({pictures:{next:()=>selected.promise}}),lateSlot=late.mount();
  assert.equal(late.media.debug().motion,1);late.media.pause(true);selected.resolve(gif);await flush();
  assert.equal(late.media.debug().motion,0);assert.equal(player(lateSlot),undefined);assert.equal(late.leases.length,0);
  late.pictures.next=async()=>clip;late.media.pause(false);await flush();
  const video=player(lateSlot),playing=deferred();video.playPromise=playing.promise;
  late.media.pause(false);late.media.pause(true);playing.resolve();await flush();
  assert.equal(video.paused,true,'a late play promise cannot restart playback after pause');late.media.dispose();

  const calm=harness({ctx:{reduced:true}});calm.mount();await flush();
  assert.equal(calm.calls.length,0);assert.equal(calm.media.debug().motion,0);calm.media.dispose();
});

test('pictures-added promotes an existing still only after motion is ready and deduplicates pending work',async()=>{
  const h=harness({autoReady:url=>!url.endsWith('.mp4')}),slot=h.mount();await flush();
  const photo=player(slot);assert.equal(photo.src,still.url);assert.equal(slot.layer.hidden,false);
  let selections=0;h.pictures.next=async()=>{selections++;return clip;};
  h.emit('pictures-added');await flush();
  const pending=slot.layer.children.find(node=>node.tagName==='VIDEO');
  assert.ok(pending);assert.equal(photo.parentNode,slot.layer);assert.equal(photo.style.visibility,'');
  assert.equal(pending.style.visibility,'hidden');assert.equal(h.leases[0].releases,0);
  h.emit('pictures-added');h.emit('pictures-added');await flush();
  assert.equal(selections,1);assert.equal(h.media.debug().pending,1);assert.equal(h.media.debug().motion,1);
  pending.ready();await flush();
  assert.equal(player(slot),pending);assert.equal(photo.parentNode,null);assert.equal(h.leases[0].releases,1);
  assert.equal(h.media.debug().clips,1);assert.equal(h.timers.size,0);h.media.dispose();

  const failed=harness({autoReady:url=>!url.endsWith('.mp4')}),kept=failed.mount();await flush();
  const original=player(kept);failed.pictures.next=async()=>clip;failed.emit('pictures-added');await flush();
  kept.layer.children.find(node=>node.tagName==='VIDEO').fail();await flush();
  assert.equal(player(kept),original);assert.equal(failed.media.debug().motion,0);assert.equal(failed.media.debug().pending,0);
  assert.equal(failed.timers.size,0);failed.media.dispose();
});

test('GIF and video share the pending motion budget and freeing a slot promotes another mounted still',async()=>{
  for(const lite of [false,true]) {
    const cap=lite?1:2,pending=[];
    const h=harness({lite,pictures:{next:query=>{assert.equal(query.kind,'mixed');const job=deferred();pending.push(job);return job.promise;}}});
    const slots=Array.from({length:cap+2},()=>h.mount());await flush();
    assert.equal(pending.length,cap);assert.equal(h.media.debug().motion,cap);
    pending.forEach((job,i)=>job.resolve(i%2?gif:clip));await flush();
    assert.equal(h.media.debug().clips+h.media.debug().gifs,cap);
    assert.equal(player(slots[cap]).src,still.url);
    h.pictures.next=async()=>gif;slots[0].remove();await flush();
    assert.equal(h.media.debug().motion,cap);assert.equal(player(slots[cap]).src,gif.url);
    assert.equal(h.media.debug().clips+h.media.debug().gifs,cap);h.media.dispose();
    assert.equal(h.media.debug().motion,0);assert.equal(h.leases.every(lease=>lease.releases===1),true);
  }
});

test('source replacement and disposal release stale acquisitions, pending decoders and active players exactly once',async()=>{
  const leaseReady=deferred(),h=harness({pictures:{next:async()=>clip,acquire:()=>leaseReady.promise}}),slot=h.mount();await flush();
  h.pictures.source='none';h.emit('pictures');
  const lateLease={url:clip.url,releases:0,release(){this.releases++;}};leaseReady.resolve(lateLease);await flush();
  assert.equal(lateLease.releases,1);assert.equal(player(slot),undefined);assert.equal(h.media.debug().motion,0);h.media.dispose();

  const pending=harness({autoReady:()=>false,pictures:{next:async()=>gif}}),waiting=pending.mount();await flush();
  const image=player(waiting);assert.equal(pending.timers.size,1);
  pending.pictures.source='none';pending.emit('pictures');image.ready();await flush();
  assert.equal(pending.leases[0].releases,1);assert.equal(pending.timers.size,0);assert.equal(waiting.layer.hidden,true);
  assert.equal(pending.media.debug().gifs,0);pending.media.dispose();pending.media.dispose();
  assert.equal([...pending.listeners.values()].every(set=>set.size===0),true);

  const active=harness({pictures:{next:async()=>clip}});active.mount();await flush();active.media.dispose();active.media.dispose();
  assert.equal(active.leases[0].releases,1);assert.deepEqual(active.media.debug(),{slots:0,motion:0,pending:0,clips:0,gifs:0});
});

test('external Scrolller uses a DOM player with strict-origin before src and plain-text attribution',async()=>{
  const remote={kind:'gif',url:'https://images.scrolller.com/fixture.gif',credit:'via Scrolller <fixture>'};
  const h=harness({pictures:{source:'scrolller',next:async()=>remote,takePlayer(){throw Error('Remote sources cannot borrow a src-first player');}}});
  const slot=h.mount();await flush();const image=player(slot);
  assert.equal(image.tagName,'IMG');assert.equal(image.dataset.kind,'gif');
  assert.deepEqual(image.assignments,[{url:remote.url,policy:'strict-origin'}]);
  assert.equal(image.getAttribute('referrerpolicy'),'strict-origin');
  const credit=slot.layer.children.find(node=>node.className==='td-run-credit');
  assert.equal(credit.textContent,remote.credit);assert.equal(h.doc.nodes.some(node=>node.tagName==='CANVAS'),false);
  assert.equal(h.views,1);h.media.dispose();assert.equal(h.leases[0].releases,1);
});
