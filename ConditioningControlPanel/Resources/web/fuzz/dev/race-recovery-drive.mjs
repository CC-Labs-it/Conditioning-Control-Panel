import assert from 'node:assert/strict';
import { mkdirSync } from 'node:fs';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';

const {chromium}=createRequire('C:/wt-fe/e2e/package.json')('playwright');
const out=resolve(process.argv[2]||'dev/shots/reactions');
mkdirSync(out,{recursive:true});
const tracks=['threshold','mainframe','boudoir','keep','goonworks'];
const url=new URL(process.env.FUZZ_URL||'http://localhost:8807/fuzz/index.html');
url.searchParams.set('fresh','1');
url.searchParams.set('buy','mini_race,ticker');
const browser=await chromium.launch({channel:'chrome',headless:true,args:['--mute-audio']});
const errors=[];
const film='.mini-race-film';

async function openRace(intercept) {
  const page=await browser.newPage({viewport:{width:1100,height:850}});
  page.on('pageerror',e=>errors.push(e.message));
  if(intercept)await page.route('**/assets/race/*-loop.mp4',intercept);
  await page.goto(url.href);
  await page.waitForFunction(()=>window.fuzz?.engine.scopes.has('mini_race'));
  return page;
}

async function playing(page,id) {
  await page.waitForFunction(id=>{
    const v=document.querySelector('.mini-race-film video'), p=document.querySelector('.mini-race-film img');
    return v?.currentSrc.endsWith('/'+id+'-loop.mp4') && !v.paused && v.currentTime>.05 &&
      p?.src.endsWith('/'+id+'-poster.jpg') && p.complete && p.naturalWidth && p.hidden;
  },id);
}

async function finishTrack(page) {
  const old=await page.locator(film+' video').elementHandle();
  await old.evaluate(v=>{
    if(v.loop||!Number.isFinite(v.duration))throw Error('Expected a finite playlist clip');
    v.playbackRate=4;
    v.currentTime=v.duration-.16;
  });
  return old;
}

async function hold(page,on,off,id) {
  const v=page.locator(film+' video'), poster=page.locator(film+' img');
  await on();
  await page.waitForFunction(()=>document.querySelector('.mini-race-film video').paused);
  assert((await poster.getAttribute('src')).endsWith(id+'-poster.jpg'));
  assert(await poster.evaluate(p=>!p.hidden));
  assert(await v.evaluate((v,id)=>v.src.endsWith('/'+id+'-loop.mp4'),id));
  await off();
  await playing(page,id);
}

async function step(page,ms) {
  await page.evaluate(duration=>{
    while(duration>0){const dt=Math.min(100,duration);window.fuzz.engine.step(dt);duration-=dt;}
  },ms);
}

