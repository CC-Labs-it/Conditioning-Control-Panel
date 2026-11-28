import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { libraryOrigin } from '../host/localLibrary.js';

const boot = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
const source = boot.slice(boot.indexOf('  const library = shared.library'), boot.indexOf("  lifetime.listen(cardEl, 'click'"));
function run({ saved, consent = false, available = true, files = [], deviceOrigin = false, own = null } = {}) {
  const calls = [], events = {}, views = [];
  const feed = { consent, catalog: [], setConsent(on) { calls.push(['consent', on]); },
    setNiches(id, niches) { calls.push(['niches', id, niches]); }, subscribe(fn) { this.echo = fn; return () => {}; }, onDeclined() { return () => {}; } };
  const caps = own ? { scrolller: { consent }, ownMedia: { library: true, mediaOrigin: 'https://ccp.assets' } } : { scrolller: { consent } };
  const context = { deviceOrigin, libraryOrigin, location: { origin: 'https://ccp.game' }, shared: { coursePicks: saved ? {keep:saved} : {}, courseFiles:{keep:files}, ownMedia: own === true ? null : own,
      bridge: { send(m) { calls.push(['send', m.type]); } } }, TRACK_ID:'keep',
    scrolller:available ? feed : null, init:{caps}, track:{id:'keep'}, trackName:'The Keep',
    createLocalLibrary:()=>({count:0,own:0,setFiles(rows){this.count=rows.length;this.own=0;},setRows(rows){this.count=rows.length;this.own=rows.length;},clear(){this.count=0;}}),
    libraryRows:(m)=>m?.rows||[],
    nichesFor:()=>['beta'], isDefault:()=>true, saveCustomization(){}, saveFilesChoice(){}, pickFiles:rows=>rows,
    feedSwitch:fn=>({current:fn}), openFilePicker(){ calls.push(['picker']); }, savePick(){},
    hud:{setScrolller(v){views.push(v);},on(name,fn){events[name]=fn;}}, lifetime:{alive:true,defer(){}},
  };
  vm.runInNewContext(source, context);
  return {calls,views,events,feed,shared:context.shared};
}
test('a saved Scrolller course restores its feed after another course used local files, waiting for host echo', () => {
  const r=run({saved:{source:'scrolller'}});
  assert.equal(r.calls[0][0],'niches');
  assert.deepEqual(r.calls[1],['consent',true]);
  assert.equal(r.views.at(-1).source,'off');
  r.feed.consent=true;r.feed.echo();
  assert.equal(r.views.at(-1).source,'scrolller');
});
test('an unconfigured course preserves remote consent off, while local files work without a remote feed', () => {
  const r=run();assert.equal(r.calls.some(c=>c[0]==='consent'&&c[1]),false);
  assert.equal(r.views.at(-1).source,'off');
  const local=run({saved:{source:'files'},available:false,files:[{},{}]});
  assert.equal(local.views.at(-1).source,'files');assert.equal(local.views.at(-1).files,2);
  assert.equal(local.calls.length,0);
});
test('on a device origin a saved Scrolller course never raises consent by itself: the phone asks the player first', () => {
  const r=run({saved:{source:'scrolller'},deviceOrigin:true});
  assert.equal(r.calls.some(c=>c[0]==='consent'),false);
  assert.equal(r.views.at(-1).source,'off');
});
test('desktop library: the page asks once, a fresh course with no consent starts on My files and fills from the answer', () => {
  const rows=[{url:'https://ccp.assets/images/a.jpg',kind:'image'},{url:'https://ccp.assets/videos/b.mp4',kind:'video'}];
  const r=run({own:true});
  assert.deepEqual(r.calls.filter(c=>c[0]==='send'),[['send','own-media-list']]);
  assert.equal(r.calls.some(c=>c[0]==='consent'),false,'Scrolller consent is never touched');
  assert.equal(r.views.at(-1).source,'files');assert.equal(r.views.at(-1).files,0);
  r.shared.ownMedia={v:1,rows};r.shared.onOwnMedia();
  assert.equal(r.views.at(-1).files,2);assert.equal(r.shared.library.own,2);
  assert.equal(r.calls.some(c=>c[0]==='picker'),false,'no browser picker');
});
test('desktop library: a hand pick still wins, a Scrolller course stays Scrolller, and an answered library needs no picker', () => {
  const rows=[{url:'https://ccp.assets/images/a.jpg',kind:'image'}];
  const hand=run({own:{v:1,rows},saved:{source:'files'},files:[{},{},{}]});
  assert.equal(hand.views.at(-1).files,3);assert.equal(hand.shared.library.own,0);
  const scr=run({own:{v:1,rows},saved:{source:'scrolller'}});
  assert.equal(scr.shared.library.count,0,'nothing loads while Scrolller is the source');
  const off=run({own:{v:1,rows},saved:{source:'off'}});
  off.events.scrolller({source:'files'});
  assert.equal(off.views.at(-1).source,'files');assert.equal(off.views.at(-1).files,1);
  assert.equal(off.calls.some(c=>c[0]==='picker'),false);
  const empty=run({own:{v:1,rows:[]},saved:{source:'off'}});
  empty.events.scrolller({source:'files'});
  assert.equal(empty.calls.some(c=>c[0]==='picker'),true,'an empty library falls back to the picker');
  assert.equal(run({}).calls.some(c=>c[0]==='send'),false,'a host without the cap is never asked');
});
