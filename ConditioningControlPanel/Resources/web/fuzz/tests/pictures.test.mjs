import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createPictures, PICTURE_SOURCES } from '../ui/pictures.js';
import { pictureRow } from '../ui/picture-card.js';
import { PICTURE_CARD } from '../data/content/terms.js';
import { createEngine } from '../ui/engine.js';
import * as E from '../sim/economy.js';
import { createLife } from '../sim/save.js';

test('real Scrolller selection requires a host capability and its consent', () => {
  assert.deepEqual([...PICTURE_SOURCES], ['online', 'own', 'none', 'scrolller']);
  assert.equal(createPictures({ source: 'scrolller' }).source, 'none');
});

test('a consented feed accepts unique stills on the specified host origin and clears on revoke', async () => {
  const origin = 'http://127.0.0.1:8807';
  const sent = [];
  const p = createPictures({ scrolller:{consent:true,origin:'device'}, pageOrigin:origin, send:(...args)=>sent.push(args) });
  assert.equal(p.source, 'scrolller');
  p.requestFeed();
  assert.deepEqual(sent,[['scrolller-niches',{niches:[]}]]);
  p.receiveScrolller({v:1,rows:[
    {kind:'image',url:origin+'/a.jpg'}, {kind:'image',url:origin+'/a.jpg'}, {kind:'image',url:origin+'/b.webp'},
    {kind:'image',url:'https://example.org/c.jpg'}, {kind:'image',url:origin+'/clip.gif'},
    {kind:'video',url:origin+'/v.mp4'}, {kind:'image',url:origin+'/x.svg'}, {kind:'image',url:origin+'/bad',mime:'text/html'},
  ]});
  assert.equal(await p.next(), null, 'consumers must support visible attribution and removal');
  assert.deepEqual(await p.next({allowScrolller:true}), {url:origin+'/a.jpg',kind:'picture',credit:'via Scrolller'});
  assert.equal((await p.next({allowScrolller:true})).url, origin+'/b.webp');
  assert.equal((await p.next({allowScrolller:true})).url, origin+'/a.jpg');
  assert.equal(await p.next({kind:'clip'}), null);
  p.setRemoteConsent(false);
  assert.equal(await p.next(), null);
  assert.equal(p.source, 'none');
  p.receiveScrolller({v:1,rows:[{kind:'image',url:origin+'/c.jpg'}]});
  assert.equal(await p.next(), null);
});

test('the consent card waits for the host, and its cancellation preserves an explicit local selection', async () => {
  const sent = [], decisions = [];
  const pictures = createPictures({source:'own',scrolller:{consent:false},send:(...args)=>sent.push(args)});
  const cancel = pictures.chooseScrolller(value => decisions.push(value));
  assert.equal(pictures.source,'own');
  assert.deepEqual(sent[0], ['set-setting',{key:'media.remoteConsent',value:true,ask:true}]);
  cancel(); pictures.setRemoteConsent(true);
  assert.equal(pictures.source,'own'); assert.deepEqual(decisions,[]);
  pictures.chooseScrolller(value => decisions.push(value));
  assert.deepEqual(decisions,[true]); assert.equal(pictures.source,'scrolller');
  pictures.receiveScrolller({v:1,rows:[{kind:'image',url:'https://cdn.scrolller.com/a.jpg'},{kind:'image',url:'https://scrolller.com.example.org/b.jpg'}]});
  assert.equal((await pictures.next({allowScrolller:true})).url,'https://cdn.scrolller.com/a.jpg');
  pictures.setSource('none');
});

test('none resolves null, online resolves a local placeholder with no network', async () => {
  const p = createPictures({ source: 'none' });
  assert.equal(await p.next({ niche: 'x' }), null);
  p.setSource('online');
  const got = await p.next({ niche: 'x' });
  assert.ok(got.url.startsWith('data:image/svg+xml,'));
  assert.equal(got.credit, null);
});

test('own files stay blob urls on this device, rotate, and skip anything that is not an image', async () => {
  const p = createPictures({ source: 'own' });
  assert.equal(await p.next(), null, 'no files yet is an empty frame, not an error');
  const kept = p.addOwn([new Blob(['a'], { type: 'image/png' }), new Blob(['b'], { type: 'text/plain' }), new Blob(['c'], { type: 'image/webp' })]);
  assert.equal(kept, 2);
  const a = await p.next(), b = await p.next(), c = await p.next();
  assert.ok(a.url.startsWith('blob:'));
  assert.notEqual(a.url, b.url);
  assert.equal(a.url, c.url);
  p.clearOwn();
  assert.equal(p.ownCount, 0);
  assert.equal(await p.next(), null);
});