try {
  const page=await openRace();
  const video=page.locator(film+' video'), poster=page.locator(film+' img');
  await playing(page,tracks[0]);
  await video.evaluate(v=>v.pause());
  await playing(page,tracks[0]);

  for(let i=1;i<=tracks.length;i++){
    const expected=tracks[i%tracks.length], previous=await finishTrack(page);
    await playing(page,expected);
    // A decoder can still send events after its source was torn down.
    await previous.evaluate(v=>{v.dispatchEvent(new Event('ended'));v.dispatchEvent(new Event('error'));});
    await previous.dispose();
    assert.equal(await video.count(),1);
    assert(await video.evaluate((v,id)=>v.currentSrc.endsWith('/'+id+'-loop.mp4'),expected));
  }

  const chip=page.locator('.dk-chip[data-fz-owner=mini_race]');
  await hold(page,()=>page.evaluate(()=>fuzz.engine.setPaused(true)),()=>page.evaluate(()=>fuzz.engine.setPaused(false)),'threshold');
  await hold(page,()=>chip.click(),()=>chip.click(),'threshold');
  await hold(page,()=>page.evaluate(()=>fuzz.ui.setInit({gentle:true})),()=>page.evaluate(()=>fuzz.ui.setInit({gentle:false})),'threshold');
  await hold(page,
    ()=>page.evaluate(()=>{Object.defineProperty(document,'hidden',{configurable:true,value:true});document.dispatchEvent(new Event('visibilitychange'));}),
    ()=>page.evaluate(()=>{delete document.hidden;document.dispatchEvent(new Event('visibilitychange'));}),
    'threshold');
  await page.locator('[data-arcade=mini_race]').screenshot({path:resolve(out,'racing-thoughts-fixed.png')});
  const ticker=page.locator('#l-ticker');
  const mask=await ticker.evaluate(e=>getComputedStyle(e).maskImage);
  assert(mask.includes('88%')&&mask.includes('100%'),'matching right-hand ticker fade');
  await ticker.screenshot({path:resolve(out,'ticker-both-edges.png')});

  // Jam only this decoder. Its time never advances even if the browser reports play.
  const original=await video.elementHandle();
  await original.evaluate(v=>{
    window.raceRecoveryCount=0;
    Object.defineProperty(v,'currentTime',{configurable:true,get:()=>12});
    v.load=()=>{window.raceRecoveryCount++;v.dispatchEvent(new Event('emptied'));};
    v.dispatchEvent(new Event('waiting'));
  });
  assert(await poster.isVisible());
  await step(page,18000);
  await playing(page,'mainframe');
  const reloads=await page.evaluate(()=>window.raceRecoveryCount);
  assert(reloads>=1&&reloads<=3,'bounded decoder reload and disposal');
  await original.dispose();

  const bonusState=()=>page.evaluate(()=>fuzz.state.store.mini_race.race.leftMs);
  const remaining=await bonusState();
  assert(remaining<25000,'bonus has already counted down during playback');
  const mainframe=await finishTrack(page);
  await playing(page,'boudoir');
  await mainframe.dispose();
  assert((await bonusState())<=remaining,'changing course preserves elapsed bonus time');
  await page.evaluate(()=>fuzz.engine.switches.set('mini_race',false));
  await video.waitFor({state:'detached'});
  await page.evaluate(()=>fuzz.engine.switches.set('mini_race',true));
  await playing(page,'boudoir');
  assert((await bonusState())<=remaining,'restored course preserves the countdown');
  await step(page,(await bonusState())+1000);
  const collect=page.locator('.mini-race-collect');
  const paid=()=>page.evaluate(()=>fuzz.state.sources.mini_race||0);
  const before=await paid();
  await collect.click();
  const award=await paid();
  assert(award>before,'claim after a course change pays');
  assert(await collect.isDisabled(),'collect enters cooldown');
  await collect.evaluate(b=>b.click());
  assert.equal(await paid(),award,'no second payout');
  await page.close();

  let notifyRequest;
  const inFlight=new Promise(resolve=>{notifyRequest=resolve;});
  const missing=await openRace(route=>{
    if(route.request().url().endsWith('mainframe-loop.mp4'))notifyRequest(route);
    else return route.continue();
  });
  await playing(missing,'threshold');
  const threshold=await finishTrack(missing);
  await missing.waitForFunction(()=>document.querySelector('.mini-race-film video').src.endsWith('mainframe-loop.mp4'));
  await missing.evaluate(()=>fuzz.engine.setPaused(true));
  const request=await inFlight;
  await request.abort('failed');
  await missing.waitForFunction(()=>!!document.querySelector('.mini-race-film video').error);
  assert(await missing.locator(film+' img').isVisible());
  assert(await missing.locator(film+' video').evaluate(v=>v.src.endsWith('mainframe-loop.mp4')));
  await missing.evaluate(()=>fuzz.engine.setPaused(false));
  await playing(missing,'boudoir');
  await threshold.dispose();
  await missing.close();

  const requests=[];
  const offline=await openRace(route=>{requests.push(route.request().url());return route.abort('failed');});
  await offline.waitForFunction(()=>document.querySelector('.mini-race-film img')?.naturalWidth>0);
  for(let i=0;i<30&&new Set(requests).size<tracks.length;i++)await offline.waitForTimeout(100);
  assert.equal(new Set(requests).size,tracks.length,'all five tracks tried before waiting');
  const atRest=requests.length;
  await step(offline,10000);
  await offline.waitForTimeout(150);
  assert.equal(requests.length,atRest,'offline playlist creates no immediate retry storm');
  assert(await offline.locator(film+' img').isVisible());
  await offline.unroute('**/assets/race/*-loop.mp4');
  await step(offline,61000);
  await offline.waitForFunction(()=>{const v=document.querySelector('.mini-race-film video');return v && !v.paused && v.currentTime>.05;});
  await offline.close();
  assert.deepEqual(errors,[]);
  console.log('Passed five native clip boundaries, wrap, posters, holds, stale events, decoder stalls, a missing clip, all-clips-offline recovery and ticker fade.');
} finally { await browser.close(); }
