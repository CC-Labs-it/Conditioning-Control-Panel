import {reviewPack} from './expo-review-pack.mjs';
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
    const reviewRate=src=>src.includes('ev_h0.')?1:rate;
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
  const ready=()=>game.waitForFunction(()=>__expo.narrationState().archive?.total===40);
  const save=()=>writeFile(path.join(output,mode+'.json'),JSON.stringify(report,null,2)+'\n');
  await ready();
  report.admission=await game.evaluate(()=>({feet:__expo.state().feet,yaw:__expo.camera.rotation.y,
    guide:document.querySelector('.expo-route-guide')?.innerText,visibleRooms:__expo.state().loaded}));
  await shot('admission');
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
    const slots=(await (await context.request.get(new URL('station-route.json',packRoot).href)).json()).slots;
    const records=(await (await context.request.get(new URL('narration/manifest.json',packRoot).href)).json()).filter(r=>r.set==='event');
    const byId=new Map(slots.map(s=>[s.id,s]));
    await game.evaluate(()=>{
      window.archiveObservations={played:[],frames:{},panels:{},maxVoices:0};
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
        if(!voice.id?.startsWith('ev_'))return;
        if(obs.played.at(-1)!==voice.id)obs.played.push(voice.id);
        const physical=s.stations.find(p=>p.project?.active && p.id===s.archive.playingAt);
        if(!physical?.project.full)return;
        const title=writing('label',physical.id),caption=writing('caption',physical.id);
        if(title?.length && caption?.length)obs.panels[voice.id]={header:title[0],title:title.slice(1).join(' '),section:caption[0],body:caption.slice(1).join(' ')};
        const images=obs.frames[voice.id]||=[];if(!images.includes(physical.project.texture))images.push(physical.project.texture);
      },35);
    });
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
    // A visitor can choose the aquarium first, then leave it while its reel continues playing.
    let slot=byId.get('slot_09');await aim(slot,'screen');
    await page.waitForTimeout(1500);
    assert.equal((await state()).displayed,undefined);
    assert.equal(await aim(slot,'replay'),'expo_replay_'+slot.id);
    await page.keyboard.press('e');
    await game.waitForFunction(()=>__expo.narrationState().archive.displayed==='ev_h0',null,{timeout:45000});
    const first=await state();assert.equal(first.displayed,records[0].id);
    await game.waitForFunction(()=>window.archiveObservations.frames.ev_h0?.length>0);
    const playhead=await game.evaluate(()=>__expo.narrationState().voice.seconds);
    const elsewhere=byId.get('slot_12');assert.equal(await aim(elsewhere,'replay'),'expo_replay_'+elsewhere.id);
    await page.keyboard.press('e');
    report.handoff=await state();
    assert.equal(report.handoff.playingAt,slot.id);
    assert.equal(report.handoff.displayed,records[0].id);
    assert.ok(await game.evaluate(()=>__expo.narrationState().voice.seconds)>=playhead);
    await aim(slot,'screen');
    await shot('original-screen');
    const destinations=slots.filter(s=>s.id!==slot.id);
    const length=mode==='sample'?2:records.length;
    for(let i=0;i<length;i++){
      if(i>0){
        slot=destinations[i-1];
        assert.equal(await aim(slot,'replay'),'expo_replay_'+slot.id,'main button at '+slot.id);
        await page.keyboard.press('e');
      }
      await game.waitForFunction(n=>__expo.narrationState().archive.completed>=n,i+1,{timeout:80000});
      const result=await state();assert.equal(result.completed,i+1);
      if(i===0){
        await page.waitForTimeout(3200);
        const parked=await game.evaluate(id=>{
          const s=__expo.narrationState();
          return {count:s.archive.completed,playing:s.archive.playingAt,own:s.stations.find(p=>p.id===id).record};
        },slot.id);
        assert.deepEqual(parked,{count:1,playing:undefined,own:records[0].id});
      }
      report.events.push({number:i+1,id:records[i].id,slot:slot.id,completed:result.completed});
      if(i===0||i===19||i===39)await shot('after-'+(i+1));
      await save();
    }
    assert.equal(await aim(slot,'review'),'expo_review_'+slot.id);
    await page.keyboard.press('e');
    await game.waitForFunction(id=>__expo.narrationState().voice.id===id,records[length-1].id,{timeout:15000});
    assert.equal((await state()).completed,length);
    report.observations=await game.evaluate(()=>window.archiveObservations);
    assert.deepEqual(report.observations.played,records.slice(0,length).map(r=>r.id));
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
