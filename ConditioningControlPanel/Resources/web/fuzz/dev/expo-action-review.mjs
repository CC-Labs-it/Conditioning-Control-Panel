import {createRequire} from 'node:module';
import {mkdir,writeFile} from 'node:fs/promises';
import assert from 'node:assert/strict';
import path from 'node:path';
import {installGameAudio,beginGameAudio,finishGameAudio,beginTabVideo,muxWalk} from './expo-film.mjs';
const {chromium}=createRequire('C:/wt-fe/e2e/')('playwright'),out=path.resolve(process.argv[2]);
const base=process.env.FUZZ_BASE||'http://127.0.0.1:8937/fuzz/';
if(!['localhost','127.0.0.1','[::1]'].includes(new URL(base).hostname))throw Error('Use a local preview.');
await mkdir(out,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',args:['--mute-audio']});
const proof={devices:[],errors:[]};
try{
 for(const phone of [false,true]){
  const name=phone?'touch':'desktop',ctx=await browser.newContext({viewport:phone?{width:393,height:852}:{width:1440,height:900},
    hasTouch:phone,isMobile:phone,reducedMotion:phone?'reduce':'no-preference'});
  await installGameAudio(ctx);const page=await ctx.newPage();page.on('pageerror',e=>proof.errors.push(e.message));
  await ctx.addInitScript(() => {
    const media = new Set(), events = [], original = HTMLMediaElement.prototype.play;
    window.expoVoiceAudit = {events, maximum:0};
    HTMLMediaElement.prototype.play = function(...args) {
      if (this.src.includes('/assets/expo/narration/') && !media.has(this)) {
        media.add(this);
        const id = this.src.split('/').pop().replace(/\.mp3$/, '');
        for (const type of ['playing','ended']) this.addEventListener(type, () => {
          const live = Array.from(media).filter(el => !el.paused && !el.ended).length;
          window.expoVoiceAudit.maximum = Math.max(window.expoVoiceAudit.maximum,live);
          events.push({type,id,seconds:this.currentTime,hoof:window.__expo?.exhibits.state.hoof});
        });
      }
      return original.apply(this,args);
    };
  });
  await page.goto(base+'dev/play-ending.html?end=ll_end&pictures=none&herd=12');
  await page.getByRole('button',{name:'Play Llama Expo 2037'}).click();
  const f=page.frames().find(fr=>fr.url().includes('/fuzz/index.html'));await f.locator('.expo-worm #btn').click();
  await f.waitForFunction(()=>window.__expo?.state().ready,null,{timeout:90000});
  await f.waitForFunction(()=>!document.querySelector('.expo-crossing'),null,{timeout:8000});
  const visit={name,controls:[],floors:[]};proof.devices.push(visit);
  const use=()=>phone?f.locator('.expo-interact').tap():page.keyboard.press('e');
  async function measure(zone,node,feet){
    const descriptor=await f.evaluate(async({zone,node,feet})=>{
      const T=await import('three'),e=__expo;await e.go(zone,feet);
      const cap=e.scene.getObjectByName(node);
      if(!cap)return null;
      const centre=new T.Box3().setFromObject(cap,true).getCenter(new T.Vector3());e.lookAt(...centre.toArray());
      return {native:centre.toArray(),label:document.querySelector('.expo-prompt').textContent,cap:node,caps:e.buttons().length};
    },{zone,node,feet});
    if(!descriptor)return;
    assert(descriptor.label,node+' is out of reach');await page.screenshot({path:path.join(out,name+'-'+node+'-rest.jpg'),quality:91});
    const filmed=!phone&&['press_main','expo_replay_ex_press'].includes(node);
    const basename=path.join(out,name+'-'+node),stop=filmed?await beginTabVideo(page,basename+'-raw.mp4'):null;
    const audioAt=filmed?await beginGameAudio(f):0;
    if(filmed)await page.waitForTimeout(420);
    const watch=f.evaluate(async node=>{
      const T=await import('three'),e=__expo,n=e.scene.getObjectByName(node),origin=n.getWorldPosition(new T.Vector3()),p=new T.Vector3();
      const samples=[],start=performance.now();let depressed=null;
      while(performance.now()-start<820){
        await new Promise(r=>requestAnimationFrame(r));const cap=e.buttons().find(c=>c.node===node);
        samples.push({ms:performance.now()-start,metres:n.getWorldPosition(p).distanceTo(origin),...cap});
        if(!depressed&&cap.amount>.80){e.draw();depressed=e.renderer.domElement.toDataURL('image/jpeg',.94).split(',')[1];}
      }return {samples,depressed};
    },node);
    await page.waitForTimeout(70);await use();
    if(!phone&&node==='press_main'){await use();await use();}
    const {samples,depressed}=await watch,max=Math.max(...samples.map(r=>r.metres));
    if(depressed)await writeFile(basename+'-depressed.jpg',Buffer.from(depressed,'base64'));
    if(filmed){
      await page.waitForTimeout(1450);await finishGameAudio(f,basename+'-audio.webm');await stop();
      await muxWalk(basename+'-raw.mp4',basename+'-audio.webm',basename+'.mp4',2.5,(audioAt-stop.startedAt)/1000);
    }
    assert(max>.002,node+' cap did not travel');assert(samples.at(-1).metres<.0005,node+' cap did not return');
    if(phone)assert(samples.every(s=>s.amount===0||s.amount===1),'Gentle must show still positions.');
    visit.controls.push({...descriptor,max,samples});
  }
  async function rapid(zone,feet,aim,count){
    const start=await f.evaluate(async p=>{
      const e=__expo;await e.go(p.zone,p.feet);e.lookAt(...p.aim);e.root.focus();
      return {target:e.focusTarget(),state:e.exhibits.state.hoof,taps:e.exhibits.state.taps,
        caps:e.buttons().filter(c=>c.zone===p.zone)};
    },{zone,feet,aim});
    assert(start.target,'Missing '+zone+' input target');
    for(let i=0;i<count;i++)await use();
    const end=await f.evaluate(zone=>{const e=__expo;return {state:e.exhibits.state.hoof,misses:e.exhibits.state.misses,
      taps:e.exhibits.state.taps,caps:e.buttons().filter(c=>c.zone===zone)};},zone);
    if(zone==='hoof'){
      assert.equal(end.misses,0);assert.equal(end.state,'idle');
      await f.waitForFunction(()=>__expo.narrationState().voice.id==='hoof_1',null,{timeout:45000});
      await page.screenshot({path:path.join(out,name+'-hoof-listening.jpg'),quality:89});
      await f.waitForFunction(()=>__expo.exhibits.state.hoof==='checkbox',null,{timeout:25000});
      for(let i=0;i<count;i++)await use();
      assert.equal(await f.evaluate(()=>__expo.exhibits.state.misses),0);
      await f.waitForFunction(()=>__expo.exhibits.state.misses===1,null,{timeout:15000});
      const audio=await f.evaluate(()=>window.expoVoiceAudit);
      const hoof=audio.events.filter(e=>['hoof_1','hoof_2','hoof_3'].includes(e.id));
      assert.equal(audio.maximum,1);
      assert.deepEqual(hoof.map(e=>e.type+' '+e.id),['playing hoof_1','ended hoof_1','playing hoof_2','ended hoof_2','playing hoof_3','ended hoof_3']);
      assert.equal(hoof.find(e=>e.id==='hoof_2'&&e.type==='playing').hoof,'idle');
      visit.hoofAudio=audio;
    }else assert.equal(end.taps-start.taps,count);
    (visit.rapid||=[]).push({zone,count,start,end});
  }
  await rapid('hoof',[-18.4,0,8.02],[-18.4,1.58,9.84],5);
  await rapid('habitat',[19.4,0,8.13],[19.40,1.56,9.55],10);
  if(process.env.RAPID_ONLY==='1'){await ctx.close();continue;}
  await measure('hall','expo_replay_ex_press',[-2.409,0,-6.652]);
  await measure('hall','press_main',[.04,0,-7.76]);
  await measure('atrium','expo_control_greenhouse',[9.16,0,23.86]);
  if(!phone){
    await beginGameAudio(f);
    for(const [zone,feet,surface,yaw] of [['hall',[0,0,13.1],'stone',0],['hall',[3.4,0,11.7],'carpet',0],
      ['hall',[10.7,3.4,5.15],'wood',0],['catwalk',[-22.5,11.95,-3.66],'metal',0]]){
      await f.evaluate(async p=>await __expo.go(p.zone,p.feet,p.yaw),{zone,feet,yaw});
      const before=await f.evaluate(()=>__expo.footfalls());await page.keyboard.down('w');await page.waitForTimeout(715);await page.keyboard.up('w');
      const after=await f.evaluate(()=>({sound:__expo.footfalls(),feet:__expo.state().feet,surface:__expo.state().surface}));
      visit.floors.push({zone,feet,surface,before,after});
      assert(after.sound.count>before.count,zone+' '+surface+' produced no footfall');
      assert.equal(after.sound.last.surface,surface);assert(after.sound.last.body.endsWith('/sfx/thud.mp3'));
    }
    await finishGameAudio(f,path.join(out,'footsteps-live.webm'));
  }
  await ctx.close();
 }
 assert.equal(proof.errors.length,0);
}finally{await writeFile(path.join(out,'actions.json'),JSON.stringify(proof,null,2));await browser.close();}
