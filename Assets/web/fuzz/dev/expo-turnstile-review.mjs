import {createRequire} from 'node:module';
import {mkdir,writeFile} from 'node:fs/promises';
import assert from 'node:assert/strict';
import path from 'node:path';
import {installGameAudio,beginGameAudio,finishGameAudio,beginTabVideo,muxWalk} from './expo-film.mjs';
const {chromium}=createRequire('C:/wt-fe/e2e/')('playwright');
const out=path.resolve(process.argv[2]);
const route=new URL('dev/play-ending.html?end=ll_end&pictures=none',process.env.FUZZ_BASE||'http://127.0.0.1:8937/fuzz/').href;
await mkdir(out,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',args:['--mute-audio','--autoplay-policy=no-user-gesture-required']});
const proof={eyeFeet:[.62,0,36.83],aim:[-.52,.85,35.34],stills:[],errors:[]};
async function enter(){
  const ctx=await browser.newContext({viewport:{width:1920,height:1080}});await installGameAudio(ctx);
  const page=await ctx.newPage();page.on('pageerror',e=>proof.errors.push(e.message));
  await page.goto(route);await page.getByRole('button',{name:'Play Llama Expo 2037'}).click();
  const frame=page.frames().find(f=>f.url().includes('/fuzz/index.html'));await frame.locator('.expo-worm #btn').click();
  await frame.waitForFunction(()=>window.__expo?.state().ready,null,{timeout:100000});
  return {ctx,page,frame};
}
async function measure(frame){
  return frame.evaluate(async()=>{
    const T=await import('three'),e=__expo,n=e.scene.getObjectByName('turnstile_arm');
    n.updateWorldMatrix(true,false);
    const tips=n.userData.neutralTipsLocalThree;
    if(!Array.isArray(tips)||tips.length!==9)throw Error('Missing native three-arm geometry.');
    return {q:n.quaternion.toArray(),pivot:n.getWorldPosition(new T.Vector3()).toArray(),
      tips:[0,3,6].map(i=>n.localToWorld(new T.Vector3().fromArray(tips,i)).toArray()),
      native:n.userData.pivotThree,axis:n.userData.axis,travel:n.userData.travelDegrees,
      counted:e.exhibits.state.turnstile,feet:e.state().feet};
  });
}
const distance=(a,b)=>Math.hypot(...a.map((n,i)=>n-b[i]));
try{
  const {ctx,page,frame}=await enter();
  await frame.waitForFunction(()=>__expo.narrationState().finished.pa_arrive_1>0,null,{timeout:45000});
  await frame.evaluate(async p=>{await __expo.go('atrium',p.eyeFeet);__expo.lookAt(...p.aim);},proof);
  for(const angle of [0,-60,-120]){
    await frame.evaluate(angle=>{__expo.exhibits.setTurnstileAngle(angle);__expo.draw();},angle);
    const sample=await measure(frame);proof.stills.push({angle,...sample});
    await page.screenshot({path:path.join(out,'angle-'+Math.abs(angle)+'.png')});
  }
  const [first,middle,end]=proof.stills;
  assert(distance(first.pivot,first.native)<.002);assert(distance(first.pivot,middle.pivot)<.00001);
  assert(distance(first.pivot,end.pivot)<.00001);assert(middle.tips[0][2]<first.tips[0][2]-.2);
  for(let i=0;i<3;i++)assert(Math.abs(distance(first.pivot,first.tips[i])-distance(end.pivot,end.tips[i]))<.001);
  await frame.evaluate(()=>{__expo.exhibits.setTurnstileAngle(null);__expo.draw();});
  const bar=first.pivot.map((n,i)=>n+(first.tips[0][i]-n)*.73);
  await frame.evaluate(p=>__expo.lookAt(...p),bar);
  assert.match(await frame.locator('.expo-prompt').textContent(),/Pass the turnstile/);
  const video=path.join(out,'gate-live.mp4'),audio=path.join(out,'gate-live.webm');
  const stop=await beginTabVideo(page,video),audioAt=await beginGameAudio(frame);
  const trace=frame.evaluate(async()=>{
    const e=__expo,n=e.scene.getObjectByName('turnstile_arm'),start=n.quaternion.clone(),samples=[],begin=performance.now();
    while(performance.now()-begin<3100){
      await new Promise(r=>requestAnimationFrame(r));
      samples.push([performance.now()-begin,n.quaternion.angleTo(start)*180/Math.PI]);
    }return samples;
  });
  await page.waitForTimeout(600);await page.keyboard.press('e');
  await frame.evaluate(p=>__expo.lookAt(...p),proof.aim);
  proof.motion=await trace;proof.afterPress=await measure(frame);
  assert(proof.afterPress.counted);assert(proof.motion.some(s=>s[1]>55&&s[1]<70));
  assert(Math.abs(proof.motion.at(-1)[1]-120)<.1);
  await page.waitForTimeout(2300);await finishGameAudio(frame,audio);await stop();
  await muxWalk(video,audio,path.join(out,'turnstile.mp4'),5,(audioAt-stop.startedAt)/1000);
  proof.voice=await frame.evaluate(()=>__expo.narrationState().pa);
  await ctx.close();
  const actual=await enter();const atStart=await measure(actual.frame);
  assert(!atStart.counted);await actual.page.keyboard.down('w');await actual.page.waitForTimeout(540);await actual.page.keyboard.up('w');
  await actual.frame.waitForFunction(()=>__expo.exhibits.state.turnstile,null,{timeout:4000});
  await actual.page.waitForTimeout(1000);const crossed=await measure(actual.frame);
  assert(crossed.feet[2]<35.33);assert(atStart.feet[2]>35.33);
  proof.walk={atStart,crossed};await actual.ctx.close();assert.equal(proof.errors.length,0);
}finally{
  await writeFile(path.join(out,'turnstile.json'),JSON.stringify(proof,null,2));await browser.close();
}
