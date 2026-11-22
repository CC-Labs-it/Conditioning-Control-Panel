import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {createArchiveStory} from '../ui/expo/archive-story.js';
import {createPlanterVisit} from '../ui/expo/planter.js';
import {visitDirection} from '../ui/expo/visit-guide.js';
import {serialize,parse,createLife} from '../sim/save.js';

const manifest=JSON.parse(await readFile(new URL('../assets/expo/narration/manifest.json',import.meta.url),'utf8'));
test('the archive follows the approved order in freely chosen rooms and repeats only on request',()=>{
  const story=createArchiveStory([...manifest].reverse());
  const expected=manifest.filter(row=>row.set==='event');
  assert.equal(expected.length,40);
  for(const [i,row] of expected.entries()){
    const from='slot_'+String(40-i).padStart(2,'0'), ticket=story.request(from);
    assert.equal(ticket.row.id,row.id);
    assert.equal(story.request('another'),null);
    story.finish(ticket,true);
    assert.equal(story.canAutoplay(from),false);
    assert.equal(story.at(from),row);
    const review=story.request(from,true);
    assert.equal(review.row.id,row.id);
    story.finish(review,true);
    assert.equal(story.completed,i+1);
    assert.ok(row.archive.title && row.archive.date && row.archive.body);
    assert.equal(row.slides.length,3);
  }
  assert.equal(story.request('last'),null);
  assert.equal(story.displayed.id,expected.at(-1).id);
});
test('abandoned, stale and interrupted events cannot advance progress',()=>{
  let saved,sites;
  const story=createArchiveStory(manifest,'bad-old-id',(id,places)=>{saved=id;sites=places;});
  const lost=story.request('slot_13');
  story.finish(lost,false);
  const retry=story.request('slot_22');
  story.finish(lost,true);
  assert.equal(story.completed,0);
  story.finish(retry,true);
  assert.equal(createArchiveStory(manifest,saved).next.archive.number,2);
  const restored=createArchiveStory(manifest,saved,null,sites);
  assert.equal(restored.canAutoplay('slot_22'),false);
  assert.equal(restored.at('slot_22').archive.number,1);
  for(let i=0;i<2;i++)story.finish(story.request('slot_22'),true);
  assert.equal(story.canAutoplay('slot_22'),false);
  assert.equal(story.canAutoplay('slot_11'),true);
  assert.equal(story.request('slot_01').row.archive.number,2);
});
test('older saves retain their place and every completed record has a replay screen',()=>{
  const records=manifest.filter(r=>r.set==='event');
  for(const n of [0,1,12,39,40]){
    for(const history of [undefined,['slot_05','slot_05','unknown']]){
      const s=createArchiveStory(manifest,records[n-1]?.id,null,history);
      const slots=Array.from({length:40},(_,i)=>'slot_'+String(i+1).padStart(2,'0'));
      assert.deepEqual(slots.map(id=>s.assigned(id)?.archive.number).filter(Boolean).sort((a,b)=>a-b),records.slice(0,n).map(r=>r.archive.number));
      assert.equal(slots.filter(id=>s.canAutoplay(id)).length,40-n);
    }
  }
});
test('the planter claims the lifetime on first landing, including a visit abandoned before the first cue',()=>{
  const life=createLife();
  function visit(owner) {
    const once=()=>{const flags=owner.store.ll_end||={};if(flags.planterDetour)return false;flags.planterDetour=true;return true;};
    return createPlanterVisit(()=>{},once);
  }
  const first=visit(life);
  first.land({planter:true},{support:{planter:false}});
  assert.equal(first.held,true);
  first.leave();
  const imported=visit(parse(serialize(null,life)).life);
  imported.land({planter:true},{support:{planter:false}});
  assert.equal(imported.held,false);
});

test('admission and local exhibit directions are independent of optional record progress',()=>{
  const story=createArchiveStory(manifest), at={x:0,y:0,z:0}, visitor={ticket:{issued:true}};
  assert.match(visitDirection(null,story,'atrium',at,{}),/turnstile/);
  const direction=visitDirection(null,story,'atrium',at,visitor);
  story.finish(story.request('slot_14'),true);
  assert.equal(visitDirection(null,story,'atrium',at,visitor),direction);
  assert.match(visitDirection(null,story,'hoof',at,{hoof:'pictures'}),/picture grid/);
  assert.match(visitDirection(null,story,'shop',at,{boughtExit:true}),/Renewal door/);
});

test('a simultaneous planter claim cannot start a second set of timed cues',async()=>{
  let resolve;
  const claim=new Promise(r=>{resolve=r;});
  const cues=[];
  const other=createPlanterVisit(id=>cues.push(id),()=>claim);
  other.land({planter:true},{support:{}});
  other.tick(120);
  assert.deepEqual(cues,[]);
  resolve(false);
  await claim;
  other.tick(120);
  assert.equal(other.held,false);
  assert.deepEqual(cues,[]);
});
