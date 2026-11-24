import {reviewPack,isolateReviewInput} from './expo-review-pack.mjs';
import {ARCHIVE_SCREENS} from '../ui/expo/archive-screens.js';
import assert from 'node:assert/strict';
import {mkdir, writeFile} from 'node:fs/promises';
import {createRequire} from 'node:module';
import {createHash} from 'node:crypto';
import path from 'node:path';

const {chromium}=createRequire(process.env.PW_DIR||'C:/wt-fe/e2e/')('playwright');
const base=new URL(process.env.FUZZ_BASE||'http://127.0.0.1:8937/fuzz/');
assert(['localhost','127.0.0.1','[::1]'].includes(base.hostname));
const output=path.resolve(process.argv[2]||'dev-captures/ordered-tour');
const mode=process.argv[3]||'story';
const rate=Number(process.env.EXPO_REVIEW_RATE||1);
assert(Number.isFinite(rate) && rate>=.25 && rate<=8,'Playback rate must be between 0.25 and 8');
await mkdir(output,{recursive:true});
const browser=await chromium.launch({executablePath:process.env.CHROME||'C:/Program Files/Google/Chrome/Application/chrome.exe',
  args:['--mute-audio','--autoplay-policy=no-user-gesture-required','--disable-background-timer-throttling']});
