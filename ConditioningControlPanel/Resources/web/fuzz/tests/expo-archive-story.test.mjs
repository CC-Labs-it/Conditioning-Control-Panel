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
    // A record with cues has one more picture than it has cues; the others keep three.
    assert.equal(row.slides.length,row.cues?row.cues.length+1:3);
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
  // These saves were written after the Rail talk became record 1 (they say so).
  const ours=(record,history)=>createArchiveStory(manifest,record,null,history,undefined,true);
  for(const n of [0,1,12,39,records.length]){
    for(const history of [undefined,sites]){
      const s=ours(records[n-1]?.id,history);
      assert.equal(s.completed,n);
      assert.equal(s.next,records[n]);
      if(n<records.length)assert.ok(ARCHIVE_SCREENS.every(id=>s.canStart(id)));
    }
  }
  const old=ours(records[3].id,sites);
  assert.equal(old.at('slot_05').archive.number,1);
  assert.equal(old.at('slot_01').archive.number,2);
  assert.equal(old.at('slot_14').archive.number,4);
  assert.equal(old.at('slot_22'),undefined);assert.equal(old.at('slot_40'),undefined);
  // With the story complete, only a screen that holds a record can still be pressed.
  const done=ours(records.at(-1).id,sites);
  assert.deepEqual(ARCHIVE_SCREENS.filter(id=>done.canStart(id)).sort(),['slot_01','slot_05','slot_14']);
  assert.equal(ours(records.at(-1).id).request('slot_01'),null);
});
test('the Rail talk is record 1, required viewing, with four pictures cued from the recording',()=>{
  const story=createArchiveStory(manifest),rail=story.next;
  assert.equal(rail.id,'rail_lore');assert.equal(rail.audio,'hall/rail_lore.mp3');assert.equal(rail.seconds,73.85);
  assert.deepEqual(rail.archive,{number:1,chapter:null,section:'Required viewing',title:'The Rail™',date:'2031',body:rail.subtitle});
  assert.deepEqual(rail.slides,['slides/rail_a.webp','slides/rail_b.webp','slides/rail_c.webp','slides/rail_d.webp']);
  assert.deepEqual(rail.cues,[22.7,40.8,48.4]);
  assert.equal(story.total,manifest.filter(row=>row.set==='event').length);
  const first=story.request('slot_01');story.start(first);story.finish(first,true);
  assert.equal(story.next.id,'ev_h0');assert.equal(story.next.archive.number,2);
  assert.equal(story.at('slot_01'),rail);
});
test('an older save with progress on the forty is owed the Rail talk next, then carries on where it was',()=>{
  const records=manifest.filter(r=>r.set==='event').sort((a,b)=>a.archive.number-b.archive.number);
  // Written before the Rail talk existed: four records (now 2 to 5) watched on these screens.
  const sites=['slot_05','slot_22','unknown','slot_14'];let saved;
  const old=createArchiveStory(manifest,records[4].id,(...args)=>{saved=args;},sites);
  assert.equal(old.completed,4);assert.equal(old.next.id,'rail_lore');
  assert.equal(old.at('slot_05').archive.number,2);assert.equal(old.at('slot_01').archive.number,3);
  assert.equal(old.at('slot_14').archive.number,5);
  assert.ok(ARCHIVE_SCREENS.every(id=>old.canStart(id)));
  // A cancelled screening owes it still and writes nothing.
  old.finish(old.request('slot_17'),false);assert.equal(old.next.id,'rail_lore');assert.equal(saved,undefined);
  const talk=old.request('slot_17');assert.equal(talk.row.id,'rail_lore');assert.equal(talk.review,false);
  old.start(talk);assert.equal(old.at('slot_17').id,'rail_lore');old.finish(talk,true);
  assert.equal(old.completed,5);assert.equal(old.next,records[5]);assert.equal(old.at('slot_17').id,'rail_lore');
  // Nothing the save held is lost: the same last record, its screens shifted behind the talk, and the flag.
  assert.deepEqual(saved,[records[4].id,['slot_17','slot_05','slot_01',null,'slot_14'],true]);
  const again=createArchiveStory(manifest,saved[0],null,saved[1],undefined,saved[2]);
  assert.equal(again.completed,5);assert.equal(again.next,records[5]);
  assert.equal(again.at('slot_17').id,'rail_lore');assert.equal(again.at('slot_14').archive.number,5);
  // A complete older save gets the talk too, and is complete after it.
  const done=createArchiveStory(manifest,records.at(-1).id,null,[]);
  assert.equal(done.completed,records.length-1);assert.equal(done.next.id,'rail_lore');
  const last=done.request('slot_02');done.start(last);done.finish(last,true);
  assert.equal(done.completed,records.length);assert.equal(done.next,undefined);
  // A save that has only screened the talk, or an older pack's manifest without it, owes nothing.
  assert.equal(createArchiveStory(manifest,'rail_lore').next.archive.number,2);
  const older=manifest.filter(r=>r.id!=='rail_lore').map(r=>r.archive?{...r,archive:{...r.archive,number:r.archive.number-1}}:r);
  const pack=createArchiveStory(older,records[2].id,null,['slot_01','slot_02']);
  assert.equal(pack.completed,2);assert.equal(pack.at('slot_02').id,records[2].id);assert.equal(pack.next.id,records[3].id);
  assert.equal(pack.total,records.length-1);
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
  const old=createArchiveStory(manifest,manifest.find(r=>r.archive?.number===2).id,(_,slots)=>{history=slots;},[],undefined,true);
  assert.equal(old.at('slot_01'),undefined);
  const third=old.request('slot_09');old.start(third);old.finish(third,true);
  assert.deepEqual(history,[null,null,'slot_09']);
  const resumed=createArchiveStory(manifest,third.row.id,null,history,undefined,true);
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
  visit.tick(8.9);assert.equal(visit.held,true);
  visit.tick(.1);assert.equal(visit.held,false);assert.equal(visit.released,true);
  visit.tick(60);assert.deepEqual(visit.state(),{used:true,held:false,seconds:99,cues:3});
  const voices=JSON.parse(await readFile(new URL('../assets/expo/audio/planter-voices.json',import.meta.url),'utf8'));
  assert.deepEqual(Object.keys(voices),planterLines);
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
