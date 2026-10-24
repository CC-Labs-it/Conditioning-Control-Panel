import{test}from'node:test';import assert from'node:assert/strict';
import{createLoading}from'../hud/loading.js';

test('boot cover reports monotonic real progress and waits for a complete painted menu before revealing it',async()=>{
  const nodes={'.boot-stage':{textContent:''},'.boot-fill':{style:{}},'.boot-percent':{textContent:''},'[role=progressbar]':{setAttribute(k,v){this[k]=v;}}};
  const styles={},classes=new Set(),events={},frames=[],timers=[];
  const root={querySelector:s=>nodes[s],style:{setProperty(k,v){styles[k]=v;}},classList:{add:c=>classes.add(c)},addEventListener(k,v){events[k]=v;},remove(){this.removed=true;}};
  const doc={getElementById:()=>root,body:{dataset:{boot:'loading'}}};
  const raf=globalThis.requestAnimationFrame,timeout=globalThis.setTimeout;
  globalThis.requestAnimationFrame=fn=>{frames.push(fn);};globalThis.setTimeout=fn=>{timers.push(fn);};
  try{
    const cover=createLoading(doc);cover.set('Opening',.2);cover.set('Racers',.1);
    assert.equal(nodes['[role=progressbar]']['aria-valuenow'],'20');
    cover.set('Ready',1);const finished=cover.finish();
    assert.equal(doc.body.dataset.boot,'loading');assert.equal(classes.size,0);
    frames.shift()();assert.equal(doc.body.dataset.boot,'loading','first frame stays fully covered');
    frames.shift()();await finished;
    assert.equal(doc.body.dataset.boot,'ready');assert.ok(classes.has('boot-leaving'));
    cover.set('Late update',.4);assert.equal(nodes['.boot-percent'].textContent,'100%');
    events.transitionend();assert.ok(root.removed);
  }finally{globalThis.requestAnimationFrame=raf;globalThis.setTimeout=timeout;}
});
