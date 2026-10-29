import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const boot = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
const source = boot.slice(boot.indexOf('  const library = shared.library'), boot.indexOf("  lifetime.listen(cardEl, 'click'"));
function run({ saved, consent = false, available = true, files = [] } = {}) {
  const calls = [], events = {}, views = [];
  const feed = { consent, catalog: [], setConsent(on) { calls.push(['consent', on]); },
    setNiches(id, niches) { calls.push(['niches', id, niches]); }, subscribe(fn) { this.echo = fn; return () => {}; } };
  const context = { shared: { coursePicks: saved ? {keep:saved} : {}, courseFiles:{keep:files} }, TRACK_ID:'keep',
    scrolller:available ? feed : null, init:{caps:{scrolller:{consent}}}, track:{id:'keep'}, trackName:'The Keep',
    createLocalLibrary:()=>({count:0,setFiles(rows){this.count=rows.length;},clear(){this.count=0;}}),
    nichesFor:()=>['beta'], isDefault:()=>true, saveCustomization(){}, saveFilesChoice(){}, pickFiles:rows=>rows,
    feedSwitch:fn=>({current:fn}), openFilePicker(){}, savePick(){},
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
