import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {createArchiveStory} from '../ui/expo/archive-story.js';
import {ARCHIVE_SCREENS,screenHome} from '../ui/expo/archive-screens.js';
import {createPlanterVisit,planterLines} from '../ui/expo/planter.js';
import {visitDirection} from '../ui/expo/visit-guide.js';
import {serialize,parse,createLife} from '../sim/save.js';

const manifest=JSON.parse(await readFile(new URL('../assets/expo/narration/manifest.json',import.meta.url),'utf8'));
test('whichever screen is pressed plays the next record of the story, and a screen rests on the last one it showed',()=>{
  const story=createArchiveStory([...manifest].reverse());
  const expected=manifest.filter(row=>row.set==='event');
  assert.ok(expected.length>=40);assert.equal(story.total,expected.length);
  for(const [i,row] of expected.entries()){
    const from=ARCHIVE_SCREENS[i%ARCHIVE_SCREENS.length], ticket=story.request(from);
    assert.equal(ticket.row.id,row.id);
    assert.equal(story.request('slot_02'),null);
    assert.equal(story.canStart(from),false);
    story.start(ticket);story.finish(ticket,true);
    assert.equal(story.at(from),row);
    assert.equal(story.completed,i+1);
    assert.ok(row.archive.title && row.archive.date && row.archive.body);
    assert.equal(row.slides.length,3);
  }
  // Every record has been seen: a screen replays its own last one, and a replay advances nothing.
  const replay=story.request('slot_01');
  assert.equal(replay.review,true);assert.equal(replay.row,story.at('slot_01'));
  story.start(replay);story.finish(replay,true);
  assert.equal(story.completed,expected.length);
  assert.equal(story.displayed.id,expected.at(-1).id);
});
test('one screen can carry the whole story, and a screen that showed nothing has nothing to replay',()=>{
  const story=createArchiveStory(manifest);
  for(let i=0;i<story.total;i++){
    const ticket=story.request('slot_14');assert.equal(ticket.row.archive.number,i+1);
    story.start(ticket);story.finish(ticket,true);
  }
  assert.equal(story.at('slot_14').archive.number,story.total);
  assert.equal(story.canStart('slot_14'),true);
  assert.equal(story.canStart('slot_02'),false);assert.equal(story.request('slot_02'),null);
});
test('abandoned, stale and interrupted events cannot advance progress',()=>{
  let saved,sites;
  const story=createArchiveStory(manifest,'bad-old-id',(id,places)=>{saved=id;sites=places;});
  const lost=story.request('slot_13');
  story.finish(lost,false);
  const retry=story.request('slot_27');
  story.finish(lost,true);
  assert.equal(story.completed,0);
  story.finish(retry,true);
  assert.equal(createArchiveStory(manifest,saved).next.archive.number,2);
  const restored=createArchiveStory(manifest,saved,null,sites);
  assert.equal(restored.at('slot_27').archive.number,1);
  assert.equal(restored.at('slot_13'),undefined);
  // The same screen takes the next two records and rests on the later one.
  for(let i=0;i<2;i++)story.finish(story.request('slot_27'),true);
  assert.equal(story.at('slot_27').archive.number,3);
  assert.deepEqual(sites,['slot_27','slot_27','slot_27']);
  assert.equal(story.request('slot_01').row.archive.number,4);
});
test('an older save keeps its place, and a record watched on a retired screen rests on the one that took its place',()=>{
  const records=manifest.filter(r=>r.set==='event');
  for(let n=1;n<=40;n++)assert.ok(ARCHIVE_SCREENS.includes(screenHome('slot_'+String(n).padStart(2,'0'))),'slot '+n+' has a home');
  assert.equal(screenHome('unknown'),undefined);
  const sites=['slot_05','slot_22','unknown','slot_40'];
  for(const n of [0,1,12,39,records.length]){
    for(const history of [undefined,sites]){
      const s=createArchiveStory(manifest,records[n-1]?.id,null,history);
      assert.equal(s.completed,n);
      assert.equal(s.next,records[n]);
      if(n<records.length)assert.ok(ARCHIVE_SCREENS.every(id=>s.canStart(id)));
    }
  }
  const old=createArchiveStory(manifest,records[3].id,null,sites);
  assert.equal(old.at('slot_05').archive.number,1);
  assert.equal(old.at('slot_01').archive.number,2);
  assert.equal(old.at('slot_14').archive.number,4);
  assert.equal(old.at('slot_22'),undefined);assert.equal(old.at('slot_40'),undefined);
  // With the story complete, only a screen that holds a record can still be pressed.
  const done=createArchiveStory(manifest,records.at(-1).id,null,sites);
  assert.deepEqual(ARCHIVE_SCREENS.filter(id=>done.canStart(id)).sort(),['slot_01','slot_05','slot_14']);
  assert.equal(createArchiveStory(manifest,records.at(-1).id).request('slot_01'),null);
});
test('a screen shows nothing until a record has started on it, and a cancelled start leaves it blank',()=>{
  const story=createArchiveStory(manifest);
  const next=story.next,first=story.request('slot_08');
  assert.equal(story.at('slot_08'),undefined);
  assert.equal(story.displayed,undefined);
  story.finish(first,false);story.start(first);
  assert.equal(story.at('slot_08'),undefined);
  const playing=story.request('slot_12');
  story.start(playing);
  assert.equal(story.at('slot_12'),next);
  assert.equal(story.at('slot_08'),undefined);
  story.finish(playing,true);
  assert.equal(story.at('slot_12'),next);
  assert.equal(story.at('slot_08'),undefined);

  // A save that only knows its last record: the earlier ones rest nowhere, later ones are remembered by screen.
  let history;
  const old=createArchiveStory(manifest,manifest.find(r=>r.archive?.number===2).id,(_,slots)=>{history=slots;});
  assert.equal(old.at('slot_01'),undefined);
  const third=old.request('slot_09');old.start(third);old.finish(third,true);
  assert.deepEqual(history,[null,null,'slot_09']);
  const resumed=createArchiveStory(manifest,third.row.id,null,history);
  assert.equal(resumed.at('slot_01'),undefined);
  assert.equal(resumed.at('slot_09').archive.number,3);
});
test('seventeen screens, never more than one in a small room, and every one of them is in the route file',async()=>{
  const route=JSON.parse(await readFile(new URL('../assets/expo/station-route.json',import.meta.url),'utf8'));
  const rooms={};
  for(const id of ARCHIVE_SCREENS){
    const slot=route.slots.find(row=>row.id===id);assert.ok(slot,id+' is in the route file');
    (rooms[slot.zone]||=[]).push(slot);
  }
  assert.equal(ARCHIVE_SCREENS.length,17);assert.equal(new Set(ARCHIVE_SCREENS).size,17);
  assert.deepEqual(Object.fromEntries(Object.entries(rooms).map(([zone,list])=>[zone,list.length])),
    {atrium:3,chronology:2,forum:2,commons:2,aquarium:2,service:1,hall:2,hoof:1,habitat:1,shop:1});
  // The Atrium's three stand on three floors, the Grand Hall's two on two levels.
  for(const zone of ['atrium','hall'])assert.equal(new Set(rooms[zone].map(slot=>slot.standing[1])).size,rooms[zone].length);
  // A retired screen rests its records in its own room.
  for(const slot of route.slots)assert.equal(route.slots.find(row=>row.id===screenHome(slot.id)).zone,slot.zone,slot.id);
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

test('the planter speaks at 10, 45 and 90 seconds and holds the visitor until the release words',async()=>{
  const cues=[];
  const visit=createPlanterVisit(id=>cues.push(id));
  visit.land({planter:true},{support:{}});
  visit.tick(9.9);assert.deepEqual(cues,[]);
  visit.tick(.1);assert.deepEqual(cues,['planter_10']);
  visit.tick(35);assert.deepEqual(cues,['planter_10','planter_45']);
  visit.tick(45);assert.deepEqual(cues,['planter_10','planter_45','planter_90']);
  assert.equal(visit.held,true);assert.equal(visit.released,false);
  visit.tick(9.9);assert.equal(visit.held,true);
  visit.tick(.1);assert.equal(visit.held,false);assert.equal(visit.released,true);
  visit.tick(60);assert.deepEqual(visit.state(),{used:true,held:false,seconds:100,cues:3});
  const voices=JSON.parse(await readFile(new URL('../assets/expo/audio/planter-voices.json',import.meta.url),'utf8'));
  assert.deepEqual(Object.keys(voices),planterLines);
  // Each line has its recorded clip beside the voice file, and it is a real recording.
  for(const [id,line] of Object.entries(voices)){
    assert.equal(line.audio,id+'.mp3');
    const clip=await readFile(new URL('../assets/expo/audio/'+line.audio,import.meta.url));
    assert.ok(clip.length>100000,id+' clip');
  }
  // House copy: no exclamation marks, no en or em dashes.
  const banned=c=>c==='!'||c.codePointAt(0)===0x2013||c.codePointAt(0)===0x2014;
  for(const line of Object.values(voices))assert.ok(line.subtitle&&line.seconds>0&&![...line.subtitle].some(banned));
  // A line ends before the next one is due.
  assert.ok(10+voices.planter_10.seconds<=45&&45+voices.planter_45.seconds<=90);
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
