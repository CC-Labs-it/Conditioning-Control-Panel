import test from 'node:test';
import assert from 'node:assert/strict';
import {createPictures} from '../ui/pictures.js';
import {scrolllerMedia} from '../ui/scrolller-pictures.js';

const origin = 'http://127.0.0.1:8817';
const host = {consent:true, origin:'device', clips:true};
const feed = rows => ({v:1, rows});
const row = (i, type='video') => ({kind:type, url:origin+'/'+i, mime:type==='video' ? 'video/mp4' : 'image/jpeg'});
const rows = Array.from({length:36}, (_,i) => row(i, i < 12 ? 'image' : 'video'));

test('Fuzz validates thirty-six unique device rows while still applying origin, MIME and clip capability checks', () => {
  const input = feed([{...rows[0],url:'https://example.org/stranger'}, ...rows, rows[1], row(37)]);
  assert.equal(scrolllerMedia(input,host,origin).length,36);
  assert.equal(scrolllerMedia(input,{...host,clips:false},origin).length,12);
  assert.equal(scrolllerMedia(feed(rows.map(r => ({...r,mime:'text/html'}))),host,origin).length,0);
});

test('twelve photos and twenty-four loops make a nonrepeating, motion-first round', async () => {
  const p = createPictures({scrolller:host,pageOrigin:origin});
  p.receiveScrolller(feed(rows));
  const turns = [];
  for (let i=0;i<72;i++) turns.push(await p.next({kind:'mixed',allowScrolller:true,previousUrl:turns.at(-1)?.url}));
  for (let i=0;i<72;i+=36) {
    const round=turns.slice(i,i+36);
    assert.equal(new Set(round.map(x=>x.url)).size,36);
    assert.equal(round.filter(x=>x.kind==='clip').length,24);
    assert(round.every(x=>x.credit==='via Scrolller'));
  }
  assert(turns.every((x,i)=>!i||x.url!==turns[i-1].url));
  p.setRemoteConsent(false);
});

test('warming more rows leaves a print in flight and offers new playable media before repeating', async () => {
  const changes=[], adds=[];
  const p=createPictures({scrolller:host,pageOrigin:origin,onChange:x=>changes.push(x),onAdded:x=>adds.push(x)});
  const start=[rows[0],rows[12]];
  p.receiveScrolller(feed(start));
  const first=await p.next({kind:'gif',allowScrolller:true});
  const lease=new AbortController();
  p.rewardBatch(1,rows[0].url,lease.signal);
  const revision=p.rewardRevision;
  p.receiveScrolller(feed(rows));
  assert.equal(p.rewardRevision,revision);
  assert.equal(changes.length,1); assert.equal(adds.length,1);
  for(let i=0;i<23;i++) assert.notEqual((await p.next({kind:'gif',allowScrolller:true})).url,first.url);
  assert(p.rewardBatch(3).every(r=>r.url!==rows[0].url));
  p.receiveScrolller(feed(rows.slice(1)));
  assert.notEqual(p.rewardRevision,revision);
  lease.abort();p.setRemoteConsent(false);
});

test('local GIFs and actual videos share the moving rotation', async () => {
  const p=createPictures({source:'own'});
  const files=Array.from({length:24},(_,i)=>new File(['sample'],i+'.media',{type:i%2?'image/gif':'video/webm'}));
  p.addOwn(files);
  const dealt=[];
  for(let i=0;i<24;i++) dealt.push(await p.next({kind:'mixed'}));
  assert.equal(dealt.filter(row=>row.kind==='gif').length,12);
  assert.equal(dealt.filter(row=>row.kind==='clip').length,12);
  assert.equal(new Set(dealt.map(row=>row.url)).size,24);
  p.clearOwn();
});
