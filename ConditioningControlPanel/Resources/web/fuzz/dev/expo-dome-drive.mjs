import { createRequire } from 'node:module';
import { mkdir,writeFile } from 'node:fs/promises';
import path from 'node:path';
const {chromium}=createRequire('C:/wt-fe/e2e/')(process.env.PW_DIR||'playwright');
const origin=process.env.FUZZ_BASE||'http://127.0.0.1:8937/fuzz/';
if(!['localhost','127.0.0.1','[::1]'].includes(new URL(origin).hostname))throw Error('Use a local preview.');
const dir=path.resolve(process.argv[2]||'./dev-captures/dome-controls');await mkdir(dir,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_EXE||'C:/Program Files/Google/Chrome/Application/chrome.exe',
  args:['--mute-audio','--autoplay-policy=no-user-gesture-required']});
const report={visits:[],errors:[]};
try {
 for(const reduced of [false,true]){
  const context=await browser.newContext({viewport:{width:1100,height:720},reducedMotion:reduced?'reduce':'no-preference'});
  const page=await context.newPage();page.on('pageerror',e=>report.errors.push(e.message));
  await page.goto(origin+'dev/play-ending.html?end=ll_end&pictures=none');
  const launch=page.getByRole('button',{name:'Play Llama Expo 2037'});await launch.waitFor();
  const frame=page.frames().find(f=>f.url().includes('/fuzz/index.html'));await launch.click();await frame.locator('.expo-worm #btn').click();
  await frame.waitForFunction(()=>!!window.__expo,null,{timeout:120000});
  const checks=await frame.evaluate(async()=>{
    const x=__expo;await x.go('dome',[-20,0,-4]);x.lookAt(-22,1.5,-5);
    const active=x.state().elapsed,depth=x.state().seconds;
    await new Promise(r=>setTimeout(r,1300));
    return {depthTimeRate:(x.state().seconds-depth)/(x.state().elapsed-active),doorways:[]};
  });
  const check=(pass,message)=>{if(!pass)report.errors.push((reduced?'Gentle':'Normal')+' '+message);};
  check(Math.abs(checks.depthTimeRate-(reduced?.65:1))<.04,'clock rate '+checks.depthTimeRate);
  await frame.locator('.expo-root').focus();await page.keyboard.press('Escape');
  await frame.waitForFunction(()=>fuzz.engine.paused);
  const before=await frame.evaluate(()=>__expo.state().seconds);await page.waitForTimeout(620);
  checks.pauseFroze=await frame.evaluate(t=>__expo.state().seconds===t,before);check(checks.pauseFroze,'pause clock');
  if(!reduced){
    await frame.locator('#gentle').check();await frame.locator('#resume').click();
    checks.playerGentle=await frame.evaluate(async()=>{
      const x=__expo,initial=x.state();await new Promise(r=>setTimeout(r,1100));const final=x.state();
      return {preference:document.body.classList.contains('gentle'),systemReduced:matchMedia('(prefers-reduced-motion: reduce)').matches,
        rate:(final.seconds-initial.seconds)/(final.elapsed-initial.elapsed)};
    });
    check(checks.playerGentle.preference&&!checks.playerGentle.systemReduced&&Math.abs(checks.playerGentle.rate-.65)<.04,'in-game Gentle preference');
    await frame.locator('.expo-root').focus();await page.keyboard.press('Escape');
    await frame.locator('#gentle').uncheck();await frame.locator('#resume').click();
  }else await frame.locator('#resume').click();
  for(const goal of [4,28,62,113,174,210]){
   const state=await frame.evaluate(async({goal,reduced})=>{
    const x=__expo;await x.go('dome',[-14.1,0,-5]);x.lookAt(-9,1.6,-5);
    x.deepen(Math.max(0,goal-x.state().seconds)/(reduced?.65:1)+.2);
    const before=x.state(),art=[];
    x.scene.traverse(m=>{if(m.isMesh&&m.visible&&m.material?.uniforms?.film)art.push(m);});
    const frames=[0,1,2,3,4,5].filter(i=>x.scene.getObjectByName('dome_frame_'+i)?.visible).length;
    for(let j=0;j<63;j++){x.walker.move(.067,0);x.walker.step(.016);await new Promise(r=>requestAnimationFrame(r));}
    const after=x.state();
    return {phase:before.phase,depth:before.depth,bubbles:art.length,frames,exit:after.zone,secondsAfter:after.seconds,feet:after.feet};
   },{goal,reduced});
   checks.doorways.push(state);
   check(state.phase===checks.doorways.length-1&&state.exit==='hall','phase doorway '+JSON.stringify(state));
   if(goal===62){
    const target=await frame.evaluate(async()=>{
      const x=__expo;await x.go('dome',[-20,0,-4]);const candidates=[];
      x.scene.traverse(m=>{if(m.isMesh&&m.visible&&m.material?.uniforms?.film)candidates.push(m);});
      candidates.sort((a,b)=>Math.abs(1.6-a.position.y)-Math.abs(1.6-b.position.y));
      const mesh=candidates[0];if(!mesh)return null;
      await x.go('dome',[mesh.position.x+.92,0,mesh.position.z]);x.lookAt(...mesh.position.toArray());
      window.__expoPopTarget=mesh;return document.querySelector('.expo-prompt').textContent;
    });
    await frame.locator('.expo-root').focus();await page.keyboard.press('e');
    checks.poppedWithKey=await frame.evaluate(()=>window.__expoPopTarget?.visible===false);
    check(checks.poppedWithKey&&target==='Pop bubble','real E bubble pop');
   }
  }
  const last=checks.doorways.at(-1);check(last.frames===(reduced?4:6),'physical frame count');
  check(last.bubbles<=(reduced?5:16)&&last.bubbles>=3,'physical bubble count');
  await frame.evaluate(async()=>{await __expo.go('dome',[-20,0,-4]);});
  await frame.waitForFunction(()=>__expo.state().loaded.includes('catwalk'),null,{timeout:60000});
  checks.oculusLinked=true;
  await frame.evaluate(()=>fuzz.engine.setSuspended(true,'panic'));await frame.locator('.expo-root').waitFor({state:'detached'});
  checks.panic=true;report.visits.push({reduced,...checks});await context.close();
 }
}finally{
 await writeFile(path.join(dir,'result.json'),JSON.stringify(report,null,2));await browser.close();
}
console.log(JSON.stringify(report));if(report.errors.length)process.exitCode=1;
