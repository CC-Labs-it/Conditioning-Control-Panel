import {reviewPack, packFile} from './expo-review-pack.mjs';
// Cold desktop and touch entries with the ordinary purchase, an active room route and the owner settings.
import {createRequire} from 'node:module';
import {createHash} from 'node:crypto';
import {mkdir,writeFile} from 'node:fs/promises';
import path from 'node:path';
const {chromium}=createRequire('C:/wt-fe/e2e/')(process.env.PW_DIR||'playwright');
const base=process.env.FUZZ_BASE||'http://127.0.0.1:8937/fuzz/',dir=path.resolve(process.argv[2]||'./dev-captures/entry');
if(!['localhost','127.0.0.1','[::1]'].includes(new URL(base).hostname))throw Error('Use a local preview.');
const expected=process.env.EXPECT_START||'atrium';
const link=Number(process.env.MBPS||20),rtt=Number(process.env.RTT||35);
const integrated=process.env.GPU==='integrated';
const report={expected,integrated,network:{megabitsPerSecond:link,latencyMs:rtt},
  measurement:'First room bytes count completed zone geometry, collision, surface maps, lightmaps and reflection probes. Wall-clock timing also includes other game loading. Phone uses touch input on the same physical GPU.',devices:[],errors:[]};
async function entranceReceipts(packRoot){
  const folder=new URL('zones/',packRoot);
  const plan=await(await fetch(new URL('zones.json',folder))).json();
  const def=plan.zones.find(z=>z.id===plan.start.id);
  const result={};
  for(const file of new Set([def.file||def.id+'.glb',def.mobileFile].filter(Boolean))){
    const response=await fetch(new URL(file,folder));if(!response.ok)throw Error('Missing served room '+file);
    const bytes=Buffer.from(await response.arrayBuffer());result[file]={bytes:bytes.byteLength,sha256:createHash('sha256').update(bytes).digest('hex')};
  }
  return result;
}
await mkdir(dir,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_EXE||'C:/Program Files/Google/Chrome/Application/chrome.exe',
  args:['--mute-audio','--autoplay-policy=no-user-gesture-required',...(integrated?['--force_low_power_gpu']:[])]});