test('a source change reaches unlocks as a pictures event, and through ctx.pictures', () => {
  const heard = [];
  let ctx;
  const run = E.createRun();
  E.grant(run, 'stray');
  const ui = { doc: {}, buttonFor: () => ({}), layer: () => ({}) };
  const engine = createEngine({ run, life: createLife(), ui, registry: { stray: { mount(c) { ctx = c; c.on('pictures', (e) => heard.push(e.source)); return {}; } } } });
  engine.mount('stray');
  assert.equal(ctx.pictures.source, 'none', 'empty frames until the player chooses');
  ctx.pictures.setSource('own');
  assert.equal(ctx.pictures.source, 'own');
  assert.deepEqual(heard, ['own']);
});

test('a new pick replaces the last one, and a pick with nothing usable changes nothing', async () => {
  const heard = [];
  const p = createPictures({ source: 'own', onChange: (s) => heard.push(s) });
  assert.equal(p.replaceOwn([new Blob(['a'], { type: 'image/png' }), new Blob(['g'], { type: 'image/gif' })]), 2);
  const first = await p.next();
  assert.equal(p.replaceOwn([new Blob(['t'], { type: 'text/plain' })]), 0);
  assert.equal(p.ownCount, 2);
  assert.equal(p.replaceOwn([new Blob(['b'], { type: 'image/webp' })]), 1);
  assert.equal(p.ownCount, 1);
  assert.notEqual((await p.next()).url, first.url);
  assert.deepEqual(heard, ['own', 'own']);
});

test('repeated host snapshots preserve an active deal and the slideshow position', async () => {
  const origin = 'http://127.0.0.1:8817';
  const p = createPictures({scrolller:{consent:true,origin:'device'},pageOrigin:origin});
  const frame = {v:1,rows:['one','two'].map(id => ({kind:'image',url:origin+'/'+id}))};
  p.receiveScrolller(frame);
  const revision = p.rewardRevision;
  const first = await p.next({allowScrolller:true});
  p.receiveScrolller(frame);
  assert.equal(p.rewardRevision,revision);
  assert.notEqual((await p.next({allowScrolller:true})).url,first.url);
  p.setSource('none');
});

test('the pause row says what is true', () => {
  assert.match(pictureRow({ source: 'none', ownCount: 0 }), /empty/);
  assert.match(pictureRow({ source: 'own', ownCount: 2 }), /2/);
  assert.match(pictureRow({ source: 'own', ownCount: 0 }), /not loaded/);
});

test('the picker delegates sign-in and metered fetching to the host', () => {
  const card = readFileSync(new URL('../ui/picture-card.js', import.meta.url), 'utf8');
  assert.ok(!/fetch\(|XMLHttpRequest|scrolller\.com/i.test(card));
  assert.match(PICTURE_CARD.signinSub, /daily Scrolller allowance/);
  assert.match(PICTURE_CARD.filesSub, /50 GIFs/);
});

test('the local route stays dormant, and approved motion rows are available to credited screens', async () => {
  const origin = 'http://127.0.0.1:8817', sent = [];
  const p = createPictures({source:'none',scrolller:{consent:false,origin:'device',clips:true,sessionOptIn:true},pageOrigin:origin,send:(...args)=>sent.push(args)});
  const feed = {v:1,rows:[{kind:'video',url:origin+'/video',mime:'video/mp4'}, {kind:'image',url:origin+'/print',mime:'image/jpeg'}]};
  p.requestFeed(); p.receiveScrolller(feed);
  assert.deepEqual(sent,[]); assert.equal(await p.next({allowScrolller:true}),null);
  let accepted;
  p.chooseScrolller(value => { accepted = value; }); p.setRemoteConsent(true);
  p.receiveScrolller({...feed,usedPercent:17}); assert.equal(accepted,true);
  assert.match(pictureRow(p), /17%/);
  const revision = p.rewardRevision;
  p.receiveScrolller({...feed,usedPercent:18});
  assert.equal(p.rewardRevision,revision);
  assert.match(pictureRow(p), /18%/);
  assert.equal((await p.next({kind:'gif',allowScrolller:true})).kind,'clip');
  assert.equal((await p.next({kind:'picture',allowScrolller:true})).credit,'via Scrolller');
  assert.deepEqual(p.rewardBatch().map(row => row.url),[origin+'/print'], 'a movie cannot enter the standalone photo prize deck');
  p.setSource('own');
  assert.equal(p.usedPercent,null);
  assert.equal(sent.at(-1)[0],'scrolller-release');
  p.receiveScrolller(feed);
  assert.equal(await p.next({allowScrolller:true}),null);
});