const report={mode,rate,errors:[],events:[]};
try {
  const context=await browser.newContext({viewport:{width:1280,height:760}});
  await isolateReviewInput(context);
  if(mode==='story' || mode==='sample')await context.addInitScript(rate=>{
    const writing=new WeakMap();
    window.expoTestPanel=canvas=>writing.get(canvas);
    const proto=CanvasRenderingContext2D.prototype;
    const fillRect=proto.fillRect,fillText=proto.fillText;
    proto.fillRect=function(x,y,w,h){
      if(x===0 && y===0 && w===this.canvas.width && h===this.canvas.height)writing.set(this.canvas,[]);
      return fillRect.apply(this,arguments);
    };
    proto.fillText=function(text){
      writing.get(this.canvas)?.push(String(text));
      return fillText.apply(this,arguments);
    };
    // The story's first record plays at its own pace so the hand-off can be watched; the drive names it once known.
    const reviewRate=src=>window.expoReviewFirst&&src.includes('/narration/'+window.expoReviewFirst)?1:rate;
    document.addEventListener('play',e=>{if(e.target?.src?.includes('/narration/hall/'))e.target.playbackRate=reviewRate(e.target.src);},true);
    const original=HTMLMediaElement.prototype.play;
    HTMLMediaElement.prototype.play=function(){
      if(this.src.includes('/narration/hall/'))this.playbackRate=reviewRate(this.src);
      return original.call(this);
    };
  },rate);
  let packRoot;
  const page=await context.newPage();
  const pictureReads=[];
  report.pictureHashes={};
  page.on('response',response=>{
    const file=new URL(response.url()).pathname.split('/narration/')[1];
    if(response.status()!==200 || !(file==='resting-atlas.webp' || file?.startsWith('slides/')))return;
    pictureReads.push(response.body().then(bytes=>{
      report.pictureHashes[file]=createHash('sha256').update(bytes).digest('hex');
    }).catch(()=>{})); // A speculative request can be cancelled when its room unloads.
  });
  page.on('pageerror',e=>report.errors.push(e.message));
  async function enter(){
    await page.goto(new URL('dev/play-ending.html?end=ll_end&pictures=none',base).href);
    packRoot=await reviewPack(page);
    await page.getByRole('button',{name:'Play Llama Expo 2037'}).click();
    const g=page.frames().find(f=>f.url().includes('/fuzz/index.html'));
    await g.locator('.expo-worm #btn').click();
    await g.waitForFunction(()=>window.__expo?.state().ready,null,{timeout:90000});
    await page.waitForTimeout(1400);
    return g;
  }
  let game=await enter();
  const shot=name=>page.screenshot({path:path.join(output,name+'.jpg'),type:'jpeg',quality:90});
  const state=()=>game.evaluate(()=>__expo.narrationState().archive);
  const ready=()=>game.waitForFunction(()=>__expo.narrationState().archive?.total>0);
  const save=()=>writeFile(path.join(output,mode+'.json'),JSON.stringify(report,null,2)+'\n');
  await ready();
  report.admission=await game.evaluate(()=>({feet:__expo.state().feet,yaw:__expo.camera.rotation.y,
    guide:document.querySelector('.expo-route-guide')?.innerText,visibleRooms:__expo.state().loaded}));
  await shot('admission');
  await game.waitForFunction(()=>!__expo.rails().some(r=>r.up&&r.id==='welcome'),null,{timeout:45000});
  await page.keyboard.down('w');await page.waitForTimeout(600);await page.keyboard.up('w');
  report.walked=await game.evaluate(()=>__expo.walker.feet.toArray());
  assert(Math.hypot(report.walked[0]-report.admission.feet[0],report.walked[2]-report.admission.feet[2])>.4);
  if(mode==='planter'){
    async function land(){
      await game.evaluate(async()=>{await __expo.go('hall',[10.28,3.4,-6.60]);__expo.lookAt(4,3.12,-6.60);__expo.root.focus();});
      await page.keyboard.down('w');await page.waitForTimeout(230);await page.keyboard.press('Space');
      await page.waitForTimeout(510);await page.keyboard.up('w');
      await game.waitForFunction(()=>__expo.state().planter.used,null,{timeout:3500});
      await page.waitForTimeout(250);
      return game.evaluate(()=>({planter:__expo.state().planter,feet:__expo.walker.feet.toArray()}));
    }
    report.firstLanding=await land();assert(report.firstLanding.planter.held);
    game=await enter();report.secondLanding=await land();assert(!report.secondLanding.planter.held);
    await page.keyboard.down('s');await page.waitForTimeout(420);await page.keyboard.up('s');
    report.free=await game.evaluate(()=>__expo.walker.feet.toArray());
    assert(Math.hypot(report.free[0]-report.secondLanding.feet[0],report.free[2]-report.secondLanding.feet[2])>.5);
  }
  if(mode==='story' || mode==='sample'){
    // Only the screens the building puts up; the route file may list more bays.
    const slots=(await (await context.request.get(new URL('station-route.json',packRoot).href)).json()).slots.filter(s=>ARCHIVE_SCREENS.includes(s.id));
    // The story order is the archive number, whatever order the manifest lists its rows in.
    const records=(await (await context.request.get(new URL('narration/manifest.json',packRoot).href)).json())
      .filter(r=>r.set==='event').sort((a,b)=>a.archive.number-b.archive.number);
    assert.equal((await state()).total,records.length,'the archive counts the manifest records');
    assert.equal(slots.length,ARCHIVE_SCREENS.length,'every built screen is on the route');
    const byId=new Map(slots.map(s=>[s.id,s]));
    await game.evaluate(({ids,first})=>{
      window.expoReviewFirst=first;
      const story=new Set(ids);
      window.archiveObservations={played:[],frames:{},panels:{},maxVoices:0,screens:{}};
      const writing=(kind,slot)=>{
        let lines;
        __expo.scene.getObjectByName('expo_'+kind+'_'+slot)?.traverse(node=>{
          const canvas=node.material?.map?.image;
          if(canvas)lines=window.expoTestPanel(canvas);
        });
        return lines;
      };
      setInterval(()=>{
        const s=__expo.narrationState(), obs=window.archiveObservations;
        const voice=s.voice;
        obs.maxVoices=Math.max(obs.maxVoices,Number(!!voice.id)+Number(!!s.pa.id));
        if(!story.has(voice.id))return;
        if(obs.played.at(-1)!==voice.id)obs.played.push(voice.id);
        if(s.archive.playingAt)(obs.screens[voice.id]||=[]).includes(s.archive.playingAt)||obs.screens[voice.id].push(s.archive.playingAt);
        const physical=s.stations.find(p=>p.project?.active && p.id===s.archive.playingAt);
        if(!physical?.project.full)return;
        const title=writing('label',physical.id),caption=writing('caption',physical.id);
        if(title?.length && caption?.length)obs.panels[voice.id]={header:title[0],title:title.slice(1).join(' '),section:caption[0],body:caption.slice(1).join(' ')};
        const images=obs.frames[voice.id]||=[];if(!images.includes(physical.project.texture))images.push(physical.project.texture);
      },35);
    },{ids:records.map(r=>r.id),first:records[0].audio});
    async function aim(slot,kind){
      return game.evaluate(async ({slot,kind})=>{
        const {Box3,Vector3}=await import('three');
        await __expo.go(slot.zone,slot.standing);
        const node=__expo.scene.getObjectByName('expo_'+kind+'_'+slot.id);
        if(!node)throw Error('Missing '+kind+' at '+slot.id);
        __expo.lookAt(...new Box3().setFromObject(node,true).getCenter(new Vector3()).toArray());
        __expo.root.focus();return __expo.focusTarget()?.owner;
      },{slot,kind});
    }
    // Approaching the aquarium first starts its nearest projector.
    let slot=byId.get('slot_09');await aim(slot,'screen');
    await game.waitForFunction(id=>__expo.narrationState().archive.displayed===id,records[0].id,{timeout:45000});
    const first=await state();assert.equal(first.displayed,records[0].id);
    await game.waitForFunction(id=>window.archiveObservations.frames[id]?.length>0,records[0].id);
    const playhead=await game.evaluate(()=>__expo.narrationState().voice.seconds);
    await game.waitForFunction(()=>__expo.rails().some(r=>r.up&&r.id==='view'));
    await game.evaluate(()=>{const p=__expo.walker.feet;__expo.move(0,-4);return p.toArray();});
    report.handoff=await state();
    assert.equal(report.handoff.playingAt,slot.id);
    assert.equal(report.handoff.displayed,records[0].id);
    assert.ok(await game.evaluate(()=>__expo.narrationState().voice.seconds)>=playhead);
    await shot('original-screen');
    // A screen holds many records: the visit goes round the other screens and each press plays the story's next one.
    const destinations=slots.filter(s=>s.id!==slot.id);
    const length=mode==='sample'?2:records.length;
    const wanted=()=>game.evaluate(()=>__expo.narrationState().archive.playingAt);
    for(let i=0;i<length;i++){
      let played=slot.id;
      if(i>0){
        slot=destinations[(i-1)%destinations.length];
        assert.equal(await aim(slot,'replay'),'expo_replay_'+slot.id,'main button at '+slot.id);
        // The first walk into a room can start its nearest screen by itself; then that screen plays the next record.
        await page.waitForTimeout(450);
        played=await wanted();
        if(!played){
          assert.equal((await state()).next,records[i].id);
          await page.keyboard.press('e');
          await game.waitForFunction(()=>__expo.narrationState().archive.playingAt,null,{timeout:3000});
          played=await wanted();
          assert.equal(played,slot.id,'the pressed screen plays the next record');
        }
      }
      // The first record plays at its own pace: allow for its length, whatever record that is.
      const pace=i===0?1:rate,timeout=Math.max(80000,((Number(records[i].seconds)||0)/pace+30)*1000);
      await game.waitForFunction(n=>__expo.narrationState().archive.completed>=n,i+1,{timeout});
      const result=await state();assert.equal(result.completed,i+1);
      if(i===0){
        await page.waitForTimeout(3200);
        const parked=await game.evaluate(id=>{
          const s=__expo.narrationState();
          return {count:s.archive.completed,playing:s.archive.playingAt,own:s.stations.find(p=>p.id===id).record};
        },slot.id);
        assert.deepEqual(parked,{count:1,playing:undefined,own:records[0].id});
      }
      report.events.push({number:i+1,id:records[i].id,slot:played,pressedAt:slot.id,completed:result.completed});
      if(i===0||i===Math.floor(length/2)-1||i===length-1)await shot('after-'+(i+1));
      await save();
    }
    // One more press on the last screen: the story's next record while any remain, else that screen's own last one.
    const last=report.events.filter(e=>e.slot===slot.id).at(-1)?.id;
    const encore=length<records.length?records[length].id:last;
    assert.equal(await aim(slot,'replay'),'expo_replay_'+slot.id);
    await page.waitForTimeout(450);
    if(!(await wanted()))await page.keyboard.press('e');
    await game.waitForFunction(id=>__expo.narrationState().voice.id===id,encore,{timeout:15000});
    assert.equal((await state()).playingAt,slot.id);
    assert.equal((await state()).completed,length);
    report.encore={slot:slot.id,id:encore};
    report.observations=await game.evaluate(()=>window.archiveObservations);
    assert.deepEqual(report.observations.played,records.slice(0,length+(length<records.length?1:0)).map(r=>r.id));
    assert.equal(report.observations.maxVoices,1);
    const normal=text=>text.replace(/\s+/g,' ').trim();
    for(const row of records.slice(0,length)){
      const panel=report.observations.panels[row.id];
      assert.ok(panel,'digital panels '+row.id);
      assert.equal(normal(panel.title),normal(row.archive.title));
      assert.equal(normal(panel.body),normal(row.archive.body));
      assert.ok(panel.header.includes(row.archive.date));
      assert.deepEqual(report.observations.frames[row.id],row.slides,'projected photographs '+row.id);
    }
  }
  await Promise.all(pictureReads);
  for(const pictures of Object.values(report.observations?.frames||{}))
    for(const picture of pictures)assert.ok(report.pictureHashes[picture],picture+' was displayed and transferred');
  assert.deepEqual(report.errors,[]);report.passed=true;
}catch(error){report.errors.push(error.message);throw error;}
finally{await writeFile(path.join(output,mode+'.json'),JSON.stringify(report,null,2)+'\n');await browser.close();}