try{
 for(const phone of [false,true]){
  const name=phone?'phone':'desktop',size=phone?{width:393,height:852}:{width:1920,height:1080};
  const cx=await browser.newContext({viewport:size,deviceScaleFactor:1,hasTouch:phone,isMobile:phone,reducedMotion:phone?'reduce':'no-preference'});
  let packRoot;
  const page=await cx.newPage();page.on('pageerror',e=>report.errors.push(name+' '+e.message));
  let kitReceived;
  if(process.env.EXPO_COLLECTION_ART||process.env.EXPO_COLLECTION_ONLY)page.on('response',response=>{
    if(/\/collection\/collection-[a-f0-9]+\.glb(?:\?|$)/.test(response.url()))
      kitReceived=response.body().then(body=>({file:new URL(response.url()).pathname.split('/').at(-1),
        sha256:createHash('sha256').update(body).digest('hex'),bytes:body.byteLength}));
  });
  page.on('console',m=>{if(/(WebGL|GL_).*INVALID_(OPERATION|VALUE|ENUM)|THREE\.WebGLProgram: Shader Error/i.test(m.text()))report.errors.push(name+' '+m.text());});
  const cdp=await cx.newCDPSession(page);await cdp.send('Network.enable');
  const files=new Map(),before=new Set(),otherExpo=new Map();let arrived=false;
  cdp.on('Network.responseReceived',e=>{
    const name=packFile(e.response.url,packRoot);if(!name)return;
    if(!name.startsWith('zones/')){otherExpo.set(e.requestId,{name,bytes:0,beforeArrival:false});return;}
    files.set(e.requestId,{name:name.slice(6),bytes:0,beforeArrival:!arrived});
  });
  cdp.on('Network.loadingFinished',e=>{
    const f=files.get(e.requestId);if(f){f.bytes=e.encodedDataLength;if(!arrived)before.add(e.requestId);}
    const media=otherExpo.get(e.requestId);if(media){media.bytes=e.encodedDataLength;media.beforeArrival=!arrived;}
  });
  await page.goto(base+'dev/play-ending.html?end=ll_end&pictures=none');
  packRoot=await reviewPack(page);
  (report.roomVersions||=[]).push(await entranceReceipts(packRoot));
  const button=page.getByRole('button',{name:'Play Llama Expo 2037'});await button.waitFor();
  const frame=page.frames().find(f=>f.url().includes('/fuzz/index.html'));
  await button.evaluate(el=>el.ownerDocument.addEventListener('click',function record(event){
    if(!el.contains(event.target))return;window.__expoPurchaseTime=performance.timeOrigin+performance.now();
    el.ownerDocument.removeEventListener('click',record,true);
  },true));
  await frame.evaluate(()=>{
    document.addEventListener('click',function record(event){
      if(!event.target.closest?.('.expo-worm #btn'))return;
      document.removeEventListener('click',record,true);
      const now=()=>performance.timeOrigin+performance.now();
      const timing=window.__expoEntryTime={yes:now(),rendered:0,paint:0,uncovered:0,firstCover:null};
      const wait=()=>{
        if(window.__expo?.state().ready){
          const curtain=document.querySelector('.expo-crossing'),cover=curtain?Number(curtain.dataset.cover??1):0;
          if(!timing.rendered)timing.rendered=now();
          if(!timing.paint&&cover<.65){timing.firstCover=cover;timing.paint=now();}
          if(!curtain){timing.uncovered=now();return;}
        }
        requestAnimationFrame(wait);
      };
      requestAnimationFrame(wait);
    },true);
  });
  if(link>0)await cdp.send('Network.emulateNetworkConditions',{offline:false,latency:rtt,downloadThroughput:link*125000,uploadThroughput:link*125000,connectionType:phone?'cellular4g':'wifi'});
  const purchase=Date.now();await button.click();await frame.locator('.expo-worm #btn').waitFor();
  const then=Date.now();
  if(phone)await frame.locator('.expo-worm #btn').tap();else await frame.locator('.expo-worm #btn').click();
  await frame.waitForFunction(()=>window.__expoEntryTime?.paint>0,null,{timeout:120000});arrived=true;
  const commandTiming={yesToRoomMs:Date.now()-then,purchaseToRoomMs:Date.now()-purchase};
  await frame.waitForFunction(()=>window.__expoEntryTime?.uncovered>0,null,{timeout:30000});
  const time=await frame.evaluate(()=>({...window.__expoEntryTime,purchase:parent.__expoPurchaseTime}));
  const reveal={readyFromYesMs:Math.round(time.rendered-time.yes),visibleFromYesMs:Math.round(time.paint-time.yes),
    visibleFromPurchaseMs:Math.round(time.paint-time.purchase),firstCover:time.firstCover,
    fullyRevealedFromYesMs:Math.round(time.uncovered-time.yes),fullyRevealedFromPurchaseMs:Math.round(time.uncovered-time.purchase)};
  (report.reveals||=[]).push({name,...reveal});
  const entryMs=Math.round(time.paint-time.yes),purchaseToRoomMs=Math.round(time.paint-time.purchase),firstFiles=[...before].map(id=>files.get(id));
  const mediaBeforePaint=[...otherExpo.values()].filter(r=>r.beforeArrival);
  const allExpoFirstBytes=[...firstFiles,...mediaBeforePaint].reduce((sum,r)=>sum+r.bytes,0);
  const state=await frame.evaluate(()=>__expo.state());
  if(state.zone!==expected)throw Error('Expected '+expected+' entry but found '+state.zone);
  console.log(name+' first room '+entryMs+' ms, '+(firstFiles.reduce((n,f)=>n+f.bytes,0)/1e6).toFixed(2)+' MB');
  await page.screenshot({path:path.join(dir,name+'-entry.jpg'),type:'jpeg',quality:92});
  async function frameTiming(){return frame.evaluate(async()=>{
    const e=__expo,intervals=[],d=e.renderer.getContext(),gpuTimes=[];
    const timer=d.getExtension('EXT_disjoint_timer_query_webgl2');
    if(timer){
      for(let sample=0;sample<4;sample++){
        const query=d.createQuery();d.beginQuery(timer.TIME_ELAPSED_EXT,query);
        e.draw();d.endQuery(timer.TIME_ELAPSED_EXT);d.flush();const deadline=performance.now()+600;
        while(!d.getQueryParameter(query,d.QUERY_RESULT_AVAILABLE)&&performance.now()<deadline)await new Promise(r=>requestAnimationFrame(r));
        if(d.getQueryParameter(query,d.QUERY_RESULT_AVAILABLE)&&!d.getParameter(timer.GPU_DISJOINT_EXT))gpuTimes.push(d.getQueryParameter(query,d.QUERY_RESULT)/1e6);
        d.deleteQuery(query);
      }
    }
    let previous=performance.now(),draw=e.renderer.info.render.frame;
    const start=previous;while(performance.now()-start<4000){
      await new Promise(r=>requestAnimationFrame(r));
      const now=performance.now(),drawNow=e.renderer.info.render.frame;
      if(drawNow!==draw){intervals.push(now-previous);previous=now;draw=drawNow;}
    }
    const ext=d.getExtension('WEBGL_debug_renderer_info');gpuTimes.sort((a,b)=>a-b);
    intervals.shift();intervals.sort((a,b)=>a-b);
    return {gpu:ext?d.getParameter(ext.UNMASKED_RENDERER_WEBGL):'Unavailable',medianMs:intervals[Math.floor(intervals.length*.5)],p95Ms:intervals[Math.floor(intervals.length*.95)],
      gpuDrawMs:gpuTimes.length?gpuTimes[Math.floor(gpuTimes.length/2)]:null,frames:intervals.length,pixelRatio:e.state().pixelRatio,textureMemory:e.state().textures};
  });}
  async function collectionTiming(only){
    const manifest=await frame.evaluate(async()=>((await(await fetch(window.expoReviewAssetURL('collection/collection.json'))).json())));
    const selected=manifest.pieces.filter(p=>!only||p.id===only);
    if(!selected.length)throw Error('Missing collection artwork '+only);
    const views={};
    for(const piece of selected){
      await frame.evaluate(async p=>{
        const feet=[p.camera[0],p.camera[1]-1.6,p.camera[2]];
        await __expo.go(p.zone,feet);__expo.lookAt(...p.target);
      },piece);
      await frame.waitForFunction(id=>{
        const root=__expo.scene.getObjectByName('collection_'+id);
        if(!root)return false;
        const guest=root.getObjectByName('guest_anchor_'+id);
        return !guest||guest.children.some(n=>n.isGroup||n.isScene);
      },piece.id,{timeout:60000});
      views[piece.id]=await frameTiming();
      await page.screenshot({path:path.join(dir,name+'-collection-'+piece.id+'.jpg'),type:'jpeg',quality:92});
    }
    const model=await kitReceived;
    if(model?.sha256!==manifest.sha256||model.file!==manifest.model)throw Error('Received collection differs from its manifest.');
    if(process.env.EXPECT_COLLECTION_SHA&&model.sha256!==process.env.EXPECT_COLLECTION_SHA)throw Error('Unexpected kit '+model.sha256);
    return {views,model};
  }
  const initialTiming=await frameTiming();
  if(process.env.EXPECT_GPU&&!initialTiming.gpu.toLowerCase().includes(process.env.EXPECT_GPU.toLowerCase()))report.errors.push(name+' unexpected GPU '+initialTiming.gpu);
  if(state.textures.pendingDetailMaps>0){
    await frame.waitForFunction(()=>__expo.state().textures.pendingDetailMaps===0,null,{timeout:60000});
    await page.screenshot({path:path.join(dir,name+'-full-detail.jpg'),quality:92,type:'jpeg'});
  }
  const detailAfter=await frame.evaluate(()=>__expo.state().textures);
  if(process.env.EXPO_COLLECTION_ONLY){
    const {views,model}=await collectionTiming(process.env.EXPO_COLLECTION_ONLY);
    let partReceipt;
    if(process.env.EXPO_PROOF_PART){
      const proof=await frame.evaluate(part=>{
        const e=__expo,mesh=e.scene.getObjectByName(part);
        if(!mesh)throw Error('Missing fitted part '+part);
        let triangles=0;mesh.traverse(n=>{if(n.geometry)triangles+=(n.geometry.index?.count||n.geometry.attributes.position.count)/3;});
        const expose=value=>{mesh.visible=value;e.draw();return e.renderer.domElement.toDataURL('image/png').split(',')[1];};
        const before=expose(false),after=expose(true);
        return {before,after,part,triangles,feet:e.state().feet,aim:e.camera.getWorldDirection(e.camera.position.clone()).toArray()};
      },process.env.EXPO_PROOF_PART);
      for(const key of ['before','after'])await writeFile(path.join(dir,name+'-fitting-'+key+'.png'),Buffer.from(proof[key],'base64'));
      const {before:unusedBefore,after:unusedAfter,...record}=proof;partReceipt=record;
    }
    report.devices.push({name,size,entryMs,initialTiming,collectionViews:views,collectionModel:model,partReceipt});
    await frame.evaluate(()=>fuzz.engine.setSuspended(true,'panic'));await cx.close();continue;
  }
  let alloyComparison;
  if(!phone&&process.env.COMPARE_ALLOY==='1'){
    alloyComparison=await frame.evaluate(()=>{
      const e=__expo;window.__expoReviewAim=e.camera.quaternion.clone();e.lookAt(1.7,3.65,26.5);const selected=new Set(),others=new Set();
      e.scene.traverse(n=>{if(n.isMesh)for(const m of [n.material].flat())
        if(m.lightMap){if(m.name==='alloy'||m.name==='utility_silver')selected.add(m);else others.add(m);}});
      window.__expoReviewMetal=[...selected];return {targetMaterials:[...selected].map(m=>({name:m.name,final:m.envMapIntensity})),
        otherMaterials:[...others].map(m=>({name:m.name,intensity:m.envMapIntensity}))};
    });
    if(!alloyComparison.targetMaterials.length)throw Error('Missing baked alloy material for entrance comparison.');
    for(const [name,intensity] of [['prior',.33],['revised',1.25]]){
      await frame.evaluate(value=>{for(const m of window.__expoReviewMetal)m.envMapIntensity=value;__expo.draw();},intensity);
      await page.screenshot({path:path.join(dir,'entrance-metal-'+name+'.png')});
    }
    await frame.evaluate(()=>{__expo.camera.quaternion.copy(window.__expoReviewAim);delete window.__expoReviewAim;delete window.__expoReviewMetal;});
  }
  if(process.env.ENTRY_ONLY==='1'){
    report.devices.push({name,size,entryMs,purchaseToRoomMs,commandTiming,firstBytes:firstFiles.reduce((sum,file)=>sum+file.bytes,0),allExpoFirstBytes,mediaBeforePaint,firstFiles,state,initialTiming,detailAfter});
    await frame.evaluate(()=>fuzz.engine.setSuspended(true,'panic'));await cx.close();continue;
  }
  async function touch(kind,x,y){await cdp.send('Input.dispatchTouchEvent',{type:kind,touchPoints:kind==='touchEnd'?[]:[{x,y,radiusX:5,radiusY:5,id:1}]});}
  await frame.locator('.expo-root').focus();
  const pilot=process.env.ENTRY_ONLY==='walk';
  const whileWalking=()=>pilot?frameTiming():page.waitForTimeout(900);
  let walkTiming;
  if(phone){
    const stick=await frame.locator('.expo-stick').boundingBox();if(!stick)throw Error('Missing phone stick.');
    const x=stick.x+stick.width/2,y=stick.y+stick.height/2;
    await touch('touchStart',x,y);await touch('touchMove',x,y-32);walkTiming=await whileWalking();await touch('touchEnd');
    await touch('touchStart',291,315);await touch('touchMove',241,315);await touch('touchEnd');
  }else{await page.keyboard.down('w');walkTiming=await whileWalking();await page.keyboard.up('w');}
  const walked=await frame.evaluate(()=>__expo.state());
  const distance=Math.hypot(walked.feet[0]-state.feet[0],walked.feet[2]-state.feet[2]);
  if(distance<.3)report.errors.push(name+' physical walk failed');
  if(pilot){
    const firstBytes=firstFiles.reduce((sum,item)=>sum+item.bytes,0);
    report.devices.push({name,size,entryMs,purchaseToRoomMs,commandTiming,firstBytes,allExpoFirstBytes,
      mediaBeforePaint,firstFiles,state,initialTiming,detailAfter,walkTiming,walkedMeters:distance,alloyComparison});
    await frame.evaluate(()=>fuzz.engine.setSuspended(true,'panic'));await cx.close();continue;
  }
  await frame.evaluate(async()=>{
    const files=await Promise.all(['margaret','gary','reginald','nana'].map(async id=>new File([await(await fetch('assets/llamas/'+id+'.webp')).blob()],id+'.webp',{type:'image/webp'})));
    fuzz.engine.pictures.addOwn(files);fuzz.engine.pictures.setSource('own');
  });
  await frame.evaluate(async()=>{await __expo.go('dome',[-19,0,-5]);__expo.lookAt(-22,1.45,-5);});
  await frame.waitForFunction(()=>__expo.state().loaded.includes('catwalk'),null,{timeout:90000});
  await frame.evaluate(()=>{__expo.deepen(300);__expo.deepen(125);});
  const dreamTiming=await frameTiming();
  const dreamMedia=await frame.evaluate(()=>({source:'own',pictures:[...document.querySelectorAll('.expo-media-face img')].filter(im=>im.complete&&im.naturalWidth>0).length,
    visible:[...document.querySelectorAll('.expo-media-face')].filter(el=>!el.hidden&&el.querySelector('img,video')).length}));
  if(dreamMedia.pictures===0)report.errors.push(name+' missing museum-source pictures at full depth');
  const shared=await frame.evaluate(()=>__expo.state().textures);
  const rooms={dome:dreamTiming};let collectionViews={},collectionModel;
  if(process.env.ALL_ROOMS==='1'){
    const cameras={atrium:[state.feet,[1.7,3.65,26.5]],hall:[[0,0,13.2],[0,2.4,-9]],
      hoof:[[-18.46,0,8.92],[-18.4,1.75,10.45]],habitat:[[19.4,0,8.13],[19.4,1.56,9.55]],
      twelve:[[.1,-.68,-21.95],[-.38,.94,-30.12]],shop:[[20.84,0,-9.75],[21.2,1.32,-11.42]],
      service:[[25.02,0,29.59],[25.01,1.04,27.21]],secret:[[81.44,0,40.3],[85.8,1.30,41.19]],
      catwalk:[[-22.45,11.95,-4.62],[-22,6,-5]],chronology:[[7.84,0,47.52],[8,1.56,59]],
      forum:[[-18.8,0,103.18],[-30,1.72,104.1]],commons:[[-66.14,0,83.2],[-67,1.72,70.1]],aquarium:[[-44.29,0,45.38],[-32.28,1.71,44.36]]};
    const known=await frame.evaluate(async()=>((await(await fetch(window.expoReviewAssetURL('zones/zones.json'))).json()).zones.map(z=>z.id)));
    if(process.env.EXPO_REQUIRED_ROOMS&&known.length!==Number(process.env.EXPO_REQUIRED_ROOMS))
      throw Error('Expected '+process.env.EXPO_REQUIRED_ROOMS+' rooms, served '+known.length);
    for(const zone of known.filter(id=>cameras[id])){
      await frame.evaluate(async({zone,view})=>{await __expo.go(zone,view[0]);__expo.lookAt(...view[1]);},{zone,view:cameras[zone]});
      if(zone==='twelve')await frame.waitForFunction(()=>__expo.state().loaded.includes('hall'),null,{timeout:90000});
      rooms[zone]=await frameTiming();
    }
    if(process.env.EXPO_COLLECTION_ART==='1'){
      ({views:collectionViews,model:collectionModel}=await collectionTiming());
    }
  }
  await frame.evaluate(async({zone,at})=>{await __expo.go(zone,at);}, {zone:state.zone,at:state.feet});
  await frame.waitForTimeout(900);
  const returned=await frame.evaluate(()=>__expo.state());
  if(shared.sharedHits===0)report.errors.push(name+' shared textures were not reused');
  const list=[...files.values()],firstBytes=firstFiles.reduce((sum,file)=>sum+file.bytes,0);
  report.devices.push({name,size,entryMs,purchaseToRoomMs,commandTiming,firstBytes,allExpoFirstBytes,mediaBeforePaint,firstFiles,state,initialTiming,detailAfter,dreamTiming,dreamMedia,rooms,collectionViews,collectionModel,walkedMeters:distance,shared,returned,
    usedSmallerTextures:list.some(f=>f.name.includes('-mobile.webp'))});
  await frame.evaluate(()=>fuzz.engine.setSuspended(true,'panic'));await frame.locator('.expo-root').waitFor({state:'detached'});await cx.close();
 }
}catch(e){report.errors.push(e.message);throw e;}
finally{await writeFile(path.join(dir,'result.json'),JSON.stringify(report,null,2));await browser.close();}
console.log(JSON.stringify(report.devices.map(d=>({name:d.name,entryMs:d.entryMs,MB:d.firstBytes/1e6,median:d.initialTiming.medianMs,p95:d.initialTiming.p95Ms,walked:d.walkedMeters}))));
if(report.errors.length)process.exitCode=1;
