// Real-time recording and projector regression. Component controls use isolated test geometry.
// Final location captures use the ending's authored stations through its ordinary shop purchase.
import {createRequire} from 'node:module';
import {mkdir,writeFile} from 'node:fs/promises';
import assert from 'node:assert/strict';
import path from 'node:path';
import {beginGameAudio,finishGameAudio,installGameAudio} from './expo-film.mjs';
const {chromium}=createRequire('C:/wt-fe/e2e/')(process.env.PW_DIR||'playwright');
const dir=path.resolve(process.argv[2]||'dev-captures/narration'),base=process.env.FUZZ_BASE||'http://127.0.0.1:8937/fuzz/';
if(!['localhost','127.0.0.1','[::1]'].includes(new URL(base).hostname))throw Error('Local review only.');
const report={errors:[],checks:{},sources:[],started:new Date().toISOString()};
await mkdir(dir,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_EXE||'C:/Program Files/Google/Chrome/Application/chrome.exe',
  args:['--mute-audio','--autoplay-policy=no-user-gesture-required']});
try{
  const cx=await browser.newContext({viewport:{width:1280,height:720},deviceScaleFactor:1});
  await installGameAudio(cx);
  const page=await cx.newPage();
  page.on('console',m=>{if(/Shader Error|INVALID_OPERATION|recording unavailable/.test(m.text()))report.errors.push(m.text());});
  page.on('pageerror',e=>report.errors.push(e.message));
  page.on('response',r=>{if(r.url().includes('/assets/expo/narration/'))report.sources.push([r.status(),r.url().split('/narration/')[1]]);});
  await page.goto(base+'index.html');
  // Use a fresh frame with the game's import map and the actual audio kit. It has no prior ending sounds.
  await page.evaluate(async()=>{
    const T=await import('three'),{createNarration}=await import('./ui/expo/narration.js');
    const {createKit}=await import('./audio/kit.js');
    const kit=createKit();kit.arm();
    const scene=new T.Scene(),camera=new T.PerspectiveCamera(50,16/9,.02,50);
    camera.position.set(0,1.6,2);camera.lookAt(0,1,0);
    const group=new T.Group(),blocked={on:true},buttons=new Map(),clicks=[];
    const speechHandles=[],projected=[];
    const audio={...kit,stream(url,options){
      const handle=kit.stream(url,options);
      if(options.bus==='voice')speechHandles.push(handle);
      return handle;
    }};
    const ctx={audio,paused:false,gentle:false,reduced:false};
    const root=document.createElement('main');root.style.cssText='position:fixed;inset:0;z-index:10000;background:#18211c;color:white';document.body.append(root);
    const renderer=new T.WebGLRenderer({antialias:true});renderer.setSize(960,540);root.append(renderer.domElement);
    const lamp=new T.AmbientLight(0xffffff,2);scene.add(lamp);
    function add(name,geo,loc,col){
      const mesh=new T.Mesh(geo,new T.MeshStandardMaterial({color:col}));mesh.name=name;mesh.position.fromArray(loc);group.add(mesh);
    }
    function station(id,x){
      add('expo_station_'+id,new T.BoxGeometry(.2,.15,.1),[x,1,.4],0x705c38);
      add('expo_replay_'+id,new T.BoxGeometry(.08,.08,.08),[x+.2,1,.4],0x225533);
      const screen=new T.PlaneGeometry(1.15,.86),uv=screen.attributes.uv;
      for(let i=0;i<uv.count;i++)uv.setY(i,1-uv.getY(i)); // glTF stores the top row at V=0.
      add('expo_screen_'+id,screen,[x,1.4,0],0xffffff);
      add('expo_lens_'+id,new T.SphereGeometry(.04,12,8),[x,.99,.52],0xe6c56c);
    }
    station('log_kevin',0);station('ev_h0',8);station('log_greg',15);station('log_nana',21);
    add('expo_horn_test',new T.BoxGeometry(.22,.16,.1),[1,2.7,0],0x888888);
    scene.add(group);scene.updateMatrixWorld(true);
    const controls={register:(m,l,f)=>{buttons.set(m.name,f);return()=>buttons.delete(m.name);},
      walker:{zone:'test',blocked:()=>blocked.on},sound:{play:(...args)=>{
        const el=speechHandles.map(h=>h.el).find(el=>el&&!el.paused&&!el.ended);
        clicks.push({at:performance.now(),args,source:el?.currentSrc.split('/').at(-1),seconds:el?.currentTime,duration:el?.duration});
        kit.play('assets/expo/audio/press.ogg',{bus:'sfx',gain:args[1],pan:args[2]});}}};
    const n=createNarration({ctx,camera,scene,root,...controls,assetURL:p=>'assets/expo/'+p,lite:false,owned:()=>['kevin','greg']});
    n.mount('test',group);
    const start=performance.now();let previous=performance.now(), stopped=false;
    const occupancy={peak:0,decodedVoiceElements:0,dualCaptions:0};
    function draw(){if(stopped)return;const now=performance.now();
      if(!ctx.paused)n.tick(Math.min(1,(now-previous)/1000));previous=now;renderer.render(scene,camera);
      const count=[...root.querySelectorAll('.expo-narration p')].filter(p=>p.textContent).length;
      if(count>1)occupancy.dualCaptions++;
      const state=n.debug(),project=state.stations.find(s=>s.id==='ev_h0')?.project;
      if(state.voice.id==='ev_h0'&&project?.full&&projected.at(-1)?.file!==project.texture)
        projected.push({file:project.texture,seconds:state.voice.seconds,duration:state.voice.duration});
      const playing=speechHandles.filter(h=>h.el&&!h.el.paused&&!h.el.ended).length;
      occupancy.decodedVoiceElements=Math.max(occupancy.decodedVoiceElements,playing);
      const level={};n.meter(level);occupancy.peak=Math.max(occupancy.peak,level.voices);requestAnimationFrame(draw);}
    draw();
    window.narrationBench={n,ctx,camera,blocked,buttons,clicks,root,kit,group,occupancy,projected,age:()=>performance.now()-start,
      hold:on=>{ctx.paused=on;kit.hold(on);n.pause(on);},
      end:()=>{stopped=true;n.dispose();renderer.dispose();root.remove();}};
  });
  const wait=(fn,arg,ms=30000)=>page.waitForFunction(fn,arg,{timeout:ms});
  const peek=()=>page.evaluate(()=>narrationBench.n.debug());
  await page.waitForTimeout(500);
  let s=await peek();assert(!s.heard.includes('log_kevin'));assert(!s.stations.some(x=>x.id==='log_nana'));
  await page.evaluate(()=>{narrationBench.blocked.on=false;narrationBench.camera.lookAt(0,1.6,12);});
  await page.waitForTimeout(1250);assert(!(await peek()).heard.includes('log_kevin'));
  const look=target=>page.evaluate(target=>{narrationBench.camera.lookAt(...target);},target);
  await look([0,1.4,0]);await page.waitForTimeout(450);
  await look([0,1.6,12]);await page.waitForTimeout(260);
  await look([0,1.4,0]);await page.waitForTimeout(550);assert(!(await peek()).heard.includes('log_kevin'));
  await wait(()=>narrationBench.n.debug().heard.includes('log_kevin'));
  await wait(()=>narrationBench.n.debug().voice.seconds>.3);
  s=await peek();assert.equal(s.voice.id,'log_kevin');assert.equal(s.voice.subtitle,'[Kevin is speaking]');
  report.checks.lineOfSight={blocked:true,offAxisStarted:false,interruptedDwellReset:true,hiddenUnowned:true};
  await beginGameAudio(page);
  await page.evaluate(()=>{narrationBench.camera.position.x=7.8;narrationBench.camera.lookAt(8,1.4,0);});
  await wait(()=>narrationBench.n.debug().voice.queue.includes('ev_h0'));
  const distant=(await peek()).voice;assert(distant.gain<.30);
  await page.evaluate(()=>{narrationBench.camera.position.set(0,1.6,2);void narrationBench.n.cue('pa_turnstile');});
  await wait(()=>narrationBench.n.debug().pa.queue.includes('pa_turnstile'));
  const queued=await peek();assert.equal(queued.voice.id,'log_kevin');assert(!queued.pa.id);
  const canonical=await page.evaluate(async()=>await(await fetch('assets/expo/narration/manifest.json')).json());
  report.checks.mix={farGain:distant.gain,queued:queued.speech.queue,active:queued.speech.playing,paWaiting:true};
  const interruptedAt=queued.voice.seconds;
  await page.evaluate(()=>{void narrationBench.n.planter('planter_10');});
  await wait(()=>narrationBench.n.debug().voice.id==='planter_10');
  const plant=await peek();assert.equal(plant.speech.interrupted,'log_kevin');
  const heldPanel=plant.stations.find(s=>s.id==='log_kevin').project;
  assert(heldPanel.paused&&heldPanel.active);
  const lines=await page.evaluate(async()=>await(await fetch('assets/expo/audio/planter-voices.json')).json());
  assert.equal(plant.voice.subtitle,lines.planter_10.subtitle);
  await page.evaluate(()=>narrationBench.hold(true));const a=await peek();
  await page.waitForTimeout(750);const b=await peek();assert.equal(a.voice.seconds,b.voice.seconds);
  assert.deepEqual(b.stations.find(s=>s.id==='log_kevin').project,heldPanel);
  report.checks.pause={from:a.voice.seconds,to:b.voice.seconds};
  await page.evaluate(()=>narrationBench.hold(false));
  await wait(()=>narrationBench.n.debug().voice.id==='log_kevin');
  const resumed=await peek();assert(resumed.voice.seconds>=interruptedAt&&resumed.voice.seconds<interruptedAt+.8);
  report.checks.planter={timedSeconds:lines.planter_10.seconds,subtitle:plant.voice.subtitle,
    interrupted:plant.speech.interrupted,pausedAt:interruptedAt,resumedAt:resumed.voice.seconds};
  await wait(()=>narrationBench.n.debug().voice.subtitle==='Thank you, Kevin.',null,26000);
  const kevin=await peek();assert(kevin.voice.seconds>=15);report.checks.kevinRoomToneSeconds=kevin.voice.seconds;
  await wait(()=>narrationBench.n.debug().voice.id==='ev_h0');
  s=await peek();assert.equal(s.voice.subtitle,canonical.find(r=>r.id==='ev_h0').subtitle);
  await page.evaluate(()=>{const b=narrationBench;b.camera.position.set(8.55,1.6,2.1);b.camera.lookAt(8,1.4,0);});
  await wait(()=>narrationBench.n.debug().stations.find(s=>s.id==='ev_h0').project.lamp>.8);
  await page.screenshot({path:path.join(dir,'component-projection.jpg'),quality:90,type:'jpeg'});
  await page.evaluate(()=>{narrationBench.ctx.gentle=true;});
  await page.waitForTimeout(180);
  const gentle=(await peek()).stations.find(s=>s.id==='ev_h0');assert.equal(gentle.project.zoom,0);
  await page.evaluate(()=>{narrationBench.ctx.gentle=false;});
  await wait(()=>narrationBench.n.debug().finished.ev_h0===1,null,30000);
  await wait(()=>narrationBench.n.debug().pa.id==='pa_turnstile');
  assert.equal((await peek()).pa.subtitle,canonical.find(r=>r.id==='pa_turnstile').subtitle);
  await wait(()=>narrationBench.n.debug().finished.pa_turnstile===1);
  await page.waitForTimeout(1000);
  const rest=(await peek()).stations.find(s=>s.id==='ev_h0');
  const expected=canonical.find(r=>r.id==='ev_h0').slides;
  assert(rest.project.lamp<.1&&!rest.project.active&&!rest.project.full);
  assert.equal(rest.project.slide,expected[1]);assert.equal(rest.project.resting,expected[1]);
  const clocks=await page.evaluate(()=>({displayed:narrationBench.projected,clicks:narrationBench.clicks.filter(c=>c.source==='ev_h0.mp3')}));
  assert.deepEqual(clocks.displayed.map(v=>v.file),expected);
  assert.equal(clocks.clicks.length,3);
  for(const [i,mark] of [0,.30,.70].entries()){
    const change=clocks.clicks[i].seconds/clocks.clicks[i].duration;
    assert(change>=mark-.003&&change<=mark+.025,'Mechanical slide change '+i+' at '+change);
    const visible=clocks.displayed[i].seconds/clocks.displayed[i].duration;
    assert(visible>=mark-.003&&visible<=mark+.045,'Decoded projected slide '+i+' at '+visible);
  }
  report.checks.slideProgram={...clocks,middleAtRest:rest.project.slide};
  await page.evaluate(()=>{const b=narrationBench;b.camera.position.x=0;});
  await page.waitForTimeout(320);assert(!(await peek()).voice.id);
  await page.evaluate(()=>{void narrationBench.buttons.get('expo_replay_log_kevin')();});
  await wait(()=>narrationBench.n.debug().voice.id==='log_kevin');
  s=await peek();assert.equal(s.voice.subtitle,'[Kevin is speaking]');
  report.checks.projectors={gentleZoom:gentle.project.zoom,rest:rest.project,clicks:await page.evaluate(()=>narrationBench.clicks)};
  report.checks.revisit={finished:s.finished,replay:s.voice.id};
  report.checks.speechOccupancy=await page.evaluate(()=>narrationBench.occupancy);
  assert.equal(report.checks.speechOccupancy.peak,1);assert.equal(report.checks.speechOccupancy.decodedVoiceElements,1);
  assert.equal(report.checks.speechOccupancy.dualCaptions,0);
  await finishGameAudio(page,path.join(dir,'recorded-component-mix.webm'));
  await page.evaluate(()=>narrationBench.end());
  assert.equal(await page.locator('.expo-narration').count(),0);
  await cx.close();
  if(report.errors.length)throw Error(report.errors.join('\n'));
}finally{
  report.ended=new Date().toISOString();
  await writeFile(path.join(dir,'component-checks.json'),JSON.stringify(report,null,2));await browser.close();
}
